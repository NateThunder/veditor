"""Local JSON-line worker for Veditor picture background removal."""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import sys
import time
from typing import Any
import urllib.request


#== runtime constants =========================================================
MODEL_NAMES = ("u2netp", "u2net", "birefnet-general")
MODEL_ASSETS = (
    (
        "u2netp",
        "u2netp.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
        "8e83ca70e441ab06c318d82300c84806",
    ),
    (
        "u2net",
        "u2net.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx",
        "60024c5c889badc19c04ad937298a77b",
    ),
    (
        "birefnet-general",
        "birefnet-general.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-epoch_244.onnx",
        "7a35a0141cbbc80de11d9c9a28f52697",
    ),
)
RUNTIME_SCHEMA_VERSION = 1
#==============================================================================


#== output shaping ============================================================
def emit(message_type: str, **values: Any) -> None:
    print(
        json.dumps({"type": message_type, **values}, ensure_ascii=False, separators=(",", ":")),
        flush=True,
    )
#==============================================================================


#== download progress =========================================================
def installer_progress(
    percent: float,
    stage: str,
    message: str,
    bytes_received: int | None = None,
    bytes_total: int | None = None,
) -> None:
    values: dict[str, Any] = {
        "percent": max(0.0, min(100.0, percent)),
        "stage": stage,
        "message": message,
    }
    if bytes_received is not None:
        values["bytesReceived"] = bytes_received
    if bytes_total is not None:
        values["bytesTotal"] = bytes_total
    emit("installer_progress", **values)


def md5_matches(path: Path, expected_digest: str) -> bool:
    if not path.is_file():
        return False

    digest = hashlib.md5()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest().lower() == expected_digest.lower()


def remote_size(url: str) -> int | None:
    try:
        request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "Veditor/1.0"})
        with urllib.request.urlopen(request, timeout=30) as response:
            value = response.headers.get("Content-Length")
            return int(value) if value is not None else None
    except (OSError, ValueError):
        return None


def format_download_message(model_name: str, received: int, total: int | None) -> str:
    received_megabytes = received / (1024 * 1024)
    if total is None or total <= 0:
        return f"Downloading {model_name}: {received_megabytes:,.1f} MB"
    total_megabytes = total / (1024 * 1024)
    return f"Downloading {model_name}: {received_megabytes:,.1f} of {total_megabytes:,.1f} MB"


def download_model(
    model_name: str,
    destination: Path,
    url: str,
    expected_digest: str,
    model_index: int,
    model_count: int,
    known_size: int | None,
    completed_bytes: int,
    aggregate_bytes: int | None,
) -> int:
    partial_path = destination.with_suffix(destination.suffix + ".partial")
    partial_path.unlink(missing_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "Veditor/1.0"})
    last_reported_percent = -1
    last_reported_at = 0.0

    try:
        with urllib.request.urlopen(request, timeout=60) as response, partial_path.open("wb") as output:
            response_size = response.headers.get("Content-Length")
            total = int(response_size) if response_size is not None else known_size
            received = 0
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
                received += len(chunk)

                if aggregate_bytes and total:
                    percent = 55.0 + ((completed_bytes + received) / aggregate_bytes * 40.0)
                else:
                    file_fraction = min(1.0, received / total) if total else 0.0
                    percent = 55.0 + (((model_index - 1) + file_fraction) / model_count * 40.0)

                rounded_percent = int(percent)
                now = time.monotonic()
                if rounded_percent != last_reported_percent or now - last_reported_at >= 0.5:
                    installer_progress(
                        percent,
                        "models",
                        format_download_message(model_name, received, total),
                        received,
                        total,
                    )
                    last_reported_percent = rounded_percent
                    last_reported_at = now

        if not md5_matches(partial_path, expected_digest):
            raise ValueError(f"Downloaded {model_name} model failed checksum verification.")
        partial_path.replace(destination)
        return destination.stat().st_size
    except BaseException:
        partial_path.unlink(missing_ok=True)
        raise
#==============================================================================


#== runtime inspection ========================================================
def marker_data() -> dict[str, Any]:
    try:
        value = json.loads((Path(__file__).resolve().parent / "installation.json").read_text(encoding="utf-8-sig"))
        return value if isinstance(value, dict) else {}
    except (OSError, TypeError, ValueError):
        return {}


def check_runtime() -> int:
    missing: list[str] = []
    versions: dict[str, str | None] = {}
    for distribution in ("rembg", "Pillow", "onnxruntime", "onnxruntime-gpu"):
        try:
            versions[distribution] = importlib.metadata.version(distribution)
        except importlib.metadata.PackageNotFoundError:
            versions[distribution] = None

    if versions["rembg"] is None:
        missing.append("rembg")
    if versions["Pillow"] is None:
        missing.append("Pillow")
    if versions["onnxruntime"] is None and versions["onnxruntime-gpu"] is None:
        missing.append("ONNX Runtime")

    models_directory = Path(os.environ.get("U2NET_HOME", Path.home() / ".u2net"))
    if len(list(models_directory.glob("*.onnx"))) < len(MODEL_NAMES):
        missing.append("all three background-removal models")

    marker = marker_data()
    marker_valid = bool(
        marker.get("complete") is True
        and marker.get("schemaVersion") == RUNTIME_SCHEMA_VERSION
        and marker.get("mode") in {"CPU", "CUDA"}
    )
    if not marker_valid:
        missing.append("valid installation marker")

    cuda_available = False
    gpu_name = None
    try:
        import onnxruntime as ort

        cuda_available = "CUDAExecutionProvider" in ort.get_available_providers()
        if cuda_available:
            gpu_name = "NVIDIA CUDA device"
    except Exception:
        pass

    installed = not missing and (marker.get("mode") != "CUDA" or cuda_available)
    emit(
        "runtime_status",
        installed=installed,
        installationMode=marker.get("mode"),
        cudaAvailable=cuda_available,
        gpuName=gpu_name,
        pythonVersion=sys.version.split()[0],
        rembgVersion=versions["rembg"],
        missingComponents=missing,
        message="Background Removal is ready." if installed else "Runtime installation or repair is required.",
    )
    return 0 if installed else 1
#==============================================================================


#== model preparation =========================================================
def download_models() -> int:
    #== model discovery and verification =====================================
    models_directory = Path(os.environ.get("U2NET_HOME", Path.home() / ".u2net"))
    models_directory.mkdir(parents=True, exist_ok=True)
    installer_progress(55.0, "models", "Checking downloaded background-removal models.")

    asset_states: list[tuple[str, str, str, str, Path, bool, int | None]] = []
    aggregate_bytes = 0
    all_sizes_known = True
    for model_name, file_name, url, expected_digest in MODEL_ASSETS:
        destination = models_directory / file_name
        valid = md5_matches(destination, expected_digest)
        if destination.exists() and not valid:
            destination.unlink()
        size = destination.stat().st_size if valid else remote_size(url)
        if size is None:
            all_sizes_known = False
        else:
            aggregate_bytes += size
        asset_states.append((model_name, file_name, url, expected_digest, destination, valid, size))
    #=========================================================================

    #== verified model downloads =============================================
    completed_bytes = 0
    for index, (model_name, _, url, expected_digest, destination, valid, size) in enumerate(asset_states, start=1):
        if valid:
            completed_bytes += size or destination.stat().st_size
            percent = 55.0 + ((completed_bytes / aggregate_bytes) * 40.0) if all_sizes_known and aggregate_bytes else 55.0
            installer_progress(percent, "models", f"Using verified {model_name} model.", size, size)
            continue

        downloaded_size = download_model(
            model_name,
            destination,
            url,
            expected_digest,
            index,
            len(asset_states),
            size,
            completed_bytes,
            aggregate_bytes if all_sizes_known else None,
        )
        completed_bytes += downloaded_size

    installer_progress(95.0, "models", "All three background-removal models are verified.")
    return 0
#==============================================================================


#== background removal ========================================================
def remove_background(input_path: Path, output_path: Path, model_name: str, alpha_matting: bool) -> int:
    from PIL import Image
    from rembg import new_session, remove

    if not input_path.is_file():
        raise FileNotFoundError("The selected picture does not exist.")
    if model_name not in MODEL_NAMES:
        raise ValueError("The requested quality model is not supported.")

    emit("progress", percent=5, message="Reading original picture")
    source_bytes = input_path.read_bytes()
    emit("progress", percent=15, message=f"Loading {model_name} model")
    session = new_session(model_name)
    emit("progress", percent=30, message="Separating subject from background")
    result_bytes = remove(
        source_bytes,
        session=session,
        alpha_matting=alpha_matting,
        force_return_bytes=True,
    )

    emit("progress", percent=90, message="Validating transparent PNG")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    partial_path = output_path.with_suffix(output_path.suffix + ".partial")
    partial_path.write_bytes(result_bytes)
    with Image.open(partial_path) as result:
        result.verify()
        if result.format != "PNG":
            raise ValueError("The model did not return a PNG result.")
    with Image.open(partial_path) as result:
        if "A" not in result.getbands():
            raise ValueError("The model result does not contain transparency.")
    partial_path.replace(output_path)
    emit("completed", percent=100, message="Background removed", outputPath=str(output_path.resolve()))
    return 0
#==============================================================================


#== entry point ===============================================================
def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--download-models", action="store_true")
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--model", choices=MODEL_NAMES, default="u2net")
    parser.add_argument("--alpha-matting", action="store_true")
    args = parser.parse_args()

    try:
        if args.check:
            return check_runtime()
        if args.download_models:
            return download_models()
        if args.input is None or args.output is None:
            raise ValueError("Input and output paths are required.")
        return remove_background(args.input.resolve(), args.output.resolve(), args.model, args.alpha_matting)
    except Exception as exc:
        emit("error", message=str(exc), exceptionType=type(exc).__name__)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
#==============================================================================
