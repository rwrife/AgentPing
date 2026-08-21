#include <cinttypes>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

namespace {
constexpr auto kTag = "agentping-display";
constexpr TickType_t kHeartbeatInterval = pdMS_TO_TICKS(5000);
}  // namespace

extern "C" void app_main() {
  ESP_LOGI(
      kTag,
      "{\"service\":\"agentping-display\",\"state\":\"baseline\","
      "\"message\":\"display and touch are not initialized yet\"}");

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
