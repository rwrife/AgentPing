#include "display_profile.h"

#include <cassert>
#include <cstdio>
#include <initializer_list>

using namespace agentping::board;

int main() {
#ifdef AGENTPING_TARGET_WAVESHARE_ESP32_S3_LCD_1_47B
  static_assert(kWidth == 172 && kHeight == 320);
  static_assert(kMedium.width == 100 && kMedium.height == 186);
#else
  static_assert(kWidth == 280 && kHeight == 456);
  static_assert(kLow.width == 140 && kLow.height == 228);
  static_assert(kMedium.width == 160 && kMedium.height == 260);
  static_assert(kHigh.width == 192 && kHigh.height == 312);
  static_assert(kMax.width == 208 && kMax.height == 338);
  static_assert(kBubbleMargin == 16 && kBubbleHeight == 164);
  static_assert(kThoughtX == 184 && kThoughtY == 188);
#endif
  static_assert(kLow.width * 2 == kWidth && kLow.height * 2 == kHeight);
  static_assert(kThoughtX + 10 <= kWidth && kThoughtY + 10 < kHeight / 2);
  static_assert(kBubbleMargin + kBubbleHeight < kHeight / 2);
  for (const auto mode : {kLow, kMedium, kHigh, kMax, kNative}) {
    assert(mode.width > 0 && mode.width <= kWidth);
    assert(mode.height > 0 && mode.height <= kHeight);
    assert(mode.width % 2 == 0 && mode.height % 2 == 0);
    // At most two pixels of aspect-ratio rounding after scaling.
    const int scaled_height = mode.height * kWidth / mode.width;
    assert(scaled_height >= kHeight - 2 && scaled_height <= kHeight + 2);
  }
  std::puts("display_profile: all tests passed");
}
