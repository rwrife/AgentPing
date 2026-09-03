#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

python_bin="${PYTHON_BIN:-python3}"
"$python_bin" hardware/kicad/export_bom.py --check
kicad-cli sch erc --exit-code-violations \
  --output hardware/reports/generated/erc.rpt \
  hardware/kicad/agentping-carrier.kicad_sch
kicad-cli pcb drc --exit-code-violations \
  --output hardware/reports/generated/drc.rpt \
  hardware/kicad/agentping-carrier.kicad_pcb
sha256sum --check hardware/fabrication/rev-a0/checksums.sha256
