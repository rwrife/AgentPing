# Meshy rig integration

Source: `C:\Users\ryrife\Downloads\Meshy_AI_Pixel_Pal_rigged\Meshy_AI_Pixel_Pal_biped`.
Files are copied unchanged to `public/character/rigged`; its manifest records hashes.

The runtime loads `Meshy_AI_Pixel_Pal_biped_Animation_Idle_11_withSkin.fbx`.
It has 33 bones with Mixamo-style names and a 1.958-second idle clip with 99
tracks. The short secondary clip is unused. An unrelated `Icosphere` helper
is excluded at runtime. The separate Character_output FBX is retained for reference.

## Narrow feet

At the start of the idle, foot-bone spacing is approximately 45 FBX world units,
compared with 69 in the bind pose. Much of the narrow stance comes from the clip.
Animation lab offers a reversible 0–12° hip spread, defaulting to 6°; zero uses
the source animation. Foot orientation is preserved. This is a preview adjustment,
not a replacement for refining skin weights, contact, or the authored idle.

## Motion and face

The idle drives the skeleton. Attention layers an authored Wave for Help over
it, repeats every 12 seconds, and blends state changes over 0.5 seconds. Walking and Running are also available as explicit previews. Boot uses a procedural skeletal jump; success uses a whole-character hop.
Reduced motion freezes the idle and disables the wave. Pausing must never
accumulate additive bone rotations; the rig browser test covers that regression.

Face projection uses bind-pose vertex coordinates mapped to the original asset's
coordinate range before skinning. The dynamic texture consequently follows the
head. A dedicated screen material and UV island remain preferable for final assets.

Next asset pass: widen the authored idle, refine rigid head/shell weights, and
provide wave, walk/jump entrance, thinking, and success clips on the same skeleton.
Validate hand placement in the lower-half attention crop at native 280 × 456.

## Additional animation export

Imported Walking (1.04s), Running (0.64s), and Wave for Help (4.76s) from
`C:\Users\ryrife\Downloads\Meshy_AI_Pixel_Pal_full_animation\Meshy_AI_Pixel_Pal_biped`.
The longest take from each FBX is serialized as a Three.js animation clip under
`public/character/animations`, avoiding duplicate meshes and textures. Source FBXs
are untouched. Only matching skeleton tracks are applied; wrapper tracks are
excluded to preserve the existing model placement.

Automatic mode uses idle and the attention wave with gentle entry/exit blends.
Animation lab offers Walking, Running, and Wave for Help loop previews. Walking
and running do not yet drive an on-screen travel path or the startup entrance.

Startup uses a provisional procedural skeletal jump: offscreen rise, crest, descent,
landing crouch, and recovery before listening at three seconds. No authored jump
clip was present in the supplied exports. Selecting Waking up replays the entrance.

Hanging out adds three procedural dance variations (disco, shoulder shimmy,
robot arms), shuffled once per load. Four-second gestures are separated by
quiet idle and blend in/out. Attention interrupts them; reduced motion and
manual clip previews disable them. These are not imported dance clips.

Thinking uses a procedural hand-under-chin pose with the opposite arm hanging naturally, a slight forward tilt, and a 0.9-second blend. Reduced motion holds the pose without animation.

Idle variety now includes jumping jacks, a full twirl, and side bounces alongside
the three arm dances. Root movement and skeleton gestures share the same schedule.
All six play once per shuffled cycle, with quiet intervals and attention interruption.
