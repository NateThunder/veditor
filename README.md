# VeditorWindow

VeditorWindow is a Windows Forms desktop app for downloading media with `yt-dlp`, converting files with `ffmpeg`, and previewing the active file inside an embedded WebView2 surface. The application keeps the workflow in one window: paste a URL, download into a local folder, inspect the result, and convert it to common audio or video formats.

## What The App Does

- Downloads online media into a user-selected output folder.
- Optionally extracts audio during download.
- Opens local media files and previews them inside the app.
- Converts the active media file to `MP3`, `WAV`, `M4A`, `MP4`, `MKV`, or `MOV`.
- Shows progress, logs, preview metadata, and source-vs-output size summaries.

## Requirements

- Windows desktop environment.
- .NET Windows target: [`net10.0-windows`](./VeditorWindow.csproj#L5) with [`UseWindowsForms`](./VeditorWindow.csproj#L7).
- WebView2 support for the embedded preview surface. The project references [`Microsoft.Web.WebView2`](./VeditorWindow.csproj#L19).
- External tools available either in `tools/` or on `PATH`:
  - [`yt-dlp.exe`](./tools/README.txt#L13)
  - [`deno.exe`](./tools/README.txt#L14) for modern YouTube support
  - [`ffmpeg.exe` and `ffprobe.exe`](./tools/README.txt#L15)

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

The project copies `tools\**\*` into the output directory during build, so keeping the downloader and codec binaries inside `tools/` is the intended local workflow.

## Repository Layout

- [`Program.cs`](./Program.cs#L9): WinForms entry point that initializes the app and opens the main window.
- [`Form1.cs`](./Form1.cs#L9): primary window, UI composition, download flow, conversion flow, preview flow, status/log updates, and tool lookup.
- [`Form1.Designer.cs`](./Form1.Designer.cs): designer-managed controls used by the main window.
- [`tools/README.txt`](./tools/README.txt#L1): expected external-tool placement.

## Tool Resolution

The app resolves `yt-dlp`, `ffmpeg`, `ffprobe`, and `deno` through [`ResolveToolPath`](./Form1.cs#L2522). Search roots are gathered by [`EnumerateSearchRoots`](./Form1.cs#L2591), which checks the app base directory, the current working directory, parent folders, and finally `PATH`. That means local tool drops under `tools/` work both from the repository root and from the built output folder.

## General Code Structure Schema

This is the concrete runtime shape of the current application, not a generic template. Each box starts with the plain-language behavior and then names the exact code that performs that job.

```text
     +--------------------------------------+
     | App startup.                         |
     | Program.Main                         |
     | ApplicationConfiguration.Initialize  |
     | Application.Run(new Form1())         |
     +--------------------------------------+
                        |
                        v
     +--------------------------------------+
     | The main window is built and the     |
     | workspace shell is styled.           |
     | Form1()                              |
     | InitializeComponent()                |
     | ConfigureStudioLayout()              |
     | SetUiBusy(AppOperation.None)         |
     | UpdateVideoQualityUi()               |
     +--------------------------------------+
                        |
                        v
     +--------------------------------------+
     | Preview bootstraps in the background |
     | after the window is shown.           |
     | Form1_Shown                          |
     | EnsurePreviewReadyAsync()            |
     | webPreview.NavigationCompleted       |
     +--------------------------------------+
                        |
           +------------+----------------------+
           |                                   |
           v                                   v
+----------------------------------+   +----------------------------------+
| The user starts a download.      |   | The user opens or converts the   |
| StartDownloadAsync()             |   | current media file.              |
| URL + folder checks              |   | StartConversionAsync()           |
| CaptureOutputFolderSnapshot()    |   | GetPreferredMediaPath()          |
| ResolveToolPath()                |   | IsAudioOnlyMedia()               |
+----------------------------------+   +----------------------------------+
           |                                    |
           v                                    v
+----------------------------------+   +----------------------------------+
| yt-dlp is configured and started.|   | ffmpeg arguments are selected    |
| BuildStartInfo()                 |   | and the conversion starts.       |
| ProcessStartInfo                 |   | BuildConversionStartInfo()       |
| ReadLinesAsync()                 |   | GetConversionArguments()         |
| HandleOutputLine()               |   | BuildVideoConversionArguments()  |
+----------------------------------+   +----------------------------------+
           |                                   |
           +------------+----------------------+
                        |
                        v
     +--------------------------------------+
     | The active media file is chosen,     |
     | summarized, and loaded into preview. |
     | ResolveCompletedMediaPath()          |
     | SetCurrentMediaSource()              |
     | LoadPreviewAsync()                   |
     | NavigatePreviewToMedia()             |
     | BuildMediaPreviewHtml()              |
     +--------------------------------------+
                        |
                        v
     +--------------------------------------+
     | Background work becomes visible UI:  |
     | status text, logs, buttons, preview  |
     | metadata, and size summaries.        |
     | UpdateStatus()                       |
     | AppendLog()                          |
     | RefreshPreviewSummary()              |
     | UpdatePreviewButtons()               |
     | BuildConversionSizeSummary()         |
     +--------------------------------------+
                        |
                        v
     +--------------------------------------+
     | Failures are surfaced safely and     |
     | running work is cleaned up.          |
     | try/catch in download/conversion     |
     | ShowPreviewState()                   |
     | OnFormClosing()                      |
     +--------------------------------------+
```

### Schema Mapping

- `entry point`: [`Program.Main`](./Program.cs#L9) initializes WinForms and opens [`Form1`](./Form1.cs#L75).
- `input collection`: [`ConfigureStudioLayout`](./Form1.cs#L90) builds the workspace shell around the controls declared in [`Form1.Designer.cs`](./Form1.Designer.cs).
- `precondition checks`: [`StartDownloadAsync`](./Form1.cs#L1171) validates URL, output folder, and required tools; [`StartConversionAsync`](./Form1.cs#L1344) validates the active media source and target format rules.
- `normalization`: [`CaptureOutputFolderSnapshot`](./Form1.cs#L2215) records the starting output set, while [`ResolveCompletedMediaPath`](./Form1.cs#L2239) normalizes detection of the finished media file.
- `business rules`: [`GetConversionArguments`](./Form1.cs#L1572), [`BuildVideoConversionArguments`](./Form1.cs#L1588), and [`GetSelectedVideoQualityPreset`](./Form1.cs#L2113) choose output behavior and compression settings.
- `state transition`: [`SetUiBusy`](./Form1.cs#L1747), [`SetCurrentMediaSource`](./Form1.cs#L1968), [`UpdatePreviewButtons`](./Form1.cs#L2086), and [`UpdateConversionButtons`](./Form1.cs#L2098) move the UI between idle, working, and ready states.
- `external service call`: [`BuildStartInfo`](./Form1.cs#L1465) launches `yt-dlp`; [`BuildConversionStartInfo`](./Form1.cs#L1541) launches `ffmpeg`; [`EnsurePreviewReadyAsync`](./Form1.cs#L1892) initializes the embedded WebView2 runtime.
- `output shaping`: [`HandleOutputLine`](./Form1.cs#L1670), [`RefreshPreviewSummary`](./Form1.cs#L2023), and [`BuildConversionSizeSummary`](./Form1.cs#L2154) turn backend output into user-facing status and summary text.
- `error handling`: [`StartDownloadAsync`](./Form1.cs#L1171), [`StartConversionAsync`](./Form1.cs#L1344), and [`webPreview_NavigationCompleted`](./Form1.cs#L1975) surface process, preview, and navigation failures without crashing the UI.
- `cleanup`: [`OnFormClosing`](./Form1.cs#L1147) cancels active work and tears down the running process on shutdown.

## Runtime Notes

- The default output folder is set in [`Form1`](./Form1.cs#L75) to the user's `My Videos\VeditorDownloads` directory.
- Preview rendering is browser-backed, so some codecs may not play inside WebView2 even when the file exists. The HTML generated by [`BuildMediaPreviewHtml`](./Form1.cs#L2335) shows an in-app fallback message, and the UI exposes an external-open path for unsupported formats.
- Download completion is detected from both `yt-dlp` output parsing and folder-state comparison, which is why the README's flow references both [`HandleOutputLine`](./Form1.cs#L1670) and [`ResolveCompletedMediaPath`](./Form1.cs#L2239).
