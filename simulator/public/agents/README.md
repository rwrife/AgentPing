# Agent logos

SVG paths from [LobeHub Icons](https://github.com/lobehub/lobe-icons), pinned to
commit `a94750e3f5f8fc33757b839d85030e742284e43a`, retrieved 2026-09-15.

- `claude.svg`: Claude starburst, used for Claude Code.
- `githubcopilot.svg`: GitHub Copilot headset mark.
- `codex.svg`: Codex terminal mark.

Source directory: `packages/static-svg/icons/`. The original SVG files are kept
unchanged. The renderer draws their 24 × 24 paths in the current face color;
the native 280 × 456 framebuffer supplies the final small-screen rasterization.
No remote image requests are made at runtime. The adjacent LICENSE retains the
icon library's MIT notice; brand marks remain the property of their owners.
