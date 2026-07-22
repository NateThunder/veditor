using VeditorWindow.Models;

namespace VeditorWindow.Tests;

public sealed class BackgroundRemovalOptionsTests
{
    [Theory]
    [InlineData(BackgroundRemovalQuality.Fast, "u2netp")]
    [InlineData(BackgroundRemovalQuality.Balanced, "u2net")]
    [InlineData(BackgroundRemovalQuality.Best, "birefnet-general")]
    public void ModelName_MapsQualityToExpectedInstalledModel(BackgroundRemovalQuality quality, string expected)
    {
        //== act ==============================================================
        var options = new BackgroundRemovalOptions(quality, RefineEdges: false);
        //=====================================================================

        //== assertions =======================================================
        Assert.Equal(expected, options.ModelName);
        //=====================================================================
    }
}
