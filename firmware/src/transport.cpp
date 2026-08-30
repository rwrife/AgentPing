#include "transport.h"

#include "agentping_protocol_v1.h"
#include "config_store.h"
#include "protocol_core.h"
#include "ui.h"

#include "esp_event.h"
#include "esp_log.h"
#include "esp_netif.h"
#include "esp_netif_sntp.h"
#include "esp_random.h"
#include "esp_timer.h"
#include "esp_websocket_client.h"
#include "esp_wifi.h"
#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "mbedtls/md.h"

#include <cstdio>
#include <cstring>
#include <ctime>
#include <string>
#include <utility>

namespace agentping {
namespace {

constexpr EventBits_t kWifiConnected = BIT0;
constexpr EventBits_t kWebSocketConnected = BIT1;
constexpr EventBits_t kWebSocketClosed = BIT2;
constexpr TickType_t kConnectTimeout = pdMS_TO_TICKS(15000);
constexpr std::int64_t kTimeCheckpointIntervalUs = 6LL * 60LL * 60LL * 1000LL * 1000LL;

EventGroupHandle_t events = nullptr;
SemaphoreHandle_t state_mutex = nullptr;
ProtocolState protocol_state;
TextFrameAssembler frame_assembler;
std::uint64_t configuration_generation = 0;

bool lock_state() {
  return state_mutex != nullptr && xSemaphoreTake(state_mutex, portMAX_DELAY) == pdTRUE;
}

void unlock_state() { xSemaphoreGive(state_mutex); }

bool token_is_header_safe(const std::string& token) {
  if (token.size() < 32 || token.size() > 512) return false;
  for (const unsigned char character : token) {
    if (character < 0x21 || character > 0x7e || character == ':' || character == '\\') {
      return false;
    }
  }
  return true;
}

void wifi_event(void*, esp_event_base_t base, std::int32_t identifier, void*) {
  if (base == WIFI_EVENT && identifier == WIFI_EVENT_STA_START) {
    esp_wifi_connect();
  } else if (base == WIFI_EVENT && identifier == WIFI_EVENT_STA_DISCONNECTED) {
    xEventGroupClearBits(events, kWifiConnected);
    xEventGroupSetBits(events, kWebSocketClosed);
    esp_wifi_connect();
  } else if (base == IP_EVENT && identifier == IP_EVENT_STA_GOT_IP) {
    xEventGroupSetBits(events, kWifiConnected);
  }
}

void reject_frame(const char* reason) {
  ESP_LOGW("transport", "WebSocket frame rejected: %s", reason);
  xEventGroupSetBits(events, kWebSocketClosed);
}

void apply_complete_frame(std::string_view wire) {
  Envelope message;
  ParseError result = parse_envelope(wire, message);
  ViewModel model;
  if (result == ParseError::none && lock_state()) {
    ProtocolState candidate = protocol_state;
    const std::uint64_t previous_resume = protocol_state.resume_sequence();
    result = candidate.apply(message);
    if (result == ParseError::none && candidate.resume_sequence() != previous_resume
        && !save_resume_state(configuration_generation, candidate.resume_sequence(),
                              candidate.view())) {
      result = ParseError::ordering;
      ESP_LOGE("transport", "resume checkpoint persistence failed; state not committed");
    }
    if (result == ParseError::none) {
      protocol_state = std::move(candidate);
      model = protocol_state.view();
    }
    unlock_state();
  }
  if (result != ParseError::none) {
    ESP_LOGW("transport", "protocol message rejected: %s", parse_error_name(result));
    xEventGroupSetBits(events, kWebSocketClosed);
    return;
  }
  ui::render(model);
}

void websocket_event(void*, esp_event_base_t, std::int32_t identifier, void* raw_event) {
  auto* event = static_cast<esp_websocket_event_data_t*>(raw_event);
  if (identifier == WEBSOCKET_EVENT_CONNECTED) {
    frame_assembler.reset();
    xEventGroupSetBits(events, kWebSocketConnected);
    return;
  }
  if (identifier == WEBSOCKET_EVENT_DISCONNECTED || identifier == WEBSOCKET_EVENT_ERROR) {
    xEventGroupSetBits(events, kWebSocketClosed);
    return;
  }
  if (identifier != WEBSOCKET_EVENT_DATA || event == nullptr) return;
  if (event->op_code == 0x9 || event->op_code == 0xA) return;  // client handles ping/pong
  if (event->op_code == 0x8) {
    xEventGroupSetBits(events, kWebSocketClosed);
    return;
  }
  if (event->data_len < 0 || event->payload_len < 0 || event->payload_offset < 0) {
    reject_frame("negative length");
    return;
  }
  const std::size_t chunk_size = static_cast<std::size_t>(event->data_len);
  if (chunk_size > 0 && event->data_ptr == nullptr) {
    reject_frame("missing frame data");
    return;
  }
  const std::string_view chunk = chunk_size == 0
      ? std::string_view{}
      : std::string_view(event->data_ptr, chunk_size);
  std::string_view complete;
  const FrameAssemblyStatus status = frame_assembler.push(
      event->op_code, event->fin, static_cast<std::size_t>(event->payload_len),
      static_cast<std::size_t>(event->payload_offset), chunk, complete);
  if (status == FrameAssemblyStatus::rejected) {
    reject_frame("text continuation order or 16 KiB message bound");
  } else if (status == FrameAssemblyStatus::complete) {
    apply_complete_frame(complete);
  }
}

std::string random_identifier(const char* prefix) {
  char value[48];
  std::snprintf(value, sizeof(value), "%s-%08lx%08lx", prefix,
                static_cast<unsigned long>(esp_random()),
                static_cast<unsigned long>(esp_random()));
  return value;
}

std::string random_uuid() {
  const std::uint32_t a = esp_random();
  const std::uint32_t b = esp_random();
  const std::uint32_t c = esp_random();
  const std::uint32_t d = esp_random();
  char value[37];
  std::snprintf(value, sizeof(value), "%08lx-%04lx-4%03lx-%04lx-%04lx%08lx",
                static_cast<unsigned long>(a),
                static_cast<unsigned long>((b >> 16U) & 0xffffU),
                static_cast<unsigned long>(b & 0xfffU),
                static_cast<unsigned long>(0x8000U | (c & 0x3fffU)),
                static_cast<unsigned long>((d >> 16U) & 0xffffU),
                static_cast<unsigned long>(d));
  return value;
}

std::string timestamp() {
  const std::time_t now = std::time(nullptr);
  std::tm utc{};
  gmtime_r(&now, &utc);
  char value[32];
  std::strftime(value, sizeof(value), "%Y-%m-%dT%H:%M:%SZ", &utc);
  return value;
}

std::string sha256_hex(std::string_view value) {
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  unsigned char digest[32] = {};
  if (info == nullptr
      || mbedtls_md(info, reinterpret_cast<const unsigned char*>(value.data()),
                    value.size(), digest) != 0) {
    return {};
  }
  static constexpr char kHex[] = "0123456789abcdef";
  std::string output(64, '0');
  for (std::size_t index = 0; index < sizeof(digest); ++index) {
    output[index * 2] = kHex[digest[index] >> 4U];
    output[index * 2 + 1] = kHex[digest[index] & 0x0fU];
  }
  return output;
}

std::string action_payload(const PreparedAction& action) {
  const std::string digest = action.message_type == "approval" && action.destructive
      ? sha256_hex(action.canonical_prompt)
      : std::string{};
  if (action.message_type == "approval" && action.destructive && digest.empty()) return {};
  return serialize_action_payload(action, random_uuid(), digest, timestamp());
}

std::string envelope(const char* type, const std::string& payload,
                     const std::string& connection, std::uint64_t sequence) {
  return std::string("{\"protocolVersion\":\"") + protocol_v1::kVersion
      + "\",\"messageId\":\"" + random_uuid() + "\",\"type\":\"" + type
      + "\",\"sentAt\":\"" + timestamp() + "\",\"connectionId\":\""
      + connection + "\",\"sequence\":" + std::to_string(sequence)
      + ",\"payload\":" + payload + "}";
}

bool send_text(esp_websocket_client_handle_t client, const std::string& value) {
  if (value.size() > protocol_v1::kMaxMessageBytes) return false;
  return esp_websocket_client_send_text(client, value.data(), value.size(),
                                        pdMS_TO_TICKS(5000))
      == static_cast<int>(value.size());
}

void synchronize_time(std::uint64_t enrollment_generation) {
  const esp_sntp_config_t config = ESP_NETIF_SNTP_DEFAULT_CONFIG("pool.ntp.org");
  const esp_err_t initialized = esp_netif_sntp_init(&config);
  if (initialized != ESP_OK && initialized != ESP_ERR_INVALID_STATE) {
    ESP_LOGW("time", "SNTP initialization failed: %s", esp_err_to_name(initialized));
    return;
  }
  const esp_err_t synced = esp_netif_sntp_sync_wait(pdMS_TO_TICKS(10000));
  if (synced == ESP_OK) {
    ESP_LOGI("time", "clock synchronized; timestamp value omitted from logs");
    if (!save_time_checkpoint(enrollment_generation)) {
      ESP_LOGW("time", "time checkpoint could not be persisted");
    }
  } else {
    ESP_LOGW("time", "SNTP unavailable; using the persisted provisioning checkpoint");
  }
  esp_netif_sntp_deinit();
}

bool initialize_wifi(const DeviceConfig& config) {
  esp_err_t result = esp_netif_init();
  if (result != ESP_OK && result != ESP_ERR_INVALID_STATE) return false;
  result = esp_event_loop_create_default();
  if (result != ESP_OK && result != ESP_ERR_INVALID_STATE) return false;
  if (esp_netif_create_default_wifi_sta() == nullptr) return false;
  const wifi_init_config_t initialization = WIFI_INIT_CONFIG_DEFAULT();
  if (esp_wifi_init(&initialization) != ESP_OK) return false;
  if (esp_event_handler_register(WIFI_EVENT, ESP_EVENT_ANY_ID, wifi_event, nullptr) != ESP_OK
      || esp_event_handler_register(IP_EVENT, IP_EVENT_STA_GOT_IP, wifi_event, nullptr) != ESP_OK) {
    return false;
  }
  wifi_config_t wifi{};
  std::memcpy(wifi.sta.ssid, config.ssid.data(), config.ssid.size());
  std::memcpy(wifi.sta.password, config.wifi_password.data(), config.wifi_password.size());
  wifi.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;
  wifi.sta.pmf_cfg.capable = true;
  wifi.sta.pmf_cfg.required = false;
  return esp_wifi_set_mode(WIFI_MODE_STA) == ESP_OK
      && esp_wifi_set_config(WIFI_IF_STA, &wifi) == ESP_OK
      && esp_wifi_start() == ESP_OK;
}

}  // namespace

void run_transport(const DeviceConfig& config) {
  if (!is_private_wss_endpoint(config.wss_url)) {
    ESP_LOGE("transport", "endpoint rejected: require an RFC1918 IPv4 literal wss://.../ws URL");
    return;
  }
  if (!token_is_header_safe(config.device_token)) {
    ESP_LOGE("transport", "device token rejected before header construction");
    return;
  }
  configuration_generation = config.configuration_generation;
  events = xEventGroupCreate();
  state_mutex = xSemaphoreCreateMutex();
  if (events == nullptr || state_mutex == nullptr) {
    ESP_LOGE("transport", "could not allocate synchronization primitives");
    return;
  }
  protocol_state.restore_resume_state(config.resume_sequence, config.resume_view);
  if (!initialize_wifi(config)) {
    ESP_LOGE("transport", "Wi-Fi initialization failed");
    return;
  }

  xEventGroupWaitBits(events, kWifiConnected, pdFALSE, pdTRUE, portMAX_DELAY);
  synchronize_time(configuration_generation);
  std::int64_t next_time_checkpoint = esp_timer_get_time() + kTimeCheckpointIntervalUs;

  for (unsigned attempt = 0;; ++attempt) {
    xEventGroupClearBits(events, kWebSocketConnected | kWebSocketClosed);
    if (lock_state()) {
      protocol_state.disconnected();
      unlock_state();
    }
    ui::render({});

    const std::string authorization =
        std::string("Authorization: ") + "Bearer" + " " + config.device_token + "\r\n";
    esp_websocket_client_config_t websocket{};
    websocket.uri = config.wss_url.c_str();
    websocket.headers = authorization.c_str();
    websocket.cert_pem = config.server_certificate_pem.c_str();
    websocket.cert_len = config.server_certificate_pem.size() + 1;
    websocket.network_timeout_ms = 10000;
    websocket.ping_interval_sec = protocol_v1::kHeartbeatIntervalSeconds;
    websocket.pingpong_timeout_sec = 10;
    websocket.disable_auto_reconnect = true;

    esp_websocket_client_handle_t client = esp_websocket_client_init(&websocket);
    bool session_started = false;
    if (client != nullptr) {
      esp_websocket_register_events(client, WEBSOCKET_EVENT_ANY, websocket_event, nullptr);
      session_started = esp_websocket_client_start(client) == ESP_OK;
    }
    if (session_started) {
      const EventBits_t connected = xEventGroupWaitBits(
          events, kWebSocketConnected | kWebSocketClosed, pdFALSE, pdFALSE, kConnectTimeout);
      if ((connected & kWebSocketConnected) != 0 && (connected & kWebSocketClosed) == 0) {
        const std::string connection = random_identifier("display");
        std::uint64_t outgoing_sequence = 1;
        std::uint64_t resume = 0;
        if (lock_state()) {
          resume = protocol_state.resume_sequence();
          unlock_state();
        }
        const std::string capability = envelope(
            "capability",
            "{\"deviceId\":\"" + config.device_id
                + "\",\"role\":\"display\",\"supportedVersions\":[\"1.0\"],"
                  "\"features\":[\"events\",\"sessions\",\"attention\",\"approve\",\"deny\",\"reply\",\"cancel\",\"acknowledge\",\"resume\"],"
                  "\"maxMessageBytes\":16384,\"resumeFromSequence\":"
                + std::to_string(resume) + ",\"softwareVersion\":\"0.1.0\"}",
            connection, outgoing_sequence++);
        if (!send_text(client, capability)) xEventGroupSetBits(events, kWebSocketClosed);

        std::int64_t next_heartbeat = esp_timer_get_time()
            + static_cast<std::int64_t>(protocol_v1::kHeartbeatIntervalSeconds) * 1000LL * 1000LL;
        while ((xEventGroupWaitBits(events, kWebSocketClosed, pdFALSE, pdTRUE, pdMS_TO_TICKS(100))
                & kWebSocketClosed) == 0) {
          ui::PendingAction selected_action;
          if (ui::take_action(selected_action)) {
            ViewModel action_view;
            if (lock_state()) {
              action_view = protocol_state.view();
              unlock_state();
            }
            const PreparedAction prepared = prepare_action(
                action_view, selected_action.action, selected_action.text,
                selected_action.confirmed, static_cast<std::int64_t>(std::time(nullptr)));
            if (prepared.status != ActionPreparationStatus::ready) {
              ui::render({UiState::failed, "Response not sent",
                          prepared.status == ActionPreparationStatus::expired
                              ? "The request expired"
                              : "The request changed or is not allowed",
                          action_view.revision});
            } else {
              const std::string payload = action_payload(prepared);
              const std::string action_message = payload.empty()
                  ? std::string{}
                  : envelope(prepared.message_type.c_str(), payload, connection, outgoing_sequence++);
              if (action_message.empty() || !send_text(client, action_message)) {
                ESP_LOGW("transport", "device action send failed; action content omitted");
                xEventGroupSetBits(events, kWebSocketClosed);
                break;
              }
              ESP_LOGI("transport", "device action sent; type=%s content omitted",
                       prepared.message_type.c_str());
            }
          }

          const std::int64_t now_us = esp_timer_get_time();
          if (now_us >= next_heartbeat) {
            std::uint64_t received_sequence = 0;
            if (lock_state()) {
              received_sequence = protocol_state.last_received_sequence();
              unlock_state();
            }
            const std::string heartbeat = envelope(
                "heartbeat",
                "{\"uptimeMs\":" + std::to_string(now_us / 1000)
                    + ",\"status\":\"ready\",\"lastReceivedSequence\":"
                    + std::to_string(received_sequence) + ",\"queueDepth\":0}",
                connection, outgoing_sequence++);
            if (!send_text(client, heartbeat)) {
              xEventGroupSetBits(events, kWebSocketClosed);
              break;
            }
            next_heartbeat = now_us
                + static_cast<std::int64_t>(protocol_v1::kHeartbeatIntervalSeconds) * 1000LL * 1000LL;
          }
          if (now_us >= next_time_checkpoint) {
            if (!save_time_checkpoint(configuration_generation)) {
              ESP_LOGW("time", "periodic checkpoint failed");
            }
            next_time_checkpoint = now_us + kTimeCheckpointIntervalUs;
          }
        }
      }
    }
    if (client != nullptr) {
      esp_websocket_client_stop(client);
      esp_websocket_client_destroy(client);
    }
    xEventGroupWaitBits(events, kWifiConnected, pdFALSE, pdTRUE, portMAX_DELAY);
    const std::uint32_t delay = backoff_delay_ms(attempt, esp_random());
    ESP_LOGI("transport", "reconnect scheduled in %lu ms", static_cast<unsigned long>(delay));
    vTaskDelay(pdMS_TO_TICKS(delay));
  }
}

}  // namespace agentping
