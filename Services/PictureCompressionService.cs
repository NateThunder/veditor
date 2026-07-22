using System.Diagnostics;
using System.Globalization;
using System.Collections.ObjectModel;

namespace VeditorWindow.Services;

internal enum PictureCompressionMode
{
    Lossless,
    Lossy
}

internal readonly record struct PictureCompressionOptions(
    PictureCompressionMode Mode,
    int Quality,
    bool StripMetadata);

internal readonly record struct PictureCompressionResult(
    string SourcePath,
    string OutputPath,
    long SourceBytes,
    long OutputBytes);

internal static class PictureCompressionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> LosslessOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".tif", ".tiff"
    };

    internal static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    internal static bool IsLosslessOnly(string path) => LosslessOnlyExtensions.Contains(Path.GetExtension(path));

    internal static string CreateCopyPath(string sourcePath)
    {
        //== output shaping ===================================================
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var candidate = Path.Combine(directory, $"{stem}_compressed{extension}");
        var suffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem}_compressed_{suffix}{extension}");
            suffix++;
        }

        return candidate;
        //=====================================================================
    }

    internal static async Task<PictureCompressionResult> CompressAsync(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        PictureCompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        //== input validation =================================================
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source picture was not found.", sourcePath);
        }

        if (!IsSupported(sourcePath))
        {
            throw new NotSupportedException($"{Path.GetExtension(sourcePath)} pictures are not supported.");
        }

        if (options.Mode == PictureCompressionMode.Lossy && IsLosslessOnly(sourcePath))
        {
            throw new NotSupportedException("BMP and TIFF pictures support lossless compression only.");
        }

        var normalizedQuality = Math.Clamp(options.Quality, 1, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
        //=====================================================================

        //== external service call ===========================================
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");

        if (options.StripMetadata)
        {
            startInfo.ArgumentList.Add("-map_metadata");
            startInfo.ArgumentList.Add("-1");
        }

        var isLosslessJpeg = options.Mode == PictureCompressionMode.Lossless &&
                             Path.GetExtension(sourcePath) is ".jpg" or ".jpeg" or ".JPG" or ".JPEG";
        if (isLosslessJpeg)
        {
            // Preserve the existing JPEG bitstream; only its container metadata may change.
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("copy");
        }
        else
        {
            AddFormatArguments(startInfo.ArgumentList, sourcePath, options.Mode, normalizedQuality);
        }
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        string error;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            error = await errorTask;
        }
        catch (OperationCanceledException)
        {
            //== cleanup superseded compression ===============================
            TryTerminateProcess(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
                await errorTask;
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and the wait.
            }
            //=================================================================

            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Picture compression failed with exit code {process.ExitCode}."
                : error.Trim());
        }
        //=====================================================================

        return new PictureCompressionResult(
            sourcePath,
            outputPath,
            new FileInfo(sourcePath).Length,
            new FileInfo(outputPath).Length);
    }

    private static void TryTerminateProcess(Process process)
    {
        //== cleanup ==========================================================
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process cannot be terminated because it is no longer accessible.
        }
        //=====================================================================
    }

    private static void AddFormatArguments(
        Collection<string> arguments,
        string sourcePath,
        PictureCompressionMode mode,
        int quality)
    {
        //== format-specific compression =====================================
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                arguments.Add("-q:v");
                arguments.Add(mode == PictureCompressionMode.Lossless
                    ? "2"
                    : MapJpegQuality(quality).ToString(CultureInfo.InvariantCulture));
                break;

            case ".png":
                if (mode == PictureCompressionMode.Lossy)
                {
                    var colors = Math.Clamp((int)Math.Round(16D + (quality / 100D * 240D)), 16, 256);
                    arguments.Add("-vf");
                    arguments.Add($"split[s0][s1];[s0]palettegen=max_colors={colors}[p];[s1][p]paletteuse=dither=sierra2_4a");
                }
                arguments.Add("-compression_level");
                arguments.Add("6");
                break;

            case ".webp":
                arguments.Add("-lossless");
                arguments.Add(mode == PictureCompressionMode.Lossless ? "1" : "0");
                arguments.Add("-quality");
                arguments.Add(quality.ToString(CultureInfo.InvariantCulture));
                arguments.Add("-compression_level");
                arguments.Add("4");
                break;

            case ".tif":
            case ".tiff":
                arguments.Add("-compression_algo");
                arguments.Add("deflate");
                break;
        }
        //=====================================================================
    }

    private static int MapJpegQuality(int quality)
    {
        //== normalization ====================================================
        return Math.Clamp((int)Math.Round(31D - ((quality - 1D) / 99D * 29D)), 2, 31);
        //=====================================================================
    }
}
