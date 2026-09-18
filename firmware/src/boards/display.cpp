#include "display.h"
#include "board.h"

#include "esp_check.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_timer.h"

#include <cstdint>

namespace agentping::board {
namespace {
constexpr char kTag[] = "display";
constexpr int kDrawRows = 40;
esp_lcd_panel_handle_t panel = nullptr;
bool swap_pixel_bytes = true;

bool color_transfer_done(esp_lcd_panel_io_handle_t, esp_lcd_panel_io_event_data_t*,
                         void* context) {
  lv_display_flush_ready(static_cast<lv_display_t*>(context));
  return false;
}

void flush(lv_display_t* display, const lv_area_t* area, std::uint8_t* pixels) {
  if (swap_pixel_bytes) {
    lv_draw_sw_rgb565_swap(pixels, (area->x2-area->x1+1)*(area->y2-area->y1+1));
  }
  const esp_err_t result = esp_lcd_panel_draw_bitmap(
      panel, area->x1, area->y1, area->x2 + 1, area->y2 + 1, pixels);
  if (result != ESP_OK) {
    ESP_LOGE(kTag, "display transfer failed: %s", esp_err_to_name(result));
    lv_display_flush_ready(display);
  }
}

void round_invalidated_area(lv_event_t* event) {
  auto* area = static_cast<lv_area_t*>(lv_event_get_param(event));
  if (area == nullptr) return;
  area->x1 &= ~1;
  area->y1 &= ~1;
  area->x2 |= 1;
  area->y2 |= 1;
  if (area->x2 >= kWidth) area->x2 = kWidth - 1;
  if (area->y2 >= kHeight) area->y2 = kHeight - 1;
}

void tick(void*) { lv_tick_inc(2); }
}

esp_err_t initialize_display(esp_lcd_panel_handle_t hardware,
                             esp_lcd_panel_io_handle_t io, bool swap_bytes,
                             lv_display_t** result) {
  panel = hardware;
  swap_pixel_bytes = swap_bytes;
  lv_init();
  const auto bytes = kWidth * kDrawRows * sizeof(std::uint16_t);
  auto* buffer = heap_caps_malloc(bytes, MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);
  ESP_RETURN_ON_FALSE(buffer, ESP_ERR_NO_MEM, kTag, "DMA buffer allocation failed");
  auto* display = lv_display_create(kWidth, kHeight);
  ESP_RETURN_ON_FALSE(display, ESP_ERR_NO_MEM, kTag, "LVGL display allocation failed");
  lv_display_set_color_format(display, LV_COLOR_FORMAT_RGB565);
  lv_display_set_flush_cb(display, flush);
  lv_display_set_buffers(display, buffer, nullptr, bytes, LV_DISPLAY_RENDER_MODE_PARTIAL);
  lv_display_add_event_cb(display, round_invalidated_area, LV_EVENT_INVALIDATE_AREA, nullptr);
  const esp_lcd_panel_io_callbacks_t callbacks = {
      .on_color_trans_done = color_transfer_done,
  };
  ESP_RETURN_ON_ERROR(esp_lcd_panel_io_register_event_callbacks(io, &callbacks, display),
                      kTag, "display completion callback failed");
  const esp_timer_create_args_t timer_args = {
      .callback = tick,
      .arg = nullptr,
      .dispatch_method = ESP_TIMER_TASK,
      .name = "lv_tick",
      .skip_unhandled_events = true,
  };
  esp_timer_handle_t timer = nullptr;
  ESP_RETURN_ON_ERROR(esp_timer_create(&timer_args, &timer), kTag, "LVGL timer create failed");
  ESP_RETURN_ON_ERROR(esp_timer_start_periodic(timer, 2000), kTag, "LVGL timer start failed");
  *result = display;
  return ESP_OK;
}
}
