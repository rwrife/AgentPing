/*
 * SPDX-FileCopyrightText: 2015-2025 Espressif Systems (Shanghai) CO LTD
 *
 * SPDX-License-Identifier: Apache-2.0
 *
 * Modified for AgentPing: reduced to the one-point FT6146/FT3168-compatible
 * path used by the Waveshare ESP32-C6-Touch-AMOLED-1.64. Upstream evidence:
 * waveshareteam/ESP32-C6-Touch-AMOLED-1.64 commit
 * b90e28c953c1fc882258fa8dbd56b7706bc888b7.
 */

#include "esp_lcd_touch_ft3168.h"

#include "driver/gpio.h"
#include "esp_check.h"

#include <stdlib.h>
#include <string.h>

#define POINT_COUNT_REGISTER 0x02
#define MAX_POINTS 1

static const char* TAG = "FT3168";

static esp_err_t read_data(esp_lcd_touch_handle_t touch) {
  ESP_RETURN_ON_FALSE(touch != NULL, ESP_ERR_INVALID_ARG, TAG, "touch handle is null");
  uint8_t data[5] = {};
  ESP_RETURN_ON_ERROR(
      esp_lcd_panel_io_rx_param(touch->io, POINT_COUNT_REGISTER, data, sizeof(data)),
      TAG, "I2C read failed");

  portENTER_CRITICAL(&touch->data.lock);
  touch->data.points = data[0] > 0 ? 1 : 0;
  if (touch->data.points != 0) {
    touch->data.coords[0].track_id = 0;
    touch->data.coords[0].x = (uint16_t)(((data[1] & 0x0fU) << 8U) | data[2]);
    touch->data.coords[0].y = (uint16_t)(((data[3] & 0x0fU) << 8U) | data[4]);
    touch->data.coords[0].strength = 0;
  }
  portEXIT_CRITICAL(&touch->data.lock);
  return ESP_OK;
}

static bool get_xy(esp_lcd_touch_handle_t touch, uint16_t* x, uint16_t* y,
                   uint16_t* strength, uint8_t* count, uint8_t maximum) {
  if (touch == NULL || x == NULL || y == NULL || count == NULL || maximum == 0) return false;
  portENTER_CRITICAL(&touch->data.lock);
  *count = touch->data.points > 0 ? 1 : 0;
  if (*count != 0) {
    *x = touch->data.coords[0].x;
    *y = touch->data.coords[0].y;
    if (strength != NULL) *strength = touch->data.coords[0].strength;
  }
  touch->data.points = 0;
  portEXIT_CRITICAL(&touch->data.lock);
  return *count != 0;
}

static esp_err_t delete_touch(esp_lcd_touch_handle_t touch) {
  ESP_RETURN_ON_FALSE(touch != NULL, ESP_ERR_INVALID_ARG, TAG, "touch handle is null");
  if (touch->config.int_gpio_num != GPIO_NUM_NC) {
    if (touch->config.interrupt_callback != NULL) {
      gpio_isr_handler_remove(touch->config.int_gpio_num);
    }
    gpio_reset_pin(touch->config.int_gpio_num);
  }
  free(touch);
  return ESP_OK;
}

esp_err_t esp_lcd_touch_new_i2c_ft3168(esp_lcd_panel_io_handle_t io,
                                       const esp_lcd_touch_config_t* config,
                                       esp_lcd_touch_handle_t* output) {
  ESP_RETURN_ON_FALSE(io != NULL && config != NULL && output != NULL,
                      ESP_ERR_INVALID_ARG, TAG, "invalid argument");
  esp_lcd_touch_handle_t touch = calloc(1, sizeof(esp_lcd_touch_t));
  ESP_RETURN_ON_FALSE(touch != NULL, ESP_ERR_NO_MEM, TAG, "no memory");
  touch->io = io;
  touch->read_data = read_data;
  touch->get_xy = get_xy;
  touch->del = delete_touch;
  touch->data.lock.owner = portMUX_FREE_VAL;
  memcpy(&touch->config, config, sizeof(*config));

  if (config->int_gpio_num != GPIO_NUM_NC) {
    gpio_config_t interrupt = {};
    interrupt.pin_bit_mask = BIT64(config->int_gpio_num);
    interrupt.mode = GPIO_MODE_INPUT;
    interrupt.intr_type = config->levels.interrupt ? GPIO_INTR_POSEDGE : GPIO_INTR_NEGEDGE;
    esp_err_t result = gpio_config(&interrupt);
    if (result != ESP_OK) {
      free(touch);
      return result;
    }
    if (config->interrupt_callback != NULL) {
      result = esp_lcd_touch_register_interrupt_callback(touch, config->interrupt_callback);
      if (result != ESP_OK) {
        gpio_reset_pin(config->int_gpio_num);
        free(touch);
        return result;
      }
    }
  }

  uint8_t mode = 0;
  const esp_err_t probe = esp_lcd_panel_io_rx_param(io, 0x00, &mode, 1);
  if (probe != ESP_OK) {
    if (config->int_gpio_num != GPIO_NUM_NC) gpio_reset_pin(config->int_gpio_num);
    free(touch);
    return probe;
  }
  *output = touch;
  return ESP_OK;
}
