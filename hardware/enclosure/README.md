# Board-specific monitor enclosures

Choose the **exact board name** before printing. These are standalone module
enclosures, not housings for the optional 70 x 60 mm Rev A0 carrier.

| Board | Enclosure | Status |
|---|---|---|
| Waveshare ESP32-C6-Touch-AMOLED-1.64 | [Measured-board snap-fit pod](esp32-c6-touch-amoled-1.64/README.md) | Original 42 x 56 x 15.1 mm geometry and generated artifacts preserved; unprinted fit-check prototype |
| Waveshare ESP32-S3-LCD-1.47B | [C6-style snap-fit pod](esp32-s3-lcd-1.47b/README.md) | Two-piece unprinted prototype, 7.7 mm user-measured stack; glass outline/aperture fit and retention still unverified |

The S3 **B** model is a 172 x 320 ST7789 LCD, without touch. It is not the
non-B ESP32-S3-LCD-1.47 and does not share the C6 mounting pattern or enclosure.
The standard S3 tray targets the bare, **unsoldered-header** board.
For rear-facing GPIO headers, choose the optional [pin-access backs](pin-access/README.md)
for C6 or S3. They retain the original fronts and vent-mounted arms, with two
slots for recessed pins. Verify the actual header projection before fitting a mount.
Neither design claims verified board fit, printability on a particular printer,
adhesive strength, thermal performance, or production readiness.

Both retain the monitor-edge adhesive concept: a flat housing side perpendicular
to the screen bonds to the monitor edge. There are no extra mounting tabs.
Follow each board's own adhesive-land and cable-access instructions.

Both also use the **same rear vent interface**: four rectangular **10 x 2 mm**
openings, **6 mm pitch**, x = -5..5 and lower Y edges = -8, -2, 4, 10 mm,
through a **2.8 mm** rear floor from Z = 0. The existing C6 files were not
changed. These dimensions are fixed for future use: report any board/accessory
conflict rather than changing the slots. Both use the [monitor arm and desktop stand](mounts/README.md). CAD checks
verify both arms against both trays: 2.6 mm tab insertion leaves 0.2 mm before
the inner floor, and the middle two vents remain open. Physical retention is untested.

## Check the existing files without regenerating them

From the repository root:

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware\enclosure\verify_enclosures.py
```

This verifies preserved C6 SHA-256 hashes, STEP solids, watertight/manifold meshes,
mesh/solid volume agreement, print-bed placement, and assembled C6 part overlap.
It also measures the actual rear vent openings and pitch in both STEP files,
verifies clear tunnels and floor depth, and compares the two vent interfaces.
The S3 now has two parts: the checks require zero assembled overlap and verify
released snap catches, button keepouts and 0.3 mm clearance above an **assumed full-board glass envelope**.
Its 36 x 51 x 14.8 mm stack includes four 2.8 mm printed support pads with M2 screw access.
The full-board aperture avoids guessing a tighter glass-retaining rim: actual
glass width/length, PCB offsets and active-area bounds remain essential to
finalizing that rim. No mechanical model of the real S3 board is available:
**actual board-to-case interference and retention are not verified**.
Each generator also validates its new output when explicitly run.

`c6-preservation.json` records the pre-move C6 files (including CAD generator,
STL/STEP/FCStd, render source, previews, and older 3MF slicer projects). The C6
README updates reproduction paths and links to the shared mounts. Rebuilding CAD may change file
serialization/hashes; the preservation check intentionally detects that.
The manifest also records LF-normalized hashes for Git-managed text files
so a checkout's automatic CRLF conversion does not falsely fail preservation.
Binary artifacts must match their original byte hashes.
