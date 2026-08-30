#pragma once

#include "agentping_protocol_v1.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>

namespace agentping {

enum class UiState {
  disconnected,
  idle,
  running,
  waiting,
  completed,
  failed,
};

struct ViewModel {
  ViewModel() = default;
  ViewModel(UiState initial_state, std::string initial_title,
            std::string initial_detail, std::uint64_t initial_revision)
      : state(initial_state), title(std::move(initial_title)),
        detail(std::move(initial_detail)), revision(initial_revision) {}

  UiState state = UiState::disconnected;
  std::string title = "Disconnected";
  std::string detail = "Provision or reconnect";
  std::uint64_t revision = 0;
  std::string provider;
  std::string session_id;
  std::string attention_id;
  std::string presented_message_id;
  std::string response_deadline_at;
  std::int64_t response_deadline_epoch = 0;
  bool destructive = false;
  bool allow_approve = false;
  bool allow_deny = false;
  bool allow_reply = false;
  bool allow_cancel = false;
  bool allow_acknowledge = false;
};

enum class ActionPreparationStatus {
  ready,
  confirmation_required,
  not_allowed,
  expired,
  invalid,
};

struct PreparedAction {
  ActionPreparationStatus status = ActionPreparationStatus::invalid;
  std::string message_type;
  std::string action;
  std::string reason;
  std::string text;
  std::string attention_id;
  std::string presented_message_id;
  std::string canonical_prompt;
  std::uint64_t expected_revision = 0;
  bool destructive = false;
};

std::string action_context(const ViewModel& view);
std::string serialize_action_payload(
    const PreparedAction& action,
    std::string_view action_id,
    std::string_view prompt_digest,
    std::string_view confirmed_at);
PreparedAction prepare_action(const ViewModel& view, std::string_view action,
                              std::string_view text, bool confirmed,
                              std::int64_t now_epoch);

enum class ParseError {
  none,
  too_large,
  malformed_json,
  schema,
  unsupported_version,
  connection,
  ordering,
  negotiation,
};

enum class FrameAssemblyStatus {
  incomplete,
  complete,
  rejected,
};

class TextFrameAssembler {
 public:
  FrameAssemblyStatus push(std::uint8_t opcode, bool fin,
                           std::size_t payload_length, std::size_t payload_offset,
                           std::string_view chunk, std::string_view& complete_message);
  void reset();

 private:
  std::array<char, protocol_v1::kMaxMessageBytes> data_{};
  std::size_t message_used_ = 0;
  std::size_t frame_used_ = 0;
  std::size_t frame_expected_ = 0;
  std::uint8_t frame_opcode_ = 0;
  bool message_active_ = false;
};

struct CapabilityPayload {
  std::uint64_t resume_from_sequence = 0;
  bool reset_state = false;
  bool has_snapshot_item_count = false;
  std::uint64_t snapshot_item_count = 0;
  bool has_snapshot_checkpoint = false;
  std::uint64_t snapshot_checkpoint = 0;
};

struct Envelope {
  std::string message_id;
  std::string type;
  std::string connection_id;
  std::uint64_t sequence = 0;
  std::uint64_t server_sequence = 0;
  bool has_server_sequence = false;
  std::string state;
  std::string title;
  std::string detail;
  std::uint64_t revision = 0;
  std::string provider;
  std::string session_id;
  std::string attention_id;
  std::string response_deadline_at;
  std::int64_t response_deadline_epoch = 0;
  bool destructive = false;
  bool allow_approve = false;
  bool allow_deny = false;
  bool allow_reply = false;
  bool allow_cancel = false;
  bool allow_acknowledge = false;
  CapabilityPayload capability;
};

ParseError parse_envelope(std::string_view wire, Envelope& out);
const char* parse_error_name(ParseError error);

class ProtocolState {
 public:
  ParseError apply(const Envelope& message);
  void disconnected();

  const ViewModel& view() const { return view_; }
  std::uint64_t resume_sequence() const { return resume_sequence_; }
  std::uint64_t last_received_sequence() const { return last_received_sequence_; }
  void restore_resume_sequence(std::uint64_t value) { resume_sequence_ = value; }
  void restore_resume_state(std::uint64_t sequence, const ViewModel& view) {
    resume_sequence_ = sequence;
    retained_view_ = view;
    if (!view.session_id.empty() && !view.provider.empty()) {
      session_providers_[view.session_id] = view.provider;
    }
  }

 private:
  std::string connection_;
  std::uint64_t next_sequence_ = 1;
  std::uint64_t last_received_sequence_ = 0;
  std::uint64_t resume_sequence_ = 0;
  std::uint64_t snapshot_remaining_ = 0;
  std::uint64_t snapshot_checkpoint_ = 0;
  bool negotiated_ = false;
  bool snapshot_in_progress_ = false;
  std::unordered_map<std::string, std::string> session_providers_;
  ViewModel view_;
  ViewModel retained_view_;
};

std::uint32_t backoff_delay_ms(unsigned attempt, std::uint32_t random_value);
bool is_private_wss_endpoint(std::string_view endpoint);

}  // namespace agentping
