/*
 * SPDX-FileCopyrightText: 2015-2025 Espressif Systems (Shanghai) CO LTD
 * SPDX-License-Identifier: Apache-2.0
 *
 * Modified for AgentPing from the Waveshare FT3168-compatible component at
 * commit b90e28c953c1fc882258fa8dbd56b7706bc888b7.
 */
#pragma once

#include "esp_lcd_touch.h"

#ifdef __cplusplus
extern "C" {
#endif

#define ESP_LCD_TOUCH_IO_I2C_FT3168_ADDRESS 0x38
#define ESP_LCD_TOUCH_IO_I2C_FT3168_CONFIG()             \
  {                                                       \
    .dev_addr = ESP_LCD_TOUCH_IO_I2C_FT3168_ADDRESS,      \
    .on_color_trans_done = NULL,                          \
    .user_ctx = NULL,                                     \
    .control_phase_bytes = 1,                             \
    .dc_bit_offset = 0,                                   \
    .lcd_cmd_bits = 8,                                    \
    .lcd_param_bits = 0,                                  \
    .flags = {.dc_low_on_data = 0, .disable_control_phase = 1}, \
    .scl_speed_hz = 100000,                               \
  }

esp_err_t esp_lcd_touch_new_i2c_ft3168(
    esp_lcd_panel_io_handle_t io,
    const esp_lcd_touch_config_t* config,
    esp_lcd_touch_handle_t* touch);

#ifdef __cplusplus
}
#endif
