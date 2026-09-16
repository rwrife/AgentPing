# Live 3D hardware experiment

`live3d_usb` is a separate, USB-only software-rendering profile for the
Waveshare ESP32-C6 Touch AMOLED 1.64. It transforms the skeleton and rasterizes
the robot on the device; it does not play baked image frames or stream video
from the PC. This experimental profile does not yet implement the notification
protocol or the simulator's state animations.

## Asset pipeline

The user-provided `pingpal.fbx` contains one skinned mesh, 27 bones, UVs, and
embedded textures, but no animation clips. The original benchmark exercised
the arm bones with procedural rotations; the current demo boots into the
converted Mixamo wave described below. It uses the base-color texture only;
normal maps, metallic maps, and physically based lighting are not implemented.

The converted model retains all 2,668 triangles with 2,822 deduplicated vertices.
Positions use signed Q12 fixed point, skin weights use four normalized bytes,
and the texture is 128 x 128 RGB565. Model data occupies 103,136 bytes in flash.
An additional 8,192-byte 64 x 64 texture is included for comparison; its RAM
cache is allocated only when selected and released on returning to 128 x 128.
The source FBX is converted on the PC; the MCU does not parse FBX files.

From the repository root, with simulator dependencies and Python Pillow installed:

```powershell
node simulator/scripts/export-live3d.mjs D:/projects/pingpal.fbx
python scripts/encode_live3d.py
$env:IDF_COMPONENT_CHECK_NEW_VERSION='false'
$env:IDF_COMPONENT_API_TIMEOUT='10'
.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb
.\.venv-firmware\Scripts\pio.exe run -d firmware -e live3d_usb -t upload --upload-port COM5
```

Pause the AgentPing USB worker before uploading or opening a serial monitor;
only one process can own COM5. Preserve its configuration so it can resume
after restoring the notification firmware.

## Profiling

Commands are newline-delimited ASCII: `low`, `medium`, `high`, `max`, `native`,
`fast`, `tex64`, `tex128`, `pause`, `resume`, `status`, and `frame`. Native
280 x 456 rendering with the sharper 128 x 128 texture is the boot default.
The firmware reports frame rate,
skinning, rasterization, display time, and heap usage every five seconds.
Allocation checks reserve 24 KiB and fall back to the medium mode on failure.
`frame` returns a dimensions header followed by raw little-endian RGB565 bytes.

```powershell
python tools/benchmark_live3d.py --port COM5
```

The benchmark requires pyserial and Pillow. It writes timing logs and an actual
device framebuffer PNG to `test-results/live3d-device/`, then resumes animation.
The captured image validates the renderer's output; it is not a photograph of
the physical display.

## Measured on the device

September 16, 2026, ESP32-C6 at its stock 160 MHz clock, all 2,668 triangles:

| Rendering mode | Texture | Measured FPS |
| --- | --- | --- |
| Native 280 x 456 | 128 x 128 | 13.2 |
| Native 280 x 456 | 64 x 64, cached in RAM | 13.8 |
| 140 x 228, integer 2x output | 128 x 128 | 16.1 |
| 140 x 228, integer 2x output | 64 x 64, cached in RAM | 16.9 |

Native mode uses a full color framebuffer plus a reusable 32-row depth buffer:
273,280 bytes total. The original full color + full depth approach required
510,720 bytes and was rejected by the allocation guard. Strip visibility masks,
back-face culling, fixed-point vertex/raster math, and throughput-oriented
compiler optimization lifted native rendering from about 6.7 to 13.2 FPS.
The original benchmark application was 640,201 bytes with 102,940 bytes of static RAM. Native
128 x 128 texture mode leaves 40,204 bytes of free heap; selecting the smaller
RAM-cached texture consumes approximately 8 KiB, which is recovered when switching back.

The older low/medium/high/max modes use LVGL scaling and remain available as
comparison baselines. `fast` writes 2 x 2 pixel blocks directly into the full
color buffer, avoiding the approximately 107 ms LVGL scaling overhead.
Native display transfer/drawing takes approximately 23 ms per frame.

This is a textured, unlit, orthographic renderer. The captured images verify
that the mesh and base-color texture render on the device. These measurements
cover procedural arm movement and torso rotation, not the eventual full UX,
dynamic face texture, notification bubbles, or imported animation clips.

## Stored Mixamo wave

`Waving.fbx` is a 14-key looping clip at 24 samples/second (last key at
0.5417 seconds). `export-motion.mjs` maps its world-space rest-to-animated
rotation changes onto the validated Pixel Pal skeleton. Target bone lengths
are retained; root translation and finger tracks are omitted for this in-place
wave. All 27 target rotations are quantized to signed 16-bit quaternions.

The generated `assets/wave_motion.h` stores **3,024 bytes of motion in flash**.
The live demo starts it automatically at the requested **two-thirds speed**
(0.8125 seconds per loop) and interpolates poses at the display's render rate,
including the loop seam and a 400 ms entry blend. The original 24 Hz sample
timing is retained; playback speed is a separate multiplier. It needs no PC
connection or clip-sized RAM allocation to play the stored wave.

```powershell
node simulator/scripts/export-motion.mjs C:/Users/ryrife/Downloads/Waving.fbx
```

The converter also writes `simulator/test-results/live3d/waving.json` for
optional USB testing. `wave` selects the stored clip, `motionstatus` reports
flash versus USB playback, `seek N` holds a key for visual checks, and `play`
resumes looping. An experimental upload path accepts up to 64 keys if enough
RAM remains, validates frame indices, quaternion norms, and an FNV-1a checksum
before playback, and frees the uploaded clip when `wave` selects flash again.
This is whole-clip upload into RAM, not a continuous streaming protocol or a
persistent flash upload service.

```powershell
python tools/upload_motion.py simulator/test-results/live3d/waving.json --port COM5
python tools/check_usb_motion.py simulator/test-results/live3d/waving.json --port COM5
```

Hardware validation: the stored wave plays at approximately 12.8–13.0 rendered
FPS at native resolution with 40,140 bytes free heap. The application is
649,363 bytes with 102,996 bytes static RAM. Startup from flash, framebuffer
appearance, successful RAM upload/playback, malformed upload rejection,
checksum rejection, and memory recovery when returning to flash were checked
on COM5. Final playback reports `source=flash` and `speed=0.667`.

## Restore notification firmware

```powershell
.\.venv-firmware\Scripts\pio.exe run -d firmware -e character_usb -t upload --upload-port COM5
```

Restart the installed USB notification worker after restoring `character_usb`.
The live renderer uses stock CPU clocks and the existing display driver.
