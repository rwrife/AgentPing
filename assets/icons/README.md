# Demo face icons

`enghub.bin` is a one-bit conversion of the user-supplied
[EngHub SVG](https://eng.ms/static/media/EngHubLogoBlue.Co08K0NS.svg).
Its source is retained as `enghub.svg`; the artwork belongs to its original owner.
Use `#0078D4` to match the source blue. Regenerate from the repository root with
`node simulator/scripts/pack-face-icon.mjs assets/icons/enghub.svg assets/icons/enghub.bin`
after installing simulator dependencies (`npm ci` in `simulator/`). The converter
uses installed Microsoft Edge to rasterize the SVG.

```powershell
.\companion\robot.cmd icon --file assets/icons/enghub.bin --color "#0078D4" --message "EngHub: Your review is needed"
```

`heart-48.bin` and `teams-48.bin` are original demo artwork. The Teams-style
glyph uses a T tile and people silhouettes; it is not an official Microsoft asset
or a Teams notification integration. `teams-48.svg` previews the same pixels.

Each bitmap is 48 x 48, packed into 288 bytes, row-major with the leftmost pixel
in the least-significant bit. Regenerate the Teams-style files with
`python scripts/make_teams_demo_icon.py`.

From the repository root, with the USB worker running:

```powershell
.\companion\robot.cmd icon --file assets/icons/teams-48.bin --color "#8b88ff" --message "Teams: Your meeting starts now"
```

The notice dismisses after 30 seconds. Adding `--state error` overrides purple
with red. For MCP, pass the bitmap bytes as a 576-character hexadecimal string
to `robot_icon`, with `color="#8b88ff"` and your message.
