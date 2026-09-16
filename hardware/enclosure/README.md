# Pixel Pal monitor pod - fit-check prototype

Two-piece printable enclosure for the Waveshare ESP32-C6-Touch-AMOLED-1.64.
Dimensions are millimetres. This is an unprinted prototype, not a verified production fit.

## Files

- `front-shell.stl`: shared shell, already oriented front-down on the print bed.
- `back-cover.stl`: plain removable back; the same housing works on either monitor edge.
- STEP files: individual editable solids in assembled coordinates.
- `pixel-pal-monitor-pod.FCStd`: both parts in assembled coordinates.
- `preview.png`: illustrative assembly; the black display and cyan face are placeholders, not printed features.
- `build_enclosure.py`: editable dimensions and reproducible FreeCAD generator.
- `validation.json`: solid/mesh validation and actual bounding dimensions.

## Size and mounting

Pod: **42 wide x 56 tall x 18.8 deep**, with no tabs or wings.
The screen faces the viewer. One flat vertical side of the housing bonds directly
to the monitor's side bezel, perpendicular to the screen: edge-to-edge mounting.

Use a **12 mm deep x 30 mm tall adhesive pad** on either side wall. Position it
in the uninterrupted flat area above the button opening, away from the rounded
front shoulder. In model coordinates this area is x = +/-21, y = -8 to +22,
z = 3 to 15. The 12 mm bonding width fits within the measured 19.05 mm bezel.
The pad bonds only to the shell, so the rear cover remains removable. Set the
front-to-back alignment to suit the monitor, allowing for adhesive thickness.

The shell has a 29.92 x 45 mm internal pocket, a 27.2 x 42.2 front opening,
a 16 x 11 mm bottom USB opening, and generous side button openings.
No battery is included. Rear ventilation slots remain unobstructed.

## Print and assembly

Selected material: white PLA for contrast with the black display and bright face colors.
Starting print settings: 0.2 mm layers, four walls, 20-30% infill.
Use your PLA manufacturer's temperature profile. Keep the adhesive side away
from hot exhaust vents; confirm that the monitor mounting area stays cool.
Print one shell and one back cover. STL files are in print orientation and millimetres;
check the slicer dimensions before printing. The shell's openings may require
local support depending on bridging performance. Keep support scars off the
front glass contact edge and adhesive face.

## M2 board mounting and snap closure

The user confirmed M2 threaded metal standoffs and no pin headers; the standoffs
clear the rear components. Four 2.3 mm clearance holes in the backplate align
with the manufacturer's 22 x 38.5 mm mounting pattern. Printed spacer pads carry
the standoffs, keeping pressure away from the PCB and components.

**Spacer height is provisional: 4.8 mm**, derived from the manufacturer's 9.6 mm
pin-free drawing envelope. Measure the actual screen-front-to-standoff-foot
height before final printing. Change STANDOFF_SPACER in the generator so the
display sits behind the bezel without pressure on the glass. Hole-pattern fit
also needs checking on the actual board revision.

Fasten the board to the backplate using M2 machine screws, then insert the
assembly into the front shell. Four cantilever catches engage pockets in the
shell. Release slots beside each catch are accessible from the rear; gently push
the catches inward while withdrawing the backplate. No shell screws are used.

Screw grip through the current backplate and spacers is 7.6 mm. Choose screw
length from that grip plus the measured usable thread engagement; do not bottom
screws in the metal standoffs. Do not force the shell closed if the screen touches.

The PLA snap beams are 1 mm thick and about 9 mm long, with a 0.2 mm nominal
engagement. Print a fit test before installing electronics: flex and fatigue
performance are not validated by CAD. The upright printed beams are sensitive
to layer adhesion; adjust fit rather than forcing a tight catch. The backplate
is a removable service part, not intended for frequent cycling.

## Source and remaining checks

Manufacturer source:
https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64/Resources-And-Documents

Drawing/STEP directory:
https://github.com/waveshareteam/ESP32-C6-Touch-AMOLED-1.64/tree/main/hardware/dimensions

The linked drawing is dated 2024-12-21 and depicts an older board layout;
confirm connector and button positions on the actual revision. The linked STEP
bounding box is 29.12 x 44.2 x 13.9 including rear protrusions. It informed the
pocket and depth; measured production dimensions remain authoritative.

CAD checks verify closed single solids, printable closed meshes, and no overlap
between the assembled shell and the back. They do not validate adhesive
strength, print tolerances, heat behaviour, or physical board retention.

Regenerate with:

```powershell
& 'C:\Program Files\FreeCAD 1.1\bin\python.exe' hardware/enclosure/build_enclosure.py
```

Front refinement: a 3 mm outer-edge radius rolls the face into the side walls; a 0.65 mm radius softens the screen opening. The internal module pocket dimensions are retained. Regenerated STL/STEP files pass closed-solid and assembly-overlap checks.

The button on the bonded side may be inaccessible once mounted; check the required
button access before choosing the monitor side.

Render the white PLA preview with Blender 5.0:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.0\blender.exe' -b --python hardware/enclosure/render_preview.py
```
