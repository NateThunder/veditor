namespace VeditorWindow.UI;

internal enum MediaDropFailure
{
    None,
    NoFiles,
    MultipleFiles,
    Folder,
    MissingFile,
    UnsupportedFormat
}

internal readonly record struct MediaDropValidationResult(
    bool IsValid,
    string? MediaPath,
    MediaDropFailure Failure,
    string Message);

internal static class MediaDropValidator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".flv", ".wmv", ".webm",
        ".mp3", ".m4a", ".wav", ".aac", ".ogg", ".flac",
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"
    };

    internal const string SupportedFormatHint = "MP4, MOV, MKV, AVI, WEBM, MP3, WAV, PNG, JPG and more";

    internal static MediaDropValidationResult Validate(IReadOnlyList<string>? paths)
    {
        //== input validation =================================================
        if (paths is null || paths.Count == 0)
        {
            return Failure(MediaDropFailure.NoFiles, "Drop one media file here.");
        }

        if (paths.Count != 1)
        {
            return Failure(MediaDropFailure.MultipleFiles, "Choose one media file at a time.");
        }

        var path = paths[0];
        if (Directory.Exists(path))
        {
            return Failure(MediaDropFailure.Folder, "Folders are not supported. Drop one media file instead.");
        }

        if (!File.Exists(path))
        {
            return Failure(MediaDropFailure.MissingFile, "That file is no longer available.");
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            return Failure(MediaDropFailure.UnsupportedFormat, $"Unsupported format. {SupportedFormatHint}.");
        }
        //=====================================================================

        return new MediaDropValidationResult(true, Path.GetFullPath(path), MediaDropFailure.None, string.Empty);
    }

    internal static bool IsSupportedExtension(string extension)
    {
        return SupportedExtensions.Contains(extension.StartsWith('.') ? extension : $".{extension}");
    }

    private static MediaDropValidationResult Failure(MediaDropFailure failure, string message)
    {
        return new MediaDropValidationResult(false, null, failure, message);
    }
}
