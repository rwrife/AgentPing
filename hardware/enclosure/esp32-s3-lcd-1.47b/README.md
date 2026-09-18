# ESP32-S3-LCD-1.47B snap-fit monitor pod

The S3 uses a compact **36 x 51 mm rounded outline**, 5 mm corner
radius, 2 mm front chamfer, recessed locating lip and four spring catches.
Its **14.5 mm depth** follows the measured S3 stack (C6: 15.1 mm).
The bezel bands are 7.34 mm at the sides and 6.815 mm at the ends, close to
the C6 bands of 7.4 and 6.9 mm. The outer sides and front are continuous; USB
exits through the rear tray. **Print the new front and one new back together:**
the older 42 x 56 mm S3 parts do not fit this compact revision.

![S3 assembled pod](preview.png)
![S3 snap-fit interior](exploded.png)

For the **ESP32-S3-LCD-1.47B**, non-touch 172 x 320 board without headers.
This is an unprinted fit-check prototype. The existing C6 geometry is preserved.

For fitted rear GPIO headers, use the optional [pin-access back](../pin-access/README.md):
print `clearance-tray-pin-access.stl` instead of `clearance-tray.stl` with
the current compact front and the existing mount. Pin tips must remain recessed; actual header
projection and mating-connector fit require checking.

## Dimensions and board retention

| Feature | Millimetres |
|---|---|
| Body | 36 x 51 x 14.5 |
| PCB envelope | 20.32 x 36.37 |
| Internal pocket | 24.32 x 37.37 (3 mm wider overall) |
| Front aperture | 21.32 x 37.37 |
| Floor / four printed support pads | 2.8 / 2.5 high, 4.0 diameter |
| M2 clearance / rear head recess | 2.3 diameter / 4.2 diameter x 1.3 deep |
| User-measured support feet to glass top | 7.7 |
| Nominal glass top / rim underside | Z = 13.0 / 13.3 |
| Rear seam / locating lip top | Z = 8.9 / 11.7 |
| Locating lip / socket | 32 x 47 / 32.5 x 47.5 |
| Front rim / aperture chamfer | 1.2 / 0.5 |
| USB tunnel | 14 wide; Z = 3.6..11.9 |

The aperture uses the full PCB envelope plus 0.5 mm per side because glass
outline and offset are not dimensioned in the manufacturer drawing. It does
not clamp or positively retain the board. **Both S3 backs now have four printed
support pads and M2 screw access**, replacing the earlier adhesive-only support.
The pads are 2.5 mm high, raised by 2 mm after the first USB cable fit check.
The rear walls, locating lip and catches rise by the same 2 mm, giving a
14.5 mm total depth. The compact front matches both current back options. The USB
tunnel floor stays at Z = 3.6 while its ceiling rises to 11.9, providing
2 mm more clearance below the raised connector. Do not add adhesive on the pads.

The manufacturer-dimensioned hole centres (USB toward -Y) are:

- USB end: X = +/-8.16, Y = -15.785 mm.
- Antenna end: X = +/-6.64, Y = +16.215 mm.

Fasten through the rear into the board's metal M2 standoffs. Nominal screw grip
is 4.0 mm above the head recess; choose screw length for that grip plus the
actual usable thread engagement, without bottoming. Dry-fit all four screws
without force. The printed pads have 4 mm outside diameter and a 2.3 mm hole.
The pin-access back trims their edges with rounded slots while retaining at
least 0.5 mm continuous material around every screw hole above the head recess.
Actual board fit, standoff thread depth and screw-head size remain physical checks.

The internal pocket is widened by **3 mm overall (1.5 mm per side)** along
its full length, providing room for the buttons inside the closed enclosure.
A centred board with 1.5 mm button protrusion has 0.5 mm nominal clearance
per side. Locate and secure the board on its four printed pads; that clearance is
not an additional allowance for board movement. No individual button recesses
or external openings are needed. The front aperture remains 21.32 mm wide.
Open the housing for BOOT/RESET or microSD service, as with the C6 closed sides.
USB plug dimensions and insertion clearance must be checked with the actual cable.

## Shared monitor and desktop mounts

Use the unchanged [monitor arm and desktop stand](../mounts/README.md) on either
board enclosure. Both have four **10 x 2 mm rectangular vents**, X = -5..5,
lower Y edges **-8, -2, 4, 10**, 6 mm pitch, through a **2.8 mm floor**.
The arm engages the first and last vents, 18 mm apart. Its 2.6 mm tips stop
0.2 mm before the floor's inner surface. The middle two vents remain open,
and the shoulders maintain the 4 mm air gap. No adapter change is required.

Print the mount's grip coupons first. Detach the mount before releasing the
bezel. Depress the four catches through the rear release slots and gently lift
the front; never pull the catches through their pockets without releasing them.
The S3's shorter snap beams need a physical force/durability check.

## Printing and verification

Print `clearance-tray.stl` floor-down and `front-bezel.stl` face-down; supplied
meshes already have those orientations. Matching STEP and `s3b-monitor-tray.FCStd`
retain assembly coordinates. Start with 0.2 mm layers, four walls and 20–30%
infill. Inspect USB bridging, snap pockets and release slots in the slicer.

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware/enclosure/esp32-s3-lcd-1.47b/build_enclosure.py
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware/enclosure/verify_enclosures.py
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' -b --python hardware/enclosure/esp32-s3-lcd-1.47b/render_preview.py
```

Validation checks released STEP/STL/FCStd agreement, single solids, closed
manifold meshes, zero assembled overlap, nominal glass/button clearance,
removal after catch release, identical vents and both actual mount arms.
CAD checks do not establish printed retention, thermal performance or real board fit.
The screen/face in previews is illustrative.

Sources: [manufacturer drawing](../../references/manufacturer/esp32-s3-lcd-1.47b/dimensions.jpg),
[component photo](../../references/manufacturer/esp32-s3-lcd-1.47b/components.jpg),
[reference manifest](../../references/MANIFEST.md). Stack height and button
protrusion use the recorded user measurements; remaining allowances are design assumptions.
