# Release rollback and recovery

This document defines rollback expectations across bridge, companion, firmware, and hardware artifacts.

## 1) Immediate release rollback

If a tagged release is invalid:

1. Mark the GitHub release as superseded and publish a rollback notice.
2. Re-point deployment/installation guidance to the last known good tag.
3. Preserve the failed bundle and checksums for audit; do not overwrite history.

## 2) Bridge rollback

- Stop the running bridge/companion process.
- Replace bridge binaries with the prior release artifact.
- Keep persistent state files (`bridge-state.json`, credential stores) unless corruption is suspected.
- Start bridge and verify:
  - `GET /health` returns `Healthy`
  - `GET /api/status` reports protocol `1.0` and expected non-secret counts

If persistence corruption is suspected, restore from a known backup snapshot and restart.

## 3) Companion rollback

- Uninstall unsigned MSI/app package for the failed build.
- Install the previous known-good MSI/app package.
- Verify startup, tray controls, and pairing management UI baseline.
- Reference `docs/windows-troubleshooting.md` for common post-rollback operator issues.

## 4) Firmware rollback

- Flash the previous known-good firmware binaries (`bootloader`, `partitions`, app image).
- Re-provision via serial JSON if enrollment data changed.
- For recovery erase, follow `firmware/BRINGUP.md` BOOT-hold reset path.

## 5) Hardware rollback posture

Hardware fabrication outputs are static snapshots. If a board revision is invalid:
- do not re-use invalid fabrication bundles,
- return to the prior reviewed fabrication snapshot (`hardware/fabrication/<rev>/`),
- regenerate KiCad exports only from the matching editable sources and rerun ERC/DRC evidence.

## 6) Support/troubleshooting map

- Bridge runtime and adapter behavior: `bridge/README.md`, `docs/provider-adapters.md`
- Windows operator issues: `docs/windows-troubleshooting.md`
- Device provisioning/bring-up/recovery: `firmware/README.md`, `firmware/BRINGUP.md`
- Hardware assembly and constraints: `hardware/README.md`

## 7) Evidence rules

During rollback incidents, clearly separate:
- static CI/build/simulation results,
- configuration rollback actions,
- physical bench retest outcomes.

Never present one category as proof for another.
