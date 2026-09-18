# Pixel Pal monitor pod - measured-board snap-fit prototype

White PLA, two-piece edge-mounted housing for the pin-free Waveshare
ESP32-C6-Touch-AMOLED-1.64. Dimensions are millimetres. This is an unprinted
fit-check prototype, not a verified production fit.

For fitted rear GPIO headers, the optional [pin-access back](../pin-access/README.md)
adds two slots while retaining this front, M2 supports and existing mounts.
Print `back-cover-pin-access.stl` instead of `back-cover.stl`; verify that pin
tips remain recessed before attaching the mount.

## Current dimensions

| Feature | Dimension |
|---|---|
| Assembled housing | 42 x 56 x 15.1 |
| Front bezel depth | 5.6 |
| Rear floor | 2.8 |
| Rear exterior seam height | 9.5 |
| Rear recessed locating lip top | 12.3 |
| Locating lip / socket | 38 x 52 / 38.5 x 52.5 |
| Module pocket | 29.92 x 45 |
| Screen opening | 27.2 x 42.2 |
| Screen clearance above measured glass | 0.3 |
| Front retaining rim thickness | 1.2 |
| Board mounting holes | 22 x 39 pitch, symmetric, M2 |

The user measured **9.3 mm from metal standoff feet to screen top**, confirmed
M2 threaded standoffs and no header pins, and estimated the mounting pattern
as 22 x 39. Hole centres are (+/-11, +/-19.5) relative to the enclosure centre.
Four 1.5 mm printed pads lift the metal standoffs above the rear floor for USB clearance.
Depth = 2.8 floor + 1.5 pads + 9.3 module + 0.3 glass clearance + 1.2 front rim.
The 39 mm pitch supersedes the older drawing's 38.5 mm value; dry-fit all four
screws without force because the user measurement is approximate.

## Mounting and assembly

Both this C6 pod and the S3 pod accept the same [monitor arm and desktop stand](../mounts/README.md). Their four 10 x 2 mm rear vents and 2.8 mm floor are identical. Remove the mount before using the rear snap-release slots.

1. Fasten the board to the rear tray through four 2.3 mm clearance holes using
   M2 machine screws into its metal standoffs. USB points down.
2. Rear screw-head recesses are 4.2 mm diameter x 1.3 mm deep. Check the actual
   screw heads fit. Remaining screw grip is 3.0 mm; choose length as grip plus
   usable thread engagement, without bottoming in the metal standoffs.
3. Slide the thin front bezel over the recessed rear lip. Four spring catches
   engage internal pockets. The glass must not be clamped or pressed by the bezel.
4. Bond either flat side of the **rear tray** to the monitor edge. Use a
   **6 x 30 mm adhesive strip**, in the uninterrupted side region x = +/-21,
   y = -8 to +22, z = 1 to 7. The pad stays below the seam, so the front bezel
   can be removed without removing the adhesive. This fits the 19.05 mm bezel.
5. Rear release slots access each catch: push inward gently and ease the bezel
   forward. Physical release access and clip force must be confirmed on a test
   print before attaching to the monitor.

There are no mounting tabs. The screen faces the viewer; the adhesive side is
perpendicular to the screen. A button on the bonded side may become inaccessible.
Check the USB plug and necessary button access before permanent mounting.

The rear USB cutout is 13 mm wide, centred on the housing, with its bottom
edge 3.6 mm above the back surface. The cut extends to the same 12.7 mm top
height; the front shell has no USB notch.

## Printing

White PLA, 0.2 mm layers, four walls, 20-30% infill as starting settings.
Use the filament manufacturer's temperature profile. Keep the mounting area
away from hot monitor exhaust. The supplied STL files are oriented for printing:
front bezel face-down, rear tray floor-down. Local support may be needed at
connector openings and latch pockets; inspect the slicer preview.

Snap beams are 1 mm thick and 9.5 mm tall, with 0.25 mm nominal engagement.
The locating lip has 0.25 mm clearance per side. PLA layer adhesion and printer
accuracy affect clip strength; print a fit check before inserting electronics.
These catches are intended for occasional service, not frequent cycling.

## Files and reproduction

- `front-shell.stl`, `back-cover.stl`: print one of each (back-cover is now a tray).
- Matching STEP files: solids in assembled coordinates.
- `pixel-pal-monitor-pod.FCStd`: FreeCAD assembly.
- `preview.png`, `exploded.png`: assembled and separated views; screen/face are illustrative.
- `build_enclosure.py`: dimensions and CAD generator.
- `render_preview.py`, `preview.blend`: visual preview source.
- `validation.json`: actual mesh extents and solid-check results.

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware\enclosure\esp32-c6-touch-amoled-1.64\build_enclosure.py
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' -b --python hardware\enclosure\esp32-c6-touch-amoled-1.64\render_preview.py
```

The front has a 2 mm, 45-degree outer chamfer and a 0.5 mm, 45-degree
chamfer around the screen opening. The outline corners remain rounded.
CAD checks verify single valid solids, closed print meshes, and no assembled
part overlap. They do not prove board fit, latch force, adhesive strength,
thermal performance, or physical print tolerances.

## Reference

Manufacturer drawing/STEP references informed the original outline and openings:
https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64/Resources-And-Documents
https://github.com/waveshareteam/ESP32-C6-Touch-AMOLED-1.64/tree/main/hardware/dimensions

The manufacturer's 2024-12-21 drawing depicts an older board layout. The user's
9.3 mm height and symmetric 22 x 39 pattern take precedence. Confirm the
connector/button positions on the actual board during the first physical fit.

## BOOT / RESET access

Side walls are closed; open the enclosure to access BOOT/RESET. The front shell
has no external button or USB notches. Internal snap-fit pockets remain.

The checked-in 3MF files are earlier slicer projects. Import the current STL files
for the latest USB opening and housing geometry.
