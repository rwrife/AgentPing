# AgentPing MVP release runbook

This runbook defines the reproducible release path for AgentPing MVP tags.

## Scope

Release artifacts include:
- bridge build outputs and container image build proof
- unsigned Windows companion packages (`win-x64`, `win-arm64`) and WiX MSI outputs
- firmware binaries/checksums from the pinned PlatformIO/ESP-IDF toolchain
- hardware fabrication bundle + schematic-backed BOM from editable KiCad sources
- generated protocol reference documentation
- dependency/license inventory snapshots

## Preconditions

1. Changes merged to `main`.
2. Canonical CI workflows green for the target commit.
3. No unreviewed security/privacy exceptions.
4. Hardware claims remain bounded to static/build evidence unless physical validation is recorded separately.

## Automated release workflow

The `.github/workflows/release.yml` workflow is the canonical release automation.

### Trigger by tag

```bash
git checkout main
git pull --ff-only origin main
git tag -a v0.1.0 -m "AgentPing v0.1.0"
git push origin v0.1.0
```

### Trigger manually

```bash
gh workflow run release.yml -f release_version=v0.1.0-rc1
```

## What the workflow verifies before publication

Linux verification job (must pass):
- `python3 -m unittest discover -s tools/tests -v`
- `firmware/tests/run_host_tests.sh`
- `python3 protocol/validate.py`
- `python3 protocol/generate_reference.py --check`
- `dotnet restore AgentPing.sln --locked-mode`
- `dotnet build AgentPing.sln --configuration Release --no-restore`
- `dotnet test AgentPing.sln --configuration Release --no-build`
- `./integration/smoke-bridge.sh`
- `./integration/smoke-provider-adapters.sh`
- `./integration/smoke-e2e-simulator.sh`
- `platformio run -d firmware`
- `docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:release .`

Windows packaging jobs (must pass):
- `dotnet test AgentPing.sln --configuration Release --no-restore`
- self-contained companion and bridge publish for `win-x64` and `win-arm64`
- WiX MSI build per RID

Only after these jobs pass does the workflow assemble/publish the release bundle.

## Artifact outputs

Bundle structure (`agentping-<version>-bundle.tar.gz`):
- `linux-inputs/` firmware binaries, protocol reference, hardware bundle/BOM/reports, license inventories, changelog/release docs
- `windows/win-x64/` companion + bridge app and unsigned MSI
- `windows/win-arm64/` companion + bridge app and unsigned MSI
- `SHA256SUMS.txt` checksums for all bundled files

## Reproducible local dry run (no tag required)

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt --requirement protocol/requirements-ci.txt
python3 protocol/generate_reference.py
./scripts/verify.sh
docker build --file bridge/AgentPing.Bridge/Dockerfile --tag agentping-bridge:local .
```

Expected static outputs:
- `protocol validation passed ...`
- `protocol reference is current ...` (with `--check`)
- `.NET` tests pass for bridge + companion test projects
- `SMOKE_RESULT=PASS`, `ADAPTER_SMOKE_RESULT=PASS`
- E2E simulator smoke passes (`BridgeEndToEndSimulationTests`)
- PlatformIO emits binaries under `firmware/.pio/build/waveshare_esp32_c6_touch_amoled_1_64/`

## Physical steps (explicitly not CI-validated)

The following require hardware and must be recorded outside CI:
- flashing the actual Waveshare module and validating display/touch/network behavior
- private-LAN TLS enrollment and certificate pinning against the real bridge host
- haptic/load behavior on the optional carrier board
- enclosure/mechanical fit and RF behavior
- Authenticode signing and real Windows install/upgrade/uninstall validation

Do not mark these as complete from simulator output alone.
