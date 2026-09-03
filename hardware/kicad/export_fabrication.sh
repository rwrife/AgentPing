#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

output_dir="${1:-hardware/fabrication/candidate}"
pcb="hardware/kicad/agentping-carrier.kicad_pcb"

mkdir -p "$output_dir/gerbers"
kicad-cli pcb export gerbers --board-plot-params \
  --output "$output_dir/gerbers" "$pcb"
kicad-cli pcb export drill --output "$output_dir/gerbers" \
  --excellon-separate-th --generate-map --map-format pdf --generate-report \
  --report-path "$output_dir/drill-report.rpt" "$pcb"
kicad-cli pcb export pos --format csv --units mm --side front --exclude-dnp \
  --output "$output_dir/agentping-carrier-all-pos.csv" "$pcb"
kicad-cli pcb export step --force \
  --output "$output_dir/agentping-carrier-board.step" "$pcb"
kicad-cli pcb export ipc2581 --version C --units mm \
  --output "$output_dir/agentping-carrier.ipc" "$pcb"
(
  cd "$output_dir"
  sha256sum agentping-carrier-all-pos.csv agentping-carrier-board.step \
    agentping-carrier.ipc drill-report.rpt > checksums.sha256
)
printf 'Fabrication candidate exported to %s\n' "$output_dir"
