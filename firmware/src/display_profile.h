#pragma once

namespace agentping::board {
#if defined(AGENTPING_TARGET_WAVESHARE_ESP32_C6_TOUCH_AMOLED_1_64) == defined(AGENTPING_TARGET_WAVESHARE_ESP32_S3_LCD_1_47B)
#error "Select exactly one AgentPing board target"
#elif defined(AGENTPING_TARGET_WAVESHARE_ESP32_S3_LCD_1_47B)
constexpr int kWidth = 172;
constexpr int kHeight = 320;
#else
constexpr int kWidth = 280;
constexpr int kHeight = 456;
#endif

struct Resolution { int width; int height; };
constexpr Resolution kNative{kWidth, kHeight};
constexpr Resolution kLow{kWidth / 2, kHeight / 2};
constexpr Resolution kMedium = kWidth == 280 ? Resolution{160, 260} : Resolution{100, 186};
constexpr Resolution kHigh = kWidth == 280 ? Resolution{192, 312} : Resolution{120, 224};
constexpr Resolution kMax = kWidth == 280 ? Resolution{208, 338} : Resolution{140, 260};
constexpr int kBubbleMargin = kWidth == 280 ? 16 : 8;
constexpr int kBubblePadding = kWidth == 280 ? 12 : 6;
constexpr int kBubbleHeight = kWidth == 280 ? 164 : 116;
constexpr int kThoughtX = kWidth * 184 / 280;
constexpr int kThoughtY = kBubbleMargin + kBubbleHeight + 8;
}
