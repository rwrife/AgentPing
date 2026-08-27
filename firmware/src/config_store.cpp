#include "config_store.h"

#include "protocol_core.h"

#include "cJSON.h"
#include "esp_log.h"
#include "esp_random.h"
#include "esp_system.h"
#include "nvs.h"

#include <cctype>
#include <cstddef>
#include <cmath>
#include <cstdio>
#include <ctime>
#include <cstring>
#include <memory>
#include <new>
#include <string>
#include <string_view>
#include <sys/time.h>
#include <type_traits>

namespace agentping {
namespace {

constexpr char kNamespace[] = "agentping";
constexpr std::time_t kMinimumProvisioningTime = 1700000000;
constexpr std::time_t kMaximumProvisioningTime = 4102444800;  // 2100-01-01 UTC
constexpr std::size_t kProvisioningLineBytes = 8192;
constexpr std::uint32_t kEnrollmentRecordMagic = 0x41504531;  // "APE1"
constexpr std::uint32_t kResumeRecordMagic = 0x41505231;  // "APR1"
constexpr std::uint32_t kTimeRecordMagic = 0x41505431;  // "APT1"
constexpr std::uint8_t kRecordVersion = 1;
constexpr std::size_t kMaxPersistedTitleBytes = 120 * 4;
constexpr std::size_t kMaxPersistedDetailBytes = 1024 * 4;

struct EnrollmentRecord {
  std::uint32_t magic = kEnrollmentRecordMagic;
  std::uint32_t checksum = 0;
  std::uint8_t version = kRecordVersion;
  std::uint8_t reserved[7] = {};
  std::uint64_t generation = 0;
  std::uint64_t unix_time = 0;
  char ssid[33] = {};
  char wifi_password[64] = {};
  char wss_url[256] = {};
  char device_id[129] = {};
  char device_token[513] = {};
  char server_certificate_pem[3501] = {};
};

struct ResumeRecord {
  std::uint32_t magic = kResumeRecordMagic;
  std::uint32_t checksum = 0;
  std::uint8_t version = kRecordVersion;
  std::uint8_t state = 0;
  std::uint8_t reserved[6] = {};
  std::uint64_t generation = 0;
  std::uint64_t sequence = 0;
  std::uint64_t revision = 0;
  char title[kMaxPersistedTitleBytes + 1] = {};
  char detail[kMaxPersistedDetailBytes + 1] = {};
  char reserved_tail[6] = {};
};

struct TimeRecord {
  std::uint32_t magic = kTimeRecordMagic;
  std::uint32_t checksum = 0;
  std::uint8_t version = kRecordVersion;
  std::uint8_t reserved[7] = {};
  std::uint64_t generation = 0;
  std::uint64_t unix_time = 0;
};

static_assert(std::is_trivially_copyable_v<EnrollmentRecord>);
static_assert(std::is_trivially_copyable_v<ResumeRecord>);
static_assert(std::is_trivially_copyable_v<TimeRecord>);

template <typename Record>
std::uint32_t record_checksum(const Record& record) {
  const auto* bytes = reinterpret_cast<const unsigned char*>(&record);
  constexpr std::size_t checksum_offset = offsetof(Record, checksum);
  std::uint32_t value = 2166136261U;
  for (std::size_t index = 0; index < sizeof(record); ++index) {
    const unsigned char byte = index >= checksum_offset
            && index < checksum_offset + sizeof(record.checksum)
        ? 0
        : bytes[index];
    value = (value ^ byte) * 16777619U;
  }
  return value;
}

template <std::size_t Size>
bool load_record_string(const char (&value)[Size], std::string& output) {
  const std::size_t length = ::strnlen(value, Size);
  if (length == Size) return false;
  output.assign(value, length);
  return true;
}

template <std::size_t Size>
void store_record_string(char (&output)[Size], const std::string& value) {
  std::memcpy(output, value.data(), value.size());
}

bool json_string(cJSON* root, const char* field, std::string& output,
                 std::size_t minimum, std::size_t maximum) {
  cJSON* value = cJSON_GetObjectItemCaseSensitive(root, field);
  if (!cJSON_IsString(value) || value->valuestring == nullptr) return false;
  const std::size_t length = std::strlen(value->valuestring);
  if (length < minimum || length > maximum) return false;
  output.assign(value->valuestring, length);
  return true;
}

bool has_exact_fields(cJSON* root) {
  static constexpr const char* kFields[] = {
      "ssid", "wifiPassword", "wssUrl", "deviceId", "deviceToken",
      "serverCertificatePem", "unixTime"};
  if (!cJSON_IsObject(root) || cJSON_GetArraySize(root) != 7) return false;
  for (const char* field : kFields) {
    if (cJSON_GetObjectItemCaseSensitive(root, field) == nullptr) return false;
  }
  return true;
}

bool safe_identifier(const std::string& value) {
  if (value.empty() || !std::isalnum(static_cast<unsigned char>(value.front()))) return false;
  for (const unsigned char character : value) {
    if (!std::isalnum(character) && character != '.' && character != '_'
        && character != ':' && character != '-') return false;
  }
  return true;
}

bool safe_token(const std::string& value) {
  for (const unsigned char character : value) {
    if (character < 0x21 || character > 0x7e || character == ':' || character == '\\') {
      return false;
    }
  }
  return true;
}

bool certificate_pem(const std::string& value) {
  constexpr std::string_view begin = "-----BEGIN CERTIFICATE-----\n";
  constexpr std::string_view end = "-----END CERTIFICATE-----";
  if (value.rfind(begin, 0) != 0) return false;
  const std::size_t end_at = value.find(end, begin.size());
  if (end_at == std::string::npos) return false;
  for (std::size_t index = end_at + end.size(); index < value.size(); ++index) {
    if (value[index] != '\r' && value[index] != '\n') return false;
  }
  return true;
}

bool load_enrollment_record(nvs_handle_t handle, DeviceConfig& config,
                            std::uint64_t& provisioned_time) {
  auto record = std::unique_ptr<EnrollmentRecord>(new (std::nothrow) EnrollmentRecord());
  if (!record) return false;
  std::size_t size = sizeof(*record);
  if (nvs_get_blob(handle, "enroll_v1", record.get(), &size) != ESP_OK
      || size != sizeof(*record) || record->magic != kEnrollmentRecordMagic
      || record->version != kRecordVersion || record->generation == 0
      || record->checksum != record_checksum(*record)
      || !load_record_string(record->ssid, config.ssid)
      || !load_record_string(record->wifi_password, config.wifi_password)
      || !load_record_string(record->wss_url, config.wss_url)
      || !load_record_string(record->device_id, config.device_id)
      || !load_record_string(record->device_token, config.device_token)
      || !load_record_string(record->server_certificate_pem, config.server_certificate_pem)) {
    return false;
  }
  config.configuration_generation = record->generation;
  provisioned_time = record->unix_time;
  return config.complete() && is_private_wss_endpoint(config.wss_url)
      && safe_identifier(config.device_id) && safe_token(config.device_token)
      && certificate_pem(config.server_certificate_pem);
}

bool load_resume_record(nvs_handle_t handle, DeviceConfig& config) {
  auto record = std::unique_ptr<ResumeRecord>(new (std::nothrow) ResumeRecord());
  if (!record) return false;
  std::size_t size = sizeof(*record);
  if (nvs_get_blob(handle, "resume_v1", record.get(), &size) != ESP_OK
      || size != sizeof(*record) || record->magic != kResumeRecordMagic
      || record->version != kRecordVersion
      || record->generation != config.configuration_generation
      || record->checksum != record_checksum(*record)
      || record->state > static_cast<std::uint8_t>(UiState::failed)
      || record->sequence > 9007199254740991ULL
      || ::strnlen(record->title, sizeof(record->title)) == sizeof(record->title)
      || ::strnlen(record->detail, sizeof(record->detail)) == sizeof(record->detail)) {
    return false;
  }
  config.resume_sequence = record->sequence;
  config.resume_view = {
      static_cast<UiState>(record->state), record->title, record->detail, record->revision};
  return true;
}

std::uint64_t load_time_record(nvs_handle_t handle, std::uint64_t generation,
                               std::uint64_t fallback) {
  TimeRecord record;
  std::size_t size = sizeof(record);
  if (nvs_get_blob(handle, "time_v1", &record, &size) != ESP_OK
      || size != sizeof(record) || record.magic != kTimeRecordMagic
      || record.version != kRecordVersion || record.generation != generation
      || record.checksum != record_checksum(record)
      || record.unix_time < static_cast<std::uint64_t>(kMinimumProvisioningTime)
      || record.unix_time > static_cast<std::uint64_t>(kMaximumProvisioningTime)) {
    return fallback;
  }
  return record.unix_time;
}

}  // namespace

bool DeviceConfig::complete() const {
  return !ssid.empty() && !wifi_password.empty() && !wss_url.empty()
      && !device_id.empty() && !device_token.empty() && !server_certificate_pem.empty()
      && configuration_generation != 0;
}

bool load_config(DeviceConfig& config) {
  nvs_handle_t handle;
  if (nvs_open(kNamespace, NVS_READONLY, &handle) != ESP_OK) return false;
  std::uint64_t provisioned_time = 0;
  const bool ok = load_enrollment_record(handle, config, provisioned_time);
  if (ok) {
    const std::uint64_t last_time = load_time_record(
        handle, config.configuration_generation, provisioned_time);
    if (last_time >= static_cast<std::uint64_t>(kMinimumProvisioningTime)
        && last_time <= static_cast<std::uint64_t>(kMaximumProvisioningTime)) {
      timeval clock = {static_cast<std::time_t>(last_time), 0};
      settimeofday(&clock, nullptr);
    }
  }
  if (!ok || !load_resume_record(handle, config)) {
    config.resume_sequence = 0;
    config.resume_view = {};
  }
  nvs_close(handle);
  return ok;
}

bool save_resume_state(std::uint64_t configuration_generation,
                       std::uint64_t sequence, const ViewModel& view) {
  if (configuration_generation == 0 || sequence > 9007199254740991ULL
      || view.title.size() > kMaxPersistedTitleBytes
      || view.detail.size() > kMaxPersistedDetailBytes
      || static_cast<std::uint8_t>(view.state) > static_cast<std::uint8_t>(UiState::failed)) {
    return false;
  }
  auto record = std::unique_ptr<ResumeRecord>(new (std::nothrow) ResumeRecord());
  if (!record) return false;
  record->state = static_cast<std::uint8_t>(view.state);
  record->generation = configuration_generation;
  record->sequence = sequence;
  record->revision = view.revision;
  store_record_string(record->title, view.title);
  store_record_string(record->detail, view.detail);
  record->checksum = record_checksum(*record);
  nvs_handle_t handle;
  if (nvs_open(kNamespace, NVS_READWRITE, &handle) != ESP_OK) return false;
  const bool ok = nvs_set_blob(handle, "resume_v1", record.get(), sizeof(*record)) == ESP_OK
      && nvs_commit(handle) == ESP_OK;
  nvs_close(handle);
  return ok;
}

bool save_time_checkpoint(std::uint64_t configuration_generation) {
  const std::time_t now = std::time(nullptr);
  if (configuration_generation == 0 || now < kMinimumProvisioningTime
      || now > kMaximumProvisioningTime) {
    return false;
  }
  TimeRecord record;
  record.generation = configuration_generation;
  record.unix_time = static_cast<std::uint64_t>(now);
  record.checksum = record_checksum(record);
  nvs_handle_t handle;
  if (nvs_open(kNamespace, NVS_READWRITE, &handle) != ESP_OK) return false;
  const bool ok = nvs_set_blob(handle, "time_v1", &record, sizeof(record)) == ESP_OK
      && nvs_commit(handle) == ESP_OK;
  nvs_close(handle);
  return ok;
}

bool erase_config() {
  nvs_handle_t handle;
  if (nvs_open(kNamespace, NVS_READWRITE, &handle) != ESP_OK) return false;
  const bool ok = nvs_erase_all(handle) == ESP_OK && nvs_commit(handle) == ESP_OK;
  nvs_close(handle);
  return ok;
}

void provisioning_console() {
  ESP_LOGW("provision", "configuration absent; send one compact JSON line over USB serial");
  ESP_LOGW("provision",
           "required fields: ssid,wifiPassword,wssUrl,deviceId,deviceToken,"
           "serverCertificatePem,unixTime");
  auto line = std::unique_ptr<char[]>(new (std::nothrow) char[kProvisioningLineBytes]);
  if (!line) {
    ESP_LOGE("provision", "could not allocate the bounded serial input buffer");
    return;
  }
  while (std::fgets(line.get(), kProvisioningLineBytes, stdin) != nullptr) {
    const std::size_t length = std::strlen(line.get());
    if (length == kProvisioningLineBytes - 1 && line[length - 1] != '\n') {
      ESP_LOGE("provision", "provisioning document exceeds serial input limit");
      int next = 0;
      while ((next = std::fgetc(stdin)) != '\n' && next != EOF) {}
      continue;
    }
    cJSON* root = cJSON_ParseWithLength(line.get(), length);
    DeviceConfig candidate;
    cJSON* epoch = root == nullptr ? nullptr : cJSON_GetObjectItemCaseSensitive(root, "unixTime");
    const bool valid = root != nullptr && has_exact_fields(root)
        && json_string(root, "ssid", candidate.ssid, 1, 32)
        && json_string(root, "wifiPassword", candidate.wifi_password, 8, 63)
        && json_string(root, "wssUrl", candidate.wss_url, 1, 255)
        && json_string(root, "deviceId", candidate.device_id, 1, 128)
        && json_string(root, "deviceToken", candidate.device_token, 32, 512)
        && json_string(root, "serverCertificatePem", candidate.server_certificate_pem, 128, 3500)
        && is_private_wss_endpoint(candidate.wss_url)
        && safe_identifier(candidate.device_id)
        && safe_token(candidate.device_token)
        && certificate_pem(candidate.server_certificate_pem)
        && cJSON_IsNumber(epoch)
        && epoch->valuedouble >= static_cast<double>(kMinimumProvisioningTime)
        && epoch->valuedouble <= static_cast<double>(kMaximumProvisioningTime)
        && std::floor(epoch->valuedouble) == epoch->valuedouble;
    if (!valid) {
      cJSON_Delete(root);
      ESP_LOGE("provision", "provisioning rejected; no values were written");
      continue;
    }

    auto record = std::unique_ptr<EnrollmentRecord>(new (std::nothrow) EnrollmentRecord());
    if (!record) {
      cJSON_Delete(root);
      ESP_LOGE("provision", "could not allocate the enrollment record");
      continue;
    }
    record->generation = (static_cast<std::uint64_t>(esp_random()) << 32U) | esp_random();
    if (record->generation == 0) record->generation = 1;
    record->unix_time = static_cast<std::uint64_t>(epoch->valuedouble);
    store_record_string(record->ssid, candidate.ssid);
    store_record_string(record->wifi_password, candidate.wifi_password);
    store_record_string(record->wss_url, candidate.wss_url);
    store_record_string(record->device_id, candidate.device_id);
    store_record_string(record->device_token, candidate.device_token);
    store_record_string(record->server_certificate_pem, candidate.server_certificate_pem);
    record->checksum = record_checksum(*record);

    nvs_handle_t handle;
    const esp_err_t opened = nvs_open(kNamespace, NVS_READWRITE, &handle);
    const bool stored = opened == ESP_OK
        && nvs_set_blob(handle, "enroll_v1", record.get(), sizeof(*record)) == ESP_OK
        && nvs_commit(handle) == ESP_OK;
    if (opened == ESP_OK) nvs_close(handle);
    cJSON_Delete(root);
    if (!stored) {
      ESP_LOGE("provision", "NVS write failed; provisioning did not commit");
      continue;
    }
    ESP_LOGI("provision", "configuration stored; secret fields were not logged; rebooting");
    esp_restart();
  }
}

}  // namespace agentping
