using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class WatermarkOutputPathGeneratorTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"VeditorTests-{Guid.NewGuid():N}");

    public WatermarkOutputPathGeneratorTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void CreateVideoOutputPath_UsesMp4BesideSource()
    {
        //== arrange ===========================================================
        var sourcePath = Path.Combine(_temporaryDirectory, "sample.mkv");
        File.WriteAllText(sourcePath, "source");
        //=====================================================================

        //== act ===============================================================
        var outputPath = WatermarkOutputPathGenerator.CreateVideoOutputPath(sourcePath);
        //=====================================================================

        //== assertions =======================================================
        Assert.Equal(
            Path.Combine(_temporaryDirectory, "sample_watermark_removed.mp4"),
            outputPath);
        Assert.False(string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("source", File.ReadAllText(sourcePath));
        //=====================================================================
    }

    [Fact]
    public void CreateVideoOutputPath_IncrementsExistingName()
    {
        //== arrange ===========================================================
        var sourcePath = Path.Combine(_temporaryDirectory, "sample.mp4");
        var firstOutput = Path.Combine(_temporaryDirectory, "sample_watermark_removed.mp4");
        var secondOutput = Path.Combine(_temporaryDirectory, "sample_watermark_removed_1.mp4");
        File.WriteAllText(sourcePath, "source");
        File.WriteAllText(firstOutput, "existing");
        File.WriteAllText(secondOutput, "existing");
        //=====================================================================

        var outputPath = WatermarkOutputPathGenerator.CreateVideoOutputPath(sourcePath);

        Assert.Equal(
            Path.Combine(_temporaryDirectory, "sample_watermark_removed_2.mp4"),
            outputPath);
    }

    [Fact]
    public void CreateProcessedOutputPath_PreservesImageExtensionAndNumbersDuplicates()
    {
        //== arrange ===========================================================
        var sourcePath = Path.Combine(_temporaryDirectory, "still.photo.JPEG");
        var existingPath = Path.Combine(_temporaryDirectory, "still.photo_watermark_removed.JPEG");
        File.WriteAllText(sourcePath, "source");
        File.WriteAllText(existingPath, "existing");
        //=====================================================================

        var outputPath = WatermarkOutputPathGenerator.CreateProcessedOutputPath(sourcePath);

        Assert.Equal(
            Path.Combine(_temporaryDirectory, "still.photo_watermark_removed_1.JPEG"),
            outputPath);
        Assert.Equal("source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public void CreateProcessedOutputPath_UsesMp4ForVideoContainers()
    {
        var sourcePath = Path.Combine(_temporaryDirectory, "clip.mov");
        File.WriteAllText(sourcePath, "source");

        var outputPath = WatermarkOutputPathGenerator.CreateProcessedOutputPath(sourcePath);

        Assert.EndsWith("clip_watermark_removed.mp4", outputPath, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        //== cleanup ===========================================================
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        //=====================================================================
    }
}
