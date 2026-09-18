#ifdef AGENTPING_TARGET_WAVESHARE_ESP32_S3_LCD_1_47B
#ifndef AGENTPING_LIVE3D
#error "The non-touch ESP32-S3-LCD-1.47B currently supports the live3d USB application only"
#endif
#include "board.h"
#include "display.h"

#include "driver/ledc.h"
#include "driver/spi_master.h"
#include "esp_check.h"
#include "esp_lcd_panel_vendor.h"
#include "esp_log.h"

#include <algorithm>
#include <cstdint>

namespace agentping::board {
namespace {
constexpr char kTag[] = "board";
constexpr int kBacklight = 46;
bool backlight_ready = false;

// ESP32-S3-LCD-1.47B ESP-IDF demo, LCD_Driver/Vernon_ST7789T.
struct PanelCommand {
  int command;
  std::uint8_t data[14];
  std::size_t size;
};
constexpr PanelCommand kInitCommands[] = {
    {0xB0, {0x00, 0xE8}, 2},
    {0xB2, {0x0C, 0x0C, 0x00, 0x33, 0x33}, 5},
    {0xB7, {0x75}, 1},
    {0xBB, {0x1A}, 1},
    {0xC0, {0x80}, 1},
    {0xC2, {0x01, 0xFF}, 2},
    {0xC3, {0x13}, 1},
    {0xC4, {0x20}, 1},
    {0xC6, {0x0F}, 1},
    {0xD0, {0xA4, 0xA1}, 2},
    {0xE0, {0xD0, 0x0D, 0x14, 0x0D, 0x0D, 0x09, 0x38,
            0x44, 0x4E, 0x3A, 0x17, 0x18, 0x2F, 0x30}, 14},
    {0xE1, {0xD0, 0x09, 0x0F, 0x08, 0x07, 0x14, 0x37,
            0x44, 0x4D, 0x38, 0x15, 0x16, 0x2C, 0x2E}, 14},
};
}

esp_err_t initialize() {
  ledc_timer_config_t timer{};
  timer.speed_mode = LEDC_LOW_SPEED_MODE;
  timer.timer_num = LEDC_TIMER_0;
  timer.duty_resolution = LEDC_TIMER_13_BIT;
  timer.freq_hz = 4000;
  timer.clk_cfg = LEDC_AUTO_CLK;
  ESP_RETURN_ON_ERROR(ledc_timer_config(&timer), kTag, "backlight timer failed");
  ledc_channel_config_t channel{};
  channel.gpio_num = kBacklight;
  channel.speed_mode = LEDC_LOW_SPEED_MODE;
  channel.channel = LEDC_CHANNEL_0;
  channel.timer_sel = LEDC_TIMER_0;
  ESP_RETURN_ON_ERROR(ledc_channel_config(&channel), kTag, "backlight channel failed");
  backlight_ready = true;

  spi_bus_config_t bus{};
  bus.sclk_io_num = 40;
  bus.mosi_io_num = 45;
  bus.miso_io_num = -1;
  bus.quadwp_io_num = -1;
  bus.quadhd_io_num = -1;
  bus.max_transfer_sz = kWidth * kHeight * 2;
  ESP_RETURN_ON_ERROR(spi_bus_initialize(SPI3_HOST, &bus, SPI_DMA_CH_AUTO),
                      kTag, "LCD SPI initialization failed");
  esp_lcd_panel_io_spi_config_t io_config{};
  io_config.cs_gpio_num = 42;
  io_config.dc_gpio_num = 41;
  io_config.pclk_hz = 12 * 1000 * 1000;
  io_config.trans_queue_depth = 10;
  io_config.lcd_cmd_bits = 8;
  io_config.lcd_param_bits = 8;
  esp_lcd_panel_io_handle_t io = nullptr;
  ESP_RETURN_ON_ERROR(esp_lcd_new_panel_io_spi(
      static_cast<esp_lcd_spi_bus_handle_t>(SPI3_HOST), &io_config, &io),
      kTag, "ST7789 panel I/O failed");
  esp_lcd_panel_dev_config_t config{};
  config.reset_gpio_num = 39;
  config.rgb_ele_order = LCD_RGB_ELEMENT_ORDER_BGR;
  config.bits_per_pixel = 16;
  esp_lcd_panel_handle_t panel = nullptr;
  ESP_RETURN_ON_ERROR(esp_lcd_new_panel_st7789(io, &config, &panel),
                      kTag, "ST7789 driver failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_reset(panel), kTag, "LCD reset failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_init(panel), kTag, "LCD initialization failed");
  for (const auto& command : kInitCommands) {
    ESP_RETURN_ON_ERROR(esp_lcd_panel_io_tx_param(
        io, command.command, command.data, command.size), kTag, "LCD register setup failed");
  }
  ESP_RETURN_ON_ERROR(esp_lcd_panel_mirror(panel, true, false), kTag, "LCD orientation failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_set_gap(panel, 34, 0), kTag, "LCD offset failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_invert_color(panel, true), kTag, "LCD inversion failed");
  ESP_RETURN_ON_ERROR(esp_lcd_panel_disp_on_off(panel, true), kTag, "LCD enable failed");
  lv_display_t* display = nullptr;
  // RAMCTRL 0xB0=00,E8 selects little-endian RGB565, matching LVGL's buffer.
  ESP_RETURN_ON_ERROR(initialize_display(panel, io, false, &display),
                      kTag, "LVGL display initialization failed");
  ESP_LOGI(kTag, "ESP32-S3-LCD-1.47B ST7789 172x320 initialized (no touch)");
  return ESP_OK;
}

void set_brightness(unsigned percent) {
  if (!backlight_ready) return;
  const auto duty = std::min(percent, 100U) * 8191U / 100U;
  esp_err_t result = ledc_set_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0, duty);
  if (result == ESP_OK) result = ledc_update_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0);
  if (result != ESP_OK) ESP_LOGE(kTag, "backlight update failed: %s", esp_err_to_name(result));
}
}
#endif
