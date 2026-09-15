# Pixel Pal animation lifecycle

## AMOLED visual direction

Only the robot and temporary chat bubbles belong over a faint blue-black gradient
that drifts during non-critical states; no branding, status captions, dots, guides, scanlines, or borders.
Keep the face screen black. Keep the shell
light with its blue shading so its silhouette reads clearly. Use vivid cyan for
startup/idle/thinking, blue for host listening/disconnection, amber for attention,
lime for completion, and pink-red for errors. Pair colors with distinct expressions,
agent identity, and text so meaning does not depend on color alone. Status text
is bright off-white, with readable cool secondary text. Validate final brightness
and saturation on the physical panel; the desktop preview establishes the palette.

## Primary interaction: an agent needs you

The most important behavior is to attract attention and clearly identify which
agent application is asking.

**Target choreography (proposed timing):**

1. Immediately interrupt idle/thinking and show the requesting software's icon.
2. Over 0–1.2 seconds, turn toward the viewer, raise one arm, and wave twice.
3. Over 0.4–1.25 seconds, smoothly frame the face in the **bottom half** of the
   display. Use the **top half** for a speech bubble with the agent name and
   message. Keep the waving hand inside the lower portrait crop; rig composition
   must account for this.
4. Settle into a frontal hold. Keep a large software icon on the face, with the
   software name and reason in the bubble above. Identity must remain
   readable without waiting for an alternating animation or relying on color.
5. While unresolved, retain the icon and use occasional gentle reminders,
   initially proposed at 10 seconds, with a configurable quieter mode. Avoid a
   constant wave or repeated zoom. Respect reduced-motion settings.
6. A tap opens the request details and available actions. It does not approve,
   deny, or dismiss a request by itself. Existing firmware confirmation rules apply.
7. On a matching host resolution/cancellation event, lower the arm, restore the
   ordinary framing, and return to thinking, success, or idle as appropriate.

Attention and error messages share this bubble layout. At 280 × 456, reserve
pixels 0–227 for the header/bubble and 228–455 for the face close-up. Use a dark
bubble with a state-colored outline and tail, bright 16 px message text, and
five lines per page. Wrap by measured pixel width, including long identifiers.
Long messages advance by tapping the bubble, with a page count; never auto-scroll
or silently truncate them. Paging is separate from request resolution. Keep the
full message available to assistive technology in the desktop prototype.

Chat bubbles hide after 30 seconds of wall-clock time, independent of animation
speed and pause. New message content or agent identity starts a fresh display
period. Paging does not reset the deadline. Tapping the robot reopens a hidden
message for another 30 seconds. Bubble expiration returns to a wider robot view.

An unresolved request never expires solely because an animation or bubble finishes.
An unrelated completion event must not dismiss it. With several pending requests,
keep the selected request stable, show a count, and allow explicit navigation;
do not silently rotate agent identity while a user is deciding.

During idle, listening, working, success, and disconnection, slowly vary camera
position and scale to move the robot across a broad area of the display. Keep
large camera movement off during message reading and the boot entrance. The
attention pose has small continuous shifts even between reminder gestures.
These measures reduce persistent static content; they are not a guarantee against
burn-in. Brightness, inactivity dimming, panel limits, and physical aging still
need hardware validation. Explicit simulator pause/reduced-motion settings remain
available for inspection and disable ambient camera movement.

## State and clip plan

| State | Trigger / exit | Body animation | Face | Camera |
|---|---|---|---|---|
| Startup | Power on; play once, then listen | Rise/straighten, small greeting | Power glyph → eyes open → smile | Establish character |
| Host waiting | Startup finished, no host event yet | Quiet breathing, occasional listening tilt | Listening indicator | Normal portrait |
| Playful idle | Host available, no active work or requests | Sparse look-around, weight shift, curious gestures | Blinks and friendly expressions | Normal portrait |
| Thinking / working | Host reports active work | Focused lean and restrained rhythmic movement | Tracking eyes / thinking dots | Normal portrait |
| Attention | Permission, question, or actionable host request | Raise arm, wave, settle; highest UX priority | Requesting agent software icon | Zoom to head and upper torso |
| Completed | Work finishes with no unresolved attention | Small celebratory hop/nod | Happy eyes | Normal portrait; return to idle |
| Error | Host reports failure | Concerned tilt | Concerned face; actionable errors use attention with agent identity | Normal or attention framing |
| Disconnected | Established host connection lost | Settle, subdued motion | Connection-lost indicator | Preserve pending request identity if any |

Host waiting is distinct from playful idle and from losing an established
connection. Firmware/bridge integration must define whether the existing
handshake or first state snapshot establishes host availability. These visual
states are a presentation layer, not new protocol message types.

## Required rig and authored clips

Prepare a humanoid skeleton with head, neck, torso, shoulders, upper/lower arms,
hands, hips, upper/lower legs, and feet. Use a relaxed arms-down runtime pose,
preserving the T-pose as the bind pose. Split the screen into a dedicated
`FaceScreen` material with a clean 0–1 UV island and attach it to the head.
Leave the bezel and head shell on their original materials.

Deliver clips: `startup`, `listen_loop`, `idle_loop`, `idle_playful`, `think_loop`,
`attention_wave`, `attention_hold`, `attention_release`, `success`, `error`, and
`disconnected_loop`. Loops must join cleanly. Keep root position consistent and
motion bounded so the character stays inside the display. The wave is the first
rigged clip to validate at 280 × 456.

Use locally bundled recognizable icons for Codex, Claude Code, and Copilot,
with an explicit generic fallback and software name for other integrations.
Maintain a fixed agent-ID-to-asset mapping; host event text should not be
interpreted as an arbitrary image URL. Check icons at actual device resolution,
including one-color variants.

## Implemented preview versus next stage

Implemented: eight selectable visual states, a startup face that transitions to
host listening after three animation seconds, playful idle movement, thinking,
a smoothly reversible attention/error zoom over 1.2 seconds into the lower half, a paginated speech bubble
in the upper half with editable sample messages, stable front-facing agent identity, three distinct
locally bundled agent logos, and explicit simulated resolution. The demo runs
startup → listening → idle → thinking → attention and stops there. Reduced motion
starts paused and applies attention framing immediately.

Still to implement: skeletal wave/arms-down poses, real
host event ingestion and pending-request queue, request detail/actions UX,
occasional reminder choreography, authored transitions, and hardware asset baking.
The current model has no rig; the preview does not claim to demonstrate a wave.

For the ESP32-C6, bake body/gesture frames on the PC and animate the face/icon
separately through LVGL. Export per-frame face placement/masks when the head
moves, or bake combined frames. Keep the static frontal attention hold inexpensive
to render. Validate memory, decode cost, display flushing, touch, and sustained
animation timing on hardware after the desktop UX is approved.


## Smooth choreography in the current prototype

The boot entrance is a whole-body jump from below the display, with a soft
landing and a small squash/stretch. Idle and host listening use lateral drifting,
turns, leaning, and occasional hops; thinking uses quieter motion. Attention
combines two small root-motion bounces and a rocking gesture with the zoom, then
settles for reading, with a brief reminder approximately every ten seconds.
This is root motion on the unrigged mesh, not skeletal walking or arm waving.

Camera scale, camera position, and the lower-half viewport interpolate together
for 1.2 seconds. Bubble visibility follows that transition. Body poses blend for
0.5 seconds and faces crossfade for 0.3 seconds. An interrupted transition starts
from its currently displayed pose and camera instead of snapping to an endpoint.
Pause freezes these timelines. Reduced motion skips animated transitions and
physical motion. The rigged wave remains the next asset milestone.
