#include <cinttypes>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "agentping_protocol_v1.h"

namespace {
constexpr auto kTag = "agentping-display";
constexpr TickType_t kHeartbeatInterval = pdMS_TO_TICKS(5000);
}  // namespace

extern "C" void app_main() {
  ESP_LOGI(
      kTag,
      "{\"service\":\"agentping-display\",\"state\":\"baseline\","
      "\"protocol_contract\":\"%s\",\"max_message_bytes\":%zu,"
      "\"message\":\"display, touch, networking, and pairing are not initialized yet\"}",
      agentping::protocol_v1::kVersion,
      agentping::protocol_v1::kMaxMessageBytes);

  while (true) {
    const auto uptimeMs = esp_timer_get_time() / 1000;
    ESP_LOGI(
        kTag,
        "{\"service\":\"agentping-display\",\"state\":\"alive\","
        "\"uptime_ms\":%" PRId64 "}",
        uptimeMs);
    vTaskDelay(kHeartbeatInterval);
  }
}
