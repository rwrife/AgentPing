#include "protocol_core.h"

#include "agentping_protocol_v1.h"

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <limits>
#include <map>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace agentping {
namespace {

constexpr std::uint64_t kMaxSafeInteger = 9007199254740991ULL;
constexpr unsigned kMaxJsonDepth = 16;

bool valid_utf8(std::string_view input) {
  std::size_t index = 0;
  auto continuation = [&input](std::size_t at) {
    return at < input.size()
        && (static_cast<unsigned char>(input[at]) & 0xc0U) == 0x80U;
  };
  while (index < input.size()) {
    const unsigned char first = static_cast<unsigned char>(input[index]);
    if (first <= 0x7fU) {
      ++index;
      continue;
    }
    if (first >= 0xc2U && first <= 0xdfU) {
      if (!continuation(index + 1)) return false;
      index += 2;
      continue;
    }
    if (first >= 0xe0U && first <= 0xefU) {
      if (index + 2 >= input.size() || !continuation(index + 1)
          || !continuation(index + 2)) return false;
      const unsigned char second = static_cast<unsigned char>(input[index + 1]);
      if ((first == 0xe0U && second < 0xa0U) || (first == 0xedU && second > 0x9fU)) {
        return false;
      }
      index += 3;
      continue;
    }
    if (first >= 0xf0U && first <= 0xf4U) {
      if (index + 3 >= input.size() || !continuation(index + 1)
          || !continuation(index + 2) || !continuation(index + 3)) return false;
      const unsigned char second = static_cast<unsigned char>(input[index + 1]);
      if ((first == 0xf0U && second < 0x90U) || (first == 0xf4U && second > 0x8fU)) {
        return false;
      }
      index += 4;
      continue;
    }
    return false;
  }
  return true;
}

struct JsonValue {
  enum class Type { null_value, boolean, number, string, object, array };
  Type type = Type::null_value;
  bool boolean = false;
  std::uint64_t number = 0;
  std::string string;
  std::map<std::string, JsonValue> object;
  std::vector<JsonValue> array;
};

class JsonParser {
 public:
  explicit JsonParser(std::string_view input) : input_(input) {}

  bool parse(JsonValue& output) {
    return parse_value(output, 0) && whitespace() && position_ == input_.size();
  }

 private:
  bool whitespace() {
    while (position_ < input_.size()
           && (input_[position_] == ' ' || input_[position_] == '\t'
               || input_[position_] == '\r' || input_[position_] == '\n')) {
      ++position_;
    }
    return true;
  }

  bool consume(char expected) {
    whitespace();
    if (position_ >= input_.size() || input_[position_] != expected) {
      return false;
    }
    ++position_;
    return true;
  }

  bool literal(std::string_view expected) {
    whitespace();
    if (input_.substr(position_, expected.size()) != expected) {
      return false;
    }
    position_ += expected.size();
    return true;
  }

  static int hex_value(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
  }

  bool unicode_escape(std::uint32_t& codepoint) {
    if (position_ + 4 > input_.size()) return false;
    codepoint = 0;
    for (unsigned index = 0; index < 4; ++index) {
      const int digit = hex_value(input_[position_++]);
      if (digit < 0) return false;
      codepoint = (codepoint << 4U) | static_cast<std::uint32_t>(digit);
    }
    return true;
  }

  static void append_utf8(std::string& output, std::uint32_t codepoint) {
    if (codepoint <= 0x7f) {
      output.push_back(static_cast<char>(codepoint));
    } else if (codepoint <= 0x7ff) {
      output.push_back(static_cast<char>(0xc0U | (codepoint >> 6U)));
      output.push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
    } else if (codepoint <= 0xffff) {
      output.push_back(static_cast<char>(0xe0U | (codepoint >> 12U)));
      output.push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3fU)));
      output.push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
    } else {
      output.push_back(static_cast<char>(0xf0U | (codepoint >> 18U)));
      output.push_back(static_cast<char>(0x80U | ((codepoint >> 12U) & 0x3fU)));
      output.push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3fU)));
      output.push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
    }
  }

  bool parse_string(std::string& output) {
    if (!consume('"')) return false;
    output.clear();
    while (position_ < input_.size()) {
      const unsigned char byte = static_cast<unsigned char>(input_[position_++]);
      if (byte == '"') return true;
      if (byte < 0x20) return false;
      if (byte != '\\') {
        output.push_back(static_cast<char>(byte));
        continue;
      }
      if (position_ >= input_.size()) return false;
      const char escape = input_[position_++];
      switch (escape) {
        case '"': output.push_back('"'); break;
        case '\\': output.push_back('\\'); break;
        case '/': output.push_back('/'); break;
        case 'b': output.push_back('\b'); break;
        case 'f': output.push_back('\f'); break;
        case 'n': output.push_back('\n'); break;
        case 'r': output.push_back('\r'); break;
        case 't': output.push_back('\t'); break;
        case 'u': {
          std::uint32_t first = 0;
          if (!unicode_escape(first)) return false;
          std::uint32_t codepoint = first;
          if (first >= 0xd800 && first <= 0xdbff) {
            if (position_ + 2 > input_.size() || input_[position_] != '\\'
                || input_[position_ + 1] != 'u') {
              return false;
            }
            position_ += 2;
            std::uint32_t second = 0;
            if (!unicode_escape(second) || second < 0xdc00 || second > 0xdfff) return false;
            codepoint = 0x10000U + ((first - 0xd800U) << 10U) + (second - 0xdc00U);
          } else if (first >= 0xdc00 && first <= 0xdfff) {
            return false;
          }
          append_utf8(output, codepoint);
          break;
        }
        default: return false;
      }
    }
    return false;
  }

  bool parse_number(std::uint64_t& output) {
    whitespace();
    if (position_ >= input_.size() || !std::isdigit(static_cast<unsigned char>(input_[position_]))) {
      return false;
    }
    if (input_[position_] == '0' && position_ + 1 < input_.size()
        && std::isdigit(static_cast<unsigned char>(input_[position_ + 1]))) {
      return false;
    }
    output = 0;
    while (position_ < input_.size()
           && std::isdigit(static_cast<unsigned char>(input_[position_]))) {
      const unsigned digit = static_cast<unsigned>(input_[position_] - '0');
      if (output > (std::numeric_limits<std::uint64_t>::max() - digit) / 10U) return false;
      output = output * 10U + digit;
      ++position_;
    }
    return true;
  }

  bool parse_object(JsonValue& output, unsigned depth) {
    if (!consume('{')) return false;
    output.type = JsonValue::Type::object;
    whitespace();
    if (position_ < input_.size() && input_[position_] == '}') {
      ++position_;
      return true;
    }
    while (true) {
      std::string key;
      if (!parse_string(key) || !consume(':')) return false;
      JsonValue child;
      if (!parse_value(child, depth + 1)) return false;
      if (!output.object.emplace(std::move(key), std::move(child)).second) return false;
      whitespace();
      if (position_ < input_.size() && input_[position_] == '}') {
        ++position_;
        return true;
      }
      if (!consume(',')) return false;
    }
  }

  bool parse_array(JsonValue& output, unsigned depth) {
    if (!consume('[')) return false;
    output.type = JsonValue::Type::array;
    whitespace();
    if (position_ < input_.size() && input_[position_] == ']') {
      ++position_;
      return true;
    }
    while (true) {
      JsonValue child;
      if (!parse_value(child, depth + 1)) return false;
      output.array.push_back(std::move(child));
      whitespace();
      if (position_ < input_.size() && input_[position_] == ']') {
        ++position_;
        return true;
      }
      if (!consume(',')) return false;
    }
  }

  bool parse_value(JsonValue& output, unsigned depth) {
    if (depth > kMaxJsonDepth) return false;
    whitespace();
    if (position_ >= input_.size()) return false;
    const char next = input_[position_];
    if (next == '{') return parse_object(output, depth);
    if (next == '[') return parse_array(output, depth);
    if (next == '"') {
      output.type = JsonValue::Type::string;
      return parse_string(output.string);
    }
    if (std::isdigit(static_cast<unsigned char>(next))) {
      output.type = JsonValue::Type::number;
      return parse_number(output.number);
    }
    if (literal("true")) {
      output.type = JsonValue::Type::boolean;
      output.boolean = true;
      return true;
    }
    if (literal("false")) {
      output.type = JsonValue::Type::boolean;
      output.boolean = false;
      return true;
    }
    if (literal("null")) {
      output.type = JsonValue::Type::null_value;
      return true;
    }
    return false;
  }

  std::string_view input_;
  std::size_t position_ = 0;
};

using Object = std::map<std::string, JsonValue>;

bool only_fields(const Object& object, std::initializer_list<const char*> allowed) {
  std::set<std::string> names;
  for (const char* name : allowed) names.emplace(name);
  for (const auto& entry : object) {
    if (names.count(entry.first) == 0) return false;
  }
  return true;
}

const JsonValue* field(const Object& object, const char* name, JsonValue::Type type) {
  const auto item = object.find(name);
  if (item == object.end() || item->second.type != type) return nullptr;
  return &item->second;
}

std::size_t utf8_code_points(std::string_view value) {
  return static_cast<std::size_t>(std::count_if(
      value.begin(), value.end(), [](unsigned char byte) { return (byte & 0xc0U) != 0x80U; }));
}

bool string_field(const Object& object, const char* name, std::string& output,
                  std::size_t minimum, std::size_t maximum) {
  const JsonValue* value = field(object, name, JsonValue::Type::string);
  if (value == nullptr) return false;
  const std::size_t length = utf8_code_points(value->string);
  if (length < minimum || length > maximum) return false;
  output = value->string;
  return true;
}

bool number_field(const Object& object, const char* name, std::uint64_t& output,
                  std::uint64_t maximum = kMaxSafeInteger) {
  const JsonValue* value = field(object, name, JsonValue::Type::number);
  if (value == nullptr || value->number > maximum) return false;
  output = value->number;
  return true;
}

bool boolean_field(const Object& object, const char* name, bool& output) {
  const JsonValue* value = field(object, name, JsonValue::Type::boolean);
  if (value == nullptr) return false;
  output = value->boolean;
  return true;
}

bool is_identifier(std::string_view value, std::size_t maximum = 128) {
  if (value.empty() || value.size() > maximum || !std::isalnum(static_cast<unsigned char>(value.front()))) {
    return false;
  }
  for (const char character : value) {
    if (!std::isalnum(static_cast<unsigned char>(character)) && character != '.' && character != '_'
        && character != ':' && character != '-') {
      return false;
    }
  }
  return true;
}

bool is_provider(std::string_view value) {
  if (value.empty() || value.size() > 32 || value.front() < 'a' || value.front() > 'z') return false;
  for (const char character : value) {
    if ((character < 'a' || character > 'z') && !std::isdigit(static_cast<unsigned char>(character))
        && character != '_' && character != '-') {
      return false;
    }
  }
  return true;
}

bool is_uuid(std::string_view value) {
  if (value.size() != 36 || value[8] != '-' || value[13] != '-' || value[18] != '-'
      || value[23] != '-') {
    return false;
  }
  for (std::size_t index = 0; index < value.size(); ++index) {
    if (index == 8 || index == 13 || index == 18 || index == 23) continue;
    if (!std::isxdigit(static_cast<unsigned char>(value[index]))) return false;
  }
  const char version = static_cast<char>(std::tolower(static_cast<unsigned char>(value[14])));
  const char variant = static_cast<char>(std::tolower(static_cast<unsigned char>(value[19])));
  return version >= '1' && version <= '8' && (variant == '8' || variant == '9' || variant == 'a' || variant == 'b');
}

bool decimal_at(std::string_view value, std::size_t offset, std::size_t count,
                unsigned& output) {
  output = 0;
  if (offset + count > value.size()) return false;
  for (std::size_t index = offset; index < offset + count; ++index) {
    if (!std::isdigit(static_cast<unsigned char>(value[index]))) return false;
    output = output * 10U + static_cast<unsigned>(value[index] - '0');
  }
  return true;
}

bool is_utc_timestamp(std::string_view value) {
  if (value.size() < 20 || value.size() > 40 || value[4] != '-' || value[7] != '-'
      || value[10] != 'T' || value[13] != ':' || value[16] != ':') {
    return false;
  }
  std::size_t timestamp_end = 0;
  if (value.back() == 'Z') timestamp_end = value.size() - 1;
  else if (value.size() >= 6 && value.substr(value.size() - 6) == "+00:00") {
    timestamp_end = value.size() - 6;
  } else {
    return false;
  }
  if (timestamp_end < 19 || (timestamp_end > 19 && value[19] != '.')) return false;
  for (std::size_t index = 20; index < timestamp_end; ++index) {
    if (!std::isdigit(static_cast<unsigned char>(value[index]))) return false;
  }
  if (timestamp_end == 20) return false;

  unsigned year = 0;
  unsigned month = 0;
  unsigned day = 0;
  unsigned hour = 0;
  unsigned minute = 0;
  unsigned second = 0;
  if (!decimal_at(value, 0, 4, year) || !decimal_at(value, 5, 2, month)
      || !decimal_at(value, 8, 2, day) || !decimal_at(value, 11, 2, hour)
      || !decimal_at(value, 14, 2, minute) || !decimal_at(value, 17, 2, second)
      || year == 0 || month == 0 || month > 12 || hour > 23 || minute > 59
      || second > 59) {
    return false;
  }
  static constexpr unsigned days_in_month[] = {
      0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
  unsigned maximum_day = days_in_month[month];
  if (month == 2 && (year % 400 == 0 || (year % 4 == 0 && year % 100 != 0))) {
    maximum_day = 29;
  }
  return day >= 1 && day <= maximum_day;
}

bool timestamp_epoch(std::string_view value, std::int64_t& output) {
  if (!is_utc_timestamp(value)) return false;
  unsigned year = 0;
  unsigned month = 0;
  unsigned day = 0;
  unsigned hour = 0;
  unsigned minute = 0;
  unsigned second = 0;
  if (!decimal_at(value, 0, 4, year) || !decimal_at(value, 5, 2, month)
      || !decimal_at(value, 8, 2, day) || !decimal_at(value, 11, 2, hour)
      || !decimal_at(value, 14, 2, minute) || !decimal_at(value, 17, 2, second)) {
    return false;
  }
  const int adjusted_year = static_cast<int>(year) - (month <= 2 ? 1 : 0);
  const int era = (adjusted_year >= 0 ? adjusted_year : adjusted_year - 399) / 400;
  const unsigned year_of_era = static_cast<unsigned>(adjusted_year - era * 400);
  const unsigned shifted_month = month > 2 ? month - 3 : month + 9;
  const unsigned day_of_year = (153 * shifted_month + 2) / 5 + day - 1;
  const unsigned day_of_era = year_of_era * 365 + year_of_era / 4
      - year_of_era / 100 + day_of_year;
  const std::int64_t days = static_cast<std::int64_t>(era) * 146097
      + static_cast<std::int64_t>(day_of_era) - 719468;
  output = days * 86400 + static_cast<std::int64_t>(hour) * 3600
      + static_cast<std::int64_t>(minute) * 60 + second;
  return true;
}

bool string_array(const Object& object, const char* name, std::size_t minimum,
                  std::size_t maximum, const std::set<std::string>& allowed,
                  bool require_protocol_version = false) {
  const JsonValue* value = field(object, name, JsonValue::Type::array);
  if (value == nullptr || value->array.size() < minimum || value->array.size() > maximum) return false;
  std::set<std::string> seen;
  for (const JsonValue& item : value->array) {
    if (item.type != JsonValue::Type::string || allowed.count(item.string) == 0
        || !seen.insert(item.string).second) {
      return false;
    }
  }
  return !require_protocol_version || seen.count(protocol_v1::kVersion) != 0;
}

bool validate_capability(const Object& payload, Envelope& output) {
  if (!only_fields(payload, {"deviceId", "role", "supportedVersions", "features",
                             "maxMessageBytes", "resumeFromSequence", "resetState",
                             "snapshotItemCount", "snapshotCheckpoint", "softwareVersion"})) {
    return false;
  }
  std::string device_id;
  std::string role;
  std::uint64_t max_message_bytes = 0;
  std::uint64_t resume_from = 0;
  if (!string_field(payload, "deviceId", device_id, 1, 128) || !is_identifier(device_id)
      || !string_field(payload, "role", role, 1, 16) || role != "bridge"
      || !string_array(payload, "supportedVersions", 1, 8, {protocol_v1::kVersion}, true)
      || !string_array(payload, "features", 0, 16,
                       {"events", "sessions", "attention", "approve", "deny", "reply", "resume"})
      || !number_field(payload, "maxMessageBytes", max_message_bytes)
      || max_message_bytes != protocol_v1::kMaxMessageBytes
      || !number_field(payload, "resumeFromSequence", resume_from)) {
    return false;
  }
  output.capability.resume_from_sequence = resume_from;
  const auto reset = payload.find("resetState");
  if (reset != payload.end()) {
    if (reset->second.type != JsonValue::Type::boolean) return false;
    output.capability.reset_state = reset->second.boolean;
  }
  const auto count = payload.find("snapshotItemCount");
  if (count != payload.end()) {
    if (count->second.type != JsonValue::Type::number || count->second.number > kMaxSafeInteger) return false;
    output.capability.has_snapshot_item_count = true;
    output.capability.snapshot_item_count = count->second.number;
  }
  const auto checkpoint = payload.find("snapshotCheckpoint");
  if (checkpoint != payload.end()) {
    if (checkpoint->second.type != JsonValue::Type::number || checkpoint->second.number > kMaxSafeInteger) return false;
    output.capability.has_snapshot_checkpoint = true;
    output.capability.snapshot_checkpoint = checkpoint->second.number;
  }
  const auto software = payload.find("softwareVersion");
  if (software != payload.end()
      && (software->second.type != JsonValue::Type::string || software->second.string.empty()
          || utf8_code_points(software->second.string) > 64)) {
    return false;
  }
  if (output.capability.reset_state) {
    return output.capability.has_snapshot_item_count && output.capability.has_snapshot_checkpoint;
  }
  return !output.capability.has_snapshot_item_count && !output.capability.has_snapshot_checkpoint;
}

bool validate_session(const Object& payload, Envelope& output) {
  if (!only_fields(payload, {"sessionId", "provider", "state", "displayName", "updatedAt",
                             "revision", "unreadCount"})) {
    return false;
  }
  std::string updated_at;
  std::uint64_t unread = 0;
  if (!string_field(payload, "sessionId", output.session_id, 1, 128) || !is_identifier(output.session_id)
      || !string_field(payload, "provider", output.provider, 1, 32) || !is_provider(output.provider)
      || !string_field(payload, "state", output.state, 1, 32)
      || std::set<std::string>{"idle", "running", "waiting_for_input", "completed", "failed"}.count(output.state) == 0
      || !string_field(payload, "displayName", output.title, 1, 120)
      || !string_field(payload, "updatedAt", updated_at, 20, 40) || !is_utc_timestamp(updated_at)
      || !number_field(payload, "revision", output.revision)
      || !number_field(payload, "unreadCount", unread, 999)) {
    return false;
  }
  output.detail = output.provider + " · " + output.state;
  return true;
}

bool validate_attention(const Object& payload, Envelope& output) {
  if (!only_fields(payload, {"attentionId", "sessionId", "revision", "category", "title",
                             "body", "responseDeadlineAt", "destructive", "allowedActions"})) {
    return false;
  }
  std::string category;
  if (!string_field(payload, "attentionId", output.attention_id, 1, 128)
      || !is_identifier(output.attention_id)
      || !string_field(payload, "sessionId", output.session_id, 1, 128)
      || !is_identifier(output.session_id)
      || !number_field(payload, "revision", output.revision)
      || !string_field(payload, "category", category, 1, 16)
      || std::set<std::string>{"approval", "reply", "notification"}.count(category) == 0
      || !string_field(payload, "title", output.title, 1, 120)
      || !string_field(payload, "body", output.detail, 1, 1024)
      || !string_field(payload, "responseDeadlineAt", output.response_deadline_at, 20, 40)
      || !timestamp_epoch(output.response_deadline_at, output.response_deadline_epoch)
      || !boolean_field(payload, "destructive", output.destructive)
      || !string_array(payload, "allowedActions", 1, 5,
                       {"approve", "deny", "reply", "cancel", "acknowledge"})) {
    return false;
  }
  const auto actions = payload.find("allowedActions");
  if (actions == payload.end() || actions->second.type != JsonValue::Type::array) return false;
  for (const auto& action : actions->second.array) {
    output.allow_approve = output.allow_approve || action.string == "approve";
    output.allow_deny = output.allow_deny || action.string == "deny";
    output.allow_reply = output.allow_reply || action.string == "reply";
    output.allow_cancel = output.allow_cancel || action.string == "cancel";
    output.allow_acknowledge = output.allow_acknowledge || action.string == "acknowledge";
  }
  output.state = "waiting_for_input";
  return !output.destructive || output.allow_deny;
}

bool validate_action_echo(const Object& payload, Envelope& output, std::string_view type) {
  std::string action_id;
  if (type == "approval") {
    std::string presented_message_id;
    std::string prompt_digest;
    std::string confirmed_at;
    bool destructive = false;
    if (!only_fields(payload, {"actionId", "attentionId", "expectedRevision", "presentedMessageId",
                               "destructive", "promptDigest", "confirmedAt"})
        || !string_field(payload, "actionId", action_id, 36, 36) || !is_uuid(action_id)
        || !string_field(payload, "attentionId", output.attention_id, 1, 128)
        || !is_identifier(output.attention_id)
        || !number_field(payload, "expectedRevision", output.revision)
        || !string_field(payload, "presentedMessageId", presented_message_id, 36, 36)
        || !is_uuid(presented_message_id)
        || !boolean_field(payload, "destructive", destructive)
        || !string_field(payload, "promptDigest", prompt_digest, 64, 64)
        || !string_field(payload, "confirmedAt", confirmed_at, 20, 40)
        || !is_utc_timestamp(confirmed_at)) {
      return false;
    }
    for (const unsigned char value : prompt_digest) {
      if (!std::isdigit(value) && (value < 'a' || value > 'f')) return false;
    }
    output.title = "Approval recorded";
  } else if (type == "denial") {
    std::string reason;
    if (!only_fields(payload, {"actionId", "attentionId", "expectedRevision", "reason", "note"})
        || !string_field(payload, "actionId", action_id, 36, 36) || !is_uuid(action_id)
        || !string_field(payload, "attentionId", output.attention_id, 1, 128)
        || !is_identifier(output.attention_id)
        || !number_field(payload, "expectedRevision", output.revision)
        || !string_field(payload, "reason", reason, 1, 32)
        || std::set<std::string>{"user_denied", "user_cancelled", "acknowledged", "expired", "stale", "policy_blocked"}.count(reason) == 0) {
      return false;
    }
    const auto note = payload.find("note");
    if (note != payload.end()
        && (note->second.type != JsonValue::Type::string || !valid_utf8(note->second.string)
            || utf8_code_points(note->second.string) > 256)) {
      return false;
    }
    output.title = reason == "acknowledged" ? "Acknowledged" : "Request declined";
  } else {
    std::string text;
    if (!only_fields(payload, {"actionId", "attentionId", "expectedRevision", "text"})
        || !string_field(payload, "actionId", action_id, 36, 36) || !is_uuid(action_id)
        || !string_field(payload, "attentionId", output.attention_id, 1, 128)
        || !is_identifier(output.attention_id)
        || !number_field(payload, "expectedRevision", output.revision)
        || !string_field(payload, "text", text, 1, protocol_v1::kMaxReplyCharacters)) {
      return false;
    }
    output.title = "Reply recorded";
  }
  output.state = "completed";
  output.detail = "Bridge committed the response";
  return true;
}

bool validate_error(const Object& payload, Envelope& output) {
  if (!only_fields(payload, {"code", "message", "retryable", "relatedMessageId"})) return false;
  std::string code;
  bool retryable = false;
  if (!string_field(payload, "code", code, 2, 64)
      || !std::isupper(static_cast<unsigned char>(code.front()))
      || !string_field(payload, "message", output.detail, 1, 256)
      || !boolean_field(payload, "retryable", retryable)) {
    return false;
  }
  for (const char character : code) {
    if (!std::isupper(static_cast<unsigned char>(character))
        && !std::isdigit(static_cast<unsigned char>(character)) && character != '_') {
      return false;
    }
  }
  const auto related = payload.find("relatedMessageId");
  if (related != payload.end()
      && (related->second.type != JsonValue::Type::string || !is_uuid(related->second.string))) {
    return false;
  }
  output.state = "failed";
  output.title = "Error · " + code;
  return true;
}

bool valid_metadata_key(std::string_view key) {
  if (key.empty() || key.size() > 64 || key.front() < 'a' || key.front() > 'z') {
    return false;
  }
  auto separator = [](char value) { return value == '.' || value == '_' || value == '-'; };
  for (const char character : key) {
    if ((character < 'a' || character > 'z')
        && !std::isdigit(static_cast<unsigned char>(character)) && !separator(character)) {
      return false;
    }
  }
  static constexpr std::string_view forbidden[] = {
      "token", "secret", "password", "credential", "authorization", "cookie",
      "apikey", "api_key", "api-key"};
  for (std::size_t start = 0; start < key.size(); ++start) {
    if (start > 0 && !separator(key[start - 1])) continue;
    for (const std::string_view word : forbidden) {
      const std::size_t end = start + word.size();
      if (end <= key.size() && key.substr(start, word.size()) == word
          && (end == key.size() || separator(key[end]))) {
        return false;
      }
    }
  }
  return true;
}

bool validate_event(const Object& payload) {
  if (!only_fields(payload, {"eventId", "sessionId", "provider", "eventKind", "summary",
                             "detail", "severity", "metadata"})) {
    return false;
  }
  std::string event_id;
  std::string session_id;
  std::string provider;
  std::string kind;
  std::string summary;
  std::string severity;
  if (!string_field(payload, "eventId", event_id, 1, 128) || !is_identifier(event_id)
      || !string_field(payload, "sessionId", session_id, 1, 128) || !is_identifier(session_id)
      || !string_field(payload, "provider", provider, 1, 32) || !is_provider(provider)
      || !string_field(payload, "eventKind", kind, 1, 16)
      || std::set<std::string>{"started", "progress", "completed", "failed", "message"}.count(kind) == 0
      || !string_field(payload, "summary", summary, 1, 512)
      || !string_field(payload, "severity", severity, 1, 16)
      || std::set<std::string>{"info", "success", "warning", "error"}.count(severity) == 0) {
    return false;
  }
  const auto detail = payload.find("detail");
  if (detail != payload.end()
      && (detail->second.type != JsonValue::Type::string
          || utf8_code_points(detail->second.string) > 2048)) {
    return false;
  }
  const auto metadata = payload.find("metadata");
  if (metadata != payload.end()) {
    if (metadata->second.type != JsonValue::Type::object || metadata->second.object.size() > 16) return false;
    for (const auto& item : metadata->second.object) {
      if (!valid_metadata_key(item.first) || item.second.type != JsonValue::Type::string
          || utf8_code_points(item.second.string) > 256) {
        return false;
      }
    }
  }
  return true;
}

bool validate_heartbeat(const Object& payload) {
  if (!only_fields(payload, {"uptimeMs", "status", "lastReceivedSequence", "queueDepth"})) return false;
  std::uint64_t uptime = 0;
  std::uint64_t last = 0;
  std::uint64_t depth = 0;
  std::string status;
  return number_field(payload, "uptimeMs", uptime)
      && string_field(payload, "status", status, 1, 16)
      && std::set<std::string>{"ready", "busy", "degraded"}.count(status) != 0
      && number_field(payload, "lastReceivedSequence", last)
      && number_field(payload, "queueDepth", depth, protocol_v1::kMaxReplayWindowMessages);
}

UiState ui_state(std::string_view state) {
  if (state == "idle") return UiState::idle;
  if (state == "running") return UiState::running;
  if (state == "waiting_for_input") return UiState::waiting;
  if (state == "completed") return UiState::completed;
  return UiState::failed;
}

}  // namespace

FrameAssemblyStatus TextFrameAssembler::push(
    std::uint8_t opcode, bool fin, std::size_t payload_length,
    std::size_t payload_offset, std::string_view chunk,
    std::string_view& complete_message) {
  complete_message = {};
  if (payload_offset == 0) {
    frame_used_ = 0;
    frame_expected_ = payload_length;
    frame_opcode_ = opcode;
    if (opcode == 0x1) {
      if (message_active_) {
        reset();
        return FrameAssemblyStatus::rejected;
      }
      message_active_ = true;
      message_used_ = 0;
    } else if (opcode != 0x0 || !message_active_) {
      reset();
      return FrameAssemblyStatus::rejected;
    }
    if (message_used_ > data_.size() || payload_length > data_.size() - message_used_) {
      reset();
      return FrameAssemblyStatus::rejected;
    }
  }
  if (!message_active_ || opcode != frame_opcode_ || payload_length != frame_expected_
      || payload_offset != frame_used_ || payload_offset > payload_length
      || chunk.size() > payload_length - payload_offset || message_used_ > data_.size()
      || chunk.size() > data_.size() - message_used_) {
    reset();
    return FrameAssemblyStatus::rejected;
  }
  if (!chunk.empty()) {
    std::memcpy(data_.data() + message_used_, chunk.data(), chunk.size());
  }
  frame_used_ += chunk.size();
  message_used_ += chunk.size();
  if (frame_used_ != frame_expected_) return FrameAssemblyStatus::incomplete;
  if (!fin) return FrameAssemblyStatus::incomplete;
  if (message_used_ == 0) {
    reset();
    return FrameAssemblyStatus::rejected;
  }
  complete_message = std::string_view(data_.data(), message_used_);
  message_active_ = false;
  frame_used_ = 0;
  frame_expected_ = 0;
  frame_opcode_ = 0;
  message_used_ = 0;
  return FrameAssemblyStatus::complete;
}

void TextFrameAssembler::reset() {
  message_used_ = 0;
  frame_used_ = 0;
  frame_expected_ = 0;
  frame_opcode_ = 0;
  message_active_ = false;
}

ParseError parse_envelope(std::string_view wire, Envelope& output) {
  if (wire.size() > protocol_v1::kMaxMessageBytes) return ParseError::too_large;
  if (!valid_utf8(wire)) return ParseError::malformed_json;
  JsonValue document;
  JsonParser parser(wire);
  if (!parser.parse(document) || document.type != JsonValue::Type::object) {
    return ParseError::malformed_json;
  }
  const Object& envelope = document.object;
  if (!only_fields(envelope, {"protocolVersion", "messageId", "type", "sentAt",
                              "connectionId", "sequence", "serverSequence", "payload"})) {
    return ParseError::schema;
  }

  output = {};
  std::string protocol_version;
  std::string sent_at;
  if (!string_field(envelope, "protocolVersion", protocol_version, 1, 16)
      || !string_field(envelope, "messageId", output.message_id, 36, 36) || !is_uuid(output.message_id)
      || !string_field(envelope, "type", output.type, 1, 32)
      || !string_field(envelope, "sentAt", sent_at, 20, 40) || !is_utc_timestamp(sent_at)
      || !string_field(envelope, "connectionId", output.connection_id, 1, 64)
      || !is_identifier(output.connection_id, 64)
      || !number_field(envelope, "sequence", output.sequence)) {
    return ParseError::schema;
  }
  if (protocol_version != protocol_v1::kVersion) return ParseError::unsupported_version;

  const auto server_sequence = envelope.find("serverSequence");
  if (server_sequence != envelope.end()) {
    if (server_sequence->second.type != JsonValue::Type::number
        || server_sequence->second.number > kMaxSafeInteger) {
      return ParseError::schema;
    }
    output.has_server_sequence = true;
    output.server_sequence = server_sequence->second.number;
  }

  const JsonValue* payload = field(envelope, "payload", JsonValue::Type::object);
  if (payload == nullptr) return ParseError::schema;

  bool valid = false;
  if (output.type == "capability") valid = validate_capability(payload->object, output);
  else if (output.type == "session") valid = validate_session(payload->object, output);
  else if (output.type == "attention") valid = validate_attention(payload->object, output);
  else if (output.type == "approval" || output.type == "denial" || output.type == "reply") {
    valid = validate_action_echo(payload->object, output, output.type);
  }
  else if (output.type == "event") valid = validate_event(payload->object);
  else if (output.type == "error") valid = validate_error(payload->object, output);
  else if (output.type == "heartbeat") valid = validate_heartbeat(payload->object);
  else return ParseError::schema;
  return valid ? ParseError::none : ParseError::schema;
}

ParseError ProtocolState::apply(const Envelope& message) {
  if (connection_.empty()) {
    if (message.sequence != 1 || message.type != "capability") return ParseError::negotiation;
  } else if (message.connection_id != connection_) {
    return ParseError::connection;
  }
  if (message.sequence != next_sequence_) return ParseError::ordering;
  if (next_sequence_ == 1 && message.type != "capability") return ParseError::negotiation;
  if (next_sequence_ > 1 && !negotiated_) return ParseError::negotiation;
  const bool durable_state = message.type == "session" || message.type == "attention";
  if (message.has_server_sequence && !durable_state) return ParseError::ordering;
  if (message.has_server_sequence && snapshot_in_progress_) return ParseError::ordering;
  if (message.has_server_sequence && message.server_sequence != resume_sequence_ + 1) {
    return ParseError::ordering;
  }
  if (message.type == "capability"
      && message.capability.resume_from_sequence != resume_sequence_) return ParseError::ordering;
  if (negotiated_ && !snapshot_in_progress_ && durable_state && !message.has_server_sequence) {
    return ParseError::ordering;
  }
  if (snapshot_in_progress_ && message.type != "session" && message.type != "attention") {
    return ParseError::ordering;
  }

  ViewModel next_view = view_;
  ViewModel next_retained_view = retained_view_;
  std::uint64_t next_resume = resume_sequence_;
  std::uint64_t next_snapshot_remaining = snapshot_remaining_;
  std::uint64_t next_snapshot_checkpoint = snapshot_checkpoint_;
  bool next_snapshot_in_progress = snapshot_in_progress_;

  if (message.type == "capability") {
    if (negotiated_) return ParseError::negotiation;
    next_view = retained_view_.state == UiState::disconnected
        ? ViewModel{UiState::idle, "AgentPing", "Connected - no pending updates", 0}
        : retained_view_;
    if (message.capability.reset_state) {
      if (message.capability.snapshot_checkpoint < resume_sequence_) return ParseError::ordering;
      next_view = {UiState::idle, "AgentPing", "No active sessions", 0};
      next_retained_view = next_view;
      next_snapshot_remaining = message.capability.snapshot_item_count;
      next_snapshot_checkpoint = message.capability.snapshot_checkpoint;
      next_snapshot_in_progress = next_snapshot_remaining > 0;
      if (!next_snapshot_in_progress) next_resume = next_snapshot_checkpoint;
    }
  } else if (message.type == "session") {
    next_view = {ui_state(message.state), message.title, message.detail, message.revision};
    next_view.provider = message.provider;
    next_view.session_id = message.session_id;
    next_retained_view = next_view;
  } else if (message.type == "attention") {
    const auto provider = session_providers_.find(message.session_id);
    if (provider == session_providers_.end()) return ParseError::ordering;
    next_view = {UiState::waiting, message.title, message.detail, message.revision};
    next_view.provider = provider->second;
    next_view.session_id = message.session_id;
    next_view.attention_id = message.attention_id;
    next_view.presented_message_id = message.message_id;
    next_view.response_deadline_at = message.response_deadline_at;
    next_view.response_deadline_epoch = message.response_deadline_epoch;
    next_view.destructive = message.destructive;
    next_view.allow_approve = message.allow_approve;
    next_view.allow_deny = message.allow_deny;
    next_view.allow_reply = message.allow_reply;
    next_view.allow_cancel = message.allow_cancel;
    next_view.allow_acknowledge = message.allow_acknowledge;
    next_retained_view = next_view;
  } else if (message.type == "approval" || message.type == "denial" || message.type == "reply") {
    next_view = {UiState::completed, message.title, message.detail, message.revision};
    next_retained_view = next_view;
  } else if (message.type == "error") {
    next_view = {UiState::failed, message.title, message.detail, 0};
    next_retained_view = next_view;
  }

  if (message.has_server_sequence) next_resume = message.server_sequence;
  if (next_snapshot_in_progress && message.type != "capability") {
    if (next_snapshot_remaining == 0) return ParseError::ordering;
    --next_snapshot_remaining;
    if (next_snapshot_remaining == 0) {
      next_snapshot_in_progress = false;
      next_resume = next_snapshot_checkpoint;
    }
  }

  if (connection_.empty()) connection_ = message.connection_id;
  negotiated_ = negotiated_ || message.type == "capability";
  if (message.type == "capability" && message.capability.reset_state) {
    session_providers_.clear();
  } else if (message.type == "session") {
    if (session_providers_.find(message.session_id) == session_providers_.end()
        && session_providers_.size() >= protocol_v1::kMaxReplayWindowMessages) {
      session_providers_.erase(session_providers_.begin());
    }
    session_providers_[message.session_id] = message.provider;
  }
  view_ = std::move(next_view);
  retained_view_ = std::move(next_retained_view);
  resume_sequence_ = next_resume;
  snapshot_remaining_ = next_snapshot_remaining;
  snapshot_checkpoint_ = next_snapshot_checkpoint;
  snapshot_in_progress_ = next_snapshot_in_progress;
  last_received_sequence_ = message.sequence;
  ++next_sequence_;
  return ParseError::none;
}

void ProtocolState::disconnected() {
  connection_.clear();
  next_sequence_ = 1;
  last_received_sequence_ = 0;
  negotiated_ = false;
  snapshot_remaining_ = 0;
  snapshot_checkpoint_ = 0;
  snapshot_in_progress_ = false;
  view_ = {};
}

std::string action_context(const ViewModel& view) {
  std::string scope;
  const auto append_action = [&scope](bool allowed, const char* action) {
    if (!allowed) return;
    if (!scope.empty()) scope += ", ";
    scope += action;
  };
  append_action(view.allow_approve, "approve");
  append_action(view.allow_deny, "deny");
  append_action(view.allow_cancel, "cancel");
  append_action(view.allow_acknowledge, "acknowledge");
  append_action(view.allow_reply, "reply");

  std::string context = view.provider + " · " + view.session_id
      + "\nScope: " + scope + "\nDeadline: " + view.response_deadline_at;
  if (view.destructive) context += "\nDESTRUCTIVE · confirm twice";
  return context;
}

PreparedAction prepare_action(const ViewModel& view, std::string_view action,
                              std::string_view text, bool confirmed,
                              std::int64_t now_epoch) {
  PreparedAction result;
  if (view.state != UiState::waiting || view.attention_id.empty()
      || view.presented_message_id.empty()) {
    return result;
  }
  if (view.response_deadline_epoch <= 0 || now_epoch >= view.response_deadline_epoch) {
    result.status = ActionPreparationStatus::expired;
    return result;
  }

  bool allowed = false;
  if (action == "approve") allowed = view.allow_approve;
  else if (action == "deny") allowed = view.allow_deny;
  else if (action == "reply") allowed = view.allow_reply;
  else if (action == "cancel") allowed = view.allow_cancel;
  else if (action == "acknowledge") allowed = view.allow_acknowledge;
  else return result;
  if (!allowed) {
    result.status = ActionPreparationStatus::not_allowed;
    return result;
  }

  result.action = std::string(action);
  result.attention_id = view.attention_id;
  result.presented_message_id = view.presented_message_id;
  result.expected_revision = view.revision;
  result.destructive = view.destructive;
  result.canonical_prompt = view.title + "\n" + view.detail;
  if (action == "approve") {
    if (view.destructive && !confirmed) {
      result.status = ActionPreparationStatus::confirmation_required;
      return result;
    }
    result.message_type = "approval";
  } else if (action == "reply") {
    if (text.empty() || !valid_utf8(text) || utf8_code_points(text) > protocol_v1::kMaxReplyCharacters) {
      return result;
    }
    result.message_type = "reply";
    result.text = std::string(text);
  } else {
    result.message_type = "denial";
    if (action == "deny") result.reason = "user_denied";
    else if (action == "cancel") result.reason = "user_cancelled";
    else result.reason = "acknowledged";
  }
  result.status = ActionPreparationStatus::ready;
  return result;
}

std::uint32_t backoff_delay_ms(unsigned attempt, std::uint32_t random_value) {
  std::uint64_t cap = 1000ULL << std::min(attempt, 6U);
  cap = std::min<std::uint64_t>(cap, 60000ULL);
  const std::uint32_t jitter_range = static_cast<std::uint32_t>(cap / 4ULL + 1ULL);
  const std::uint64_t delay = cap + random_value % jitter_range;
  return static_cast<std::uint32_t>(std::min<std::uint64_t>(delay, 60000ULL));
}

bool is_private_wss_endpoint(std::string_view endpoint) {
  constexpr std::string_view prefix = "wss://";
  if (endpoint.substr(0, prefix.size()) != prefix
      || endpoint.find_first_of("@?#") != std::string_view::npos) {
    return false;
  }
  const std::size_t path_at = endpoint.find('/', prefix.size());
  if (path_at == std::string_view::npos || endpoint.substr(path_at) != "/ws") return false;
  std::string_view authority = endpoint.substr(prefix.size(), path_at - prefix.size());
  const std::size_t colon = authority.find(':');
  if (colon != std::string_view::npos) {
    if (authority.find(':', colon + 1) != std::string_view::npos) return false;
    const std::string_view port = authority.substr(colon + 1);
    if (port.empty()) return false;
    std::uint32_t port_number = 0;
    for (const char character : port) {
      if (!std::isdigit(static_cast<unsigned char>(character))) return false;
      port_number = port_number * 10U + static_cast<unsigned>(character - '0');
      if (port_number > 65535U) return false;
    }
    if (port_number == 0) return false;
    authority = authority.substr(0, colon);
  }
  unsigned octets[4] = {};
  std::size_t start = 0;
  for (unsigned index = 0; index < 4; ++index) {
    const std::size_t dot = index == 3 ? authority.size() : authority.find('.', start);
    if (dot == std::string_view::npos || dot == start) return false;
    unsigned value = 0;
    for (std::size_t position = start; position < dot; ++position) {
      if (!std::isdigit(static_cast<unsigned char>(authority[position]))) return false;
      if (position == start && dot - start > 1 && authority[position] == '0') return false;
      value = value * 10U + static_cast<unsigned>(authority[position] - '0');
      if (value > 255U) return false;
    }
    octets[index] = value;
    start = dot + 1;
  }
  if (start != authority.size() + 1) return false;
  return octets[0] == 10
      || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
      || (octets[0] == 192 && octets[1] == 168);
}

const char* parse_error_name(ParseError error) {
  switch (error) {
    case ParseError::none: return "none";
    case ParseError::too_large: return "too_large";
    case ParseError::malformed_json: return "malformed_json";
    case ParseError::schema: return "schema";
    case ParseError::unsupported_version: return "unsupported_version";
    case ParseError::connection: return "connection";
    case ParseError::ordering: return "ordering";
    case ParseError::negotiation: return "negotiation";
  }
  return "unknown";
}

}  // namespace agentping
