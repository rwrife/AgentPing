#include "ui.h"

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "lvgl.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <string>
#include <utility>

namespace agentping::ui {
namespace {

struct Theme {
  std::uint32_t color;
  const char* glyph;
  const char* label;
};

lv_obj_t* icon = nullptr;
lv_obj_t* status = nullptr;
lv_obj_t* detail = nullptr;
lv_obj_t* approve_button = nullptr;
lv_obj_t* approve_label = nullptr;
lv_obj_t* reject_button = nullptr;
lv_obj_t* reject_label = nullptr;
lv_obj_t* reply_area = nullptr;
lv_obj_t* reply_button = nullptr;
lv_obj_t* keyboard = nullptr;
lv_obj_t* screen = nullptr;
SemaphoreHandle_t mutex = nullptr;
PendingAction pending_action;
bool action_pending = false;
bool confirmation_armed = false;
bool destructive_attention = false;
std::string reject_action;
std::string active_attention_id;
unsigned shift_index = 0;
std::int64_t next_shift_us = 0;

Theme theme(UiState state) {
  switch (state) {
    case UiState::idle: return {0x38BDF8, LV_SYMBOL_PAUSE, "IDLE"};
    case UiState::running: return {0xFACC15, LV_SYMBOL_REFRESH, "ACTIVE / RUNNING"};
    case UiState::waiting: return {0xFB923C, LV_SYMBOL_BELL, "WAITING FOR INPUT"};
    case UiState::completed: return {0x4ADE80, LV_SYMBOL_OK, "COMPLETED"};
    case UiState::failed: return {0xF87171, LV_SYMBOL_WARNING, "FAILED / ERROR"};
    case UiState::disconnected: return {0xE2E8F0, LV_SYMBOL_CLOSE, "DISCONNECTED"};
  }
  return {0xE2E8F0, LV_SYMBOL_CLOSE, "DISCONNECTED"};
}

bool lock(TickType_t timeout = portMAX_DELAY) {
  return mutex != nullptr && xSemaphoreTakeRecursive(mutex, timeout) == pdTRUE;
}

void unlock() { xSemaphoreGiveRecursive(mutex); }

void align_content() {
  static constexpr std::array<std::pair<int, int>, 8> kOffsets = {{
      {-2, -2}, {0, -2}, {2, -1}, {2, 1}, {1, 2}, {-1, 2}, {-2, 1}, {-2, 0},
  }};
  const auto [x, y] = kOffsets[shift_index % kOffsets.size()];
  lv_obj_align(icon, LV_ALIGN_TOP_MID, x, 64 + y);
  lv_obj_align(status, LV_ALIGN_TOP_MID, x, 122 + y);
  lv_obj_align(detail, LV_ALIGN_TOP_MID, x, 184 + y);
}

void set_hidden(lv_obj_t* object, bool hidden) {
  if (hidden) lv_obj_add_flag(object, LV_OBJ_FLAG_HIDDEN);
  else lv_obj_remove_flag(object, LV_OBJ_FLAG_HIDDEN);
}

void queue_action(std::string action, std::string text = {}, bool confirmed = false) {
  if (action_pending || active_attention_id.empty()) return;
  pending_action = {std::move(action), std::move(text), confirmed};
  action_pending = true;
  lv_label_set_text(status, "SENDING RESPONSE");
  set_hidden(approve_button, true);
  set_hidden(reject_button, true);
  set_hidden(reply_area, true);
  set_hidden(reply_button, true);
  set_hidden(keyboard, true);
}

void approve_clicked(lv_event_t* event) {
  if (lv_event_get_code(event) != LV_EVENT_CLICKED || action_pending) return;
  if (destructive_attention && !confirmation_armed) {
    confirmation_armed = true;
    lv_label_set_text(approve_label, "CONFIRM");
    return;
  }
  queue_action("approve", {}, confirmation_armed);
}

void reject_clicked(lv_event_t* event) {
  if (lv_event_get_code(event) == LV_EVENT_CLICKED) queue_action(reject_action);
}

void reply_clicked(lv_event_t* event) {
  if (lv_event_get_code(event) != LV_EVENT_CLICKED) return;
  const char* text = lv_textarea_get_text(reply_area);
  if (text != nullptr && text[0] != '\0') queue_action("reply", text);
}

void reply_focused(lv_event_t* event) {
  const auto code = lv_event_get_code(event);
  if (code == LV_EVENT_FOCUSED) set_hidden(keyboard, false);
  else if (code == LV_EVENT_DEFOCUSED) set_hidden(keyboard, true);
}

void keyboard_action(lv_event_t* event) {
  const auto code = lv_event_get_code(event);
  if (code != LV_EVENT_CANCEL && code != LV_EVENT_READY) return;
  if (code == LV_EVENT_CANCEL) lv_textarea_set_text(reply_area, "");
  lv_obj_remove_state(reply_area, LV_STATE_FOCUSED);
  set_hidden(keyboard, true);
}

lv_obj_t* make_button(lv_obj_t* parent, const char* text, int x, int width,
                      lv_event_cb_t callback, lv_obj_t** label_out = nullptr) {
  lv_obj_t* button = lv_button_create(parent);
  lv_obj_set_size(button, width, 44);
  lv_obj_align(button, LV_ALIGN_BOTTOM_LEFT, x, -12);
  lv_obj_add_event_cb(button, callback, LV_EVENT_CLICKED, nullptr);
  lv_obj_t* label = lv_label_create(button);
  lv_label_set_text(label, text);
  lv_obj_center(label);
  if (label_out != nullptr) *label_out = label;
  return button;
}

}  // namespace

void initialize() {
  mutex = xSemaphoreCreateRecursiveMutex();
  if (mutex == nullptr) {
    ESP_LOGE("ui", "could not create LVGL mutex");
    return;
  }
  if (!lock()) return;
  screen = lv_screen_active();
  lv_obj_set_style_bg_color(screen, lv_color_hex(0x05070A), 0);
  lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);
  icon = lv_label_create(screen);
  status = lv_label_create(screen);
  detail = lv_label_create(screen);
  lv_obj_set_width(status, 244);
  lv_obj_set_width(detail, 244);
  lv_obj_set_style_text_align(status, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_set_style_text_align(detail, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_set_style_text_font(icon, &lv_font_montserrat_28, 0);
  lv_obj_set_style_text_font(status, &lv_font_montserrat_20, 0);
  lv_obj_set_style_text_font(detail, &lv_font_montserrat_14, 0);
  lv_label_set_long_mode(status, LV_LABEL_LONG_WRAP);
  lv_label_set_long_mode(detail, LV_LABEL_LONG_WRAP);

  approve_button = make_button(screen, "APPROVE", 8, 82, approve_clicked, &approve_label);
  reject_button = make_button(screen, "DENY", 99, 82, reject_clicked, &reject_label);
  reply_button = make_button(screen, "REPLY", 190, 82, reply_clicked);
  reply_area = lv_textarea_create(screen);
  lv_obj_set_size(reply_area, 264, 44);
  lv_obj_align(reply_area, LV_ALIGN_BOTTOM_MID, 0, -64);
  lv_textarea_set_one_line(reply_area, true);
  lv_textarea_set_max_length(reply_area, protocol_v1::kMaxReplyCharacters);
  lv_textarea_set_placeholder_text(reply_area, "Tap to type a reply");
  lv_obj_add_event_cb(reply_area, reply_focused, LV_EVENT_ALL, nullptr);
  keyboard = lv_keyboard_create(screen);
  lv_obj_set_size(keyboard, 280, 220);
  lv_obj_align(keyboard, LV_ALIGN_BOTTOM_MID, 0, 0);
  lv_keyboard_set_textarea(keyboard, reply_area);
  lv_obj_add_event_cb(keyboard, keyboard_action, LV_EVENT_ALL, nullptr);
  set_hidden(approve_button, true);
  set_hidden(reject_button, true);
  set_hidden(reply_button, true);
  set_hidden(reply_area, true);
  set_hidden(keyboard, true);

  align_content();
  next_shift_us = esp_timer_get_time() + 60LL * 1000LL * 1000LL;
  unlock();
}

void render(const ViewModel& model) {
  if (!lock()) {
    ESP_LOGE("ui", "state render failed because the LVGL mutex is unavailable");
    return;
  }
  const Theme selected = theme(model.state);
  const bool new_attention = model.attention_id != active_attention_id;
  if (new_attention || model.state != UiState::waiting) {
    active_attention_id = model.attention_id;
    action_pending = false;
    confirmation_armed = false;
    pending_action = {};
    lv_label_set_text(approve_label, "APPROVE");
    lv_textarea_set_text(reply_area, "");
  }
  destructive_attention = model.destructive;
  reject_action.clear();
  const char* reject_text = "DENY";
  if (model.allow_deny) reject_action = "deny";
  else if (model.allow_cancel) {
    reject_action = "cancel";
    reject_text = "CANCEL";
  } else if (model.allow_acknowledge) {
    reject_action = "acknowledge";
    reject_text = "ACK";
  }
  lv_label_set_text(reject_label, reject_text);
  const bool actionable = model.state == UiState::waiting && !model.attention_id.empty() && !action_pending;
  set_hidden(approve_button, !actionable || !model.allow_approve);
  set_hidden(reject_button, !actionable || reject_action.empty());
  set_hidden(reply_area, !actionable || !model.allow_reply);
  set_hidden(reply_button, !actionable || !model.allow_reply);
  if (!actionable || !model.allow_reply) set_hidden(keyboard, true);

  std::string body;
  if (model.state == UiState::waiting) {
    body = action_context(model) + "\n\n";
  }
  body += model.title + "\n\n" + model.detail;
  lv_label_set_text(icon, selected.glyph);
  lv_label_set_text(status, selected.label);
  lv_label_set_text(detail, body.c_str());
  for (lv_obj_t* object : {icon, status, detail}) {
    lv_obj_set_style_text_color(object, lv_color_hex(selected.color), 0);
  }
  align_content();
  unlock();
}

bool take_action(PendingAction& output) {
  if (!lock(pdMS_TO_TICKS(25))) return false;
  const bool available = action_pending && !pending_action.action.empty();
  if (available) {
    output = pending_action;
    pending_action = {};
  }
  unlock();
  return available;
}

void task() {
  if (!lock()) {
    vTaskDelay(pdMS_TO_TICKS(10));
    return;
  }
  if (esp_timer_get_time() >= next_shift_us) {
    ++shift_index;
    align_content();
    lv_obj_invalidate(screen);
    next_shift_us = esp_timer_get_time() + 60LL * 1000LL * 1000LL;
  }
  const std::uint32_t delay_ms = lv_timer_handler();
  unlock();
  vTaskDelay(pdMS_TO_TICKS(std::max<std::uint32_t>(5, std::min<std::uint32_t>(delay_ms, 50))));
}

}  // namespace agentping::ui
