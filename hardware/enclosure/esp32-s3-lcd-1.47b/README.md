# ESP32-S3-LCD-1.47B snap-fit monitor pod

The S3 uses a compact **36 x 51 mm rounded outline**, 5 mm corner
radius, 2 mm front chamfer, recessed locating lip and four spring catches.
Its **14.8 mm depth** follows the measured S3 stack (C6: 15.1 mm).
The front opening now follows the user-measured 19.3 x 33.3 mm screen,
with provisional 3 mm rounded corners and centred alignment. The bezel bands
are 8.35 mm at the sides and 8.85 mm at the ends (before edge chamfers).
The USB opening spans the rear tray and a small relief in the front. **Print the new front and one new back together:**
earlier fronts lack the matching screen opening and USB relief.

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
| Body | 36 x 51 x 14.8 |
| PCB envelope | 20.32 x 36.37 |
| Internal pocket | 24.32 x 37.37 (3 mm wider overall) |
| Front aperture at throat | 19.3 x 33.3; provisional corner radius 3.0 |
| Floor / four printed support pads | 2.8 / 2.8 high, 4.0 diameter |
| M2 clearance / rear head recess | 2.3 diameter / 4.2 diameter x 1.3 deep |
| User-measured support feet to glass top | 7.7 |
| Nominal glass top / rim underside | Z = 13.3 / 13.6 |
| Rear seam / locating lip top | Z = 9.2 / 12.0 |
| Locating lip / socket | 32 x 47 / 32.5 x 47.5 |
| Front rim / aperture chamfer | 1.2 / 0.5 |
| USB tunnel | 15 wide; Z = 3.1..10.0 |

The screen opening is separate from the unchanged PCB/button pocket. The
19.3 x 33.3 mm measurement is user supplied; its interpretation as the visible
opening, centred offset and 3 mm corner radius remain provisional. The 0.5 mm
outer aperture chamfer expands the mouth; the inner throat has the stated size.
A conservative full-board glass envelope remains 0.3 mm below the rim.

Both backs have four 2.8 mm-high printed supports, raised another 0.3 mm after
the latest cable fit check. The assembled USB opening is now 15 mm wide and Z = 3.1..10.0 mm.
Previously the front blocked the opening above the Z = 8.9 mm seam, despite
a taller cut in the tray. Raising the board 0.3 mm and the actual opening roof
0.6 mm adds 0.3 mm clearance below and above the same plug. Both parts are
checked together for an unobstructed 15 mm-wide passage. The latest revision
adds another 0.5 mm on all four edges of the preceding 14 x 5.9 mm opening,
for a 15 x 6.9 mm opening. Board height and outer dimensions stay unchanged. Do not add adhesive
on the supports. Real cable fit still requires a print check.

The manufacturer-dimensioned hole centres (USB toward -Y) are:

- USB end: X = +/-8.16, Y = -15.785 mm.
- Antenna end: X = +/-6.64, Y = +16.215 mm.

Fasten through the rear into the board's metal M2 standoffs. Nominal screw grip
is 4.3 mm above the head recess; choose screw length for that grip plus the
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
or external openings are needed. The front throat is 19.3 mm wide.
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
