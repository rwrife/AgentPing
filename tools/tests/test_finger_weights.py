import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('encode_live3d', Path(__file__).resolve().parents[2] / 'scripts/encode_live3d.py')
encoder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(encoder)

class FingerWeightsTests(unittest.TestCase):
    def test_repairs_only_root_bound_fingertips_and_is_idempotent(self):
        def vertex(x, y=900, bone=0):
            return [x, y, 100, 123, 456, bone, 0, 0, 0, 255, 0, 0, 0]
        model = {'bones': [{'name': n} for n in ('spine', 'handL', 'handR')],
                 'vertices': [vertex(-4800), vertex(4800), vertex(0), vertex(-4200), vertex(-4800, bone=2), vertex(-4800, y=-900)]}
        originals = [v[:] for v in model['vertices']]
        self.assertEqual(encoder.repair_finger_weights(model), 2)
        self.assertEqual([v[5] for v in model['vertices'][:2]], [2, 1])
        self.assertEqual(model['vertices'][2:], originals[2:])
        for before, after in zip(originals, model['vertices']):
            self.assertEqual(before[:5], after[:5])
            self.assertEqual(sum(after[9:13]), 255)
        self.assertEqual(encoder.repair_finger_weights(model), 0)
