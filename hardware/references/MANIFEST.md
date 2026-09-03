# Manufacturer evidence manifest

This manifest records the primary external evidence used for Rev A0. Source
documents were retrieved or reviewed on **2026-09-02**. URLs are retained in
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
