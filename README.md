# VeditorWindow

VeditorWindow is a Windows Forms desktop app for downloading media with `yt-dlp`, converting files with `ffmpeg`, trimming clips with `ffmpeg`, cropping and rotating video with `ffmpeg`, removing still-picture backgrounds with a local `rembg` runtime, preparing authorised video for local AI-assisted watermark cleanup, and previewing the active file inside an embedded WebView2 surface. Its dark-purple, claymorphic, preview-first shell keeps the active tool inspector permanently visible: paste a URL or open one supported file, inspect it, edit it, and export without losing the controls for the current task.

## What The App Does

- Downloads online media into a user-selected output folder.
- Offers separate, initially unchecked `MP3` and `WAV` audio-only options; selecting both creates both files from one download.
- Opens local media files and previews them inside the app.
- Accepts one supported audio or video file through a picker or the dashed drag-and-drop stage and explains invalid drops inline.
- Trims the active video with in/out points and exports a shorter clip.
- Crops and rotates the active video with a live stage overlay and exports a corrected clip.
- Removes the background from one JPEG, PNG, WebP, BMP, or TIFF picture and provides an exact side-by-side preview before saving a transparent PNG.
- Provides a local AI-assisted watermark-cleanup card with runtime health, detection preview, progress, logs, cancellation, and strict source-file preservation.
- Converts the active media file to `MP3`, `WAV`, `M4A`, `MP4`, `MKV`, or `MOV`.
- Shows progress, logs, preview metadata, and source-vs-output size summaries.

## Interface Design

The claymorphism treatment uses fully matte rounded surfaces, restrained purple accents, and three consistent depth levels: raised panels, stronger raised buttons, and recessed work areas. [`StudioTheme`](./UI/StudioTheme.cs) owns the shared palette, radii, motion, and layout dimensions; [`ClayDrawing`](./UI/ClayDrawing.cs), [`ClayPanel`](./UI/ClayPanel.cs), and [`ClayButton`](./UI/ClayButton.cs) render the reusable relief states; [`ConfigureStudioLayout`](./Form1.cs#L323) composes those controls into the window.

Spacing is part of the hierarchy. [`BuildSidebar`](./Form1.cs#L682) begins directly with the active workspace controls rather than duplicating the selected mode in a header, while [`BuildStatusBar`](./Form1.cs#L3317) gives the status indicator, message, and file details independent breathing room. Labels use measured or wrapping layouts, primary text stays high-contrast on the dark surfaces, and action controls retain content-aware padding. At widths below 1380 logical pixels, only the navigation rail becomes compact; the inspector remains visible and scrollable through the hybrid [`ClayScrollPanel`](./UI/ClayScrollPanel.cs), which keeps native scrolling underneath the clay-styled thumb. This keeps the interface readable at common Windows desktop scaling settings without flattening the clay-style depth cues.

## Requirements

- Windows desktop environment.
- .NET Windows target: [`net10.0-windows`](./VeditorWindow.csproj#L5) with [`UseWindowsForms`](./VeditorWindow.csproj#L7).
- WebView2 support for the embedded preview surface. The project references [`Microsoft.Web.WebView2`](./VeditorWindow.csproj#L30).
- External tools available either in `tools/` or on `PATH`:
  - [`yt-dlp.exe`](./tools/README.txt#L13)
  - [`deno.exe`](./tools/README.txt#L14) for modern YouTube support
  - [`ffmpeg.exe` and `ffprobe.exe`](./tools/README.txt#L15)
- The optional local WatermarkAI runtime is installed outside the repository by [`install-watermark-ai.ps1`](./tools/watermark-ai/install-watermark-ai.ps1). Its Local AppData layout and JSON-line contract are documented in [`tools/README.txt`](./tools/README.txt).
- The independent local `rembg` runtime is installed outside the repository by [`install-background-removal.ps1`](./tools/background-removal/install-background-removal.ps1). CPU is the default; explicitly selected but incompatible CUDA installations automatically fall back to CPU.

## Local Setup

1. Place the required binaries under `tools/` using the layout documented in [`tools/README.txt`](./tools/README.txt#L1).
2. Build the app:

   ```powershell
   dotnet build .\VeditorWindow.csproj
   ```

3. Run the app:

   ```powershell
   dotnet run --project .\VeditorWindow.csproj
   ```

4. Run the service and protocol tests:

   ```powershell
   dotnet test .\VeditorWindow.sln
   ```

5. Run the model-free Python worker tests:

   ```powershell
   python -m unittest discover -s .\tools\watermark-ai\tests -v
   ```

The project copies `tools\**\*` into the output directory during build, so keeping the downloader and codec binaries inside `tools/` is the intended local workflow.

## Repository Layout

- [`Program.cs`](./Program.cs#L9): WinForms entry point that initializes the app and opens the main window.
- [`Form1.cs`](./Form1.cs): primary window, dynamic UI composition, download/conversion/edit coordination, watermark-cleanup coordination, preview flow, status/log updates, and tool lookup.
- [`Form1.StudioShell.cs`](./Form1.StudioShell.cs): integrated title bar, Windows hit testing, fixed-inspector responsive layout, generated UI assets, drag-and-drop empty state, and shared file-open flow.
- [`Form1.BackgroundRemoval.cs`](./Form1.BackgroundRemoval.cs): dedicated navigation workspace, full-resolution before/after preview, quality controls, runtime installation, cancellation, Save As flow, and output-folder action.
- [`Form1.Designer.cs`](./Form1.Designer.cs): designer-managed controls used by the main window.
- [`UI/`](./UI): theme tokens, reusable matte-clay panels, buttons, check/radio controls, scrollbar, responsive policy, custom window chrome, drop validation, the recessed drop-zone surface, and the keyboard-accessible purple slider.
- [`Assets/`](./Assets): transparent 1x/2x matte brand artwork, the sculpted media empty-state illustration, and generated matte icon assets rendered inside native accessible controls.
- [`TrimTimelineControl.cs`](./TrimTimelineControl.cs#L6): custom trim-range timeline control with draggable in/out handles and playhead.
- [`Models/`](./Models): immutable background-removal and watermark options, results, runtime health, progress, and worker-message models.
- [`Services/VeditorPaths.cs`](./Services/VeditorPaths.cs): deterministic Local AppData paths for the portable runtime, models, logs, and temporary work.
- [`Services/WatermarkWorkerProtocol.cs`](./Services/WatermarkWorkerProtocol.cs): defensive JSON-line parsing separated from process execution.
- [`Services/WatermarkRemovalService.cs`](./Services/WatermarkRemovalService.cs): worker launch, environment setup, concurrent stream reading, cancellation, output validation, and partial-file cleanup.
- [`Services/BackgroundRemovalService.cs`](./Services/BackgroundRemovalService.cs): isolated `rembg` worker launch, JSON-line progress, cancellation, full-resolution temporary output validation, and cleanup.
- [`tools/background-removal/`](./tools/background-removal): pinned `rembg` CPU/CUDA requirements, portable-Python installer, local worker, and MIT notice. Python and model weights are installed only after user confirmation.
- [`tools/watermark-ai/`](./tools/watermark-ai): the CLI-only Florence-2/LaMA worker, CPU/CUDA locks, runtime checker, Windows installer, and model-free Python tests. Python, PyTorch, and model weights are never installed into this folder.
- [`tools/watermark-ai/THIRD_PARTY_NOTICES.md`](./tools/watermark-ai/THIRD_PARTY_NOTICES.md): upstream attribution and license notices for the adapted processing approach.
- [`VeditorWindow.Tests/`](./VeditorWindow.Tests): unit tests for services plus shell breakpoints, media-drop validation, custom-frame hit regions, and conversion defaults without loading AI models.
- [`tools/README.txt`](./tools/README.txt#L1): expected external-tool placement.

## Tool Resolution

The app resolves `yt-dlp`, `ffmpeg`, `ffprobe`, `deno`, and both AI-runtime installers through [`ResolveToolPath`](./Form1.cs), in the method named `ResolveToolPath`. Search roots come from `EnumerateSearchRoots`, which checks the app base directory, the current working directory, parent folders, and finally `PATH`. That means local tool drops under `tools/` work both from the repository root and from the built output folder. `ffprobe` is used by `TryReadMediaMetadataAsync` so the trim and crop workspaces can show duration and resolution before export. The resolved FFmpeg path is supplied to `WatermarkRemovalService`, which passes it to the worker through `VEDITOR_FFMPEG_PATH` and prepends its directory to the worker's `PATH`.

## Local Background Removal Runtime

The **Background** navigation workspace accepts one JPEG, PNG, WebP, BMP, TIF, or TIFF picture. It automatically adopts the currently open picture and also supports its own picker and single-file drop. **Remove background** processes the original at full resolution once; the center stage fits the original and transparent result side by side without cropping and renders transparency over a checkerboard. **Save transparent PNG** opens a Save As dialog beside the source with `name-background-removed.png` as the default. Saving does not replace the current media or dismiss the comparison.

Click **Install or repair runtime** to install portable CPython and pinned `rembg` 2.0.77 dependencies under `%LOCALAPPDATA%\Veditor\BackgroundRemoval`. The Fast (`u2netp`), Balanced (`u2net`), and Best Quality (`birefnet-general`) models are all downloaded during installation to `%LOCALAPPDATA%\Veditor\Models\Rembg`; Balanced is the UI default. CPU is selected by default. Users may explicitly select NVIDIA CUDA, and the installer reports incompatibility before automatically continuing with CPU.

Installation reports one continuous 0–100% progress value across Python, packaging tools, pinned dependencies, all three models, and final verification. Python and model transfers include byte totals; pip work uses stable weighted stages because pip does not expose an aggregate transfer size. While installation is active, the workspace, navigation, settings, drag-and-drop, and processing actions are locked. The title bar remains available, and the status bar exposes the only workspace action: **Cancel download**. Cancellation removes incomplete `.partial` files but preserves verified Python archives, pip cache entries, and complete model files for the next repair attempt under `%LOCALAPPDATA%\Veditor\Downloads\BackgroundRemoval` and `%LOCALAPPDATA%\Veditor\Models\Rembg`.

Manual commands are also available:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\background-removal\install-background-removal.ps1 -Mode CPU
powershell -ExecutionPolicy Bypass -File .\tools\background-removal\install-background-removal.ps1 -Mode CUDA
powershell -ExecutionPolicy Bypass -File .\tools\background-removal\install-background-removal.ps1 -Mode CPU -Repair
```

Processing is local and no picture is uploaded. Installation requires internet access to download Python packages and the three model files. To uninstall, close Veditor and remove `%LOCALAPPDATA%\Veditor\BackgroundRemoval`; remove `%LOCALAPPDATA%\Veditor\Models\Rembg` to delete cached models and `%LOCALAPPDATA%\Veditor\Downloads\BackgroundRemoval` to delete verified installer downloads. Upstream attribution is recorded in [`THIRD_PARTY_NOTICES.md`](./tools/background-removal/THIRD_PARTY_NOTICES.md).

## Local WatermarkAI Runtime

Watermark cleanup is intended only for media the user owns or is authorised to modify. Processing is local: media is not uploaded, while the one-time runtime installation downloads Python packages and model weights from their official hosts.

Click **Install or repair runtime** in Veditor for the normal setup. The app warns about the multi-gigabyte download, detects a compatible NVIDIA/CUDA 12.4-or-newer driver with `nvidia-smi`, installs CUDA mode when available, and otherwise installs CPU mode. An explicitly selected CUDA installation fails clearly if PyTorch cannot use the GPU; it never silently retries with CPU.

Manual commands are also available:

```powershell
# Automatic selection (same behavior as the app)
powershell -ExecutionPolicy Bypass -File .\tools\watermark-ai\install-watermark-ai.ps1 -Mode Auto -FfmpegPath .\tools\ffmpeg\ffmpeg.exe

# Force CPU or CUDA
powershell -ExecutionPolicy Bypass -File .\tools\watermark-ai\install-watermark-ai.ps1 -Mode CPU -FfmpegPath .\tools\ffmpeg\ffmpeg.exe
powershell -ExecutionPolicy Bypass -File .\tools\watermark-ai\install-watermark-ai.ps1 -Mode CUDA -FfmpegPath .\tools\ffmpeg\ffmpeg.exe

# Rebuild Python/dependencies while retaining the model cache
powershell -ExecutionPolicy Bypass -File .\tools\watermark-ai\install-watermark-ai.ps1 -Mode Auto -Repair -FfmpegPath .\tools\ffmpeg\ffmpeg.exe
```

The x64 Windows runtime uses portable CPython 3.12.7. CPU and CUDA use the tested PyTorch 2.4.1/torchvision 0.19.1 pair; CUDA uses the official `cu124` wheels. Transformers 4.57.1 is pinned because it includes native Florence-2 model support. NumPy remains on 1.26.4 for compatibility with the pinned OpenCV and LaMA path, and Pydantic 2.10.6 satisfies IOPaint 1.5.3's Pydantic 2 schema. All resolved transitive packages are also pinned in the [CPU lock](./tools/watermark-ai/requirements-windows-cpu.lock) and [CUDA lock](./tools/watermark-ai/requirements-windows-cuda.lock). PyWebView, Qt, GTK, and PyGObject are intentionally excluded because the worker has no Python GUI.

Installed files live under `%LOCALAPPDATA%\Veditor\WatermarkAI`; Hugging Face and Torch models live under `%LOCALAPPDATA%\Veditor\Models\HuggingFace` and `%LOCALAPPDATA%\Veditor\Models\Torch`. Florence-2-large is approximately 1.5 GB and big-LaMA approximately 196 MB, with additional multi-gigabyte Python/PyTorch dependencies. Downloads can resume where Hugging Face or `curl` supports it.

To inspect an installed runtime without loading full models:

```powershell
& "$env:LOCALAPPDATA\Veditor\WatermarkAI\python\python.exe" `
  "$env:LOCALAPPDATA\Veditor\WatermarkAI\check-watermark-runtime.py" `
  --ffmpeg-path .\tools\ffmpeg\ffmpeg.exe
```

Add `--deep` to load Florence-2 and LaMA as an installation diagnostic. To uninstall, close Veditor and remove `%LOCALAPPDATA%\Veditor\WatermarkAI`. Remove `%LOCALAPPDATA%\Veditor\Models` as well only when the cached models are no longer needed.

CPU processing is expected to be slow, especially for long or high-resolution video. CUDA is substantially faster but still performs detection and inpainting over many frames. The first stable version uses rectangular Florence-2 detections as LaMA masks; it can miss faint or moving marks, accept unrelated text, or produce visible fills on large/complex regions. Use detection preview and a conservative maximum box percentage before processing.

Troubleshooting:

- **Runtime says repair required:** click **Install or repair runtime**, or rerun the installer with `-Repair`.
- **CUDA was requested but unavailable:** update the NVIDIA driver, confirm `nvidia-smi` reports CUDA 12.4 or newer, and rerun CUDA installation. Choose CPU manually only if CPU processing is acceptable.
- **Models unavailable:** rerun without `-SkipModelDownload`; a skipped-model installation is deliberately marked incomplete.
- **FFmpeg missing:** place `ffmpeg.exe` and `ffprobe.exe` under `tools\ffmpeg` or add them to `PATH`.
- **CUDA out of memory:** reduce input resolution or use CPU mode; the worker reports `CUDA_OUT_OF_MEMORY` rather than hiding the GPU failure.

## General Code Structure Schema

This is the concrete runtime shape of the current application, not a generic template. Each box starts with the plain-language behavior and then names the exact code that performs that job.

```text
 +------------------------------------------------------------+
 | Windows starts one calm, custom-framed desktop window.      |
 | Program.Main -> Form1() -> ConfigureStudioLayout()          |
 | BuildIntegratedTitleBar() / ClayPanel / ClayButton          |
 +------------------------------------------------------------+
                              |
                              v
 +---------------------+  +----------------------+  +----------------------+
 | Choose a task.      |  | See or open media.   |  | Keep tools in view.  |
 | matte navigation    |  | preview/drop stage   |  | fixed inspector      |
 | BuildNavigationRail |  | BuildEmptyPreviewState| | BuildSidebar         |
 | StudioLayoutPolicy  |  | MediaDropValidator   |  | ClayScrollPanel      |
 +---------------------+  +----------------------+  +----------------------+
              \                  |                         /
               +-----------------+------------------------+
                                  |
       +--------------------------+--------------------------+
       |            |             |             |             |
       v            v             v             v             v
 +-----------+ +-----------+ +-----------+ +-----------+ +-----------+
 | Download  | | Convert   | | Compress  | | Remove a  | | Trim/crop |
 | media.    | | media.    | | pictures. | | picture's | | video.    |
 | StartDown-| | StartCon- | | RunPicture| | background| | StartTrim/|
 | loadAsync | | version-  | | OutputAsync| | RunBack-  | | CropEx-  |
 |           | | Async     | | PictureCom| | groundRem-| | portAsync |
 |           | |           | | pressionSvc| | ovalAsync | |           |
 +-----------+ +-----------+ +-----------+ +-----------+ +-----------+
       |            |             |             |             |
       +------------+-------------+-------------+-------------+
                                  |
                                  v
 +------------------------------------------------------------+
 | Clean an authorised watermark when the user opts in.       |
 | RunWatermarkOperationAsync / WatermarkRemovalService        |
 +------------------------------------------------------------+
                                  |
                                  v
 +------------------------------------------------------------+
 | Validate inputs, resolve tools, and perform local work.     |
 | ResolveToolPath / ffmpeg / ffprobe / yt-dlp / WebView2      |
 | WatermarkRemovalService / BackgroundRemovalService          |
 | worker.py / JSON-line protocols / PowerShell installers     |
 +------------------------------------------------------------+
                                  |
                                  v
 +------------------------------------------------------------+
 | Make the result current and show feedback everywhere.       |
 | SetCurrentMediaSource / LoadPreviewAsync / SetUiBusy        |
 | UpdateStatus / AppendLog / AddActivityEntry / OnFormClosing |
 +------------------------------------------------------------+
```

### Schema Mapping

- `entry point`: [`Program.Main`](./Program.cs#L9) initializes WinForms and opens [`Form1`](./Form1.cs).
- `input collection`: [`ConfigureStudioLayout`](./Form1.cs) builds the labeled navigation, flexible preview stage, and permanently docked tool inspector around designer-managed controls. [`BuildEmptyPreviewState`](./Form1.StudioShell.cs) and [`OpenMediaPathAsync`](./Form1.StudioShell.cs) share picker/drop input handling; [`BuildPictureCompressionPage`](./Form1.PictureCompression.cs) adds multi-select input for compression, while [`BuildBackgroundRemovalPage`](./Form1.BackgroundRemoval.cs) accepts one current, picked, or dropped still picture.
- `presentation and interaction`: [`StudioTheme`](./UI/StudioTheme.cs), [`ClayDrawing`](./UI/ClayDrawing.cs), [`ClayPanel`](./UI/ClayPanel.cs), [`ClayButton`](./UI/ClayButton.cs), and [`ClayCheckBox`](./UI/ClayCheckBox.cs) keep matte depth, focus, hover, selection, and disabled states consistent without replacing semantic controls with screenshots. [`ClayScrollPanel`](./UI/ClayScrollPanel.cs) uses native WinForms scrolling for reliable child-control repainting and overlays [`ClayScrollBar`](./UI/ClayScrollBar.cs) to retain the purple clay styling.
- `layout and window handling`: [`ApplyResponsiveLayout`](./Form1.StudioShell.cs#L145) switches only the left navigation at 1380 logical pixels; the inspector begins with its active controls and remains scrollable in both modes. [`StudioLayoutPolicy`](./UI/StudioLayoutPolicy.cs#L9) owns that breakpoint, while [`WindowChromeController`](./UI/WindowChromeController.cs#L22) owns resize, caption, snap-button, and system-menu hit regions.
- `input validation`: [`MediaDropValidator`](./UI/MediaDropValidator.cs#L30) accepts exactly one existing supported audio/video file and returns user-readable feedback for folders, multiple paths, missing files, and unsupported extensions.
- `precondition checks`: `StartDownloadAsync`, `StartConversionAsync`, `StartTrimExportAsync`, and `StartCropExportAsync` in [`Form1.cs`](./Form1.cs) validate their media and tool requirements. `RunBackgroundRemovalAsync` in [`Form1.BackgroundRemoval.cs`](./Form1.BackgroundRemoval.cs) requires one supported picture and a healthy isolated runtime. `RunWatermarkOperationAsync` additionally requires a video, a matching ownership confirmation, FFmpeg, a healthy runtime, and valid [`WatermarkRemovalOptions`](./Models/WatermarkRemovalOptions.cs).
- `normalization`: `ResolveCompletedMediaPath`, `RefreshCurrentMediaMetadataAsync`, and `webPreview_WebMessageReceived` in [`Form1.cs`](./Form1.cs) normalize media state. [`VeditorPaths`](./Services/VeditorPaths.cs) normalizes Local AppData roots, while [`WatermarkWorkerProtocol`](./Services/WatermarkWorkerProtocol.cs) and [`BackgroundRemovalInstallerProtocol`](./Services/BackgroundRemovalInstallerProtocol.cs) normalize JSON-line messages, byte-aware progress, casing, booleans, and percentages.
- `business rules`: conversion begins in [`StartConversionAsync`](./Form1.cs), picture output in [`RunPictureOutputAsync`](./Form1.PictureCompression.cs), background removal in [`RunBackgroundRemovalAsync`](./Form1.BackgroundRemoval.cs), trim export in [`StartTrimExportAsync`](./Form1.cs), and crop export in [`StartCropExportAsync`](./Form1.cs). [`PictureCompressionService`](./Services/PictureCompressionService.cs) preserves extensions and dimensions, while [`BackgroundRemovalOptions`](./Models/BackgroundRemovalOptions.cs) maps the three slider positions to installed models and [`BackgroundRemovalOutputPath`](./Services/BackgroundRemovalOutputPath.cs) owns the PNG naming rule.
- `state transition`: [`SetUiBusy`](./Form1.cs), [`SetCurrentMediaSource`](./Form1.cs), `SetBackgroundInstallationUi`, `UpdatePreviewButtons`, `UpdateBackgroundControls`, `UpdateWatermarkButtons`, `UpdateTrimUi`, and `UpdateCropUi` move the UI between empty, loaded, globally installation-locked, busy, authorised, and cancellable states.
- `external service call`: [`PictureCompressionService`](./Services/PictureCompressionService.cs) invokes FFmpeg for format-aware still-image encoding and exact preview sizes. [`Form1.BackgroundRemoval.cs`](./Form1.BackgroundRemoval.cs) launches the independent [background-removal installer](./tools/background-removal/install-background-removal.ps1), consumes its structured installation progress, and owns cancellation; [`BackgroundRemovalService`](./Services/BackgroundRemovalService.cs) launches the installed `rembg` worker and validates the transparent PNG. `InstallOrRepairWatermarkRuntimeAsync` launches the separate [WatermarkAI installer](./tools/watermark-ai/install-watermark-ai.ps1), while [`WatermarkRemovalService`](./Services/WatermarkRemovalService.cs) retains ownership of that worker.
- `output shaping`: `HandleOutputLine`, `HandleWatermarkProgress`, `RefreshPreviewSummary`, `UpdateTrimUi`, `UpdateCropUi`, `BuildConversionSizeSummary`, and `BuildWatermarkSizeSummary` in [`Form1.cs`](./Form1.cs) turn backend output into user-facing progress, logs, metadata, and size comparisons.
- `error handling`: process entry methods in [`Form1.cs`](./Form1.cs) surface user-facing failures; [`WatermarkRemovalService`](./Services/WatermarkRemovalService.cs) returns structured failures without showing dialogs; malformed worker lines become safe log messages in [`WatermarkWorkerProtocol`](./Services/WatermarkWorkerProtocol.cs).
- `cleanup`: `OnFormClosing` in [`Form1.cs`](./Form1.cs) cancels active tokens and tears down every tracked process. Background runtime cancellation removes only incomplete `.partial` transfers while retaining verified cache assets. [`BackgroundRemovalService`](./Services/BackgroundRemovalService.cs) and [`WatermarkRemovalService`](./Services/WatermarkRemovalService.cs) delete only their own operation-specific temporary outputs.

## Runtime Notes

- The default output folder is set in [`Form1`](./Form1.cs), in the constructor, to the user's `My Videos\VeditorDownloads` directory.
- Preview rendering is browser-backed, so some codecs may not play inside WebView2 even when the file exists. The HTML generated by `BuildMediaPreviewHtml` in [`Form1.cs`](./Form1.cs) shows an in-app fallback message, and the crop workspace uses the same document to draw and drag the live crop box.
- The trim and crop workspaces both depend on `ffprobe` metadata plus live preview state. If `ffprobe` is unavailable, export still works, but duration and resolution details may populate only after preview playback starts.
- Download completion is detected from both `yt-dlp` output parsing and folder-state comparison, implemented by `HandleOutputLine` and `ResolveCompletedMediaPath` in [`Form1.cs`](./Form1.cs).
- AI cleanup is local and opt-in. Both cleanup actions remain disabled until a video is loaded, runtime health is ready, and the user confirms ownership or permission for that exact current file.
- Full AI cleanup always targets a unique `*_watermark_removed.mp4` file beside the source. Detection previews use a temporary PNG that is deleted when the preview dialog closes. The source is never an overwrite target.
- The worker writes one valid JSON object per standard-output line and reserves standard error for diagnostics. A successful process emits exactly one `completed` message whose normalized path matches the path requested by the service. Preview additionally emits the bounding boxes and accepted area percentages in a `preview` message.
