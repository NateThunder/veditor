# yt-dlp wrapper tutorial

This project now contains a simple Windows Forms wrapper around `yt-dlp.exe`.

## 1. Download the required tools

Put these files in the project root under `tools\`:

```text
tools\
  yt-dlp.exe
  deno.exe
  ffmpeg\
    ffmpeg.exe
    ffprobe.exe
```

Use the official sources:

- yt-dlp releases: https://github.com/yt-dlp/yt-dlp
- FFmpeg builds: https://github.com/yt-dlp/FFmpeg-Builds
- Deno: https://deno.com/

If you plan to redistribute your app with the official Windows `yt-dlp.exe` bundled inside it, review the `yt-dlp` licensing notes first. The official standalone Windows executable includes GPLv3+ licensed components.

## 2. Why Deno matters

As of `yt-dlp` version `2025.11.12`, the project announced that an external JavaScript runtime is required for full YouTube support. Deno is the recommended runtime.

If `deno.exe` is missing, some YouTube downloads may fail or have limited format availability.

## 3. Build the project

The project file copies everything under `tools\` into the output folder on build:

```powershell
dotnet build
```

## 4. Run the app

Start the WinForms app, paste a video URL, choose an output folder, then click `Download`.

The `MP3` and `WAV` audio-only checkboxes both start unchecked, so the default action downloads video. Selecting either checkbox adds that audio format:

```text
-x --audio-format mp3
# or
-x --audio-format wav
```

Selecting both downloads WAV once and uses FFmpeg to create the matching MP3 without downloading the source twice. If neither audio-only option is checked, the wrapper prefers a best-video plus best-audio download and merges to MP4 when possible.

## 5. How the wrapper works

The wrapper in `Form1.cs` starts `yt-dlp.exe` with `ProcessStartInfo.ArgumentList`.

Important flags:

- `--newline`
- `--progress`
- `--progress-template`
- `--print after_move:FILE:%(filepath)s`
- `--ffmpeg-location`

The important design choice is that the app does not parse default human-readable console text. It asks `yt-dlp` for structured progress lines, which is the approach recommended by the project for embedding.

## 6. What to improve next

Useful next steps:

- Add a format picker by calling `yt-dlp -J URL` and parsing JSON.
- Add playlist support.
- Add a thumbnail preview.
- Add cookie import for authenticated sites.
- Add a settings screen for custom arguments.
