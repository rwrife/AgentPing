# Shared protocol artifacts

Protocol v1 is specified by the canonical Draft 2020-12 schema at [`v1/agentping.schema.json`](v1/agentping.schema.json). [`v1/fixtures/valid-messages.json`](v1/fixtures/valid-messages.json) contains one golden message for each supported kind; [`v1/fixtures/invalid-messages.json`](v1/fixtures/invalid-messages.json) contains fail-closed cases.

Run `python3 protocol/validate.py` to validate the schema, fixtures, 16 KiB wire bound, and generated firmware constants. Run `python3 protocol/validate.py --write-header` only after deliberately changing schema limits. The bridge serialization tests consume the same fixtures.

The schema is a contract artifact. A checked-in schema and passing static tests do not mean the LAN transport, enrollment flow, device authentication, or provider dispatch is implemented.
