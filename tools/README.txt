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
