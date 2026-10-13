"""Run with: python -m unittest discover -s Tools/Benchmarks -p test_*.py"""
import copy
import csv
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import benchmark_render as benchmark


class BenchmarkTests(unittest.TestCase):
    def test_failed_run_cleans_disposable_manifest_and_lock(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(benchmark, "ROOT", root):
                with self.assertRaises(FileNotFoundError):
                    benchmark.run(SimpleNamespace(profile=root / "missing.json"))
                self.assertFalse((root / ".benchmark.lock").exists())
                self.assertEqual(list(root.glob(".benchmark-*.plutoproject")), [])
                (root / ".benchmark.lock").write_text("123")
                with self.assertRaises(ValueError):
                    benchmark.run(SimpleNamespace())
                self.assertEqual((root / ".benchmark.lock").read_text(), "123")

    def test_snapshot_changes_only_requested_transform(self):
        text = "SCENE\t1\nENTITY\t1\t0\t1\tPlayer\t0,0,0\t0,0,0\t1,1,1\nENTITY\t2\t0\t1\tOther\t0,0,0\t0,0,0\t1,1,1\n"
        result = benchmark.snapshot_scene(text, {"entity": 1, "position": [1, 2, 3], "rotation": [0, 90, 0]})
        self.assertIn("Player\t1,2,3\t0,90,0", result)
        self.assertEqual(result.splitlines()[-1], text.splitlines()[-1])
        with self.assertRaises(ValueError):
            benchmark.snapshot_scene(text, {"entity": 99, "position": [0]*3, "rotation": [0]*3})

    def test_comparison_rejects_changed_conditions_and_geometry(self):
        report = {"conditions": {"width": 1138}, "workload": {"geometry_draws": [100]},
                  "metrics": {"frame_ms": {"mean": 10}}}
        after = copy.deepcopy(report)
        after["metrics"]["frame_ms"]["mean"] = 8
        self.assertAlmostEqual(benchmark.compare(report, after)["frame_ms"]["change_percent"], -20)
        after["conditions"]["width"] = 759
        with self.assertRaises(ValueError):
            benchmark.compare(report, after)
        after["conditions"] = report["conditions"]
        after["workload"]["geometry_draws"] = [200]
        with self.assertRaises(ValueError):
            benchmark.compare(report, after)

    def test_component_overrides_are_scoped_and_require_a_match(self):
        text = "ENTITY\t1\t0\t1\tPlayer\t0,0,0\t0,0,0\t1,1,1\nCOMPONENT\t6\tLightComponent\t1\nPROPERTY\tShadow Method\t6\t1\t2\tCSM\tVSM\nEND_COMPONENT\n"
        anchor = {"entity": 1, "position": [0]*3, "rotation": [0]*3}
        override = {"entity": 6, "component": "LightComponent", "property": "Shadow Method", "value": "0"}
        result = benchmark.snapshot_scene(text, anchor, [override])
        self.assertIn("PROPERTY\tShadow Method\t6\t0\t2\tCSM\tVSM", result)
        override["entity"] = 7
        with self.assertRaises(ValueError):
            benchmark.snapshot_scene(text, anchor, [override])

    def test_missing_and_incomplete_metrics_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.csv"
            path.write_text("sample,frame_ms\n0,1\n")
            with self.assertRaises(ValueError):
                benchmark.read_samples(path, 2)
            with self.assertRaises(ValueError):
                benchmark.read_samples(path, 1)

    def test_gpu_summary_ignores_repeated_observations(self):
        keys = ["frame_ms", "scene_update_ms", "render_present_ms", "scene_gpu_ms", "gpu_available",
                "internal_width", "internal_height", "output_width", "output_height", "opaque_triangles",
                "geometry_draws", "geometry_instances", "shadow_draws", "gpu_observation_id",
                "shadow_recording_ms", "geometry_recording_ms"]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.csv"
            with path.open("w", newline="") as stream:
                writer = csv.DictWriter(stream, keys)
                writer.writeheader()
                for available, gpu in [(1, 2), (0, 100), (1, 4)]:
                    row = dict.fromkeys(keys, 1)
                    row.update(gpu_available=available, scene_gpu_ms=gpu)
                    writer.writerow(row)
            metrics, _ = benchmark.read_samples(path, 3)
            self.assertEqual(metrics["scene_gpu_ms"]["mean"], 3)
            self.assertEqual(metrics["scene_gpu_ms"]["observations"], 2)


if __name__ == "__main__":
    unittest.main()
