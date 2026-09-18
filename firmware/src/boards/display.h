#pragma once

#include "esp_lcd_panel_io.h"
#include "esp_lcd_panel_ops.h"
#include "lvgl.h"

namespace agentping::board {
esp_err_t initialize_display(esp_lcd_panel_handle_t panel,
                             esp_lcd_panel_io_handle_t io, bool swap_bytes,
                             lv_display_t** display);
}
