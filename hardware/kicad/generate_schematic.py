#!/usr/bin/env python3
"""Generate the editable AgentPing carrier schematic from reviewed design data."""

from __future__ import annotations

import argparse
import os
from pathlib import Path


def find_symbol_dir(explicit: str | None) -> Path:
    candidates = [
        explicit,
        os.environ.get("KICAD_SYMBOL_DIR"),
        os.environ.get("KICAD9_SYMBOL_DIR"),
        "/usr/share/kicad/symbols",
        "/usr/local/share/kicad/symbols",
    ]
    for candidate in candidates:
        if candidate and Path(candidate).is_dir():
            return Path(candidate)
    raise SystemExit("KiCad symbol libraries not found; pass --symbol-dir")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--symbol-dir")
    parser.add_argument("--output", default=str(Path(__file__).with_name("agentping-carrier.kicad_sch")))
    args = parser.parse_args()

    symbol_dir = find_symbol_dir(args.symbol_dir)
    os.environ["KICAD_SYMBOL_DIR"] = str(symbol_dir)
    from kicad_sch_api import create_schematic, get_symbol_cache

    get_symbol_cache().discover_libraries([str(symbol_dir)])
    sch = create_schematic("AgentPing optional desk carrier")
    sch.set_paper_size("A3")
    sch.set_title_block(
        title="AgentPing Waveshare ESP32-C6 Touch AMOLED 1.64 carrier",
        date="2026-09-02",
        rev="A0",
        company="Open hardware design — rwrife/AgentPing",
        comments={
            1: "USB 5 V SELV only; optional carrier; unmodified Waveshare RF module",
            2: "500 mA input ceiling; haptic load <=150 mA; verify module current on bench",
            3: "Manufacturer/MPN/Datasheet properties are the BOM source of truth",
            4: "ERC/DRC are tool evidence only; no fabrication or physical validation claimed",
        },
    )

    components: dict[str, object] = {}
    used_labels: set[tuple[float, float, str]] = set()
    used_nc: set[tuple[float, float]] = set()

    def add(
        lib_id: str,
        ref: str,
        value: str,
        pos: tuple[float, float],
        footprint: str,
        *,
        manufacturer: str,
        mpn: str,
        datasheet: str,
        checked: str,
        unit_cost: str,
        notes: str,
        supplier: str = "Estimate only — re-source before order",
        supplier_pn: str = "Not checked",
        in_bom: bool = True,
    ):
        component = sch.components.add(
            lib_id, ref, value, position=pos, footprint=footprint, rotation=0
        )
        properties = {
            "Manufacturer": manufacturer,
            "MPN": mpn,
            "Supplier": supplier,
            "Supplier PN": supplier_pn,
            "Datasheet": datasheet,
            "Datasheet Checked UTC": checked,
            "Estimated Unit Cost USD": unit_cost,
            "Cost Basis": "Preliminary single-board engineering estimate; not a quote or stock check",
            "BOM Comments": notes,
        }
        for key, val in properties.items():
            component.set_property(key, val)
        component.in_bom = in_bom
        components[ref] = component
        return component

    def pin_point(ref: str, number: str):
        component = components[ref]
        pin = component.get_pin(number)
        if pin is None:
            raise ValueError(f"{ref} pin {number} not found")
        from kicad_sch_api.core.types import Point
        return Point(component.position.x + pin.position.x, component.position.y - pin.position.y)

    def connect(ref: str, pin: str, net: str) -> None:
        p = pin_point(ref, pin)
        key = (round(p.x, 6), round(p.y, 6), net)
        if key not in used_labels:
            sch.labels.add(net, (p.x, p.y))
            used_labels.add(key)

    def no_connect(ref: str, pin: str) -> None:
        p = pin_point(ref, pin)
        key = (round(p.x, 6), round(p.y, 6))
        if key not in used_nc:
            sch.no_connects.add((p.x, p.y))
            used_nc.add(key)

    checked = "2026-09-02"
    samtec_doc = "https://suddendocs.samtec.com/prints/ssw-1xx-xx-xxx-x-xx-xx-mkt.pdf"
    add(
        "Connector_Generic:Conn_01x10", "J1", "WAVESHARE MODULE P1",
        (45, 58), "Connector_PinSocket_2.54mm:PinSocket_1x10_P2.54mm_Vertical",
        manufacturer="Samtec", mpn="SSW-110-02-G-S", datasheet=samtec_doc,
        checked=checked, unit_cost="2.00",
        notes="P1 female socket. Pin 1 SDA, 2 SCL, 3 GND, 4 3V3, 5 GPIO0, 6 GPIO2, 7 GPIO3, 8 GPIO6, 9 GND, 10 VBAT. Only GPIO6/GND used by carrier.",
    )
    add(
        "Connector_Generic:Conn_01x10", "J2", "WAVESHARE MODULE P2",
        (80, 58), "Connector_PinSocket_2.54mm:PinSocket_1x10_P2.54mm_Vertical",
        manufacturer="Samtec", mpn="SSW-110-02-G-S", datasheet=samtec_doc,
        checked=checked, unit_cost="2.00",
        notes="P2 female socket. Pin 1 RXD, 2 TXD, 3 GND, 4 3V3, 5 GPIO9, 6 GPIO23, 7 USB D+, 8 USB D-, 9 GND, 10 USB 5V. Carrier uses GND and diode-isolated USB 5V only.",
    )
    add(
        "Connector:USB_C_Receptacle_USB2.0_16P", "J3", "USB-C POWER ONLY",
        (130, 55), "Connector_USB:USB_C_Receptacle_GCT_USB4105-xx-A_16P_TopMnt_Horizontal",
        manufacturer="Global Connector Technology", mpn="USB4105-GF-A",
        datasheet="https://gct.co/files/drawings/usb4105.pdf", checked=checked,
        unit_cost="1.10",
        notes="5 V power-only UFP. CC1/CC2 each have 5.1k Rd. D+/D-/SBU are intentionally NC; shell to GND.",
    )
    for ref, pos, cc in [("R1", (163, 43), "CC1"), ("R2", (177, 43), "CC2")]:
        add(
            "Device:R", ref, "5.1k 1%", pos, "Resistor_SMD:R_0603_1608Metric",
            manufacturer="Yageo", mpn="RC0603FR-075K1L",
            datasheet="https://www.yageo.com/upload/media/product/productsearch/datasheet/rchip/PYu-RC_Group_51_RoHS_L_16.pdf",
            checked=checked, unit_cost="0.02", notes=f"USB-C {cc} Rd to GND.",
        )
    add(
        "Device:D_TVS", "D1", "SMF5.0A", (163, 58), "Diode_SMD:D_SOD-123F",
        manufacturer="Littelfuse", mpn="SMF5.0A",
        datasheet="https://www.littelfuse.com/assetdocs/tvs-diodes-smf-datasheet?assetguid=7eb8a5b6-bdd0-4561-8f19-0c3cc6f9b2af",
        checked=checked, unit_cost="0.30", notes="5 V VBUS transient clamp; place beside J3 with short ground return.",
    )
    add(
        "Device:Polyfuse", "F1", "500mA HOLD / 1A TRIP", (185, 58), "Fuse:Fuse_1812_4532Metric",
        manufacturer="Bourns", mpn="MF-MSMF050-2",
        datasheet="https://www.bourns.com/docs/product-datasheets/mf-msmf.pdf",
        checked=checked, unit_cost="0.07", notes="500 mA hold PPTC enforces the provisional USB default-current ceiling.",
    )
    add(
        "Device:D_Schottky", "D2", "B140-13-F", (207, 58), "Diode_SMD:D_SMA",
        manufacturer="Diodes Incorporated", mpn="B140-13-F",
        datasheet="https://www.diodes.com/assets/Datasheets/ds13002.pdf",
        checked=checked, unit_cost="0.09", notes="Series reverse-current block; carrier USB must not be back-fed by the module USB connector.",
    )
    add(
        "Device:C", "C1", "100nF 50V X7R", (226, 58), "Capacitor_SMD:C_0603_1608Metric",
        manufacturer="Murata", mpn="GRM188R71H104KA93D",
        datasheet="https://search.murata.co.jp/Ceramy/image/img/A01X/G101/ENG/GRM188R71H104KA93-01.pdf",
        checked=checked, unit_cost="0.02", notes="High-frequency bypass only; module already carries its specified VCC bulk capacitance.",
    )

    add(
        "Device:R", "R3", "100R 1%", (120, 112), "Resistor_SMD:R_0603_1608Metric",
        manufacturer="Yageo", mpn="RC0603FR-07100RL",
        datasheet="https://www.yageo.com/upload/media/product/productsearch/datasheet/rchip/PYu-RC_Group_51_RoHS_L_16.pdf",
        checked=checked, unit_cost="0.02", notes="GPIO6 gate stopper for haptic switch.",
    )
    add(
        "Device:R", "R4", "100k 1%", (140, 126), "Resistor_SMD:R_0603_1608Metric",
        manufacturer="Yageo", mpn="RC0603FR-07100KL",
        datasheet="https://www.yageo.com/upload/media/product/productsearch/datasheet/rchip/PYu-RC_Group_51_RoHS_L_16.pdf",
        checked=checked, unit_cost="0.02", notes="MOSFET gate pulldown keeps haptic output off through reset/unpowered module states.",
    )
    add(
        "Device:Q_NMOS_GSD", "Q1", "AO3400A", (166, 112), "Package_TO_SOT_SMD:SOT-23",
        manufacturer="Alpha & Omega Semiconductor", mpn="AO3400A",
        datasheet="https://aosmd.com/res/data_sheets/AO3400A.pdf",
        checked=checked, unit_cost="0.10", notes="30 V N-MOSFET low-side switch; pin 1 G, 2 S, 3 D. RDS(on) is specified at 2.5 V gate drive. Limit connector load to 150 mA.",
    )
    add(
        "Device:D_Schottky", "D3", "B5819W-7-F", (190, 105), "Diode_SMD:D_SOD-123",
        manufacturer="Diodes Incorporated", mpn="B5819W-7-F",
        datasheet="https://www.diodes.com/assets/Datasheets/ds30217.pdf",
        checked=checked, unit_cost="0.10", notes="Flyback diode across optional inductive haptic load; cathode to protected 5 V.",
    )
    add(
        "Device:C", "C2", "100nF 50V X7R", (204, 112), "Capacitor_SMD:C_0603_1608Metric",
        manufacturer="Murata", mpn="GRM188R71H104KA93D",
        datasheet="https://search.murata.co.jp/Ceramy/image/img/A01X/G101/ENG/GRM188R71H104KA93-01.pdf",
        checked=checked, unit_cost="0.02", notes="Optional motor-terminal RF suppression capacitor; evaluate acoustic/haptic response on bench.",
    )
    add(
        "Connector_Generic:Conn_01x02", "J4", "HAPTIC 5V <=150mA", (226, 112),
        "Connector_JST:JST_PH_B2B-PH-K_1x02_P2.00mm_Vertical",
        manufacturer="JST", mpn="B2B-PH-K-S(LF)(SN)",
        datasheet="https://www.jst-mfg.com/product/pdf/eng/ePH.pdf",
        checked=checked, unit_cost="0.15", notes="Pin 1 protected 5 V, pin 2 switched low side. Mating PH housing/contacts and motor are external BOM items.",
    )

    # Module interfaces. Unused exposed pins are intentionally not routed by this carrier.
    connect("J1", "3", "GND")
    connect("J1", "8", "GPIO6_HAPTIC")
    connect("J1", "9", "GND")
    for pin in ["1", "2", "4", "5", "6", "7", "10"]:
        no_connect("J1", pin)
    connect("J2", "3", "GND")
    connect("J2", "9", "GND")
    connect("J2", "10", "+5V_MODULE")
    for pin in ["1", "2", "4", "5", "6", "7", "8"]:
        no_connect("J2", pin)

    for pin in ["A4", "A9", "B4", "B9"]:
        connect("J3", pin, "VBUS")
    for pin in ["A1", "A12", "B1", "B12", "S1"]:
        connect("J3", pin, "GND")
    connect("J3", "A5", "CC1")
    connect("J3", "B5", "CC2")
    for pin in ["A6", "A7", "A8", "B6", "B7", "B8"]:
        no_connect("J3", pin)
    connect("R1", "1", "CC1"); connect("R1", "2", "GND")
    connect("R2", "1", "CC2"); connect("R2", "2", "GND")
    connect("D1", "1", "VBUS"); connect("D1", "2", "GND")
    connect("F1", "1", "VBUS"); connect("F1", "2", "VBUS_FUSED")
    connect("D2", "2", "VBUS_FUSED"); connect("D2", "1", "+5V_MODULE")
    connect("C1", "1", "+5V_MODULE"); connect("C1", "2", "GND")

    connect("R3", "1", "GPIO6_HAPTIC"); connect("R3", "2", "HAPTIC_GATE")
    connect("R4", "1", "HAPTIC_GATE"); connect("R4", "2", "GND")
    connect("Q1", "1", "HAPTIC_GATE"); connect("Q1", "2", "GND"); connect("Q1", "3", "HAPTIC_LOW")
    connect("D3", "1", "+5V_MODULE"); connect("D3", "2", "HAPTIC_LOW")
    connect("C2", "1", "+5V_MODULE"); connect("C2", "2", "HAPTIC_LOW")
    connect("J4", "1", "+5V_MODULE"); connect("J4", "2", "HAPTIC_LOW")

    # External source declarations for ERC only.
    for index, (net, pos) in enumerate([
        ("VBUS", (156, 72)), ("VBUS_FUSED", (185, 72)),
        ("+5V_MODULE", (212, 72)), ("GND", (150, 132)),
    ], start=1):
        add(
            "power:PWR_FLAG", f"#FLG0{index}", "PWR_FLAG", pos, "",
            manufacturer="N/A", mpn="PCB_NET_FLAG", datasheet="~", checked=checked,
            unit_cost="0", notes="ERC declaration only; not a physical BOM item.",
            supplier="N/A", supplier_pn="N/A", in_bom=False,
        )
        connect(f"#FLG0{index}", "1", net)

    for index, (ref, net) in enumerate([
        ("TP1", "VBUS"), ("TP2", "+5V_MODULE"), ("TP3", "GND"),
        ("TP4", "GPIO6_HAPTIC"), ("TP5", "HAPTIC_GATE"), ("TP6", "HAPTIC_LOW"),
    ]):
        add(
            "Connector:TestPoint", ref, net, (70 + (index % 3) * 28, 165 + (index // 3) * 16),
            "TestPoint:TestPoint_Pad_D1.5mm", manufacturer="N/A", mpn="PCB_TEST_PAD",
            datasheet="~", checked=checked, unit_cost="0",
            notes="Unpopulated labeled PCB test pad.", supplier="N/A", supplier_pn="N/A", in_bom=False,
        )
        connect(ref, "1", net)

    sch.add_text("WAVESHARE MODULE SOCKETS (MECHANICALLY VERIFIED)", (30, 24), size=1.8, bold=True)
    sch.add_text("USB-C 5 V POWER ONLY / PROTECTION", (118, 24), size=1.8, bold=True)
    sch.add_text("FAIL-OFF OPTIONAL HAPTIC OUTPUT", (105, 92), size=1.8, bold=True)
    sch.add_text("J1/P1: 1 SDA  2 SCL  3 GND  4 3V3  5 GPIO0  6 GPIO2  7 GPIO3  8 GPIO6  9 GND  10 VBAT", (25, 82), size=1.0)
    sch.add_text("J2/P2: 1 RXD  2 TXD  3 GND  4 3V3  5 GPIO9  6 GPIO23  7 USB+  8 USB-  9 GND  10 USB 5V", (25, 86), size=1.0)
    sch.add_text("Do not attach provider credentials. GPIO6 only drives a local transistor gate; timeout/reset state is OFF.", (105, 145), size=1.0)
    sch.add_text("STATIC DESIGN EVIDENCE ONLY — module current, inrush, motor current, vibration, RF, thermals, and enclosure fit require physical validation.", (25, 205), size=1.1, bold=True)

    issues = sch.validate()
    errors = [issue for issue in issues if getattr(issue, "severity", "") == "error"]
    if errors:
        raise SystemExit("schematic API validation failed: " + "; ".join(str(x) for x in errors))
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    sch.save_as(output)
    print(f"generated {output} with {len(components)} symbols; validation issues={len(issues)} errors=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
