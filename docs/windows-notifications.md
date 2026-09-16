# Forward Windows notifications to the USB robot

The Windows companion can forward **new Windows app notifications** from selected
apps. The robot waves, shows a cyan version of the app logo on its face, and
displays the app name and message in a bubble. The bubble dismisses after
30 seconds and the robot returns to idle. This does not detect an app's internal
thinking state; agent hooks remain separate.

## Install on Windows 11 (x64)

1. Complete the [USB host setup](../README.md#quick-start-physical-robot-on-windows) and connect the robot.
   Start the USB host from the repository root:

   ```powershell
   .\companion\robot.cmd start
   .\companion\robot.cmd status
   ```

2. Install the .NET SDK required by `global.json` and the Windows 10/11 SDK
   with its MakeAppx packaging tools. Exit any running AgentPing Companion using
   its tray menu, then build the notification companion:

   ```powershell
   .\companion\install-notification-companion.ps1 -BuildOnly
   ```

   The package is written to
   `%LOCALAPPDATA%\AgentPing\NotificationCompanion.msix`. It includes the .NET
   runtime. This package's notification tab talks directly to the USB worker;
   the companion's separate LAN bridge does not need to run.

3. Open **Administrator PowerShell using your normal Windows account** and install:

   ```powershell
   Add-AppxPackage -Path "$env:LOCALAPPDATA\AgentPing\NotificationCompanion.msix" -AllowUnsigned
   ```

   This is a local demo package. Windows requires elevation for an unsigned
   executable package; no signing certificate or Developer Mode change is needed.
   For broad distribution, sign the package and remove the unsigned publisher OID.

4. Open **AgentPing Companion** from Start (not the executable in the build folder).
   Select **Windows notifications**, check **Forward Windows notifications to the
   robot**, and grant Windows notification access.

5. Apps appear in the list when they have a notification in Windows Notification
   Center. Send a test notification from Teams or another app if necessary, then
   check that app. Existing notifications are skipped; send another notification
   to test forwarding. Only selected apps are forwarded.

6. Keep the companion running; closing its window minimizes it to the tray. Use
   the tray's **Exit** command to stop it. App selections and forwarding preference
   are remembered across restarts. The USB host must also remain running.

## Behavior and limits

- The listener checks every two seconds. It baselines existing notifications on
  startup or re-enable, deduplicates notification IDs and creation times, and
  sends only the newest selected notification in a burst.
- Logos become a 48×48, one-bit cyan mask. An unavailable logo falls back to the
  ordinary attention face. Sender photos and rich notification images are not forwarded.
- Text is normalized to a single line of up to 192 ASCII characters. Accents are
  simplified; unsupported symbols are removed and long text is shortened.
- Windows notifications use the attention state. Error severity is not reliably
  supplied by this API, so text is not guessed to be an error. Agent hooks and
  CLI/MCP error commands retain their existing behavior.
- Notification access is read-only: forwarding never dismisses a Windows notification.
- App choices are saved in `~/.agentping/usb/windows-notifications.json`. Selected
  message text temporarily passes through the existing command queue; it is not
  written to the companion's logs. Failed deliveries are not replayed.
- Agent hooks and Windows notifications are independent sources. Avoid selecting
  an agent app if its hooks already provide the same notifications.

## Troubleshooting

- **Access unavailable:** launch the installed Start-menu app. The unpackaged EXE
  does not declare the Windows notification-listener capability.
- **Access denied:** allow AgentPing under Windows Settings > Privacy & security >
  Notifications. Then disable and re-enable forwarding in the companion.
- **App missing:** create a Windows notification from that app. In-app banners
  are not Windows notifications. Notifications already dismissed will not appear.
- **No robot response:** run `companion\robot.cmd status`, confirm the app is
  checked, and send a fresh notification after enabling forwarding. The page
  reports successful USB acknowledgment or a delivery failure.
- **Updating:** exit the companion, rebuild, and repeat the install command.
  The script generates a package version using the UTC date and minute; wait
  until the next minute before rebuilding an already installed version.
- **Remove:** uninstall AgentPing Companion from Windows Settings > Apps. The USB
  worker and CLI/MCP hooks remain independently usable.

References: [Windows notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener),
[unsigned Windows 11 packages](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package).
