# Windows companion troubleshooting

This page is intentionally available offline with the companion.

## Bridge does not start

Confirm that `bridge\AgentPing.Bridge.exe` is installed below the companion directory and that port 8742 is unused. The safe default is `127.0.0.1:8742`; do not change it to `0.0.0.0`. Export redacted logs from **Activity & support**.

## A device cannot pair

Pairing is disabled unless the user opens a short pairing window and confirms the device. LAN mode additionally requires TLS and a selected RFC1918 private IPv4 interface. Public, link-local, wildcard, and non-TLS listeners fail closed. A pairing window is single-use, expires, and has a bounded attempt count. Reopen it instead of reusing provisioning material.

## Revoke a lost device

Open **Devices & pairing**, select the device, and choose **Revoke device**. Revocation invalidates only that device token, closes its active socket, and clears queued actions. Rotate a token when the device remains trusted.

## Startup and uninstall

Startup at sign-in is off until explicitly selected. Disable it before portable removal. Installer upgrades preserve settings and pairing data; uninstall-time removal of user data must be an explicit choice.

## Accessibility and display

The management window supports keyboard traversal, Escape-to-hide, Windows screen-reader names, and per-monitor-v2 DPI scaling. If scaling is stale after moving monitors, close the tray process and reopen it. Top-level labels load from `Strings.resx`, providing a satellite-resource extension point; English is the baseline locale.

## Evidence limits

CI builds are not physical-device validation. Wi-Fi/RF, real-device TLS/pairing, touch behavior, and provider-account actions require the unchecked physical validation matrix. Windows artifacts are unsigned until a release operator performs and verifies Authenticode signing.
