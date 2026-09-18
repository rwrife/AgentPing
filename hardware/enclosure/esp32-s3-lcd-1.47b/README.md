# ESP32-S3-LCD-1.47B removable-bezel monitor case

**UNPRINTED FIT-CHECK PROTOTYPE — not a verified closed enclosure.**
This two-piece case replaces the earlier open tray. It uses the user's **7.7 mm
rear support-foot to glass-top measurement**, a vented rear tray, and a removable
front bezel located on a recessed lip. USB and both buttons have broad service
openings. It is not sealed. For the exact **ESP32-S3-LCD-1.47B**, 172 x 320
ST7789 LCD, no touch, without soldered headers; not the non-B board or B-M.

**Remaining blocker to a close-fitting glass-retaining bezel:** the manufacturer's
available drawing does not dimension the glass outline, its X/Y offset from
the PCB, or the active-display rectangle. Please measure **glass width/length,
glass-to-PCB edge offsets, and active-area edge offsets**. This prototype uses
a conservative full-board opening rather than guessing a smaller aperture.
Its rim seats on the tray, **not on the glass**; board retention remains a
separate, physically unverified adhesive-on-feet method. Do not mistake the rim
for positive retention of the electronics.

## Evidence and dimensions

Sources reviewed 2026-09-18:

- [Manufacturer product](https://www.waveshare.com/ESP32-S3-LCD-1.47B.htm)
  and [wiki revision 105396](https://www.waveshare.com/w/index.php?title=ESP32-S3-LCD-1.47B&oldid=105396).
- [Outline drawing](../../references/manufacturer/esp32-s3-lcd-1.47b/dimensions.jpg):
  **36.37 x 20.32 mm PCB**, not a dimensioned glass envelope or stack section.
- [Component photo](../../references/manufacturer/esp32-s3-lcd-1.47b/components.jpg):
  USB at one short end, BOOT/RESET on opposite adjacent long edges, rear microSD,
  antenna at the other end. Rear view with USB up: BOOT left, RESET right.
- **User:** support-foot to glass-top height **7.7 mm**; lateral button protrusion
  approximately **1.5 mm**, supplied 2026-09-18. The button protrusion is applied
  beyond PCB long edges; confirm the user's display-versus-PCB datum.
- [Manifest](../../references/MANIFEST.md): exact sources, evidence hashes and limits.

Coordinates are unchanged: PCB centre X/Y, USB toward -Y, rear exterior Z = 0,
screen toward +Z. All dimensions below are mm.

| Feature | Nominal dimension | Basis |
|---|---|---|
| Overall assembly | **25.32 x 41.37 x 12.5** | Derived |
| Rear floor / full-height lower walls | 2.8 / 2.0 | C6 vent compatibility / print assumption |
| Pocket and front aperture | 21.32 x 37.37 | PCB + 0.5 per side; glass outline **unverified** |
| Installed insulating support adhesive | 0.5 | **Assumption**, measure actual installed thickness |
| Support-foot to top glass | **7.7** | **User measurement** |
| Nominal glass top | Z = 11.0 | 2.8 floor + 0.5 pad + 7.7 module |
| Glass-to-rim vertical clearance | **0.3** | Design clearance; must not be consumed by thicker pads |
| Rim underside / top | Z = 11.3 / 12.5 | Glass clearance + 1.2 rim |
| Tray shoulder / recessed lip top | Z = 8.0 / 11.05 | Design |
| Lip wall / socket radial clearance | 0.75 / 0.25 per side | Light-duty slip-fit, not a snap latch |
| Front skirt wall | 1.0 | Design; check slicer wall generation |
| Front STL height | 4.5 | Face-down orientation |
| USB floor notch | 14 wide x 5 deep | Unmeasured cable allowance |

Total depth = **2.8 + 0.5 + 7.7 + 0.3 + 1.2 = 12.5 mm**.
If the 7.7 mm measurement already includes the installed adhesive, or the pad
thickness differs, update `SUPPORT_PAD` and regenerate both parts. Never squeeze
the glass to compensate. No arbitrary module-depth allowance remains.

## Removable front and no preload

The front slides over the recessed tray lip and seats on the shoulder at Z = 8.
There is 0.25 mm lateral and top clearance around the lip. The **1.2 mm rim**
bridges the tray wall to the full-board aperture; it does not intentionally
overlap the nominal PCB/glass envelope. Both parts have valid single solids.
The CAD glass test uses the entire pocket width/length as a conservative
**assumed** glass envelope at the measured stack height, including board float.
It proves 0.3 mm minimum distance to the front only for that assumption.

The lip is a locator, **not a friction-lock or snap catch**. After successful dry
fit, secure the front with removable exterior tape across the seam on the
unbonded side or +Y end. Tape is not part of the printed CAD and must not cover
vents, buttons, display, antenna, or the monitor adhesive land. Peel this tape
and lift the bezel straight forward for service. No new screw mounts or board
hole pattern have been invented.

## BOOT/RESET and USB

Both USB-adjacent sidewalls are open from the floor to the rim underside,
Z = 2.8..11.3, extending from the USB end to **y = -7**. The bezel continues
these broad openings instead of adding close-fitting button holes.
Each side permits **1.5 mm button protrusion + 0.5 mm print clearance**, plus
**0.5 mm possible board shift**. The total lateral keepout spans 25.32 mm.
CAD checks both parts against both button keepouts: no intersection/preload.

**Assumption:** button tops remain below the rim underside, and the complete
buttons lie inside the stated Y cutbacks. Their actual Y/Z bounds and travel
are not supplied. Confirm them before fitting the front; if either button
touches the rim or lies outside a cutback, stop and revise after measurement.
The 1.5 mm measurement alone does not establish vertical clearance.

At the monitor mounting plane x = +/-12.66, nominal button tips retain 0.5 mm
clearance even with board shift, excluding adhesive thickness. This is **not
finger clearance**. Confirm access from the side or aperture using a suitable
blunt nonconductive tool, and full release of both buttons with monitor/cable
in place. If access is obstructed, remove the bezel for service; do not force
tools against the screen. USB plug height/projection remains a physical check.
The rear microSD socket requires removing the board.

## Exact C6 rear vent interface — do not alter

Both cases have **four rectangular 10 x 2 mm through-openings**, with:

- X = **-5..5**.
- Lower Y edges = **-8, -2, 4, 10**.
- Centres = **(0,-7), (0,-1), (0,5), (0,11)**: **6 mm pitch**.
- Common rear datum **Z = 0**, **2.8 mm straight tunnel depth**.
- Four-slot envelope **10 x 20 mm**.

The pattern and tunnel depth match the untouched C6 STEP exactly. They do not
merge into the USB notch or side adhesive land. Validation measures actual
STEP rear hole wires, rectangular areas, width/height, coordinates/pitch,
clear tunnels and adjoining floor depth. Physical accessory intrusion and
board-component clearances remain unknown. **Report any conflict rather than
resizing, moving or deleting a vent.** Keep all adhesive out of the openings.

## Fit-check and monitor mounting

1. Print both parts and dry-fit the locating lip **without electronics**.
   No snap action is intended; remove burrs rather than forcing the thin lip.
2. Unplug the board. Confirm its glass envelope fits the aperture, the underside
   components do not extend below its support feet, and the feet have usable
   floor contact lands outside the vents and USB notch.
3. Use removable electrically insulating adhesive only on the actual measured
   feet, not on components, flex, antenna or glass. Verify **0.5 mm installed pad
   thickness** or adjust `SUPPORT_PAD`. If stable retention cannot be achieved,
   stop: do not rely on the bezel to trap the screen. Feet/hole locations remain
   unverified; there are deliberately no printed board posts or screws.
4. Dry-fit the front. Confirm the measured glass top is at Z = 11.0 and at least
   0.3 mm below the rim underside; no glass/button preload. Check USB insertion,
   full button operation/release, cable-pull retention and bezel removal.
5. Only then bond the **rear tray** to the monitor edge with a **6 x 20 mm**
   adhesive strip on x = +/-12.66, y = -5..15, z = 1..7. This is below the
   Z = 8 seam, so the front remains removable without unbonding the tray.
   Screen faces the viewer; bonded side is perpendicular to the screen.
6. Verify thermal behaviour, antenna performance, adhesive peel strength and
   cable strain relief. Keep away from monitor exhaust and support the cable
   independently. Neither retention nor adhesive strength has been tested.

## Files, printing and validation

- `clearance-tray.stl` / `.step`: revised rear tray (legacy filename retained).
- `front-bezel.stl` / `.step`: new removable front.
- `s3b-monitor-tray.FCStd`: **both parts** in assembled coordinates.
- `build_enclosure.py`: measured stack and parametric generator.
- `validation.json`: generated dimensions, evidence/assumptions, part and
  assembly tests, button keepouts and exact vent measurements.

STLs are oriented for printing: rear floor down, front face down. Start with
PLA, 0.2 mm layers, four walls, 20–30% infill and the filament's profile.
No intended bridges or supports in these orientations. Inspect thin lip/skirt
paths in the slicer and do not assume repeated-service durability.

From the repository root:

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware\enclosure\esp32-s3-lcd-1.47b\build_enclosure.py
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware\enclosure\verify_enclosures.py
```

Checks: single valid solids, closed connected manifold meshes, no mesh
self-intersections, print-bed placement, STEP/mesh volume/extents, FCStd/STEP
agreement, zero assembled overlap, sampled straight-forward removal, 0.3 mm
**assumed-envelope** glass clearance, no nominal button interference, and the
unchanged C6 rear vent interface and preservation hashes.

These are CAD checks, **not evidence of actual glass fit, board retention,
button travel/access, thermal performance or future accessory compatibility**.
