import sys
from pathlib import Path
from unittest import TestCase
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agentping_robot import play_motion


class MotionReplacementTests(TestCase):
    def test_releases_previous_clip_before_measuring_capacity(self):
        calls = []
        def request(root, action, args, **kwargs):
            calls.append(action)
            return {"ok": True}
        def budget(root):
            self.assertEqual(calls, ["reset"])
            return 2
        with patch("agentping_robot.load_motion", return_value={"frames": [[1], [2], [3]], "rate": 10}), \
             patch("agentping_robot.request", side_effect=request), \
             patch("agentping_robot.frame_budget", side_effect=budget):
            result = play_motion(Path("unused"), Path("dance.fbx"), 10, 0)
        self.assertEqual(calls, ["reset", "motion"])
        self.assertEqual(result["clip"]["frames"], 2)
        self.assertEqual(result["clip"]["rate"], 10)

    def test_invalid_file_does_not_interrupt_current_dance(self):
        with patch("agentping_robot.load_motion", side_effect=ValueError("invalid clip")), \
             patch("agentping_robot.request") as request:
            with self.assertRaises(ValueError):
                play_motion(Path("unused"), Path("dance.fbx"), 10, 0)
            request.assert_not_called()
