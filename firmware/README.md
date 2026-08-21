# AgentPing display firmware baseline

This is a buildable ESP-IDF/PlatformIO skeleton for the **Waveshare ESP32-C6 Touch AMOLED 1.64** module. It currently initializes only the standard ESP-IDF logger and emits a JSON heartbeat. It does **not** yet initialize the AMOLED, touch controller, Wi-Fi, LVGL, or persistent settings; those are tracked by issue #5 and require validation against Waveshare's board documentation and BSP.

The PlatformIO environment uses the generic `esp32-c6-devkitc-1` board definition and the board-supported ESP-IDF framework because the current code exercises only ESP32-C6 core facilities. The generic profile is overridden to the module's manufacturer-documented 16 MB flash size. No vendor binary, unpinned platform fork, or guessed display/touch pin map is vendored.

Hardware source checked 2026-08-21: [Waveshare ESP32-C6-Touch-AMOLED-1.64 documentation](https://docs.waveshare.com/ESP32-C6-Touch-AMOLED-1.64) (features list 16 MB flash).

## Build

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt
platformio run -d firmware
```

Expected result: PlatformIO reports `SUCCESS` for `waveshare_esp32_c6_touch_amoled_1_64` and writes the firmware images under `firmware/.pio/build/`. This is a compile/static check, not physical display or touch validation.

## Flash and monitor (physical hardware required)

```bash
platformio run -d firmware --target upload
platformio device monitor -d firmware
```

Expected serial messages contain `"state":"baseline"` followed by periodic `"state":"alive"` heartbeats. Flashing and physical behavior are not covered by CI.
