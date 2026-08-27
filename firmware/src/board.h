#pragma once
#include "esp_err.h"

namespace agentping::board {
constexpr int kWidth = 280;
constexpr int kHeight = 456;
esp_err_t initialize();
void set_brightness(unsigned percent);
}  // namespace agentping::board
