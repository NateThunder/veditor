using VeditorWindow.Models;

namespace VeditorWindow.Tests;

public sealed class WatermarkRegionTests
{
    [Fact]
    public void IsValid_AcceptsNormalizedRectangle()
    {
        Assert.True(new WatermarkRegion(0.1D, 0.2D, 0.3D, 0.4D).IsValid);
    }

    [Theory]
    [InlineData(-0.1D, 0D, 0.2D, 0.2D)]
    [InlineData(0D, 0D, 0D, 0.2D)]
    [InlineData(0.9D, 0D, 0.2D, 0.2D)]
    public void IsValid_RejectsRectangleOutsideBounds(double x, double y, double width, double height)
    {
        Assert.False(new WatermarkRegion(x, y, width, height).IsValid);
    }

    [Fact]
    public void Clamp_KeepsRectangleInsideBounds()
    {
        var clamped = new WatermarkRegion(-0.1D, 0.8D, 1.2D, 0.5D).Clamp();

        Assert.Equal(0D, clamped.X, precision: 8);
        Assert.Equal(0.8D, clamped.Y, precision: 8);
        Assert.Equal(1D, clamped.Width, precision: 8);
        Assert.Equal(0.2D, clamped.Height, precision: 8);
        Assert.True(clamped.IsValid);
    }
}
