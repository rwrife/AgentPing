import io
import json
import unittest

from tools.agentping_provider_relay import load_payload, validated_endpoint


class ProviderRelayTests(unittest.TestCase):
    def test_accepts_literal_ipv4_and_ipv6_loopback(self) -> None:
        self.assertEqual(
            "http://127.0.0.1:8742/api/adapters/codex",
            validated_endpoint("http://127.0.0.1:8742", "codex"),
        )
        self.assertEqual(
            "http://[::1]:8742/api/adapters/claude_code",
            validated_endpoint("http://[::1]:8742", "claude_code"),
        )

    def test_rejects_non_loopback_credentials_and_paths(self) -> None:
        for value in (
            "https://127.0.0.1:8742",
            "http://localhost:8742",
            "http://192.168.1.5:8742",
            "http://user:pass@127.0.0.1:8742",
            "http://127.0.0.1:8742/path",
        ):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validated_endpoint(value, "codex")

    def test_loads_codex_argument_or_stdin_as_compact_object(self) -> None:
        argument = load_payload('{"type": "agent-turn-complete"}')
        stdin = load_payload(None, io.BytesIO(b'{"hook_event_name":"SessionStart"}'))

        self.assertEqual({"type": "agent-turn-complete"}, json.loads(argument))
        self.assertEqual({"hook_event_name": "SessionStart"}, json.loads(stdin))

    def test_rejects_arrays_invalid_json_and_oversize_input(self) -> None:
        for payload in ("[]", "not-json", '{"x":"' + "x" * 16_384 + '"}'):
            with self.subTest(length=len(payload)), self.assertRaises(ValueError):
                load_payload(payload)


if __name__ == "__main__":
    unittest.main()
