# Desktop robot CLI and MCP

The USB desktop client now exposes the live robot through a CLI and a local
stdio MCP server. Both submit requests to the same worker used by notification
hooks, so only one process opens the serial port. This is the USB companion path;
the existing .NET tray application's LAN management UI is unchanged.

## Install and run on Windows

From the repository root:

```powershell
.\companion\setup-robot.ps1
.\companion\robot.cmd --port COM5 start
.\companion\robot.cmd status
.\companion\robot.cmd message "Hello from the desktop!"
.\companion\robot.cmd message "Consulting my rubber duck..." --state thinking
.\companion\robot.cmd message "The demo hit a snag." --state error
.\companion\robot.cmd dance chicken
.\companion\robot.cmd state idle
.\companion\robot.cmd joint head --y 25 --duration-ms 800
.\companion\robot.cmd joint left_elbow --x 45
.\companion\robot.cmd reset
.\companion\robot.cmd stop
```

The underlying entry point is `python tools/agentping_robot.py`. Run `--help`
or a subcommand's `--help` for options. `--state-dir` and `--port` go before
the subcommand. Start the worker once per desktop session; stop it before
flashing firmware or using tools that open USB directly. `worker-status`
reads the local worker state without sending anything to the device.

Messages accept up to 192 printable ASCII characters supported by the current
display font. Newlines and other control characters are rejected, as are
unknown commands and arguments. Acknowledgment means the command was accepted,
not that the animation has finished. Message states dismiss after 30 seconds
and return to idle. Dances play once. `state boot` replays the startup sequence.

## Joint controls

Supported joints: `head`, `torso`, `left_shoulder`, `right_shoulder`,
`left_elbow`, `right_elbow`, `left_hip`, `right_hip`, `left_knee`, `right_knee`.

These move the **rendered skeleton**, not physical motors. The first command
captures and freezes the current animated pose. Each command applies local XYZ
rotation offsets relative to that captured pose, using degrees between -90 and
90 and an eased transition lasting 100-5000 ms. Repeated commands for the same
joint replace its offset, and other joints retain their targets. Joint names
refer to the robot's own left and right. Large offsets may intersect the mesh;
there is no collision solver. Manual pose holds until another state is selected.
`reset` or `state idle` blends back into the normal idle animation.

## MCP configuration

The implementation uses the maintained v1 line of the
[official MCP Python SDK](https://py.sdk.modelcontextprotocol.io/v1/), pinned
in `tools/requirements-robot.txt`. Add this server to an MCP client's configuration,
replacing the repository path as needed:

```json
{
  "mcpServers": {
    "agentping": {
      "command": "D:\\projects\\AgentPing\\.venv-robot\\Scripts\\python.exe",
      "args": ["D:\\projects\\AgentPing\\tools\\agentping_robot.py", "mcp"]
    }
  }
}
```

Start the USB worker using `robot.cmd start` before invoking tools. The MCP
server does not change client configuration or launch the worker implicitly.
It exposes `robot_status`, `robot_message`, `robot_animation`, `robot_dance`,
`robot_move_joint`, and `robot_reset`. For example: “Show a thinking message,
then turn the robot's head 25 degrees and reset it.”

## Delivery and compatibility

Requires the `live3d_usb` firmware with `joint` support. The old baked profile
does not implement this command set. The firmware validates joint limits too.

Requests are local files under `%USERPROFILE%\.agentping\usb\commands`, consumed
once by the worker; results go to `results`. Explicit demo message text briefly
exists in the request file and is deleted when consumed or when the caller times
out. Commands expire after five seconds and are not replayed after uncertain USB
delivery. Callers wait up to eight seconds for a matching response. Result files
contain acknowledgments, not message text, and abandoned results expire after a
minute. Provider-hook payload filtering remains unchanged. This local IPC trusts
processes with access to the current user's state directory; it opens no network
listener and uses no shell to dispatch commands.

The worker remains alive until stopped; it retries USB connections and shares the
existing notification queue. It does not install a Windows startup service.
Update an old installed worker before using controls; `start` reports an
incompatible active worker rather than taking its port.

## Validation

```powershell
.\.venv-robot\Scripts\python.exe -m unittest discover -s tools/tests -p 'test_robot_control.py'
.\.venv-robot\Scripts\python.exe -m unittest discover -s tools/tests -p 'test_usb_notifications.py'
```

Hardware/MCP smoke checks require a connected device, current firmware, and a
running worker. They exercise the real MCP stdio handshake and acknowledgments.

Validated on the connected ESP32-C6: CLI head/elbow controls, actual stdio MCP
initialization and all six tools, invalid joint rejection, message/animation
commands, concurrent CLI access, and reset to idle. A device framebuffer was
inspected for the head turn and elbow bend. Free heap remained above 28 KB.
Six queue/validation tests and four existing notification tests passed, with
25 repeated concurrent-client tests after resolving Windows file-sharing races.
Run the hardware smoke check with:

```powershell
.\.venv-robot\Scripts\python.exe tools/check_robot_mcp.py
```
