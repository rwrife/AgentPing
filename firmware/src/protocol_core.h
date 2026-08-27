#pragma once

#include "agentping_protocol_v1.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

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
  UiState state = UiState::disconnected;
  std::string title = "Disconnected";
  std::string detail = "Provision or reconnect";
  std::uint64_t revision = 0;
};

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
  ViewModel view_;
  ViewModel retained_view_;
};

std::uint32_t backoff_delay_ms(unsigned attempt, std::uint32_t random_value);
bool is_private_wss_endpoint(std::string_view endpoint);

}  // namespace agentping
