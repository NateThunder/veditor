using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class PictureCompressionServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"veditor-picture-tests-{Guid.NewGuid():N}");

    public PictureCompressionServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData("photo.jpg", true, false)]
    [InlineData("graphic.png", true, false)]
    [InlineData("modern.webp", true, false)]
    [InlineData("source.bmp", true, true)]
    [InlineData("archive.tiff", true, true)]
    [InlineData("animation.gif", false, false)]
    public void FormatPolicy_MatchesInitialPictureCompressionScope(string fileName, bool supported, bool losslessOnly)
    {
        Assert.Equal(supported, PictureCompressionService.IsSupported(fileName));
        Assert.Equal(losslessOnly, PictureCompressionService.IsLosslessOnly(fileName));
    }

    [Fact]
    public void CreateCopyPath_AddsCompressedSuffixBesideSource()
    {
        var sourcePath = Path.Combine(_temporaryDirectory, "holiday.photo.jpg");

        var result = PictureCompressionService.CreateCopyPath(sourcePath);

        Assert.Equal(Path.Combine(_temporaryDirectory, "holiday.photo_compressed.jpg"), result);
    }

    [Fact]
    public void CreateCopyPath_NumbersExistingCompressedCopies()
    {
        var sourcePath = Path.Combine(_temporaryDirectory, "photo.jpg");
        File.WriteAllText(Path.Combine(_temporaryDirectory, "photo_compressed.jpg"), string.Empty);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "photo_compressed_1.jpg"), string.Empty);

        var result = PictureCompressionService.CreateCopyPath(sourcePath);

        Assert.Equal(Path.Combine(_temporaryDirectory, "photo_compressed_2.jpg"), result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
