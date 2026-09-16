"""Exercise installed Copilot hooks through the USB worker and real device.

Requires current firmware, installed hooks, and a running worker. These are
synthetic hook inputs, not evidence of a live Copilot session emitting events.
"""
import json
from pathlib import Path
import subprocess
import time

from agentping_usb_notifications import state_dir
from robot_control import request


def main():
    hooks = json.loads((Path.home() / '.copilot/hooks/agentping-usb.json').read_text())['hooks']
    records = []
    for event, expected in [('userPromptSubmitted', 'thinking'),
                            ('awaitingUserInput', 'attention'),
                            ('postToolUse', 'attention'),
                            ('errorOccurred', 'error')]:
        handler = next(h for h in hooks[event] if any('agentping_usb_notifications.py' in a for a in h.get('args', [])))
        subprocess.run([handler['exec'], *handler['args']], input='{}', text=True, check=True)
        if event == 'postToolUse':
            time.sleep(1)  # Repeated thinking must not replace attention during cooldown.
        deadline = time.monotonic() + 8
        while True:
            status = request(state_dir(), 'status', {})
            if f'state={expected} ' in status['reply']:
                break
            if time.monotonic() >= deadline:
                raise AssertionError((event, expected, status))
            time.sleep(.25)
        records.append({'event': event, 'expected': expected, **status})
        print(event, status['reply'], flush=True)
    request(state_dir(), 'reset', {})
    output = Path('test-results/copilot-thinking.json')
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(records, indent=2))
    print('PASS: thinking, attention, suppressed repeat thinking, error; reset to idle')


if __name__ == '__main__':
    main()
