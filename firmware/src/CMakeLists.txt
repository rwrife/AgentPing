# This file was automatically generated for projects
# without default 'CMakeLists.txt' file.

FILE(GLOB_RECURSE app_sources ${CMAKE_SOURCE_DIR}/src/*.*)
idf_component_register(SRCS ${app_sources}
  INCLUDE_DIRS "." "../include"
  REQUIRES esp_driver_gpio esp_driver_i2c esp_driver_spi esp_event esp_http_client
           esp_lcd esp_netif esp_timer esp_wifi json nvs_flash esp_websocket_client
           lvgl esp_lcd_co5300 esp_lcd_touch ft3168)
