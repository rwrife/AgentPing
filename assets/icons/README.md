# Demo face icons

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
