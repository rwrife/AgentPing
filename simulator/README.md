# Pixel Pal portrait simulator

A local visual prototype for the Waveshare ESP32-C6 Touch AMOLED 1.64.
The imported FBX is rendered by Three.js; a live canvas texture replaces the
front screen region of the robot's head. No hardware connection is needed.

## Run on Windows

Requires Node.js 20.19+ and a WebGL-capable browser.

```powershell
cd D:\projects\AgentPing\simulator
npm ci
npm run dev
```

Open <http://127.0.0.1:5173>. The server listens only on loopback. Fonts,
JavaScript, FBX, and textures are served locally; there are no runtime CDN calls.
Stop the server with Ctrl+C. `npm run build` produces `dist/` for static hosting.

## Animation direction

See [ANIMATION-PLAN.md](ANIMATION-PLAN.md) for startup, host listening, playful idle, thinking, and the priority wave/zoom/agent-icon attention sequence.

## Controls

- Attention and error use a top-half speech bubble and bottom-half face close-up.
  Edit the sample message in the side panel. Long messages use fixed-size text
  across pages; tap the bubble or press Enter/Space on the preview to advance.
  Paging never resolves the request. Bubbles hide after 30 seconds of real time, even when playback is paused. Tap the robot to reopen a hidden request. Sample text is local preview data only.

- Eight states include startup and host waiting alongside idle, running, waiting,
  completed, error, and disconnected. They are manual mock states, not a live
  bridge connection or the firmware's state reducer.
- Play a little workday reaches attention after 16 seconds and holds there. Selecting a state cancels the demo. Simulate resolved returns attention to thinking.
- Pause freezes animation time. Reduced-motion preference starts paused.
  Play animations or Play a little workday explicitly enables full motion, including
  smooth camera zooms. Animation motion also lets you select full, reduced, or system motion.
- Activate the character by click, Enter, or Space for a small body greeting.
- Playful stage varies the camera during non-critical states. Full character shows the entire
  character. The original unrigged T-pose remains available in Animation lab.
- Native size is 280 × 456 CSS pixels where the window permits. Enlarged mode
  fits the available space up to 2×; the measured scale appears below the display.
- The render buffer and saved PNG are always **280 × 456**, regardless of zoom
  or browser device-pixel ratio. This models pixel layout, not physical size in mm.
- Animation lab provides ±35° yaw, playback speed, original/dynamic face,
  reset, desktop preview fps, model selection, and a reversible 0–12° stance correction (default 6°).

## Rigged character

The default model is the supplied Meshy rig with its 1.96-second idle clip.
Attention adds an authored Meshy wave, blended with the idle. See
[RIG-NOTES.md](RIG-NOTES.md) for asset provenance, stance findings, and refinements.
Select Original T-pose in Animation lab to compare with the earlier asset.

## Original imported asset

Source supplied by the user:
`C:\Users\ryrife\Downloads\Meshy_AI_Pixel_Pal_0915023021_texture_fbx\Meshy_AI_Pixel_Pal_0915023021_texture_fbx`.

The original FBX and base color, normal, roughness, and metallic PNGs are copied
unchanged into `public/character/`. `manifest.json` records SHA-256 hashes and
sizes. Mesh inspection found **one mesh, one material, 5,336 triangles, zero
bones, and zero animation clips**. Source texture resolution and GPU memory
cost are desktop concerns; these assets are not firmware payloads.

The character is user-supplied Meshy output. This repository's MIT code license
does not establish rights to redistribute that character; retain the original
Meshy generation/download terms with any distributed asset pack.

## Device display policy

The moving robot and temporary bubbles sit on a faint blue-black gradient that drifts during non-critical states. A fixed connection caption appears at the bottom during Listening. Branding, other status captions, indicators, scanlines, and guides are excluded from the device framebuffer. Slow camera movement spreads the robot across more of the display during non-critical states. This is a visual mitigation, not physical burn-in validation.

## Dynamic face implementation

`src/face.js` draws each expression on a 256 × 256 canvas. `src/main.js` binds
that canvas as a Three.js `CanvasTexture` and updates it each frame.

The FBX has no separately selectable screen material. The prototype therefore
projects face UVs from the existing mesh's local X/Z coordinates, then limits
replacement to a rounded front-facing region by local Y depth and surface normal.
This follows the head surface and respects depth testing during rotation. It
does not modify the source mesh or bake changes into the original texture atlas.
Projection dimensions are calibrated specifically to this asset; replacing the
FBX requires recalibration. This method is not a substitute for an explicit face
UV island on a production rig.

The rigged model uses the supplied skeletal idle plus an authored Meshy wave.
Boot combines procedural skeletal motion with an offscreen jump; completion uses a whole-character hop. The face
projection uses bind-pose coordinates so it follows the skinned head.

## Strategy for the physical device

1. **Approve composition here.** Check expressions at native resolution, select
   portrait/full-body framing, and settle how much display space goes to messages
   and touch controls. Current character taps are greetings only, not approval
   or denial actions. Existing firmware action/confirmation policy remains authoritative.
2. **Prepare the animation asset.** A humanoid rig with head, torso, upper/lower
   arms, hands, upper/lower legs, and feet will enable real gestures. Make the
   display a dedicated material named `FaceScreen`, with nonoverlapping 0–1 UVs,
   parented/skinned to the head. Keep the bezel separate. Export neutral arms-down,
   idle, working, asking, success, and error clips; retain the T-pose as a bind pose.
   GLB is convenient for browser runtime; preserve FBX as the authoring source.
3. **Bake body animation on the PC.** For the C6, start with a small set of
   pre-rendered body frames and an independently drawn face. Keep the head nearly
   frontal for the first hardware iteration. If it turns, export per-frame face
   placement/masking or bake complete face/body combinations; a static 2D overlay
   cannot correctly follow arbitrary head rotation.
4. **Port expressions to LVGL.** Use simple eyes/mouth primitives or a small
   face atlas, drive them with the firmware's existing state reducer, and retain
   burn-in movement, reconnect behavior, and touch confirmation rules. The browser
   shader itself does not run on the microcontroller.
5. **Budget and measure.** An RGB565 full frame is 280 × 456 × 2 = 255,360 bytes
   (~249 KiB); double buffering needs ~499 KiB before LVGL, Wi-Fi, and application
   memory. The board lists 512 KiB HP SRAM and 16 MB flash. A 30-frame uncompressed
   full-screen sequence alone is ~7.3 MiB. Prefer partial updates, cropped body
   frames, small face layers, compression where practical, and incremental decode.
   Establish fps, flash usage, RAM high-water mark, and decode/flush latency on
   hardware; browser fps cannot predict these.

The device dimensions and memory are documented by
[Waveshare](https://www.waveshare.com/product/esp32-c6-touch-amoled-1.64.htm)
and in `firmware/BRINGUP.md`. The simulator does not emulate the ESP32 CPU,
LVGL, QSPI timing, color calibration, panel corner geometry, or real touch input.

## Verification

```powershell
npm test
npm run build
# With npm run dev running in another terminal; requires installed Microsoft Edge:
npm run test:browser
npm run test:rig
node tests/startup-browser.mjs
node tests/idle-variety.mjs
```

Browser checks load the real FBX, verify eight distinct rendered states and three agent badges, freeze time,
toggle the face texture, exercise framing/rotation, export and inspect a 280 × 456
PNG, cancel a sequence, check reduced motion, and verify layout at 320/375/414/768
pixels. Screenshots go to ignored `test-results/` for visual inspection. They are
development evidence, not physical device validation.

Three.js references:
[FBXLoader](https://threejs.org/docs/pages/FBXLoader.html),
[CanvasTexture](https://threejs.org/docs/pages/CanvasTexture.html).
