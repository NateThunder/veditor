from __future__ import annotations

from contextlib import redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest


#== test module loading =======================================================
ROOT = Path(__file__).resolve().parents[1]


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


worker = load_module("veditor_worker", ROOT / "veditor_worker.py")
checker = load_module("runtime_checker", ROOT / "check-watermark-runtime.py")
#==============================================================================


class WorkerValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.input_path = self.root / "input.png"
        self.input_path.write_bytes(b"image-placeholder")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def parse(self, *arguments: str):
        return worker.build_parser().parse_args(arguments)

    def assert_failure_code(self, expected_code: str, *arguments: str) -> None:
        with self.assertRaises(worker.WorkerFailure) as context:
            worker.validate_arguments(self.parse(*arguments))
        self.assertEqual(expected_code, context.exception.code)

    #== argument validation ==================================================
    def test_requires_input_and_output(self) -> None:
        self.assert_failure_code("INVALID_ARGUMENTS")

    def test_rejects_missing_input(self) -> None:
        self.assert_failure_code(
            "INVALID_INPUT",
            "--input", str(self.root / "missing.png"),
            "--output", str(self.root / "out.png"),
        )

    def test_rejects_identical_input_and_output(self) -> None:
        self.assert_failure_code(
            "INVALID_OUTPUT",
            "--input", str(self.input_path),
            "--output", str(self.input_path),
        )

    def test_rejects_unsupported_input(self) -> None:
        unsupported = self.root / "input.xyz"
        unsupported.write_text("unsupported", encoding="utf-8")
        self.assert_failure_code(
            "UNSUPPORTED_INPUT",
            "--input", str(unsupported),
            "--output", str(self.root / "out.png"),
        )

    def test_rejects_invalid_detection_interval(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--detection-skip", "11",
        )

    def test_rejects_negative_fade(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--fade-in", "-0.1",
        )

    def test_rejects_invalid_maximum_bbox(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--max-bbox-percent", "0",
        )

    def test_rejects_existing_output_without_overwrite(self) -> None:
        output = self.root / "out.png"
        output.write_bytes(b"existing")
        self.assert_failure_code(
            "OUTPUT_EXISTS",
            "--input", str(self.input_path),
            "--output", str(output),
        )

    def test_rejects_invalid_mask_padding(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--mask-padding-percent", "10.1",
        )

    def test_rejects_conflicting_preview_modes(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--preview",
            "--selection-preview",
        )

    def test_allows_png_preview_for_video_input(self) -> None:
        video_path = self.root / "input.mp4"
        video_path.write_bytes(b"video-placeholder")
        arguments = self.parse(
            "--input", str(video_path),
            "--output", str(self.root / "preview.png"),
            "--selection-preview",
        )

        _input, output, media_type = worker.validate_arguments(arguments)

        self.assertEqual(self.root / "preview.png", output)
        self.assertEqual("video", media_type)

    def test_parses_normalized_manual_regions(self) -> None:
        arguments = self.parse(
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--regions-json", '[{"X":0.1,"Y":0.2,"Width":0.3,"Height":0.4}]',
        )

        worker.validate_arguments(arguments)

        self.assertEqual(
            [{"x": 0.1, "y": 0.2, "width": 0.3, "height": 0.4}],
            arguments.regions,
        )

    def test_rejects_manual_region_outside_media_bounds(self) -> None:
        self.assert_failure_code(
            "INVALID_ARGUMENTS",
            "--input", str(self.input_path),
            "--output", str(self.root / "out.png"),
            "--regions-json", '[{"x":0.9,"y":0,"width":0.2,"height":0.2}]',
        )
    #==========================================================================


class WorkerHelperTests(unittest.TestCase):
    #== protocol formatting ==================================================
    def test_emit_writes_one_valid_json_line(self) -> None:
        stream = io.StringIO()
        with redirect_stdout(stream):
            worker.emit("status", stage="checking_runtime", message="Checking runtime")
        lines = stream.getvalue().splitlines()
        self.assertEqual(1, len(lines))
        self.assertEqual("status", json.loads(lines[0])["type"])

    def test_progress_is_clamped(self) -> None:
        self.assertEqual(0.0, worker.clamp_progress(-10))
        self.assertEqual(100.0, worker.clamp_progress(120))
        self.assertEqual(33.34, worker.clamp_progress(33.336))

    def test_preview_message_shape_is_json_compatible(self) -> None:
        stream = io.StringIO()
        detections = [{"bbox": [10, 20, 100, 80], "areaPercent": 2.1, "accepted": True}]
        with redirect_stdout(stream):
            worker.emit("preview", previewPath=r"C:\Temp\preview.png", detections=detections)
        message = json.loads(stream.getvalue())
        self.assertEqual([10, 20, 100, 80], message["detections"][0]["bbox"])
        self.assertTrue(message["detections"][0]["accepted"])

    def test_manual_regions_convert_to_pixel_boxes(self) -> None:
        detections = worker.detections_from_regions(
            [{"x": 0.1, "y": 0.2, "width": 0.3, "height": 0.4}],
            (1000, 500),
        )

        self.assertEqual([100, 100, 400, 300], detections[0]["bbox"])
        self.assertTrue(detections[0]["accepted"])
    #==========================================================================

    #== file and executable helpers ==========================================
    def test_ffmpeg_explicit_path_detection(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / "ffmpeg.exe"
            executable.write_bytes(b"placeholder")
            self.assertEqual(executable.resolve(), worker.resolve_ffmpeg(str(executable)))

    def test_installation_marker_parsing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / "installation.json"
            marker.write_text('{"complete":true,"mode":"CPU"}', encoding="utf-8")
            self.assertEqual("CPU", checker.parse_installation_marker(marker)["mode"])
            marker.write_text("not json", encoding="utf-8")
            self.assertEqual({}, checker.parse_installation_marker(marker))

    def test_installation_marker_accepts_windows_powershell_bom(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / "installation.json"
            marker.write_text('{"complete":true,"mode":"CPU"}', encoding="utf-8-sig")
            self.assertTrue(worker.parse_installation_marker(marker)["complete"])

    def test_error_code_mapping_for_cuda_oom(self) -> None:
        failure = worker.map_unexpected_failure(RuntimeError("CUDA out of memory"))
        self.assertEqual("CUDA_OUT_OF_MEMORY", failure.code)

    def test_partial_file_cleanup_helper(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            partial = Path(directory) / "partial.mp4"
            partial.write_bytes(b"partial")
            worker.cleanup_partial_output(partial)
            self.assertFalse(partial.exists())

    def test_atomic_commit_replaces_only_when_authorized(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            temporary_output = root / "temporary.png"
            final_output = root / "final.png"
            temporary_output.write_bytes(b"new")
            final_output.write_bytes(b"old")
            worker.commit_output(temporary_output, final_output, overwrite=True)
            self.assertEqual(b"new", final_output.read_bytes())
    #==========================================================================


if __name__ == "__main__":
    unittest.main()
