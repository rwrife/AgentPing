# Hardware

Hardware is separated by exact module: **Waveshare ESP32-C6-Touch-AMOLED-1.64**
and **Waveshare ESP32-S3-LCD-1.47B** (172 x 320 ST7789, no touch; not the non-B
model). Start with the [enclosure chooser](enclosure/README.md).

| Module | Standalone enclosure | Evidence status |
|---|---|---|
| C6 Touch AMOLED 1.64 | [Measured-board snap-fit pod](enclosure/esp32-c6-touch-amoled-1.64/README.md) | Original geometry/assets preserved: 9.3 mm user-measured stack, approximate 22 x 39 mm hole pattern, 42 x 56 x 15.1 mm housing |
| S3 LCD 1.47B | [C6-style snap-fit pod](enclosure/esp32-s3-lcd-1.47b/README.md) | Manufacturer 20.32 x 36.37 mm PCB; user-measured 7.7 mm stack; two-piece 42 x 56 x 12.5 mm unprinted prototype with glass-envelope/retention limits |

The optional low-voltage **Rev A0 carrier below remains C6-only**. It has not
been adapted to the S3 pinout or mounting layout. Neither small standalone
enclosure fits this 70 x 60 mm carrier. No RF design is modified.

Both enclosures retain the exact C6 rear vent interface for future use:
**four 10 x 2 mm rectangular slots on 6 mm pitch**, x = -5..5 and lower Y
edges = -8, -2, 4, 10 mm, through a **2.8 mm rear floor**. C6 artifacts remain
unchanged. The S3 copies these dimensions; any physical board/accessory conflict
must be reported rather than resolved by changing the vents.

## Rev A0 carrier

The carrier accepts USB-C 5 V power, protects it with a 500 mA hold PPTC,
TVS, and reverse-current diode, then feeds only the module's USB 5 V header
pin. An optional GPIO6-controlled low-side MOSFET drives a separately
connected haptic load limited to 150 mA. It is a SELV, desk-use interface:
there is no mains, battery charger, high-voltage, medical, life-safety, or
RF-module redesign scope.

| Artifact | Purpose |
|---|---|
| [`kicad/agentping-carrier.kicad_pro`](kicad/agentping-carrier.kicad_pro) | KiCad 9 project and DRC rules |
| [`kicad/agentping-carrier.kicad_sch`](kicad/agentping-carrier.kicad_sch) | Editable schematic; symbol properties are the BOM source of truth |
| [`kicad/agentping-carrier.kicad_pcb`](kicad/agentping-carrier.kicad_pcb) | Editable two-layer PCB, routing, zones, and rule areas |
| [`bom/agentping-carrier-bom.csv`](bom/agentping-carrier-bom.csv) | Generated schematic-backed BOM with MPNs, source URLs, and preliminary costs |
| [`fabrication/rev-a0/`](fabrication/rev-a0/) | KiCad 9.0.9 Gerbers, drills, position data, IPC-2581, STEP, schematic/assembly PDFs, and integrity hashes |
| [`references/MANIFEST.md`](references/MANIFEST.md) | Manufacturer evidence, revision dates, and mechanical source traceability |
| [`reports/generated/`](reports/generated/) | Reproducible KiCad 9.0.9 ERC and DRC reports |

## Electrical and interface constraints

| Interface | Defined constraint |
|---|---|
| USB-C J3 | Power-only USB-C sink. CC1 and CC2 use 5.1 kOhm Rd to GND; D+/D-, SBU, and all non-power module USB pins are intentionally unconnected. |
| Power path | J3 VBUS -> D1 TVS -> F1 PPTC -> D2 reverse-current block -> `+5V_MODULE` / module P2 pin 10. The design ceiling is 500 mA hold; actual module current and inrush still require bench measurement. |
| Module sockets | J1/P1 and J2/P2 are 1x10 2.54 mm sockets. The carrier uses J1 pin 8 (`GPIO6`) and GND pins, plus J2 pin 10 (USB 5 V) and GND pins only. |
| Haptic J4 | Pin 1 is protected 5 V; pin 2 is Q1's switched low side. Q1 defaults off through R4 and is gated through R3. Use only a <=150 mA load with appropriate physical and acoustic evaluation. |
| Test points | VBUS, protected 5 V, GND, GPIO6, Q1 gate, and haptic low side are exposed as unpopulated test pads. |
| Design rules | Two-layer, 1.6 mm board; 0.20 mm minimum track/clearance, 0.50 mm power tracks, 0.60/0.30 mm via/drill, 0.50 mm copper-to-edge clearance. |

## RF and mechanical boundaries

The board is 70 x 60 mm. Four M3 carrier mounting-hole centers are at
(4,4), (66,4), (4,56), and (66,56) mm from the lower-left board origin.
The module M2 centers are (9,10.25), (31,10.25), (9,48.75), and
(31,48.75) mm. The supplied module STEP model and dimensions PDF are
traceable in the reference manifest.

Both copper layers define a 17 x 45 mm `WAVESHARE MODULE RF` rule area
(x=11.5..28.5, y=7.5..52.5 mm) prohibiting pours, tracks, vias, and pads.
A nested module-body rule area also prohibits component placement. These
enforce a conservative copper-free module region in KiCad rather than relying
on an assembly instruction alone. Do not move, reduce, or override either
keepout without reviewing the specific Waveshare module revision and RF
evidence.

## Assemble and bring up

1. Assemble the USB-C power/protection path (J3, R1/R2, D1, F1, D2, C1)
and verify no short between VBUS and GND before inserting the module.
2. Apply a current-limited 5 V USB-C source; verify the protected 5 V test
point and confirm D2 prevents reverse feed toward J3.
3. Remove power, install the Waveshare module in J1/J2 with its pin-1
orientation matched to the silkscreen/assembly drawing, then bring up the
firmware using [`../firmware/BRINGUP.md`](../firmware/BRINGUP.md).
4. Populate the optional haptic path only after confirming GPIO6 remains off
during reset and the chosen load measures no more than 150 mA.

Mating JST PH housing/contacts, USB-C cable, Waveshare module, haptic
actuator, screws, standoffs, and enclosure are external ordering items; see
the BOM comments. The carrier contains no provider credentials and must not
be used to transfer them.

## Reproducible checks and fabrication exports

For both standalone enclosures, run the read-only FreeCAD check independently
of KiCad:

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware\enclosure\verify_enclosures.py
```

It checks C6 preservation hashes, solid/mesh validity, assembled C6 overlap,
and the S3 tray/bezel, part overlap, 0.3 mm assumed-envelope glass clearance,
button keepouts and common rear vents. Physical fit and retention remain unverified.
The carrier-only `verify.sh` does not claim to validate enclosure geometry.

KiCad CLI 9.0.9 was used for the checked-in reports and fabrication snapshot.
Run the checks from the repository root:

```bash
python3 -m venv /tmp/agentping-kicad-venv
/tmp/agentping-kicad-venv/bin/python -m pip install \
  --requirement hardware/kicad/requirements.txt
PYTHON_BIN=/tmp/agentping-kicad-venv/bin/python ./hardware/verify.sh
```

`verify.sh` checks that the BOM is generated from the schematic, reruns ERC
and DRC with violations treated as errors, and verifies the fabrication
snapshot hashes. It is tool-clean evidence, not physical validation.

To write a new, unversioned KiCad export set without replacing the reviewed
`rev-a0` snapshot:

```bash
./hardware/kicad/export_fabrication.sh hardware/fabrication/candidate
```

The generator scripts are maintained design-data regeneration aids. Their
schematic-generation path requires a KiCad 9 symbol-library installation in
addition to `requirements.txt`; the checked-in schematic and PCB are the
authoritative editable engineering sources and can be opened directly in
KiCad 9.

## Evidence limits

No physical PCB, assembly, USB-C interoperability, current/inrush,
haptic/acoustic, temperature, enclosure fit, RF, antenna, display, touch, or
firmware-on-carrier test has been performed for Rev A0. The BOM prices are
preliminary estimates, not quotes or stock checks. A clean ERC/DRC and
generated fabrication files demonstrate only that the current design passed
those KiCad checks. The KiCad 9.0.9 export environment used for the candidate
export did not provide the legacy `KICAD6_3DMODEL_DIR` /
`KICAD7_3DMODEL_DIR` component-model variables, so its generated STEP contains
the board solid but omits affected library component bodies. Use the supplied
Waveshare STEP model for enclosure work, and configure the matching KiCad 3D
model libraries before relying on an assembled carrier STEP.
