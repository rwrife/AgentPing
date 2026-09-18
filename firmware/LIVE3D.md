# Live 3D hardware experiment

`live3d_usb` (Waveshare ESP32-C6 Touch AMOLED 1.64) and `live3d_usb_s3`
(Waveshare ESP32-S3-LCD-1.47B) are USB-only profiles of the same software renderer.
It transforms the skeleton and rasterizes
the robot on the device; it does not play baked image frames or stream video
from the PC. Startup, idle, thinking, attention, and error animations run locally.
USB notifications show provider logos on the face, provider names, and message
bubbles; the remaining simulator states are not yet ported.

Use `-e live3d_usb_s3` instead of `-e live3d_usb` in the build/upload commands
below for the S3 board. Its LCD has no touch; the C6 robot also does not use
touch. The old `character_usb` restore command is **C6-only**.
All historical benchmark and physical-validation results below describe the
C6, not the S3. See [S3 bring-up](BRINGUP-S3.md) before relying on the new port.

## Asset pipeline

The user-provided `pingpal.fbx` contains one skinned mesh, 27 bones, UVs, and
embedded textures, but no animation clips. The original benchmark exercised
the arm bones with procedural rotations; the current demo boots through the
fall, stand-up, and idle sequence below. It uses the base-color texture plus
a separate dynamic face canvas;
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
rendering with the sharper 128 x 128 texture is the boot default:

| Command | C6 resolution | S3 resolution |
| --- | --- | --- |
| `low` | 140 x 228 | 86 x 160 |
| `medium` | 160 x 260 | 100 x 186 |
| `high` | 192 x 312 | 120 x 224 |
| `max` | 208 x 338 | 140 x 260 |
| `native` | 280 x 456 | 172 x 320 |
| `fast` | 140 x 228, 2x output | 86 x 160, 2x output |

The S3 message bubble uses a smaller font and bounds adapted to its panel.
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

## Original renderer benchmark

September 16, 2026, before the dynamic face and closer camera, ESP32-C6 at its
stock 160 MHz clock, all 2,668 triangles:

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
The `wave` command selects it at the requested **two-thirds speed**
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

Earlier wave-only hardware validation: the stored wave plays at approximately 12.8–13.0 rendered
FPS at native resolution with 40,140 bytes free heap. The application is
649,363 bytes with 102,996 bytes static RAM. Startup from flash, framebuffer
appearance, successful RAM upload/playback, malformed upload rejection,
checksum rejection, and memory recovery when returning to flash were checked
on COM5. Final playback reports `source=flash` and `speed=0.667`.

## Startup sequence and dynamic face

The boot sequence is stored entirely in flash:

| Clip | Keys at 24 Hz | Motion bytes, including hip position | Playback |
| --- | ---: | ---: | --- |
| Falling Flat Impact | 38 | 8,436 | Once, 1.5x speed (about 1.03 seconds) |
| Standing Up | 274 | 60,828 | Once, original speed (11.375 seconds) |
| Standing Idle | 145 | 32,190 | Loop, original speed (6 seconds) |

The three clips total **101,454 bytes**. The fall starts with all projected
vertices above the display, descends to a fixed floor at 78% of the display
height, and is never automatically
replayed during idle. Framing is about 21% closer than the first renderer.
The animated silhouette stays centered, while the connection caption is a
separate LVGL label fixed 16 pixels above the bottom edge.

The player renders each one-shot's final key before advancing. It retains the
outgoing bone quaternions and hip position, then blends them into the incoming
first pose over 400 ms with eased, normalized shortest-path quaternion blends.
The incoming clip's clock begins after this blend. The same player supports
smooth transitions to the stored wave and back to idle. A pending USB upload
holds the last displayed pose until a validated clip is ready. Pause/resume
preserves animation time.

Before the first desktop signal, idle shows “Waiting for connection...” and
the host-agent caption. After two minutes on this waiting screen, the entire
display goes black for 30 seconds, then returns for another two minutes. This
cycle repeats on C6 and S3 until the first desktop signal. Animation transitions
and renderer pause/resume do not reset the cycle; USB commands keep processing
while black. This reduces continuous exposure of the static caption.
The first USB `host` heartbeat or desktop state message hides it for the rest
of the boot session; silence afterward never restores it. USB power alone is not treated as a host-agent connection. `boot` replays
startup for testing, `idle` transitions to idle, and `startupstatus` reports the
state, key position, transition status, caption visibility, and `blank=1` during
the black interval. A host heartbeat or desktop state message wakes the display
immediately, including during the black interval.

The reduced FBX retains the front screen surface but has only one material.
The renderer now projects a separate 64 x 64 RGB565 face canvas over 51 front
screen triangles using their rest-pose coordinates. This preserves facial
detail independently of the 128 x 128 body atlas. Fall, recovery, and idle have
different expressions; idle blinks periodically. The face uses 8 KiB of RAM.

With the closer framing and dynamic face, hardware validation measured 10.38
FPS in idle with the connection caption and 11.08 FPS without it. Free heap
remained at 31,036 bytes. The application uses 757,649 bytes of flash and
112,116 bytes of static RAM. A cold-boot test confirmed an entirely off-screen
entrance (maximum projected Y = -12), both 400 ms transitions, looping idle,
and caption visibility before/after the first desktop signal.

Rebuild the assets from the repository root:

```powershell
python scripts/encode_live3d.py
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Falling Flat Impact.fbx' fall_motion --root
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Standing Up.fbx' stand_motion --root
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Standing Idle.fbx' idle_motion --root
.\.venv-firmware\Scripts\python.exe tools/check_startup_motion.py --port COM5
```

The last command resets the physical device and tests the sequence, off-screen
entrance, looping idle, and startup-only connection caption.

## Restore notification firmware

```powershell
.\.venv-firmware\Scripts\pio.exe run -d firmware -e character_usb -t upload --upload-port COM5
```

Restart the installed USB notification worker after restoring `character_usb`.
The live renderer uses stock CPU clocks and the selected board's display driver.

## Thinking, attention, and error states

The live renderer includes Thinking.fbx (103 keys, 22,866 bytes) and Attention
Waving.fbx (77 keys, 17,094 bytes), both with hip motion. Thinking plays at
original speed; the error wave and existing attention wave play at two-thirds
speed. Non-closed thinking/error clips blend back to their first pose over
400 ms on repeat. State changes use the same bone blend.

USB commands:

```text
thinking
thinking Checking the next step...
attention Codex is waiting for your input.
error Claude could not complete the request.
idle
viewstatus
```

Thinking without text chooses a playful thought and changes it every six
seconds. Each bubble expires 30 seconds after the command, including thinking;
expiry blends the robot back to idle and eases the camera back out. The upper bubble is
fixed 16 pixels from the top and sides and clips overly long messages with an
ellipsis. Thinking adds a small thought dot. The camera eases into a face and
shoulder view in the lower half, and eases back when returning to idle.

Existing `notify <16-hex-id> <codex|claude|copilot> <attention|completed|error>`
messages are accepted and acknowledged. They show provider names and preset
messages; errors use the new error wave. Repeated IDs do not restart the bubble.
Provider notifications display the matching real logo on the live face. The direct state commands
above accept custom text. `host` marks the desktop as seen; the idle caption stays hidden until restart.

Convert the new clips with:

```powershell
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Thinking.fbx' thinking_motion --root
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Attention Waving.fbx' error_motion --root
.\.venv-firmware\Scripts\python.exe tools/check_live_states.py
```

The expanded firmware is 825,561 bytes, with 112,208 bytes of static RAM.
Live-state tests exercise eased zoom in/out, state selection, USB error
acknowledgment, duplicate suppression, and automatic return to idle after
30 seconds.

At native resolution, the renderer invalidates the union of the current and
previous robot regions. LVGL invalidates bubble changes separately, avoiding
repeated repainting of the static area above the robot.

Final close-up measurements were approximately 12.4-12.5 FPS with bubbles
visible and 30,524 bytes of free heap, stable throughout the hardware check.

## Idle dances

After 30-60 seconds in idle, the device randomly chooses one of three stored
Mixamo dances, excluding the dance played last. It plays once at original speed
and blends back to idle, which starts a fresh random delay. The camera stays in
its full-body view. Thinking, attention and error commands interrupt immediately
with the normal pose blend and close-up camera transition. Host heartbeats do
not reset the dance timer; pausing playback pauses the idle countdown as well.
Before the first desktop signal, the waiting-for-connection caption remains
visible during automatic idle dancing, except during the 30-second black
interval described above. Dances do not restart that display cycle.

| Dance | Duration | Motion bytes |
| --- | ---: | ---: |
| Locking Hip Hop Dance | 17 seconds | 90,798 |
| Twist Dance | 9.42 seconds | 50,394 |
| Chicken Dance | 4.75 seconds | 25,530 |

These add 166,722 bytes in flash, with no clip allocation in RAM. For immediate
previews use `dance`, `dance hiphop`, `dance twist`, or `dance chicken` over USB.
Run `python tools/check_idle_dances.py --port COM5` to validate scheduling,
non-repetition, smooth entry/exit, and interruption on the physical device.

```powershell
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Locking Hip Hop Dance.fbx' hiphop_motion --root
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Twist Dance.fbx' twist_motion --root
node simulator/scripts/export-motion.mjs 'C:/Users/ryrife/Downloads/Chicken Dance.fbx' chicken_motion --root
```

With all three dances included, the application is 994,065 bytes (31.6% of
the 3 MiB app partition), with 112,224 bytes of static RAM. Hardware dance
playback measured approximately 10.9 FPS with 30,492 bytes of free heap.

## Sample rate and device memory

Stored clips are sampled at **12 Hz**, the `--rate` default. The renderer runs
near 11 FPS, so faster sampling only inflates the clip with interpolation the
display never shows. Playback speed is a separate multiplier, so lowering the
sample rate does not change how a motion looks. Re-baking the nine stored
motions at 12 Hz halves their keyframe counts and removes 154 KB of flash.

`--no-header` skips the `assets/*_motion.h` write for ad-hoc clips that are
uploaded over USB rather than baked into the build.

The LVGL pool is sized by `CONFIG_LV_MEM_SIZE_KILOBYTES` in
`sdkconfig.defaults`, not by `include/lv_conf.h` — the build sets
`CONFIG_LV_CONF_SKIP=y`, which makes that header inert. live3d only builds an
image, two labels and a dot, so the pool is 24 KB rather than the default 64 KB;
`viewstatus` reports `lvgl_used`/`lvgl_free` to confirm the headroom. That
returns about 41 KB of SRAM to the heap that uploaded clips allocate from, and
free heap measures 61 KB against 28 KB before.

live3d installs the USB Serial JTAG driver with a 2 KB RX ring buffer. The bare
console reads from a 64-byte hardware FIFO, which silently drops the 440-byte
`key` lines a motion upload sends.

Startup framing uses the same lower floor for fall, stand-up and idle. As the
robot stands, its head rises into the idle position while its bottom stays at
approximately pixel 356 on the 456-pixel panel. The camera no longer recenters
the growing stand-up silhouette.

Full-body framing now uses a projection scale of 51% of panel width (up from
46%), making the robot approximately 11% larger. The fixed startup floor and
head/shoulder close-up framing are retained.

For desktop CLI/MCP commands and smooth manual joint control, see
[robot controls](../docs/robot-control.md). Manual joint poses blend into the
next animation using the existing cached pose transition.

## Provider face logos

Provider `notify` commands display Codex, Claude, or GitHub Copilot artwork on
the face instead of eyes/mouth, including error notifications. Generic messages
retain the normal animated face. Logos survive error animation loops and clear
when the bubble expires, idle starts, or a generic message/manual pose replaces
it. No exclamation mark or extra state label is drawn over the logo.

The 48 x 48 one-bit masks occupy 864 bytes in flash and reuse the existing face
canvas. They derive from the simulator's existing SVG assets and license. Run
`node simulator/scripts/export-agent-logos.mjs` to regenerate them using installed
Edge, or set `PLAYWRIGHT_CHANNEL` to another installed Chromium browser channel.

Custom 48 x 48 one-bit icons and RGB colors can also be uploaded through the
desktop CLI/MCP. The firmware validates a staged bitmap before publishing it
and reuses the face canvas; see [custom icons](../docs/robot-control.md#custom-1-bit-face-icons).

Custom icon uploads may be committed with `iconerror <message>` to show an error. Error-state icon rendering always uses red RGB565 0xf800, including provider logos, regardless of uploaded color. `iconshow` retains attention behavior.
