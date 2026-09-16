# USB character bring-up

This dedicated `character_usb` firmware profile plays baked Pixel Pal idle and
wave clips using LVGL. It does not start Wi-Fi, TLS, provisioning, or the existing
network transport. This is display/USB bring-up, not the final agent protocol.

The 240 x 384 RGB565 frame buffer occupies 184,320 bytes. Sixty frames are stored
as 16-bit run-length/color pairs in flash (see `assets/manifest.json`). The face
is baked for this first test; dynamic messages and per-agent faces come later.

Build and upload from the repository root:

```powershell
.\.venv-firmware\Scripts\pio.exe run -d firmware -e character_usb
.\.venv-firmware\Scripts\pio.exe run -d firmware -e character_usb -t upload --upload-port COM5
.\.venv-firmware\Scripts\python.exe tools/character_usb.py wave --port COM5
```

Commands are newline-delimited ASCII: `idle`, `wave`, `pause`, `resume`, `status`.
Unknown commands are rejected; overlong lines are discarded. They are preview
controls, not agent approval commands. The device acknowledges commands with
`PAL OK` and reports free heap periodically. USB is the hardware USB Serial/JTAG
console; the COM port may vary across PCs. The image is positioned above the connection caption. The demo uses a separate
6 MB application partition to hold the larger frames.

Rebuild assets with the simulator running:

```powershell
cd simulator
node scripts/bake-firmware.mjs
cd ..
python scripts/encode_pal_frames.py
```

The generator requires Playwright/Microsoft Edge and Pillow. Original character
files remain in the simulator. Keep flash backups outside the repository because
they may include device-specific configuration.

Bench acceptance: verify correct colors/orientation, visible animation, no reboot
loop, stable heap, and acknowledged USB commands. Serial logs alone cannot prove
that the physical display is visually correct.

## First hardware check (2026-09-16)

- Built and uploaded successfully to the ESP32-C6 on COM5.
- Application: 1,792,317 bytes of the 3 MB application partition; static RAM:
  77,572 bytes. The 76,800-byte decoded frame is allocated at runtime.
- Idle playback reported 59–60 frames per five seconds, with free heap steady
  at 262,276 bytes during the short serial observation.
- USB `wave`, `idle`, `pause`, `resume`, and `status` acknowledged successfully.
  The host helper leaves DTR/RTS deasserted to avoid resetting playback when
  connecting, and tolerates a partial buffered console line before an acknowledgment.
- An intermittent touch I2C NACK occurred without stopping playback. Touch
  reliability and physical display colors/orientation remain to be verified.
- Original 16 MB flash backed up before upload to
  `C:\Users\ryrife\AppData\Local\Temp\agentping-com5-before-animation.bin`.

## Larger character rendering

Frames are baked at 240 x 384, with closer idle framing and enough clearance
for the extended waving hand. All 60 generated frames pass the encoder round-trip
check and keep the visible character clear of the frame edges. Animation data
occupies 2,684,088 bytes of flash. The first hardware-check numbers above describe
the earlier, smaller demo. This remains baked animation rather than live 3D.

The larger build uploaded successfully on COM5: application 3,194,453 bytes
of 6,291,456 available; static RAM 77,572 bytes. Runtime idle reports 59 frames
per five seconds and 154,756 bytes free heap. USB state changes acknowledge.
The intermittent touch I2C read error is still present; display playback continues.
