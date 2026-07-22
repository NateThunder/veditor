namespace VeditorWindow.Services;

public static class WatermarkOutputPathGenerator
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"
    };

    public static string CreateProcessedOutputPath(string sourcePath)
    {
        //== input validation ==================================================
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }
        //=====================================================================

        //== output shaping ===================================================
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceExtension = Path.GetExtension(fullSourcePath);
        var outputExtension = SupportedImageExtensions.Contains(sourceExtension)
            ? sourceExtension
            : ".mp4";
        var directory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new ArgumentException("The source path must include a directory.", nameof(sourcePath));
        var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);
        return CreateUniquePath(directory, $"{baseName}_watermark_removed", outputExtension);
        //=====================================================================
    }

    public static string CreateVideoOutputPath(string sourcePath)
    {
        //== input validation ==================================================
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }
        //=====================================================================

        //== output shaping ===================================================
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new ArgumentException("The source path must include a directory.", nameof(sourcePath));
        var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);

        return CreateUniquePath(directory, $"{baseName}_watermark_removed", ".mp4");
        //=====================================================================
    }

    public static string CreatePreviewOutputPath(string sourcePath, string temporaryDirectory)
    {
        //== input validation ==================================================
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(temporaryDirectory))
        {
            throw new ArgumentException("A temporary directory is required.", nameof(temporaryDirectory));
        }
        //=====================================================================

        //== output shaping ===================================================
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFullPath(sourcePath));
        return CreateUniquePath(
            Path.GetFullPath(temporaryDirectory),
            $"{baseName}_watermark_detection_preview",
            ".png");
        //=====================================================================
    }

    public static string CreateUniquePath(string directory, string baseName, string extension)
    {
        //== input validation ==================================================
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        //=====================================================================

        //== normalization ====================================================
        var normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
        var normalizedDirectory = Path.GetFullPath(directory);
        //=====================================================================

        //== unique path selection ===========================================
        var candidatePath = Path.Combine(normalizedDirectory, $"{baseName}{normalizedExtension}");
        var counter = 1;

        while (Path.Exists(candidatePath))
        {
            candidatePath = Path.Combine(
                normalizedDirectory,
                $"{baseName}_{counter}{normalizedExtension}");
            counter++;
        }

        return candidatePath;
        //=====================================================================
    }
}
