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

Use four M2.5 x 8 mm thread-forming screws for plastic through the 2.8 mm rear
clearance holes into the 2.1 mm pilot holes in the shell. Check pilot fit with
one screw first; printer tolerances may require adjusting the diameter.
Do not overtighten. These screws join printed parts; they do not engage the PCB.

Insert the device from the rear, USB downward. The perimeter rim retains the
module at the front. Before fastening the cover, check that the board sits flat,
the connector accepts your actual USB plug, and the buttons are reachable.
Any anti-rattle shims should contact only the metal frame, never the display,
components, or exposed pins. Do not force the cover onto the board. Internal
support pads may need adjustment after the first physical fit test.

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
