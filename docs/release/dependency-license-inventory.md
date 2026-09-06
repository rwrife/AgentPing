# Dependency and license inventory

This project keeps dependency versions pinned and generates machine-readable inventory outputs during release assembly.

## Automated inventory outputs

The release workflow emits these files under `linux-inputs/licenses/`:

- `licenses-dotnet.json` — NuGet package inventory + licenses (via `dotnet-project-licenses`)
- `licenses-python.json` — installed Python package inventory + licenses (via `pip-licenses`)
- `firmware-components.lock` — pinned ESP-IDF/component-registry manifest (`firmware/dependencies.lock`)

## Reproduce locally

```bash
python3 -m venv .venv-platformio
. .venv-platformio/bin/activate
python3 -m pip install --requirement firmware/requirements-ci.txt --requirement protocol/requirements-ci.txt
python3 -m pip install pip-licenses
pip-licenses --format=json --with-urls --output-file /tmp/licenses-python.json

dotnet tool install --tool-path .tools dotnet-project-licenses
.tools/dotnet-project-licenses --input AgentPing.sln --include-transitive --json --outfile /tmp/licenses-dotnet.json --unique
```

## Known upstream license anchors

| Dependency family | Version anchor | License |
|---|---|---|
| AgentPing repository code | current `main` | MIT |
| Waveshare board evidence/adapted FT3168 sources | commit `b90e28c953c1fc882258fa8dbd56b7706bc888b7` | Apache-2.0 |
| Espressif `esp_lcd_co5300`, `esp_lcd_touch`, `esp_websocket_client` | pinned in `firmware/dependencies.lock` | Apache-2.0 |
| Espressif IDF | `5.5.0` | Apache-2.0 (plus component-specific notices) |
| LVGL | `9.3.0` | MIT |
| PlatformIO Core | `6.1.18` | Apache-2.0 |
| JSON Schema validator (`jsonschema`) | pinned in `protocol/requirements-ci.txt` | MIT |

## Policy

- Keep lockfiles current and committed.
- Regenerate license inventories for each release candidate/tag.
- Treat unknown/conflicting license metadata as a release blocker until manually reviewed.
