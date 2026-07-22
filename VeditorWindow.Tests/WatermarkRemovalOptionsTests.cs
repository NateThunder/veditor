using VeditorWindow.Models;

namespace VeditorWindow.Tests;

public sealed class WatermarkRemovalOptionsTests
{
    [Fact]
    public void Defaults_AreSafeAndValid()
    {
        //== arrange and act ===================================================
        var options = new WatermarkRemovalOptions();
        var errors = options.Validate();
        //=====================================================================

        //== assertions =======================================================
        Assert.Equal("watermark", options.DetectionPrompt);
        Assert.Equal(10D, options.MaxBoundingBoxPercent);
        Assert.Equal(3, options.DetectionSkip);
        Assert.Equal(0.25D, options.FadeInSeconds);
        Assert.Equal(0.25D, options.FadeOutSeconds);
        Assert.True(options.UseGpuWhenAvailable);
        Assert.False(options.PreviewOnly);
        Assert.False(options.SelectionPreviewOnly);
        Assert.Equal(0.5D, options.MaskPaddingPercent);
        Assert.Empty(options.Regions);
        Assert.False(options.Overwrite);
        Assert.Empty(errors);
        //=====================================================================
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Validate_RejectsDetectionIntervalOutsideRange(int detectionSkip)
    {
        var options = new WatermarkRemovalOptions { DetectionSkip = detectionSkip };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("between 1 and 10", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0D)]
    [InlineData(-0.1D)]
    [InlineData(100.1D)]
    public void Validate_RejectsDetectionSizeOutsideRange(double maximumPercent)
    {
        var options = new WatermarkRemovalOptions { MaxBoundingBoxPercent = maximumPercent };

        var errors = options.Validate();

        Assert.Contains(errors, error => error.Contains("no more than 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsNonFiniteNumbersAndBlankPrompt()
    {
        var options = new WatermarkRemovalOptions
        {
            DetectionPrompt = "   ",
            MaxBoundingBoxPercent = double.NaN,
            FadeInSeconds = double.PositiveInfinity,
            FadeOutSeconds = -0.01D
        };

        var errors = options.Validate();

        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Validate_RejectsInvalidRegionPaddingAndFrame()
    {
        var options = new WatermarkRemovalOptions
        {
            PreviewFrameIndex = -1,
            MaskPaddingPercent = 10.1D,
            Regions = [new WatermarkRegion(0.9D, 0.9D, 0.2D, 0.2D)]
        };

        var errors = options.Validate();

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, error => error.Contains("frame index", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("padding", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("rectangle", StringComparison.OrdinalIgnoreCase));
    }
}
