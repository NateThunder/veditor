"""Local Florence-2 and LaMA worker for Veditor.

The worker reserves stdout for one-JSON-object-per-line protocol messages. All
AI imports are intentionally lazy so validation and runtime checks stay light.
"""

from __future__ import annotations

import argparse
import importlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import threading
from typing import Any, Iterable, Sequence


#== protocol and runtime constants ===========================================
EXIT_SUCCESS = 0
EXIT_INVALID_ARGUMENTS = 2
EXIT_DEPENDENCY_FAILURE = 3
EXIT_MODEL_DOWNLOAD_FAILURE = 4
EXIT_MODEL_LOAD_FAILURE = 5
EXIT_UNSUPPORTED_INPUT = 6
EXIT_FFMPEG_FAILURE = 7
EXIT_PROCESSING_FAILURE = 8
EXIT_OUTPUT_FAILURE = 9
EXIT_CANCELLED = 10

FLORENCE_MODEL_ID = "florence-community/Florence-2-large"
LAMA_FILENAME = "big-lama.pt"
RUNTIME_SCHEMA_VERSION = 2
SUPPORTED_IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"}
SUPPORTED_VIDEO_EXTENSIONS = {".mp4", ".avi", ".mov", ".mkv", ".flv", ".wmv", ".webm", ".m4v"}

_CANCELLED = threading.Event()


def default_models_root() -> Path:
    script_directory = Path(__file__).resolve().parent
    if script_directory.name.lower() == "watermarkai":
        return script_directory.parent / "Models"
    local_app_data = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local"))
    return local_app_data / "Veditor" / "Models"


os.environ.setdefault("HF_HOME", str(default_models_root() / "HuggingFace"))
os.environ.setdefault("TORCH_HOME", str(default_models_root() / "Torch"))
os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")
#==============================================================================


class WorkerFailure(Exception):
    """A user-facing failure with a stable protocol code and exit status."""

    def __init__(self, code: str, message: str, exit_code: int) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.exit_code = exit_code


class ProtocolArgumentParser(argparse.ArgumentParser):
    """Convert argparse failures into the worker's structured error contract."""

    def error(self, message: str) -> None:
        raise WorkerFailure("INVALID_ARGUMENTS", message, EXIT_INVALID_ARGUMENTS)


class LamaRunner:
    """Minimal compatible loader for IOPaint's big-LaMA TorchScript model."""

    def __init__(self, torch_module: Any, device: str) -> None:
        model_path = lama_model_path()
        if not model_path.is_file():
            raise WorkerFailure("MODEL_NOT_AVAILABLE", "The LaMA model is not installed.", EXIT_MODEL_DOWNLOAD_FAILURE)
        self.torch = torch_module
        self.device = device
        self.model = torch_module.jit.load(str(model_path), map_location=device).eval()

    def __call__(self, image: Any, mask: Any) -> Any:
        import numpy as np

        image_array = np.asarray(image, dtype=np.float32) / 255.0
        mask_array = (np.asarray(mask, dtype=np.float32) > 0).astype(np.float32)
        height, width = mask_array.shape
        padded_height = int(math.ceil(height / 8.0) * 8)
        padded_width = int(math.ceil(width / 8.0) * 8)
        image_array = np.pad(
            image_array,
            ((0, padded_height - height), (0, padded_width - width), (0, 0)),
            mode="reflect",
        )
        mask_array = np.pad(
            mask_array,
            ((0, padded_height - height), (0, padded_width - width)),
            mode="constant",
        )
        image_tensor = self.torch.from_numpy(image_array.transpose(2, 0, 1)).unsqueeze(0).to(self.device)
        mask_tensor = self.torch.from_numpy(mask_array).unsqueeze(0).unsqueeze(0).to(self.device)
        with self.torch.inference_mode():
            output = self.model(image_tensor, mask_tensor)[0].permute(1, 2, 0).detach().cpu().numpy()
        return np.clip(output[:height, :width] * 255.0, 0, 255).astype(np.uint8)


#== protocol output ===========================================================
def emit(message_type: str, **payload: Any) -> dict[str, Any]:
    message = {"type": message_type, **payload}
    sys.stdout.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()
    return message


def emit_progress(stage: str, percent: float, message: str) -> dict[str, Any]:
    return emit(
        "progress",
        stage=stage,
        percent=clamp_progress(percent),
        message=message,
    )


def emit_error(failure: WorkerFailure) -> None:
    emit("error", code=failure.code, message=failure.message)


def diagnostic(message: str) -> None:
    sys.stderr.write(message.rstrip() + "\n")
    sys.stderr.flush()
#==============================================================================


#== input collection ==========================================================
def build_parser() -> argparse.ArgumentParser:
    parser = ProtocolArgumentParser(add_help=True)
    parser.add_argument("--input")
    parser.add_argument("--output")
    parser.add_argument("--detection-prompt", default="watermark")
    parser.add_argument("--max-bbox-percent", type=float, default=10.0)
    parser.add_argument("--detection-skip", type=int, default=1)
    parser.add_argument("--fade-in", type=float, default=0.0)
    parser.add_argument("--fade-out", type=float, default=0.0)
    parser.add_argument("--preview", "--preview-only", action="store_true", dest="preview_only")
    parser.add_argument("--selection-preview", action="store_true")
    parser.add_argument("--frame-index", type=int)
    parser.add_argument("--regions-json")
    parser.add_argument("--mask-padding-percent", type=float, default=0.5)
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--device", choices=("auto", "cpu", "cuda"), default="auto")
    parser.add_argument("--ffmpeg-path")
    parser.add_argument("--temporary-directory")
    parser.add_argument("--check-runtime", action="store_true")
    return parser
#==============================================================================


#== input validation ==========================================================
def validate_arguments(args: argparse.Namespace) -> tuple[Path, Path, str]:
    if not args.input or not args.output:
        raise WorkerFailure(
            "INVALID_ARGUMENTS",
            "--input and --output are required for processing.",
            EXIT_INVALID_ARGUMENTS,
        )

    input_path = Path(args.input).expanduser().resolve()
    output_path = Path(args.output).expanduser().resolve()

    if not input_path.is_file():
        raise WorkerFailure("INVALID_INPUT", "The input media file does not exist.", EXIT_INVALID_ARGUMENTS)
    if paths_equal(input_path, output_path):
        raise WorkerFailure("INVALID_OUTPUT", "The output path cannot overwrite the input file.", EXIT_INVALID_ARGUMENTS)
    if output_path.exists() and not args.overwrite:
        raise WorkerFailure("OUTPUT_EXISTS", "The output file already exists; use --overwrite to replace it.", EXIT_OUTPUT_FAILURE)
    if not args.detection_prompt or not args.detection_prompt.strip():
        raise WorkerFailure("INVALID_ARGUMENTS", "The detection prompt cannot be empty.", EXIT_INVALID_ARGUMENTS)
    if args.detection_skip < 1 or args.detection_skip > 10:
        raise WorkerFailure("INVALID_ARGUMENTS", "Detection interval must be between 1 and 10.", EXIT_INVALID_ARGUMENTS)
    if not math.isfinite(args.fade_in) or args.fade_in < 0:
        raise WorkerFailure("INVALID_ARGUMENTS", "Fade-in seconds cannot be negative.", EXIT_INVALID_ARGUMENTS)
    if not math.isfinite(args.fade_out) or args.fade_out < 0:
        raise WorkerFailure("INVALID_ARGUMENTS", "Fade-out seconds cannot be negative.", EXIT_INVALID_ARGUMENTS)
    if not math.isfinite(args.max_bbox_percent) or not 0 < args.max_bbox_percent <= 100:
        raise WorkerFailure("INVALID_ARGUMENTS", "Maximum bounding-box percentage must be greater than 0 and at most 100.", EXIT_INVALID_ARGUMENTS)
    if args.frame_index is not None and args.frame_index < 0:
        raise WorkerFailure("INVALID_ARGUMENTS", "Frame index cannot be negative.", EXIT_INVALID_ARGUMENTS)
    if args.preview_only and args.selection_preview:
        raise WorkerFailure("INVALID_ARGUMENTS", "Choose either automatic preview or selection preview.", EXIT_INVALID_ARGUMENTS)
    if not math.isfinite(args.mask_padding_percent) or not 0 <= args.mask_padding_percent <= 10:
        raise WorkerFailure("INVALID_ARGUMENTS", "Mask padding must be between 0 and 10 percent.", EXIT_INVALID_ARGUMENTS)

    args.regions = parse_regions_json(args.regions_json)

    extension = input_path.suffix.lower()
    if extension in SUPPORTED_IMAGE_EXTENSIONS:
        media_type = "image"
    elif extension in SUPPORTED_VIDEO_EXTENSIONS:
        media_type = "video"
    else:
        raise WorkerFailure("UNSUPPORTED_INPUT", f"Unsupported input type: {extension or '(none)'}." , EXIT_UNSUPPORTED_INPUT)

    if args.preview_only or args.selection_preview:
        if output_path.suffix.lower() != ".png":
            raise WorkerFailure("UNSUPPORTED_OUTPUT", "Preview output must use the .png extension.", EXIT_OUTPUT_FAILURE)
    elif media_type == "video" and output_path.suffix.lower() != ".mp4":
        raise WorkerFailure("UNSUPPORTED_OUTPUT", "Video output must use the .mp4 extension.", EXIT_OUTPUT_FAILURE)
    elif media_type == "image" and output_path.suffix.lower() not in SUPPORTED_IMAGE_EXTENSIONS:
        raise WorkerFailure("UNSUPPORTED_OUTPUT", "Image output must use a supported image extension.", EXIT_OUTPUT_FAILURE)

    ensure_output_directory_writable(output_path.parent)
    return input_path, output_path, media_type


def parse_regions_json(value: str | None) -> list[dict[str, float]]:
    if not value:
        return []

    try:
        decoded = json.loads(value)
    except (TypeError, ValueError) as exc:
        raise WorkerFailure("INVALID_ARGUMENTS", "Selected watermark regions are not valid JSON.", EXIT_INVALID_ARGUMENTS) from exc

    if not isinstance(decoded, list) or len(decoded) > 64:
        raise WorkerFailure("INVALID_ARGUMENTS", "Selected watermark regions must be a list of no more than 64 rectangles.", EXIT_INVALID_ARGUMENTS)

    regions: list[dict[str, float]] = []
    for item in decoded:
        if not isinstance(item, dict):
            raise WorkerFailure("INVALID_ARGUMENTS", "Each selected watermark region must be a rectangle.", EXIT_INVALID_ARGUMENTS)
        normalized = {str(key).lower(): value for key, value in item.items()}
        try:
            x = float(normalized["x"])
            y = float(normalized["y"])
            width = float(normalized["width"])
            height = float(normalized["height"])
        except (KeyError, TypeError, ValueError) as exc:
            raise WorkerFailure("INVALID_ARGUMENTS", "A selected watermark region is missing valid coordinates.", EXIT_INVALID_ARGUMENTS) from exc
        values = (x, y, width, height)
        if not all(math.isfinite(number) for number in values) or x < 0 or y < 0 or width <= 0 or height <= 0 or x + width > 1.000001 or y + height > 1.000001:
            raise WorkerFailure("INVALID_ARGUMENTS", "Selected watermark regions must stay inside the media bounds.", EXIT_INVALID_ARGUMENTS)
        regions.append({"x": x, "y": y, "width": width, "height": height})
    return regions


def ensure_output_directory_writable(directory: Path) -> None:
    try:
        directory.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(prefix=".veditor-write-test-", dir=directory, delete=True):
            pass
    except OSError as exc:
        raise WorkerFailure("OUTPUT_NOT_WRITABLE", f"The output folder is not writable: {exc}", EXIT_OUTPUT_FAILURE) from exc


def paths_equal(first: Path, second: Path) -> bool:
    return os.path.normcase(str(first.resolve())) == os.path.normcase(str(second.resolve()))


def clamp_progress(percent: float) -> float:
    if not math.isfinite(percent):
        return 0.0
    return round(max(0.0, min(100.0, float(percent))), 2)


def resolve_ffmpeg(explicit_path: str | None) -> Path | None:
    candidates = [explicit_path, os.environ.get("VEDITOR_FFMPEG_PATH"), shutil.which("ffmpeg")]
    for candidate in candidates:
        if candidate:
            path = Path(candidate).expanduser().resolve()
            if path.is_file():
                return path
    return None
#==============================================================================


#== runtime inspection ========================================================
def package_version(distribution_name: str) -> str | None:
    try:
        return importlib.metadata.version(distribution_name)
    except importlib.metadata.PackageNotFoundError:
        return None


def lama_model_path() -> Path:
    torch_home = Path(os.environ["TORCH_HOME"])
    return torch_home / "hub" / "checkpoints" / LAMA_FILENAME


def florence_model_available() -> bool:
    try:
        from huggingface_hub import scan_cache_dir

        return any(repo.repo_id == FLORENCE_MODEL_ID for repo in scan_cache_dir().repos)
    except Exception:
        hub_root = Path(os.environ["HF_HOME"]) / "hub"
        encoded_name = "models--" + FLORENCE_MODEL_ID.replace("/", "--")
        return (hub_root / encoded_name / "snapshots").is_dir()


def collect_runtime_status(ffmpeg_path: str | None = None) -> dict[str, Any]:
    versions = {
        "torchVersion": package_version("torch"),
        "transformersVersion": package_version("transformers"),
    }
    required = ("torch", "transformers", "opencv-python-headless", "Pillow", "numpy")
    dependencies_available = all(package_version(name) is not None for name in required)
    cuda_available = False
    gpu_name = None

    if versions["torchVersion"]:
        try:
            torch = importlib.import_module("torch")
            cuda_available = bool(torch.cuda.is_available())
            if cuda_available:
                gpu_name = str(torch.cuda.get_device_name(0))
        except Exception as exc:
            dependencies_available = False
            diagnostic(f"Torch runtime inspection failed: {exc}")

    ffmpeg = resolve_ffmpeg(ffmpeg_path)
    florence_available = florence_model_available() if package_version("huggingface-hub") else False
    lama_available = lama_model_path().is_file()
    marker_path = Path(__file__).resolve().with_name("installation.json")
    marker = parse_installation_marker(marker_path)
    marker_exists = marker_path.is_file()
    marker_outdated = marker_exists and marker.get("schemaVersion") != RUNTIME_SCHEMA_VERSION
    installation_mode = str(marker.get("mode") or "").upper() or None
    marker_valid = bool(
        marker_exists
        and not marker_outdated
        and marker.get("complete") is True
        and installation_mode in {"CPU", "CUDA"}
    )
    mode_valid = installation_mode != "CUDA" or cuda_available
    installed = bool(
        dependencies_available
        and ffmpeg is not None
        and florence_available
        and lama_available
        and marker_valid
        and mode_valid
    )

    missing_components: list[str] = []
    if not marker_exists:
        missing_components.append("installation marker")
    elif marker_outdated:
        missing_components.append("outdated installation marker")
    elif not marker_valid:
        missing_components.append("invalid installation marker")
    if not dependencies_available:
        missing_components.append("Python dependencies")
    if not florence_available:
        missing_components.append("Florence-2 model")
    if not lama_available:
        missing_components.append("LaMA model")
    if ffmpeg is None:
        missing_components.append("FFmpeg")
    if installation_mode == "CUDA" and not cuda_available:
        missing_components.append("compatible CUDA device")

    message = "Runtime ready."
    if missing_components:
        message = "Runtime needs repair: " + ", ".join(missing_components) + "."

    return {
        "installed": installed,
        "pythonVersion": sys.version.split()[0],
        **versions,
        "dependenciesAvailable": dependencies_available,
        "florenceModelAvailable": florence_available,
        "lamaModelAvailable": lama_available,
        "ffmpegAvailable": ffmpeg is not None,
        "cudaAvailable": cuda_available,
        "gpuName": gpu_name,
        "installationMode": installation_mode,
        "markerExists": marker_exists,
        "markerValid": marker_valid,
        "markerOutdated": marker_outdated,
        "missingComponents": missing_components,
        "message": message,
    }


def parse_installation_marker(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError, TypeError):
        return {}


def emit_runtime_status(ffmpeg_path: str | None = None) -> None:
    status = collect_runtime_status(ffmpeg_path)
    emit(
        "runtime_status",
        installed=status["installed"],
        dependenciesAvailable=status["dependenciesAvailable"],
        cudaAvailable=status["cudaAvailable"],
        ffmpegAvailable=status["ffmpegAvailable"],
        florenceModelAvailable=status["florenceModelAvailable"],
        lamaModelAvailable=status["lamaModelAvailable"],
        markerExists=status["markerExists"],
        markerValid=status["markerValid"],
        markerOutdated=status["markerOutdated"],
        installationMode=status["installationMode"],
        gpuName=status["gpuName"],
        pythonVersion=status["pythonVersion"],
        missingComponents=status["missingComponents"],
        message=status["message"],
    )
#==============================================================================


#== device selection ==========================================================
def select_device(requested: str) -> tuple[Any, str, bool]:
    try:
        torch = importlib.import_module("torch")
    except Exception as exc:
        raise WorkerFailure("DEPENDENCY_MISSING", f"PyTorch could not be imported: {exc}", EXIT_DEPENDENCY_FAILURE) from exc

    cuda_available = bool(torch.cuda.is_available())
    torch_cuda_version = getattr(torch.version, "cuda", None)

    if requested == "cuda" and not cuda_available:
        detail = "The installed PyTorch build has no CUDA support." if not torch_cuda_version else "CUDA is not available to PyTorch."
        raise WorkerFailure("CUDA_UNAVAILABLE", detail, EXIT_DEPENDENCY_FAILURE)

    use_cuda = cuda_available and requested in {"auto", "cuda"}
    return torch, "cuda" if use_cuda else "cpu", use_cuda
#==============================================================================


#== model loading and detection ===============================================
def load_lama(device: str) -> Any:
    try:
        torch = importlib.import_module("torch")
        emit("status", stage="loading_lama", message="Loading LaMA")
        return LamaRunner(torch, device)
    except WorkerFailure:
        raise
    except Exception as exc:
        raise WorkerFailure("MODEL_LOAD_FAILED", f"Unable to load LaMA: {exc}", EXIT_MODEL_LOAD_FAILURE) from exc


def load_models(device: str, preview_only: bool) -> tuple[Any, Any, Any | None]:
    try:
        torch = importlib.import_module("torch")
        transformers = importlib.import_module("transformers")
        auto_processor = transformers.AutoProcessor
        florence_class = importlib.import_module(
            "transformers.models.florence2.modeling_florence2"
        ).Florence2ForConditionalGeneration

        emit("status", stage="loading_florence", message="Loading Florence-2")
        dtype = torch.float32 if device == "cpu" else torch.float16
        model = florence_class.from_pretrained(
            FLORENCE_MODEL_ID,
            dtype=dtype,
            local_files_only=True,
        ).to(device).eval()
        processor = auto_processor.from_pretrained(
            FLORENCE_MODEL_ID,
            local_files_only=True,
            use_fast=False,
        )

        lama = None
        if not preview_only:
            lama = load_lama(device)
        return model, processor, lama
    except WorkerFailure:
        raise
    except Exception as exc:
        message = str(exc)
        if "offline" in message.lower() or "couldn't connect" in message.lower() or "not found" in message.lower():
            raise WorkerFailure("MODEL_NOT_AVAILABLE", "Required model files are not installed. Repair the runtime.", EXIT_MODEL_DOWNLOAD_FAILURE) from exc
        raise WorkerFailure("MODEL_LOAD_FAILED", f"Unable to load the AI models: {message}", EXIT_MODEL_LOAD_FAILURE) from exc


def detect_watermarks(
    image: Any,
    model: Any,
    processor: Any,
    device: str,
    prompt_text: str,
    max_bbox_percent: float,
) -> list[dict[str, Any]]:
    task_prompt = "<OPEN_VOCABULARY_DETECTION>"
    prompt = task_prompt + prompt_text.strip()
    inputs = processor(text=prompt, images=image, return_tensors="pt")
    converted = {
        key: value.to(device=device, dtype=model.dtype) if value.is_floating_point() else value.to(device)
        for key, value in inputs.items()
    }

    generated_ids = model.generate(
        input_ids=converted["input_ids"],
        pixel_values=converted["pixel_values"],
        max_new_tokens=1024,
        do_sample=False,
        num_beams=1,
    )
    generated_text = processor.batch_decode(generated_ids, skip_special_tokens=False)[0]
    answer = processor.post_process_generation(
        generated_text,
        task=task_prompt,
        image_size=(image.width, image.height),
    )
    detection_data = answer.get(task_prompt, {}) if isinstance(answer, dict) else {}
    image_area = max(1, image.width * image.height)
    detections: list[dict[str, Any]] = []

    for raw_bbox in detection_data.get("bboxes", []):
        if len(raw_bbox) != 4:
            continue
        x1, y1, x2, y2 = normalize_bbox(raw_bbox, image.width, image.height)
        area_percent = max(0, x2 - x1) * max(0, y2 - y1) / image_area * 100.0
        detections.append(
            {
                "bbox": [x1, y1, x2, y2],
                "areaPercent": round(area_percent, 2),
                "accepted": bool(x2 > x1 and y2 > y1 and area_percent <= max_bbox_percent),
            }
        )
    return detections


def normalize_bbox(raw_bbox: Sequence[float], width: int, height: int) -> tuple[int, int, int, int]:
    x1, y1, x2, y2 = (int(round(float(value))) for value in raw_bbox)
    return (
        max(0, min(width, x1)),
        max(0, min(height, y1)),
        max(0, min(width, x2)),
        max(0, min(height, y2)),
    )


def detections_from_regions(regions: Iterable[dict[str, float]], image_size: tuple[int, int]) -> list[dict[str, Any]]:
    width, height = image_size
    detections: list[dict[str, Any]] = []
    for region in regions:
        x1 = int(round(region["x"] * width))
        y1 = int(round(region["y"] * height))
        x2 = int(round((region["x"] + region["width"]) * width))
        y2 = int(round((region["y"] + region["height"]) * height))
        detections.append({"bbox": list(normalize_bbox((x1, y1, x2, y2), width, height)), "accepted": True})
    return detections


def create_mask(
    image_size: tuple[int, int],
    detections: Iterable[dict[str, Any]],
    padding_percent: float,
) -> Any:
    from PIL import Image, ImageDraw

    mask = Image.new("L", image_size, 0)
    draw = ImageDraw.Draw(mask)
    padding = int(round(min(image_size) * padding_percent / 100.0))
    for detection in detections:
        if detection.get("accepted"):
            x1, y1, x2, y2 = detection["bbox"]
            draw.rectangle(
                (
                    max(0, x1 - padding),
                    max(0, y1 - padding),
                    min(image_size[0], x2 + padding),
                    min(image_size[1], y2 + padding),
                ),
                fill=255,
            )
    return mask


def inpaint_image(image: Any, detections: list[dict[str, Any]], lama: Any, padding_percent: float) -> Any:
    import numpy as np
    from PIL import Image

    if not any(item["accepted"] for item in detections):
        return image.copy()

    mask = create_mask(image.size, detections, padding_percent)
    mask_bounds = mask.getbbox()
    if mask_bounds is None:
        return image.copy()

    #== bounded inpainting workspace =========================================
    x1, y1, x2, y2 = mask_bounds
    margin = 64
    crop_box = (
        max(0, x1 - margin),
        max(0, y1 - margin),
        min(image.width, x2 + margin),
        min(image.height, y2 + margin),
    )
    source_crop = image.crop(crop_box)
    mask_crop = mask.crop(crop_box)
    result_array = lama(source_crop, mask_crop)
    result_crop = Image.fromarray(np.asarray(result_array, dtype=np.uint8), mode="RGB")
    output = image.copy()
    output.paste(result_crop, crop_box[:2], mask_crop)
    return output
    #==========================================================================
#==============================================================================


#== preview processing ========================================================
def load_preview_image(input_path: Path, media_type: str, frame_index: int | None) -> tuple[Any, int | None]:
    from PIL import Image

    if media_type == "image":
        return Image.open(input_path).convert("RGB"), None

    import cv2

    capture = cv2.VideoCapture(str(input_path))
    try:
        if not capture.isOpened():
            raise WorkerFailure("INVALID_INPUT", "The source does not contain a readable video stream.", EXIT_UNSUPPORTED_INPUT)
        total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
        selected_frame = frame_index if frame_index is not None else max(0, total_frames // 2)
        if total_frames > 0 and selected_frame >= total_frames:
            raise WorkerFailure("INVALID_ARGUMENTS", "The requested preview frame is outside the video.", EXIT_INVALID_ARGUMENTS)
        capture.set(cv2.CAP_PROP_POS_FRAMES, selected_frame)
        ok, frame = capture.read()
        if not ok:
            raise WorkerFailure("INVALID_INPUT", "A preview frame could not be read from the source video.", EXIT_UNSUPPORTED_INPUT)
        return Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)), selected_frame
    finally:
        capture.release()


def process_preview(
    input_path: Path,
    output_path: Path,
    media_type: str,
    args: argparse.Namespace,
    model: Any,
    processor: Any,
    device: str,
    used_gpu: bool,
    work_directory: Path,
) -> None:
    emit_progress("detection", 20, "Detecting watermark")
    image, source_frame = load_preview_image(input_path, media_type, args.frame_index)
    detections = detect_watermarks(
        image,
        model,
        processor,
        device,
        args.detection_prompt,
        args.max_bbox_percent,
    )
    temporary_output = work_directory / "preview.png"
    image.save(temporary_output, format="PNG")
    commit_output(temporary_output, output_path, args.overwrite)
    emit_progress("detection", 100, "Detection preview ready")
    emit(
        "preview",
        previewPath=str(output_path),
        detections=detections,
        sourceFrame=source_frame,
        sourceWidth=image.width,
        sourceHeight=image.height,
        noRegionDetected=not any(item["accepted"] for item in detections),
    )
    emit("completed", outputPath=str(output_path), usedGpu=used_gpu)


def process_selection_preview(
    input_path: Path,
    output_path: Path,
    media_type: str,
    args: argparse.Namespace,
    work_directory: Path,
) -> None:
    emit_progress("preview_frame", 25, "Preparing selection frame")
    image, source_frame = load_preview_image(input_path, media_type, args.frame_index)
    temporary_output = work_directory / "selection-preview.png"
    image.save(temporary_output, format="PNG")
    commit_output(temporary_output, output_path, args.overwrite)
    emit_progress("preview_frame", 100, "Selection frame ready")
    emit(
        "preview",
        previewPath=str(output_path),
        detections=[],
        sourceFrame=source_frame,
        sourceWidth=image.width,
        sourceHeight=image.height,
        noRegionDetected=True,
    )
    emit("completed", outputPath=str(output_path), usedGpu=False)
#==============================================================================


#== image processing ==========================================================
def process_image(
    input_path: Path,
    output_path: Path,
    args: argparse.Namespace,
    model: Any,
    processor: Any,
    lama: Any,
    device: str,
    used_gpu: bool,
    work_directory: Path,
) -> None:
    from PIL import Image, ImageOps

    with Image.open(input_path) as source_image:
        metadata = safe_image_metadata(source_image)
        image = ImageOps.exif_transpose(source_image).convert("RGB")

    if args.regions:
        detections = detections_from_regions(args.regions, image.size)
    else:
        emit_progress("detection", 20, "Detecting watermark")
        detections = detect_watermarks(image, model, processor, device, args.detection_prompt, args.max_bbox_percent)
        if not any(item["accepted"] for item in detections):
            raise WorkerFailure(
                "NO_REGION_DETECTED",
                "No watermark region was detected in the image.",
                EXIT_PROCESSING_FAILURE,
            )
    check_cancelled()
    emit_progress("inpainting", 65, "Removing watermark")
    result = inpaint_image(image, detections, lama, args.mask_padding_percent)

    output_format = image_output_format(output_path)
    temporary_output = work_directory / f"processed{output_path.suffix.lower()}"
    save_options = metadata
    if output_format == "JPEG":
        save_options["quality"] = 95
    result.save(temporary_output, format=output_format, **save_options)
    commit_output(temporary_output, output_path, args.overwrite)
    emit_progress("output", 100, "Output created")
    emit("completed", outputPath=str(output_path), usedGpu=used_gpu)


def safe_image_metadata(image: Any) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    if image.info.get("icc_profile"):
        metadata["icc_profile"] = image.info["icc_profile"]
    if image.info.get("dpi"):
        metadata["dpi"] = image.info["dpi"]

    try:
        exif = image.getexif()
        if exif:
            # GPS metadata is intentionally removed from newly generated files.
            exif.pop(34853, None)
            exif[274] = 1
            metadata["exif"] = exif.tobytes()
    except (AttributeError, KeyError, TypeError, ValueError):
        pass
    return metadata


def image_output_format(output_path: Path) -> str:
    mapping = {
        ".jpg": "JPEG",
        ".jpeg": "JPEG",
        ".png": "PNG",
        ".webp": "WEBP",
        ".bmp": "BMP",
        ".tif": "TIFF",
        ".tiff": "TIFF",
    }
    output_format = mapping.get(output_path.suffix.lower())
    if output_format is None:
        raise WorkerFailure("UNSUPPORTED_OUTPUT", "Image output must use a supported image extension.", EXIT_OUTPUT_FAILURE)
    return output_format
#==============================================================================


#== video processing ==========================================================
def validate_video_source(input_path: Path, ffmpeg_path: Path | None) -> tuple[Any, float, int, int, int]:
    if ffmpeg_path is None:
        raise WorkerFailure("FFMPEG_MISSING", "FFmpeg is required for video processing.", EXIT_FFMPEG_FAILURE)

    try:
        version_check = subprocess.run(
            [str(ffmpeg_path), "-version"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=15,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise WorkerFailure("FFMPEG_MISSING", f"FFmpeg could not be started: {exc}", EXIT_FFMPEG_FAILURE) from exc
    if version_check.returncode != 0:
        raise WorkerFailure("FFMPEG_MISSING", "FFmpeg failed its startup check.", EXIT_FFMPEG_FAILURE)

    import cv2

    capture = cv2.VideoCapture(str(input_path))
    if not capture.isOpened():
        capture.release()
        raise WorkerFailure("INVALID_INPUT", "The source does not contain a readable video stream.", EXIT_UNSUPPORTED_INPUT)
    fps = float(capture.get(cv2.CAP_PROP_FPS))
    width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
    if not math.isfinite(fps) or fps <= 0 or width <= 0 or height <= 0 or total_frames <= 0:
        capture.release()
        raise WorkerFailure("INVALID_INPUT", "The source video has invalid stream metadata.", EXIT_UNSUPPORTED_INPUT)
    return capture, fps, width, height, total_frames


def process_video(
    input_path: Path,
    output_path: Path,
    ffmpeg_path: Path | None,
    args: argparse.Namespace,
    model: Any,
    processor: Any,
    lama: Any,
    device: str,
    used_gpu: bool,
    work_directory: Path,
) -> None:
    import cv2
    from PIL import Image

    capture, fps, width, height, total_frames = validate_video_source(input_path, ffmpeg_path)
    detections_by_frame: dict[int, list[dict[str, Any]]] = {}

    try:
        #== detection pass ====================================================
        if args.regions:
            fixed_detections = detections_from_regions(args.regions, (width, height))
        else:
            detection_frames = list(range(0, total_frames, args.detection_skip))
            if detection_frames[-1] != total_frames - 1:
                detection_frames.append(total_frames - 1)

            emit("status", stage="detection", message="Detecting watermark")
            for position, frame_index in enumerate(detection_frames, start=1):
                check_cancelled()
                capture.set(cv2.CAP_PROP_POS_FRAMES, frame_index)
                ok, frame = capture.read()
                if not ok:
                    raise WorkerFailure("PROCESSING_FAILED", f"Video frame {frame_index} could not be read.", EXIT_PROCESSING_FAILURE)
                image = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                detections_by_frame[frame_index] = detect_watermarks(
                    image,
                    model,
                    processor,
                    device,
                    args.detection_prompt,
                    args.max_bbox_percent,
                )
                emit_progress("detection", 5 + 40 * position / len(detection_frames), "Detecting watermark")
        #=====================================================================

        #== mask timeline =====================================================
        if args.regions:
            frame_detections = {frame_index: fixed_detections for frame_index in range(total_frames)}
        else:
            fade_in_frames = int(round(args.fade_in * fps))
            fade_out_frames = int(round(args.fade_out * fps))
            frame_detections: dict[int, list[dict[str, Any]]] = {}
            for frame_index, detections in detections_by_frame.items():
                accepted = [item for item in detections if item["accepted"]]
                if not accepted:
                    continue
                start = max(0, frame_index - fade_in_frames)
                end = min(total_frames, frame_index + args.detection_skip + fade_out_frames)
                for target_frame in range(start, end):
                    bucket = frame_detections.setdefault(target_frame, [])
                    existing = {tuple(item["bbox"]) for item in bucket}
                    bucket.extend(item for item in accepted if tuple(item["bbox"]) not in existing)

            if not frame_detections:
                raise WorkerFailure(
                    "NO_REGION_DETECTED",
                    "No watermark region was detected in the video.",
                    EXIT_PROCESSING_FAILURE,
                )
        #=====================================================================

        #== inpainting pass ==================================================
        silent_video = work_directory / "processed-silent.mp4"
        writer = cv2.VideoWriter(
            str(silent_video),
            cv2.VideoWriter_fourcc(*"mp4v"),
            fps,
            (width, height),
        )
        if not writer.isOpened():
            raise WorkerFailure("OUTPUT_NOT_CREATED", "The temporary video writer could not be opened.", EXIT_OUTPUT_FAILURE)

        capture.set(cv2.CAP_PROP_POS_FRAMES, 0)
        try:
            emit("status", stage="inpainting", message="Removing watermark")
            for frame_index in range(total_frames):
                check_cancelled()
                ok, frame = capture.read()
                if not ok:
                    raise WorkerFailure("PROCESSING_FAILED", f"Video frame {frame_index} could not be read.", EXIT_PROCESSING_FAILURE)
                detections = frame_detections.get(frame_index)
                if detections:
                    image = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                    result = inpaint_image(image, detections, lama, args.mask_padding_percent)
                    frame = cv2.cvtColor(__import__("numpy").array(result), cv2.COLOR_RGB2BGR)
                writer.write(frame)
                emit_progress("inpainting", 45 + 45 * (frame_index + 1) / total_frames, "Removing watermark")
        finally:
            writer.release()
        #=====================================================================
    finally:
        capture.release()

    #== audio restoration ====================================================
    check_cancelled()
    emit_progress("audio_merge", 95, "Restoring audio")
    finalized_video = work_directory / "finalized.mp4"
    command = [
        str(ffmpeg_path),
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-i",
        str(silent_video),
        "-i",
        str(input_path),
        "-map",
        "0:v:0",
        "-map",
        "1:a?",
        "-c:v",
        "libx264",
        "-preset",
        "medium",
        "-crf",
        "18",
        "-pix_fmt",
        "yuv420p",
        "-c:a",
        "aac",
        "-sn",
        "-map_chapters",
        "-1",
        "-movflags",
        "+faststart",
        "-shortest",
        str(finalized_video),
    ]
    completed = subprocess.run(command, capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        diagnostic(completed.stderr)
        raise WorkerFailure("FFMPEG_FAILED", "FFmpeg could not restore/finalize the video audio.", EXIT_FFMPEG_FAILURE)
    verify_video_output(finalized_video)
    commit_output(finalized_video, output_path, args.overwrite)
    emit_progress("output", 100, "Output created")
    emit("completed", outputPath=str(output_path), usedGpu=used_gpu)
    #==========================================================================


def verify_video_output(path: Path) -> None:
    import cv2

    if not path.is_file() or path.stat().st_size == 0:
        raise WorkerFailure("OUTPUT_NOT_CREATED", "The final video was not created.", EXIT_OUTPUT_FAILURE)
    capture = cv2.VideoCapture(str(path))
    try:
        ok, _ = capture.read()
        if not capture.isOpened() or not ok:
            raise WorkerFailure("OUTPUT_NOT_CREATED", "The final video is not readable.", EXIT_OUTPUT_FAILURE)
    finally:
        capture.release()
#==============================================================================


#== output and cleanup ========================================================
def commit_output(temporary_output: Path, output_path: Path, overwrite: bool) -> None:
    if not temporary_output.is_file() or temporary_output.stat().st_size == 0:
        raise WorkerFailure("OUTPUT_NOT_CREATED", "Processing did not create a valid temporary output.", EXIT_OUTPUT_FAILURE)
    if output_path.exists() and not overwrite:
        raise WorkerFailure("OUTPUT_EXISTS", "The output file already exists.", EXIT_OUTPUT_FAILURE)
    staging_output = output_path.with_name(output_path.name + ".veditor-partial")
    try:
        staging_output.unlink(missing_ok=True)
        with temporary_output.open("rb") as source, staging_output.open("wb") as destination:
            shutil.copyfileobj(source, destination, length=1024 * 1024)
            destination.flush()
            os.fsync(destination.fileno())
        os.replace(staging_output, output_path)
    except OSError as exc:
        raise WorkerFailure("OUTPUT_NOT_CREATED", f"The final output could not be committed: {exc}", EXIT_OUTPUT_FAILURE) from exc
    finally:
        try:
            staging_output.unlink(missing_ok=True)
        except OSError as exc:
            diagnostic(f"Could not remove staging output: {exc}")


def check_cancelled() -> None:
    if _CANCELLED.is_set():
        raise WorkerFailure("CANCELLED", "Watermark processing was canceled.", EXIT_CANCELLED)


def handle_cancel_signal(_signum: int, _frame: Any) -> None:
    _CANCELLED.set()


def install_signal_handlers() -> None:
    for signal_name in ("SIGINT", "SIGTERM", "SIGBREAK"):
        available_signal = getattr(signal, signal_name, None)
        if available_signal is not None:
            signal.signal(available_signal, handle_cancel_signal)


def cleanup_partial_output(output_path: Path | None) -> None:
    if output_path is None or not output_path.exists():
        return
    try:
        output_path.unlink()
    except OSError as exc:
        diagnostic(f"Could not remove partial output: {exc}")
#==============================================================================


#== error mapping =============================================================
def map_unexpected_failure(exc: Exception) -> WorkerFailure:
    message = str(exc)
    lower_message = message.lower()
    if "cuda" in lower_message and "out of memory" in lower_message:
        try:
            torch = importlib.import_module("torch")
            torch.cuda.empty_cache()
        except Exception:
            pass
        return WorkerFailure("CUDA_OUT_OF_MEMORY", "CUDA ran out of memory while processing the media.", EXIT_PROCESSING_FAILURE)
    if "cuda" in lower_message:
        return WorkerFailure("CUDA_RUNTIME_ERROR", f"CUDA processing failed: {message}", EXIT_PROCESSING_FAILURE)
    return WorkerFailure("PROCESSING_FAILED", f"Watermark processing failed: {message}", EXIT_PROCESSING_FAILURE)
#==============================================================================


#== entry point ===============================================================
def run(argv: Sequence[str] | None = None) -> int:
    install_signal_handlers()

    try:
        args = build_parser().parse_args(argv)
        if args.check_runtime:
            emit_runtime_status(args.ffmpeg_path)
            return EXIT_SUCCESS

        emit("status", stage="checking_runtime", message="Checking runtime")
        input_path, output_path, media_type = validate_arguments(args)
        ffmpeg_path = resolve_ffmpeg(args.ffmpeg_path)
        if media_type == "video" and ffmpeg_path is None:
            raise WorkerFailure("FFMPEG_MISSING", "FFmpeg is required for video processing.", EXIT_FFMPEG_FAILURE)

        temporary_root = Path(args.temporary_directory).resolve() if args.temporary_directory else None

        with tempfile.TemporaryDirectory(prefix="veditor-watermark-", dir=temporary_root) as work:
            work_directory = Path(work)
            if args.selection_preview:
                process_selection_preview(input_path, output_path, media_type, args, work_directory)
                return EXIT_SUCCESS

            _torch, device, used_gpu = select_device(args.device)
            if args.regions:
                model = None
                processor = None
                lama = load_lama(device)
            else:
                model, processor, lama = load_models(device, args.preview_only)

            if args.preview_only:
                process_preview(input_path, output_path, media_type, args, model, processor, device, used_gpu, work_directory)
            elif media_type == "image":
                process_image(input_path, output_path, args, model, processor, lama, device, used_gpu, work_directory)
            else:
                process_video(input_path, output_path, ffmpeg_path, args, model, processor, lama, device, used_gpu, work_directory)
        return EXIT_SUCCESS
    except WorkerFailure as failure:
        emit_error(failure)
        return failure.exit_code
    except Exception as exc:
        failure = map_unexpected_failure(exc)
        diagnostic(repr(exc))
        emit_error(failure)
        return failure.exit_code


if __name__ == "__main__":
    raise SystemExit(run())
#==============================================================================
