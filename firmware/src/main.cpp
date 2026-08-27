#include "board.h"
#include "config_store.h"
#include "transport.h"
#include "ui.h"

#include "driver/gpio.h"
#include "esp_log.h"
#include "esp_system.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nvs_flash.h"

namespace {

constexpr gpio_num_t kBootButton = GPIO_NUM_0;

bool factory_reset_requested() {
  const gpio_config_t button = {
      .pin_bit_mask = 1ULL << kBootButton,
      .mode = GPIO_MODE_INPUT,
      .pull_up_en = GPIO_PULLUP_ENABLE,
      .pull_down_en = GPIO_PULLDOWN_DISABLE,
      .intr_type = GPIO_INTR_DISABLE,
  };
  if (gpio_config(&button) != ESP_OK || gpio_get_level(kBootButton) != 0) return false;
  ESP_LOGW("boot", "BOOT held; keep holding for three seconds to erase AgentPing settings");
  for (unsigned sample = 0; sample < 30; ++sample) {
    vTaskDelay(pdMS_TO_TICKS(100));
    if (gpio_get_level(kBootButton) != 0) return false;
  }
  return true;
}

void ui_task(void*) {
  while (true) agentping::ui::task();
}

}  // namespace

extern "C" void app_main() {
  esp_err_t nvs_result = nvs_flash_init();
  if (nvs_result == ESP_ERR_NVS_NO_FREE_PAGES || nvs_result == ESP_ERR_NVS_NEW_VERSION_FOUND) {
    ESP_ERROR_CHECK(nvs_flash_erase());
    nvs_result = nvs_flash_init();
  }
  ESP_ERROR_CHECK(nvs_result);

  ESP_ERROR_CHECK(agentping::board::initialize());
  agentping::board::set_brightness(65);
  agentping::ui::initialize();
  agentping::ui::render({});
  if (xTaskCreate(ui_task, "lvgl", 6144, nullptr, 5, nullptr) != pdPASS) {
    ESP_LOGE("boot", "LVGL task creation failed");
    return;
  }

  if (factory_reset_requested()) {
    agentping::ui::render({agentping::UiState::idle, "Factory reset", "Erasing device settings", 0});
    if (agentping::erase_config()) {
      ESP_LOGW("boot", "AgentPing NVS namespace erased; rebooting");
      esp_restart();
    }
    ESP_LOGE("boot", "factory reset failed");
    return;
  }

  agentping::DeviceConfig config;
  if (!agentping::load_config(config)) {
    agentping::ui::render({agentping::UiState::disconnected,
                           "Provisioning required",
                           "Connect USB serial and send the enrollment bundle",
                           0});
    agentping::provisioning_console();
    ESP_LOGE("boot", "provisioning console ended without a committed configuration");
    return;
  }

  agentping::run_transport(config);
}
