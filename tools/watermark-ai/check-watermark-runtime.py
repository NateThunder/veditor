"""Lightweight JSON runtime inspection for Veditor's local watermark engine."""

from __future__ import annotations

import argparse
import importlib
import importlib.metadata
import json
import os
from pathlib import Path
import shutil
import sys
from typing import Any


#== runtime constants =========================================================
FLORENCE_MODEL_ID = "florence-community/Florence-2-large"
LAMA_FILENAME = "big-lama.pt"
RUNTIME_SCHEMA_VERSION = 2


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


#== installation marker parsing ==============================================
def parse_installation_marker(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError, TypeError):
        return {}
#==============================================================================


#== dependency inspection =====================================================
def version_of(distribution_name: str) -> str | None:
    try:
        return importlib.metadata.version(distribution_name)
    except importlib.metadata.PackageNotFoundError:
        return None


def locate_ffmpeg(explicit_path: str | None) -> str | None:
    candidates = (explicit_path, os.environ.get("VEDITOR_FFMPEG_PATH"), shutil.which("ffmpeg"))
    for candidate in candidates:
        if candidate and Path(candidate).expanduser().is_file():
            return str(Path(candidate).expanduser().resolve())
    return None


def florence_available() -> bool:
    try:
        from huggingface_hub import scan_cache_dir

        return any(repo.repo_id == FLORENCE_MODEL_ID for repo in scan_cache_dir().repos)
    except Exception:
        cache_root = Path(os.environ["HF_HOME"]) / "hub"
        model_root = cache_root / ("models--" + FLORENCE_MODEL_ID.replace("/", "--"))
        return (model_root / "snapshots").is_dir()


def lama_available() -> bool:
    torch_root = Path(os.environ["TORCH_HOME"])
    return (torch_root / "hub" / "checkpoints" / LAMA_FILENAME).is_file()


def inspect_cuda() -> tuple[bool, str | None, str | None]:
    if version_of("torch") is None:
        return False, None, None
    try:
        torch = importlib.import_module("torch")
        available = bool(torch.cuda.is_available())
        gpu_name = str(torch.cuda.get_device_name(0)) if available else None
        return available, gpu_name, getattr(torch.version, "cuda", None)
    except Exception as exc:
        return False, None, f"error: {exc}"
#==============================================================================


#== deep verification =========================================================
def deep_verify(mode: str | None) -> tuple[bool, str | None]:
    try:
        import torch
        import transformers
        device = "cuda" if mode == "CUDA" else "cpu"
        dtype = torch.float16 if device == "cuda" else torch.float32
        florence_class = importlib.import_module(
            "transformers.models.florence2.modeling_florence2"
        ).Florence2ForConditionalGeneration
        florence_class.from_pretrained(
            FLORENCE_MODEL_ID,
            dtype=dtype,
            local_files_only=True,
        ).to(device).eval()
        transformers.AutoProcessor.from_pretrained(
            FLORENCE_MODEL_ID,
            local_files_only=True,
            use_fast=False,
        )
        torch.jit.load(
            str(Path(os.environ["TORCH_HOME"]) / "hub" / "checkpoints" / LAMA_FILENAME),
            map_location=device,
        ).eval()
        return True, None
    except Exception as exc:
        return False, str(exc)
#==============================================================================


#== output shaping ============================================================
def build_status(ffmpeg_path: str | None, deep: bool) -> dict[str, Any]:
    script_directory = Path(__file__).resolve().parent
    marker = parse_installation_marker(script_directory / "installation.json")
    versions = {
        "torchVersion": version_of("torch"),
        "torchvisionVersion": version_of("torchvision"),
        "transformersVersion": version_of("transformers"),
    }
    required = ("torch", "torchvision", "transformers", "numpy", "opencv-python-headless", "Pillow")
    dependencies_available = all(version_of(name) is not None for name in required)
    cuda_available, gpu_name, torch_cuda_version = inspect_cuda()
    florence_model_available = florence_available() if version_of("huggingface-hub") else False
    lama_model_available = lama_available()
    ffmpeg = locate_ffmpeg(ffmpeg_path)
    requested_mode = str(marker.get("mode") or "").upper() or None
    marker_path = script_directory / "installation.json"
    marker_exists = marker_path.is_file()
    marker_outdated = marker_exists and marker.get("schemaVersion") != RUNTIME_SCHEMA_VERSION
    marker_valid = bool(
        marker_exists
        and not marker_outdated
        and marker.get("complete") is True
        and requested_mode in {"CPU", "CUDA"}
    )
    mode_valid = requested_mode != "CUDA" or cuda_available
    deep_available, deep_error = (deep_verify(requested_mode) if deep else (None, None))
    installed = bool(
        marker_valid
        and dependencies_available
        and florence_model_available
        and lama_model_available
        and ffmpeg
        and mode_valid
        and deep_available is not False
    )

    return {
        "installed": installed,
        "pythonVersion": sys.version.split()[0],
        **versions,
        "dependenciesAvailable": dependencies_available,
        "florenceModelAvailable": florence_model_available,
        "lamaModelAvailable": lama_model_available,
        "ffmpegAvailable": ffmpeg is not None,
        "ffmpegPath": ffmpeg,
        "cudaAvailable": cuda_available,
        "torchCudaVersion": torch_cuda_version,
        "gpuName": gpu_name,
        "installationMode": requested_mode,
        "markerExists": marker_exists,
        "markerValid": marker_valid,
        "markerOutdated": marker_outdated,
        "deepVerificationPassed": deep_available,
        "deepVerificationError": deep_error,
    }
#==============================================================================


#== entry point ===============================================================
def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ffmpeg-path")
    parser.add_argument("--deep", action="store_true")
    args = parser.parse_args()
    status = build_status(args.ffmpeg_path, args.deep)
    print(json.dumps(status, ensure_ascii=False, separators=(",", ":")), flush=True)
    return 0 if status["installed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
#==============================================================================
