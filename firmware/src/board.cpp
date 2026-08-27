#include "board.h"

#include "driver/gpio.h"
#include "driver/i2c_master.h"
#include "driver/spi_master.h"
#include "esp_check.h"
#include "esp_heap_caps.h"
#include "esp_lcd_co5300.h"
#include "esp_lcd_panel_io.h"
#include "esp_lcd_panel_ops.h"
#include "esp_lcd_touch_ft3168.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "lvgl.h"

#include <algorithm>
#include <cstdint>

namespace agentping::board {
namespace {

constexpr char kTag[] = "board";
// Waveshare ESP-IDF 5.5.2 BSP and schematic at repository commit
// b90e28c953c1fc882258fa8dbd56b7706bc888b7.
constexpr gpio_num_t kLcdCs = GPIO_NUM_10;
constexpr gpio_num_t kLcdClock = GPIO_NUM_11;
constexpr gpio_num_t kData0 = GPIO_NUM_4;
constexpr gpio_num_t kData1 = GPIO_NUM_5;
constexpr gpio_num_t kData2 = GPIO_NUM_7;
constexpr gpio_num_t kData3 = GPIO_NUM_19;
constexpr gpio_num_t kLcdReset = GPIO_NUM_20;
constexpr gpio_num_t kTouchSda = GPIO_NUM_18;
constexpr gpio_num_t kTouchScl = GPIO_NUM_8;
constexpr gpio_num_t kTouchInterrupt = GPIO_NUM_1;
constexpr int kPanelXOffset = 0x14;
constexpr std::size_t kDrawRows = 40;

esp_lcd_panel_handle_t panel = nullptr;
esp_lcd_panel_io_handle_t panel_io = nullptr;
esp_lcd_touch_handle_t touch = nullptr;
lv_display_t* display = nullptr;
lv_indev_t* input = nullptr;
lv_color_t* draw_buffer = nullptr;
bool touch_was_pressed = false;

const std::uint8_t kC4[] = {0x80};
const std::uint8_t kTearing[] = {0x00};
const std::uint8_t kWriteControl[] = {0x20};
const std::uint8_t kHbm[] = {0xFF};
const std::uint8_t kBrightnessOff[] = {0x00};
const std::uint8_t kBrightnessInitial[] = {0xA6};
const co5300_lcd_init_cmd_t kInitCommands[] = {
    {0x11, nullptr, 0, 80},
    {0xC4, kC4, sizeof(kC4), 0},
    {0x35, kTearing, sizeof(kTearing), 0},
    {0x53, kWriteControl, sizeof(kWriteControl), 1},
    {0x63, kHbm, sizeof(kHbm), 1},
    {0x51, kBrightnessOff, sizeof(kBrightnessOff), 1},
    {0x29, nullptr, 0, 10},
    {0x51, kBrightnessInitial, sizeof(kBrightnessInitial), 0},
};

bool color_transfer_done(esp_lcd_panel_io_handle_t, esp_lcd_panel_io_event_data_t*,
                         void* user_context) {
  lv_display_flush_ready(static_cast<lv_display_t*>(user_context));
  return false;
}

void flush(lv_display_t*, const lv_area_t* area, std::uint8_t* pixels) {
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

void read_touch(lv_indev_t*, lv_indev_data_t* data) {
  esp_lcd_touch_point_data_t point{};
  std::uint8_t count = 0;
  const bool pressed = esp_lcd_touch_read_data(touch) == ESP_OK
      && esp_lcd_touch_get_data(touch, &point, &count, 1) == ESP_OK && count > 0;
  if (pressed) {
    data->state = LV_INDEV_STATE_PRESSED;
    data->point.x = std::min<std::uint16_t>(point.x, kWidth - 1);
    data->point.y = std::min<std::uint16_t>(point.y, kHeight - 1);
    if (!touch_was_pressed) {
      ESP_LOGI("touch", "touch down x=%u y=%u", data->point.x, data->point.y);
    }
  } else {
    data->state = LV_INDEV_STATE_RELEASED;
    if (touch_was_pressed) ESP_LOGI("touch", "touch up");
  }
  touch_was_pressed = pressed;
}

void tick(void*) { lv_tick_inc(2); }

std::uint32_t qspi_command(std::uint8_t command) {
  return (static_cast<std::uint32_t>(0x02) << 24U)
      | (static_cast<std::uint32_t>(command) << 8U);
}

}  // namespace

esp_err_t initialize() {
  const gpio_config_t reset = {
      .pin_bit_mask = 1ULL << kLcdReset,
      .mode = GPIO_MODE_OUTPUT,
      .pull_up_en = GPIO_PULLUP_DISABLE,
      .pull_down_en = GPIO_PULLDOWN_DISABLE,
      .intr_type = GPIO_INTR_DISABLE,
  };
  ESP_RETURN_ON_ERROR(gpio_config(&reset), kTag, "LCD reset GPIO configuration failed");
  ESP_RETURN_ON_ERROR(gpio_set_level(kLcdReset, 0), kTag, "LCD reset low failed");
  vTaskDelay(pdMS_TO_TICKS(10));
  ESP_RETURN_ON_ERROR(gpio_set_level(kLcdReset, 1), kTag, "LCD reset high failed");
  vTaskDelay(pdMS_TO_TICKS(10));

  spi_bus_config_t bus{};
  bus.sclk_io_num = kLcdClock;
  bus.data0_io_num = kData0;
  bus.data1_io_num = kData1;
  bus.data2_io_num = kData2;
  bus.data3_io_num = kData3;
  bus.max_transfer_sz = kWidth * kHeight * 2;
  ESP_RETURN_ON_ERROR(spi_bus_initialize(SPI2_HOST, &bus, SPI_DMA_CH_AUTO),
                      kTag, "QSPI bus initialization failed");

  esp_lcd_panel_io_spi_config_t io_config{};
  io_config.cs_gpio_num = kLcdCs;
  io_config.dc_gpio_num = GPIO_NUM_NC;
  io_config.spi_mode = 0;
  io_config.pclk_hz = 80 * 1000 * 1000;
  io_config.trans_queue_depth = 10;
  io_config.lcd_cmd_bits = 32;
  io_config.lcd_param_bits = 8;
  io_config.flags.quad_mode = true;
  ESP_RETURN_ON_ERROR(
      esp_lcd_new_panel_io_spi(
          static_cast<esp_lcd_spi_bus_handle_t>(SPI2_HOST), &io_config, &panel_io),
      kTag, "CO5300 panel I/O initialization failed");

  co5300_vendor_config_t vendor{};
  vendor.init_cmds = kInitCommands;
  vendor.init_cmds_size = sizeof(kInitCommands) / sizeof(kInitCommands[0]);
  vendor.flags.use_qspi_interface = 1;
  esp_lcd_panel_dev_config_t panel_config{};
  panel_config.reset_gpio_num = kLcdReset;
  panel_config.rgb_ele_order = LCD_RGB_ELEMENT_ORDER_RGB;
  panel_config.data_endian = LCD_RGB_DATA_ENDIAN_BIG;
  panel_config.bits_per_pixel = 16;
  panel_config.vendor_config = &vendor;
  ESP_RETURN_ON_ERROR(esp_lcd_new_panel_co5300(panel_io, &panel_config, &panel),
                      kTag, "CO5300 panel initialization failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_reset(panel), kTag, "panel reset failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_init(panel), kTag, "panel init commands failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_set_gap(panel, kPanelXOffset, 0),
                      kTag, "panel offset failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_disp_on_off(panel, true), kTag, "panel enable failed");

  i2c_master_bus_handle_t i2c = nullptr;
  i2c_master_bus_config_t i2c_config{};
  i2c_config.i2c_port = I2C_NUM_0;
  i2c_config.sda_io_num = kTouchSda;
  i2c_config.scl_io_num = kTouchScl;
  i2c_config.clk_source = I2C_CLK_SRC_DEFAULT;
  i2c_config.glitch_ignore_cnt = 7;
  i2c_config.flags.enable_internal_pullup = true;
  ESP_RETURN_ON_ERROR(i2c_new_master_bus(&i2c_config, &i2c), kTag, "touch I2C failed");
  esp_lcd_panel_io_handle_t touch_io = nullptr;
  const esp_lcd_panel_io_i2c_config_t touch_io_config = ESP_LCD_TOUCH_IO_I2C_FT3168_CONFIG();
  ESP_RETURN_ON_ERROR(esp_lcd_new_panel_io_i2c(i2c, &touch_io_config, &touch_io),
                      kTag, "touch panel I/O failed");
  esp_lcd_touch_config_t touch_config{};
  touch_config.x_max = kWidth;
  touch_config.y_max = kHeight;
  touch_config.rst_gpio_num = GPIO_NUM_NC;
  touch_config.int_gpio_num = kTouchInterrupt;
  touch_config.levels.reset = 0;
  touch_config.levels.interrupt = 0;
  ESP_RETURN_ON_ERROR(esp_lcd_touch_new_i2c_ft3168(touch_io, &touch_config, &touch),
                      kTag, "FT6146 touch initialization failed");

  lv_init();
  draw_buffer = static_cast<lv_color_t*>(heap_caps_malloc(
      kWidth * kDrawRows * sizeof(lv_color_t), MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL));
  if (draw_buffer == nullptr) return ESP_ERR_NO_MEM;
  display = lv_display_create(kWidth, kHeight);
  if (display == nullptr) return ESP_ERR_NO_MEM;
  lv_display_set_color_format(display, LV_COLOR_FORMAT_RGB565);
  lv_display_set_flush_cb(display, flush);
  lv_display_set_buffers(display, draw_buffer, nullptr,
                         kWidth * kDrawRows * sizeof(lv_color_t),
                         LV_DISPLAY_RENDER_MODE_PARTIAL);
  lv_display_add_event_cb(display, round_invalidated_area, LV_EVENT_INVALIDATE_AREA, nullptr);

  const esp_lcd_panel_io_callbacks_t callbacks = {
      .on_color_trans_done = color_transfer_done,
  };
  ESP_RETURN_ON_ERROR(esp_lcd_panel_io_register_event_callbacks(
                          panel_io, &callbacks, display),
                      kTag, "display completion callback failed");

  input = lv_indev_create();
  if (input == nullptr) return ESP_ERR_NO_MEM;
  lv_indev_set_type(input, LV_INDEV_TYPE_POINTER);
  lv_indev_set_display(input, display);
  lv_indev_set_read_cb(input, read_touch);

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
  ESP_LOGI(kTag,
           "CO5300 280x456 QSPI panel and FT6146 touch (FT3168 register protocol) initialized");
  return ESP_OK;
}

void set_brightness(unsigned percent) {
  if (panel_io == nullptr) return;
  const unsigned bounded = std::min(percent, 100U);
  const std::uint8_t value = static_cast<std::uint8_t>(bounded * 255U / 100U);
  const esp_err_t result = esp_lcd_panel_io_tx_param(panel_io, qspi_command(0x51), &value, 1);
  if (result != ESP_OK) ESP_LOGE(kTag, "brightness command failed: %s", esp_err_to_name(result));
}

}  // namespace agentping::board
