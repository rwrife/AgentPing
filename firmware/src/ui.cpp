#include "ui.h"

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "lvgl.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <string>
#include <utility>

namespace agentping::ui {
namespace {

struct Theme {
  std::uint32_t color;
  const char* glyph;
  const char* label;
};

lv_obj_t* icon = nullptr;
lv_obj_t* status = nullptr;
lv_obj_t* detail = nullptr;
lv_obj_t* screen = nullptr;
SemaphoreHandle_t mutex = nullptr;
unsigned shift_index = 0;
std::int64_t next_shift_us = 0;

Theme theme(UiState state) {
  switch (state) {
    case UiState::idle: return {0x38BDF8, LV_SYMBOL_PAUSE, "IDLE"};
    case UiState::running: return {0xFACC15, LV_SYMBOL_REFRESH, "ACTIVE / RUNNING"};
    case UiState::waiting: return {0xFB923C, LV_SYMBOL_BELL, "WAITING FOR INPUT"};
    case UiState::completed: return {0x4ADE80, LV_SYMBOL_OK, "COMPLETED"};
    case UiState::failed: return {0xF87171, LV_SYMBOL_WARNING, "FAILED / ERROR"};
    case UiState::disconnected: return {0xE2E8F0, LV_SYMBOL_CLOSE, "DISCONNECTED"};
  }
  return {0xE2E8F0, LV_SYMBOL_CLOSE, "DISCONNECTED"};
}

bool lock(TickType_t timeout = portMAX_DELAY) {
  return mutex != nullptr && xSemaphoreTakeRecursive(mutex, timeout) == pdTRUE;
}

void unlock() { xSemaphoreGiveRecursive(mutex); }

void align_content() {
  static constexpr std::array<std::pair<int, int>, 8> kOffsets = {{
      {-2, -2}, {0, -2}, {2, -1}, {2, 1}, {1, 2}, {-1, 2}, {-2, 1}, {-2, 0},
  }};
  const auto [x, y] = kOffsets[shift_index % kOffsets.size()];
  lv_obj_align(icon, LV_ALIGN_TOP_MID, x, 64 + y);
  lv_obj_align(status, LV_ALIGN_TOP_MID, x, 122 + y);
  lv_obj_align(detail, LV_ALIGN_TOP_MID, x, 184 + y);
}

}  // namespace

void initialize() {
  mutex = xSemaphoreCreateRecursiveMutex();
  if (mutex == nullptr) {
    ESP_LOGE("ui", "could not create LVGL mutex");
    return;
  }
  if (!lock()) return;
  screen = lv_screen_active();
  lv_obj_set_style_bg_color(screen, lv_color_hex(0x05070A), 0);
  lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);
  icon = lv_label_create(screen);
  status = lv_label_create(screen);
  detail = lv_label_create(screen);
  lv_obj_set_width(status, 244);
  lv_obj_set_width(detail, 244);
  lv_obj_set_style_text_align(status, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_set_style_text_align(detail, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_set_style_text_font(icon, &lv_font_montserrat_28, 0);
  lv_obj_set_style_text_font(status, &lv_font_montserrat_20, 0);
  lv_obj_set_style_text_font(detail, &lv_font_montserrat_14, 0);
  lv_label_set_long_mode(status, LV_LABEL_LONG_WRAP);
  lv_label_set_long_mode(detail, LV_LABEL_LONG_WRAP);
  align_content();
  next_shift_us = esp_timer_get_time() + 60LL * 1000LL * 1000LL;
  unlock();
}

void render(const ViewModel& model) {
  if (!lock()) {
    ESP_LOGE("ui", "state render failed because the LVGL mutex is unavailable");
    return;
  }
  const Theme selected = theme(model.state);
  const std::string body = model.title + "\n\n" + model.detail;
  lv_label_set_text(icon, selected.glyph);
  lv_label_set_text(status, selected.label);
  lv_label_set_text(detail, body.c_str());
  for (lv_obj_t* object : {icon, status, detail}) {
    lv_obj_set_style_text_color(object, lv_color_hex(selected.color), 0);
  }
  align_content();
  unlock();
}

void task() {
  if (!lock()) {
    vTaskDelay(pdMS_TO_TICKS(10));
    return;
  }
  if (esp_timer_get_time() >= next_shift_us) {
    ++shift_index;
    align_content();
    lv_obj_invalidate(screen);
    next_shift_us = esp_timer_get_time() + 60LL * 1000LL * 1000LL;
  }
  const std::uint32_t delay_ms = lv_timer_handler();
  unlock();
  vTaskDelay(pdMS_TO_TICKS(std::max<std::uint32_t>(5, std::min<std::uint32_t>(delay_ms, 50))));
}

}  // namespace agentping::ui
