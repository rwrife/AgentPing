# Firmware third-party provenance

AgentPing's repository license is MIT. The firmware also links or adapts the following independently licensed software.

## Waveshare ESP32-C6-Touch-AMOLED-1.64 evidence

- Repository: <https://github.com/waveshareteam/ESP32-C6-Touch-AMOLED-1.64>
- Pinned evidence commit: `b90e28c953c1fc882258fa8dbd56b7706bc888b7`
- Commit date: 2026-03-18
- Upstream license: Apache License 2.0
- Relevant upstream paths:
  - `hardware/schematics/ESP32-C6-Touch-AMOLED-1.64-schematic.pdf`
  - `Examples/ESP-IDF-V5.5.2/06_LVGL_Demo/components/ESP32-C6-Touch-AMOLED-1.64/`
  - `Examples/ESP-IDF-V5.5.2/06_LVGL_Demo/components/espressif__esp_lcd_touch_ft3168/`
- Product documentation: <https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64>

AgentPing reimplemented the small board-initialization layer in `src/board.cpp` from the documented values and adapted the FT3168-compatible touch component under `components/ft3168/`. The adapted files retain Apache-2.0 SPDX/provenance notices and are modified for smaller scope and current Espressif APIs. A copy of Apache-2.0 is included at `components/ft3168/LICENSE`.

### Hardware revision decision

The current product page, schematic, and ESP-IDF 5.5.2 example agree on CO5300, FT6146, OLED reset GPIO20, touch interrupt GPIO1, QSPI GPIO10/11/4/5/7/19, and I²C GPIO18/8. The repository's older Arduino 3.2.0 LVGL example identifies SH8601 and reset GPIO21. AgentPing treats that Arduino example as a legacy/incompatible board revision and does not mix its controller/reset assumptions into the current target.

## Managed Espressif/LVGL components

Exact resolved versions and component hashes are recorded in `firmware/dependencies.lock`; downloaded sources are generated under `firmware/managed_components/` and are not committed.

- Espressif CO5300 LCD driver 2.0.3 — Apache-2.0
- Espressif LCD touch abstraction 1.2.1 — Apache-2.0
- Espressif WebSocket client 1.5.0 — Apache-2.0
- Espressif IDF 5.5.0 — Apache-2.0 and component-specific licenses
- LVGL 9.3.0 — MIT

PlatformIO's `espressif32@6.12.0`, the component manifest, and `dependencies.lock` are all pinned. A clean build retrieves the managed sources and their bundled license files from the Espressif Component Registry.
