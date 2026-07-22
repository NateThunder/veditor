using VeditorWindow.UI;

namespace VeditorWindow.Tests;

public sealed class MediaDropValidatorTests : IDisposable
{
    private readonly string _testDirectory;

    public MediaDropValidatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"VeditorWindowDropTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, recursive: true);
    }

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".m4v")]
    [InlineData(".mov")]
    [InlineData(".mkv")]
    [InlineData(".avi")]
    [InlineData(".flv")]
    [InlineData(".wmv")]
    [InlineData(".webm")]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".wav")]
    [InlineData(".aac")]
    [InlineData(".ogg")]
    [InlineData(".flac")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    public void Validate_AcceptsEverySupportedExtension(string extension)
    {
        var path = CreateFile($"media{extension}");

        var result = MediaDropValidator.Validate([path]);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(path), result.MediaPath);
        Assert.Equal(MediaDropFailure.None, result.Failure);
    }

    [Fact]
    public void Validate_RejectsNoFiles()
    {
        var result = MediaDropValidator.Validate([]);

        Assert.False(result.IsValid);
        Assert.Equal(MediaDropFailure.NoFiles, result.Failure);
    }

    [Fact]
    public void Validate_RejectsMultipleFiles()
    {
        var result = MediaDropValidator.Validate([CreateFile("one.mp4"), CreateFile("two.mp4")]);

        Assert.False(result.IsValid);
        Assert.Equal(MediaDropFailure.MultipleFiles, result.Failure);
    }

    [Fact]
    public void Validate_RejectsFolder()
    {
        var result = MediaDropValidator.Validate([_testDirectory]);

        Assert.False(result.IsValid);
        Assert.Equal(MediaDropFailure.Folder, result.Failure);
    }

    [Fact]
    public void Validate_RejectsMissingFile()
    {
        var result = MediaDropValidator.Validate([Path.Combine(_testDirectory, "missing.mp4")]);

        Assert.False(result.IsValid);
        Assert.Equal(MediaDropFailure.MissingFile, result.Failure);
    }

    [Fact]
    public void Validate_RejectsUnsupportedFormat()
    {
        var result = MediaDropValidator.Validate([CreateFile("notes.txt")]);

        Assert.False(result.IsValid);
        Assert.Equal(MediaDropFailure.UnsupportedFormat, result.Failure);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_testDirectory, name);
        File.WriteAllText(path, "test");
        return path;
    }
}
