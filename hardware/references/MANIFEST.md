# Manufacturer evidence manifest

## Optional rear pin-access housings: 2026-09-18

The additive [pin-access variants](../enclosure/pin-access/README.md) use two
4 mm-wide slots through the original 2.8 mm floor. Standard C6 geometry and all mount parts are preserved. Both S3 backs now
have four printed support pads and M2 screw access; pin tips are intended
to remain inside the rear plane.
Actual header height/projection was not measured. Available support-foot to
rear-plane distances are 4.3 mm C6 and 5.3 mm S3, using the current stacks.

- C6: the archived 2024-12-21 dimension PDF shows **11 pins per row** at
  2.54 mm pitch, 22.86 mm between rows, a 43.5 mm PCB and first pin 9.33 mm
  from the USB edge. Slot centres X = +/-11.43; Y limits -14.42..14.98.
  These header coordinates are provisional for the user's measured board
  revision. The carrier's 1x10 sockets are not the source for these slots.
  Current manufacturer resources: https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64/Resources-And-Documents
- S3 B: the archived dimensioned JPG shows **9 pins per row** at 2.54 mm
  pitch, 17.78 mm between rows, 36.37 mm PCB length and nearest pin 11.31 mm
  from the USB edge. Slot centres X = +/-8.89; Y limits -8.715..15.285.
  Manufacturer resource: https://www.waveshare.com/wiki/ESP32-S3-LCD-1.47B

Slot widths/length allowances are design choices, not manufacturer connector
fit claims. Full nominal 2.54 mm-wide header bodies clear the slots; verify
actual housings, solder fillets, tip recess and connector reach in a first print.
The existing arms clear recessed pins but must be removed to insert header leads.

This manifest records the primary external evidence used for the C6-only Rev A0
carrier and board-specific standalone enclosures. Carrier source documents were
retrieved or reviewed on **2026-09-02**. URLs are retained in
the schematic properties and generated BOM so the design can be rechecked
when a part revision changes.

| Design item | Manufacturer / exact MPN | Primary source | Evidence used |
|---|---|---|---|
| Target module | Waveshare ESP32-C6 Touch AMOLED 1.64 | [`ESP32-C6-Touch-AMOLED-1.64-schematic.pdf`](manufacturer/ESP32-C6-Touch-AMOLED-1.64-schematic.pdf), [`dimensions`](manufacturer/ESP32-C6-Touch-AMOLED-1.64-dimensions-20241221.pdf), [`STEP`](manufacturer/esp32-c6-touch-amoled-1_64.stp) | P1/P2 header assignments, mounting-hole centers, module outline, and conservative carrier keepout. |
| USB-C receptacle | Global Connector Technology USB4105-GF-A | [manufacturer drawing](https://gct.co/files/drawings/usb4105.pdf) | Receptacle footprint/pin functions and power-only CC termination. |
| USB protection | Bourns MF-MSMF050-2; Littelfuse SMF5.0A; Diodes Inc. B140-13-F | [PPTC](https://www.bourns.com/docs/product-datasheets/mf-msmf.pdf), [TVS](https://www.littelfuse.com/assetdocs/tvs-diodes-smf-datasheet?assetguid=7eb8a5b6-bdd0-4561-8f19-0c3cc6f9b2af), [Schottky](https://www.diodes.com/assets/Datasheets/ds13002.pdf) | USB 5 V protection and reverse-current isolation. |
| Haptic switch | Alpha & Omega AO3400A; Diodes Inc. B5819W-7-F | [AO3400A](https://aosmd.com/res/data_sheets/AO3400A.pdf), [B5819W](https://www.diodes.com/assets/Datasheets/ds30217.pdf) | SOT-23 G/S/D mapping, 2.5 V gate-drive suitability, and inductive flyback topology. |
| Headers/connectors | Samtec SSW-110-02-G-S; JST B2B-PH-K-S(LF)(SN) | [Samtec](https://suddendocs.samtec.com/prints/ssw-1xx-xx-xxx-x-xx-xx-mkt.pdf), [JST PH](https://www.jst-mfg.com/product/pdf/eng/ePH.pdf) | Socket and haptic connector form factors. |
| Passives | Yageo RC0603FR series; Murata GRM188R71H104KA93D | [Yageo](https://www.yageo.com/upload/media/product/productsearch/datasheet/rchip/PYu-RC_Group_51_RoHS_L_16.pdf), [Murata](https://search.murata.co.jp/Ceramy/image/img/A01X/G101/ENG/GRM188R71H104KA93-01.pdf) | 0603 package, resistor values, and 100 nF X7R bypass selection. |

## Design calculations and bounds

- **USB-C role:** each CC pin has 5.1 kOhm to GND, declaring a default-current
  sink. No data or sideband signal is routed.
- **Input current:** F1 is a 500 mA hold / 1 A trip PPTC. The design therefore
  has a provisional 500 mA input limit; it is not a measured module-current
  guarantee.
- **Haptic load:** the specified 150 mA limit is below the provisional input
  ceiling and preserves headroom for the unmeasured display module. The
  flyback diode is fitted for inductive loads.
- **Gate safety:** a 100 kOhm pulldown sets the MOSFET gate low when GPIO6 is
  floating or the module is unpowered. The 100 Ohm series resistor limits
  transient gate current.
- **PCB limits:** 0.20 mm track/clearance is the checked project minimum;
  power nets use 0.50 mm routing. This is a low-voltage layout constraint,
  not a certified current or thermal rating.

Manufacturer PDFs and the imported Waveshare mechanical models are design
evidence. They do not replace a physical Rev A0 fit, current, thermal, RF, or
functional validation.

## Standalone C6 enclosure: measured board takes precedence

The preserved [C6 enclosure](../enclosure/esp32-c6-touch-amoled-1.64/README.md)
uses the user's **9.3 mm standoff-foot-to-glass height** and approximate symmetric
**22 x 39 mm M2 pattern**, rather than the older carrier reference drawing's
38.5 mm pitch. Its housing remains **42 x 56 x 15.1 mm**. The carrier was not
changed to match this measured revision. CAD, render source, previews and print
artifacts were moved byte-for-byte; hashes are in
[`c6-preservation.json`](../enclosure/c6-preservation.json).

## ESP32-S3-LCD-1.47B: reviewed 2026-09-18

Exact model: **ESP32-S3-LCD-1.47B**, 172 x 320 ST7789 LCD, no touch.
The [S3 tray](../enclosure/esp32-s3-lcd-1.47b/README.md) targets the bare B board
without soldered headers, not the non-B variant or B-M.

| Evidence | Primary URL | Use / limits |
|---|---|---|
| Product | https://www.waveshare.com/ESP32-S3-LCD-1.47B.htm | Exact B identity, variants, display and mechanical images |
| Wiki revision 105396 | https://www.waveshare.com/w/index.php?title=ESP32-S3-LCD-1.47B&oldid=105396 | Board features and authoritative resource links |
| [Outline image](manufacturer/esp32-s3-lcd-1.47b/dimensions.jpg) | https://www.waveshare.com/w/upload/9/91/ESP32-S3-LCD-1.47B-details-size.jpg | Dimensioned 36.37 x 20.32 mm PCB outline; no stack-height section |
| [Component image](manufacturer/esp32-s3-lcd-1.47b/components.jpg) | https://www.waveshare.com/w/upload/0/05/ESP32-S3-LCD-1.47B-details-intro.jpg | USB short end, adjacent opposite-edge BOOT/RESET, rear microSD, opposite-end antenna; qualitative positions only |
| [Schematic PDF](manufacturer/esp32-s3-lcd-1.47b/schematic.pdf) | https://files.waveshare.com/wiki/ESP32-S3-LCD-1.47B/ESP32-S3-LCD-1.47B_schematic_diagram.pdf | Archived exact-board electrical reference, not a mechanical drawing |
| Demo ZIP (external, not vendored) | https://files.waveshare.com/wiki/ESP32-S3-LCD-1.47B/ESP32-S3-LCD-1.47B-Demo.zip | Exact-board manufacturer software reference; not enclosure fit evidence |

SHA-256 of downloaded evidence:

```text
127f63c20b5a928170cd5011e299c1c061d123957d0a2c41b303a1b62566b360  dimensions.jpg
3df81be8ef019e9c8392b640c5711504a2d0e3e500a62d0604e5c9b6b9ca7734  components.jpg
43738d1480ef9c983bca3e7f1f7ad852c288a1bd00f1621f9ac3e6974e7539fd  schematic.pdf
```

### S3 printed supports and M2 retention

The S3 manufacturer's dimensioned image gives the USB-end hole offsets as
2.40 mm from the short edge and 2.00 mm from the long edges, and antenna-end
hole offsets as 1.97 mm and 3.52 mm respectively. Applied to the centred
20.32 x 36.37 mm board with USB toward -Y, these give USB centres
(+/-8.16, -15.785) and antenna centres (+/-6.64, +16.215) mm.
Both S3 backs now have 4 mm-diameter, 2.5 mm-high printed pads with 2.3 mm
M2 clearance holes and 4.2 x 1.3 mm rear head recesses. This replaces the
former adhesive support allowance. After a physical USB cable fit check, the
user requested 2 mm more support height: pads rise from 0.5 to 2.5 mm, and
the rear seam, lip, catches and glass rise 2 mm. USB opening bottom remains
Z = 3.6 mm, with its top raised to 11.9 mm. The front shape is reusable.
Screw thread depth and real board fit require a dry fit. The pin-access back
uses rounded slot ends and retains at least 0.5 mm annular material around
the clearance holes above the head recess. The C6 hole pattern is not reused.

### S3 user-measured button protrusion, 2026-09-18

The user reports that BOOT/RESET protrude from the display by approximately
**1.5 mm**. This is user-measured evidence, not a value taken from the
manufacturer drawing, and is applied only to the S3 prototype. The user subsequently requested 3 mm additional internal width. The pocket is
now 24.32 mm wide throughout, 3 mm wider than the 21.32 mm board-clearance
pocket, with no separate button recesses. A centred board and its nominal
1.5 mm button protrusions occupy 23.32 mm, leaving 0.5 mm on each side.
Secure the board on its printed pads with M2 screws; this gap cannot also be
claimed as extra movement tolerance. Button height/travel remain unmeasured.
The outer housing, display aperture, C6 geometry and shared vents are unchanged.

### User-required common rear vents, 2026-09-18

The user requires the C6 rear opening dimensions to remain unchanged for future
use. The preserved C6 generator and `back-cover.step` are the dimensional source:
**four 10 x 2 mm rectangular through-openings**, x = -5..5, lower Y edges
**-8, -2, 4, 10**, centres at y = -7, -1, 5, 11, **6 mm pitch**, and **2.8 mm**
rear floor/tunnel depth from the common Z = 0 rear exterior datum.

The S3 copies this complete interface without scaling or moving the vents.
Its floor remains 2.8 mm; the measured-stack revision below changes outside
depth to 14.5 mm. C6 files remain unchanged.
The verifier measures both STEP rear faces, rectangular opening areas,
coordinates/pitch, clear tunnels and adjoining floor depth and requires equality.
The nominal S3 tray has space for the pattern. The released mounts stop inside
the floor; real board-component and printed fit clearances remain unverified. Any physical conflict must be
reported rather than resolved by altering this interface.

### User-measured S3 stack and removable front, 2026-09-18

In response to the explicit question "height from the rear mounting feet/support
surface to the top of the display glass", the user answered **7.7 mm**.
This supersedes the open-tray's unmeasured 10 mm pocket allowance. The two-piece
prototype stack is **2.8 floor + 2.5 printed support pads + 7.7
measured module + 0.3 glass clearance + 1.2 rim = 14.5 mm**.

The front seats on a rear-tray shoulder, not the glass. A recessed lip locates it; four C6-style spring catches retain the front.
Rear tool slots release the catches before bezel removal.
Board screw positions follow the manufacturer dimensioned image above. The glass/active-area outline and its
X/Y offset are **not dimensioned in the available source**. Therefore the
21.32 x 37.37 mm aperture exposes the full PCB envelope plus clearance, rather
than pretending a smaller glass-retaining opening is validated. Glass width,
length, glass-to-PCB edge offsets and active-area bounds are the exact missing
measurements for that tighter rim.

CAD checks validate two solids without overlap, sampled removal, 0.3 mm
clearance to an assumed full-pocket glass envelope at the measured Z, and
button keepouts on both parts. Internal button clearance extends up to the rim underside at Z = 11.3; actual
button tops must be below this and their Y bounds within the board-length cavity. Physical glass fit, button travel, retention, support-pad
thickness and printed mount retention still require a dry fit.

The revised S3 shares the C6 42 x 56 mm rounded outline and chamfered front.
Both released arm STEP files pass CAD compatibility checks on both trays.
Their tips insert 2.6 mm into the 2.8 mm floor, leaving 0.2 mm before its inner
surface; middle vents remain open. This does not prove thermal performance.
