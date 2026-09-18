#pragma once
#include "display_profile.h"
#include "esp_err.h"

namespace agentping::board {
esp_err_t initialize();
void set_brightness(unsigned percent);
}  // namespace agentping::board
