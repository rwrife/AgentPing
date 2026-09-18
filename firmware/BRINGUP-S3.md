# ESP32-S3-LCD-1.47B bring-up

This is the **Type B, non-touch, 172 x 320 ST7789** board:
<https://www.waveshare.com/ESP32-S3-LCD-1.47B.htm>.
Do not use this firmware or enclosure with the non-B S3 model or the C6 AMOLED.
Compilation and geometry checks do not establish physical operation or fit.

## Hardware configuration

| Function | Configuration |
| --- | --- |
| LCD bus | SPI3, mode 0, 12 MHz (manufacturer ESP-IDF demo rate) |
| Clock / MOSI | GPIO40 / GPIO45 |
| CS / DC / RESET | GPIO42 / GPIO41 / GPIO39 |
| Backlight | GPIO46, active-high LEDC PWM, 4 kHz, 65% at startup |
| Panel mapping | 172 x 320 portrait, X offset 34, mirror X, BGR, inversion on |
| Pixel transfer | RGB565 little-endian, RAMCTRL `00 E8`; no software byte swap |
| USB | Native USB Serial/JTAG, VID:PID `303A:1001` |
| Flash / PSRAM | 16 MB flash; 8 MB PSRAM present but not required/enabled |
| BOOT / RESET | Retained for recovery; not robot controls |

The standard ESP-IDF ST7789 driver handles reset, addressing, and DMA transfers.
The board layer supplies the Type B power/porch/gamma register values from the
manufacturer's ESP-IDF demo. It sends both Power Control 1 bytes (`A4 A1`);
the vendor's IDF demo mistakenly specifies length 1 for that two-byte value,
while its Arduino example sends both. Sources are recorded in [THIRD_PARTY.md](THIRD_PARTY.md).

## Build and flash

From the repository root, after installing `firmware/requirements-ci.txt`:

```powershell
.\companion\robot.cmd stop
.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb_s3
.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb_s3 -t upload --upload-port COM5
```

Replace COM5 with the board's actual port. If flashing cannot enter download
mode, hold BOOT, tap RESET, release BOOT, and rediscover the port. Tap RESET
after upload if the application does not start. Close serial monitors before
starting the USB worker. With both a C6 and S3 attached, select a device with
`robot.cmd --serial <id> start` or `robot.cmd --port COM5 start`.

## On-device smoke test — 2026-09-18

Flashed `live3d_usb_s3` version `4dce8a1` to the connected ESP32-S3 on COM7;
flash hashes verified. No factory-firmware backup was made, as requested.
[Recorded results and logs](reports/s3-bringup-2026-09-18/results.json) include
startup/reset, notification expiry/deduplication, icon validation, all six render
modes, a full 172 x 320 framebuffer, and desktop CLI/MCP control tests. All passed.
Native rendering measured roughly 8–13 FPS across the tested states, with about
155 KB free heap; the resolution sweep minimum was about 131 KB.

![Framebuffer captured from the S3](reports/s3-bringup-2026-09-18/frame.png)

This is the renderer framebuffer, not a photograph of the LCD. Physical panel
orientation/color fidelity still need visual confirmation. Cable unplug/replug,
long-duration thermal stability, enclosure fit, custom-motion upload and automatic
idle-dance timing are not covered by this smoke test.

## Physical acceptance checklist (partially completed; see results above)

- Cold boot and reset: local fall/stand/idle sequence; no watchdog or allocation errors.
- All four display edges visible, correct portrait orientation and no 34-pixel shift.
- White/cyan robot face and red error icons display without red/blue or byte-order errors.
- `robot.cmd status`, message, thinking/error, provider notification, icon, dance,
  manual joint, and custom motion commands receive the existing USB acknowledgments.
- Short and long messages stay inside the upper bubble and expire to idle.
- All six resolution commands render within the screen; `native` and `frame`
  report 172 x 320 and `fast` also outputs a 172 x 320 framebuffer.
- USB unplug/replug reconnects; BOOT/RESET recovery works with the enclosure fitted.
- Measure sustained FPS, free heap, brightness, heat, and first-print fit on hardware.

Use `tools/benchmark_live3d.py --port COM5` for timing/framebuffer capture and the
existing `tools/check_startup_motion.py`, `tools/check_live_states.py`,
`tools/check_idle_dances.py`, and USB motion checks for behavioral evidence.
These open USB directly, so stop the worker first. A framebuffer capture does
not prove LCD color order, orientation, or enclosure fit; inspect the physical unit.

The S3 needs its own [enclosure](../hardware/enclosure/README.md). Keep clearance
around the protruding BOOT/RESET buttons; never let the case hold either down.
