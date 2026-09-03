# KiCad verification evidence

`generated/erc.rpt` and `generated/drc.rpt` were regenerated with KiCad CLI
9.0.9 on 2026-09-03 from the editable sources. Both report zero violations;
the DRC report also reports zero unconnected pads and zero footprint errors.

Reproduce the reports from the repository root after installing
`hardware/kicad/requirements.txt`:

```bash
PYTHON_BIN=/tmp/agentping-kicad-venv/bin/python ./hardware/verify.sh
```

These results cover only KiCad's ERC/DRC rules for the checked-in files.
They do not demonstrate fabricated-board correctness or physical behavior.
