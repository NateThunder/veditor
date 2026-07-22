using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class BackgroundRemovalInstallerProtocolTests
{
    [Fact]
    public void ParseProgress_ReadsByteAwareInstallerUpdate()
    {
        //== protocol parsing =================================================
        var progress = BackgroundRemovalInstallerProtocol.ParseProgress(
            "{\"type\":\"installer_progress\",\"percent\":68.5,\"stage\":\"models\",\"message\":\"Downloading u2net\",\"bytesReceived\":1048576,\"bytesTotal\":2097152}");
        //=====================================================================

        Assert.NotNull(progress);
        Assert.Equal(68.5D, progress.Percent);
        Assert.Equal("models", progress.Stage);
        Assert.Equal("Downloading u2net", progress.Message);
        Assert.Equal(1048576, progress.BytesReceived);
        Assert.Equal(2097152, progress.BytesTotal);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(140, 100)]
    public void ParseProgress_ClampsPercentage(double input, double expected)
    {
        var progress = BackgroundRemovalInstallerProtocol.ParseProgress(
            $"{{\"type\":\"installer_progress\",\"percent\":{input},\"stage\":\"test\",\"message\":\"Testing\"}}");

        Assert.NotNull(progress);
        Assert.Equal(expected, progress.Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"installer_result\",\"success\":true}")]
    public void ParseProgress_IgnoresNonProgressLines(string? line)
    {
        Assert.Null(BackgroundRemovalInstallerProtocol.ParseProgress(line));
    }
}
