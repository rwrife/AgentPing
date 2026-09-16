"""Exercise the actual stdio MCP server, desktop worker, and connected robot."""
import asyncio
import json
from pathlib import Path
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def main():
    script = Path(__file__).with_name("agentping_robot.py")
    server = StdioServerParameters(command=sys.executable, args=[str(script), "mcp"])
    records = []
    async with stdio_client(server) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = await session.list_tools()
            assert {t.name for t in tools.tools} == {"robot_status", "robot_message", "robot_animation", "robot_dance", "robot_move_joint", "robot_reset"}

            async def call(name, args=None):
                result = await session.call_tool(name, args or {})
                assert not result.isError, result
                value = json.loads(result.content[0].text)
                assert value["ok"], value
                records.append({"tool": name, "result": value})
                print(name, value, flush=True)
                return value

            await call("robot_reset")
            await asyncio.sleep(.6)
            await call("robot_move_joint", {"joint": "head", "y": 25, "duration_ms": 800})
            await call("robot_move_joint", {"joint": "left_elbow", "x": 45})
            await asyncio.sleep(1)
            assert "state=manual" in (await call("robot_status"))["reply"]
            invalid = await session.call_tool("robot_move_joint", {"joint": "head", "y": 91})
            assert invalid.isError, "Out-of-range joint was accepted"
            await call("robot_message", {"message": "Hello from MCP!", "state": "attention"})
            assert "bubble=1" in (await call("robot_status"))["reply"]
            # CLI and MCP share the same USB owner and receive their own replies.
            process = await asyncio.create_subprocess_exec(sys.executable, str(script), "status", stdout=asyncio.subprocess.PIPE)
            await call("robot_dance", {"name": "chicken"})
            stdout, _ = await process.communicate()
            assert process.returncode == 0 and json.loads(stdout)["ok"]
            await call("robot_animation", {"state": "thinking"})
            await call("robot_reset")
            assert "state=idle" in (await call("robot_status"))["reply"]
    output = Path("test-results/robot-control")
    output.mkdir(parents=True, exist_ok=True)
    (output / "mcp-hardware.json").write_text(json.dumps(records, indent=2))
    print("PASS: MCP initialization, six tools, joint limits, messages, animation, CLI coexistence and reset")


if __name__ == "__main__":
    asyncio.run(main())
