using VeditorWindow.UI;

namespace VeditorWindow.Tests;

public sealed class StudioLayoutPolicyTests
{
    [Theory]
    [InlineData(1260, (int)StudioLayoutMode.Compact)]
    [InlineData(1379, (int)StudioLayoutMode.Compact)]
    [InlineData(1380, (int)StudioLayoutMode.Wide)]
    [InlineData(1520, (int)StudioLayoutMode.Wide)]
    public void Resolve_UsesDocumentedBreakpoint(int width, int expected)
    {
        Assert.Equal((StudioLayoutMode)expected, StudioLayoutPolicy.Resolve(width));
    }

    [Fact]
    public void Defaults_MatchReferenceConversionState()
    {
        Assert.Equal("mp4", StudioDefaults.VideoExtension);
        Assert.Equal("h264", StudioDefaults.VideoCodec);
        Assert.Equal(3, StudioDefaults.QualityTrackValue);
        Assert.Equal(78, StudioDefaults.QualityPercent);
    }
}
