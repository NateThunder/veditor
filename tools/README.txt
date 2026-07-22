Place external tools here before running the app.

Recommended layout:

tools\
  yt-dlp.exe
  deno.exe
  ffmpeg\
    ffmpeg.exe
    ffprobe.exe

Notes:
- yt-dlp.exe is the downloader used by the WinForms wrapper.
- deno.exe is strongly recommended for modern YouTube support.
- ffmpeg.exe and ffprobe.exe are needed for merging streams and extracting mp3 audio.

AI-assisted watermark cleanup:
- The scripts under tools\watermark-ai are copied with the application, but Python,
  PyTorch, and AI model weights are installed only under LocalAppData.
- Click "Install or repair runtime" in Veditor for automatic mode selection. A
  compatible NVIDIA driver selects CUDA 12.4; otherwise the installer selects CPU.
- The runtime layout is:

  %LOCALAPPDATA%\Veditor\WatermarkAI\
    python\python.exe
    worker.py
    installation.json
    logs\
    temp\

- Hugging Face models use %LOCALAPPDATA%\Veditor\Models\HuggingFace.
- Torch models use %LOCALAPPDATA%\Veditor\Models\Torch.
- The C# service launches python.exe with -u and worker.py, passes FFmpeg through
  VEDITOR_FFMPEG_PATH, and prepends the FFmpeg directory to PATH.
- The worker must emit one valid JSON object per stdout line. stderr is treated as
  diagnostic log output. A successful operation must emit exactly one completed
  message with the requested output path.
- Runtime health is queried with worker.py --check-runtime.

Installer source files:
  tools\watermark-ai\install-watermark-ai.ps1
  tools\watermark-ai\veditor_worker.py
  tools\watermark-ai\check-watermark-runtime.py
  tools\watermark-ai\requirements-windows-cpu.lock
  tools\watermark-ai\requirements-windows-cuda.lock

Manual installation examples:
  powershell -ExecutionPolicy Bypass -File tools\watermark-ai\install-watermark-ai.ps1 -Mode Auto -FfmpegPath tools\ffmpeg\ffmpeg.exe
  powershell -ExecutionPolicy Bypass -File tools\watermark-ai\install-watermark-ai.ps1 -Mode CPU -FfmpegPath tools\ffmpeg\ffmpeg.exe
  powershell -ExecutionPolicy Bypass -File tools\watermark-ai\install-watermark-ai.ps1 -Mode CUDA -FfmpegPath tools\ffmpeg\ffmpeg.exe
  powershell -ExecutionPolicy Bypass -File tools\watermark-ai\install-watermark-ai.ps1 -Mode Auto -Repair -FfmpegPath tools\ffmpeg\ffmpeg.exe

The installer supports -SkipModelDownload for offline preparation, but writes an
incomplete marker so Veditor will not claim the runtime is ready. Florence-2-large
is about 1.5 GB and big-LaMA about 196 MB; Python and PyTorch add several GB.

Runtime verification (lightweight):
  %LOCALAPPDATA%\Veditor\WatermarkAI\python\python.exe %LOCALAPPDATA%\Veditor\WatermarkAI\check-watermark-runtime.py --ffmpeg-path tools\ffmpeg\ffmpeg.exe

Add --deep only when model-loading verification is required. CPU processing can be
very slow for video. CUDA is faster but requires a compatible NVIDIA GPU and driver.

Uninstall:
- Close Veditor.
- Remove %LOCALAPPDATA%\Veditor\WatermarkAI.
- Optionally remove %LOCALAPPDATA%\Veditor\Models to delete cached model weights.

Privacy and authorised use:
- Processing stays on this computer; installation downloads packages and models.
- Only process media you own or are authorised to modify.
- Detection uses rectangular boxes. Preview first; faint or moving marks may be
  missed and large/complex regions may show inpainting artifacts.

Picture background removal:
- tools\background-removal is copied with the app; its independent Python runtime
  is installed only after the user chooses Install or repair runtime.
- CPU is the default. CUDA can be selected explicitly; an incompatible CUDA setup
  is reported and then automatically falls back to CPU.
- The runtime layout is %LOCALAPPDATA%\Veditor\BackgroundRemoval and model files
  are stored in %LOCALAPPDATA%\Veditor\Models\Rembg.
- Verified Python downloads and pip cache entries are retained under
  %LOCALAPPDATA%\Veditor\Downloads\BackgroundRemoval for faster repair attempts.
- Installation downloads all three UI models: u2netp (Fast), u2net (Balanced), and
  birefnet-general (Best Quality). Balanced is the default slider position.
- The app shows byte-aware progress for Python and model transfers and weighted
  progress for pip stages. During installation the workspace is locked; only the
  status-bar Cancel download action and window controls remain available.
- Canceling deletes incomplete .partial files while preserving verified completed
  downloads and model files for a later retry.
- The worker accepts one JPEG, PNG, WebP, BMP, TIF, or TIFF and always produces a
  temporary full-resolution PNG. Veditor opens Save As before copying that exact
  preview result to the user's chosen path.

Manual installation examples:
  powershell -ExecutionPolicy Bypass -File tools\background-removal\install-background-removal.ps1 -Mode CPU
  powershell -ExecutionPolicy Bypass -File tools\background-removal\install-background-removal.ps1 -Mode CUDA
  powershell -ExecutionPolicy Bypass -File tools\background-removal\install-background-removal.ps1 -Mode CPU -Repair
