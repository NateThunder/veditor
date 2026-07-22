using VeditorWindow.Models;
using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class WatermarkWorkerProtocolTests
{
    [Fact]
    public void Parse_ParsesStatusAndIgnoresUnknownFields()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"status","stage":"loading_models","message":"Loading Florence-2","futureField":42}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Status, message.Type);
        Assert.Equal("loading_models", message.Stage);
        Assert.Equal("Loading Florence-2", message.Message);
    }

    [Theory]
    [InlineData(-12D, 0D)]
    [InlineData(20.5D, 20.5D)]
    [InlineData(165D, 100D)]
    public void Parse_ClampsProgressPercent(double input, double expected)
    {
        var message = WatermarkWorkerProtocol.Parse(
            $$"""{"type":"progress","stage":"detection","percent":{{input.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Progress, message.Type);
        Assert.Equal(expected, message.Percent);
    }

    [Fact]
    public void Parse_AcceptsNumericStringPercentCaseInsensitively()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"TYPE":"PROGRESS","PERCENT":"65.25"}""");

        Assert.NotNull(message);
        Assert.Equal(65.25D, message.Percent);
    }

    [Fact]
    public void Parse_ParsesCompletedOutput()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"completed","outputPath":"C:\\Videos\\clip_watermark_removed.mp4","usedGpu":true}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Completed, message.Type);
        Assert.Equal(@"C:\Videos\clip_watermark_removed.mp4", message.OutputPath);
        Assert.True(message.UsedGpu);
    }

    [Fact]
    public void Parse_ParsesErrorCodeAndMessage()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"error","code":"MODEL_LOAD_FAILED","message":"Unable to load LaMA"}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Error, message.Type);
        Assert.Equal("MODEL_LOAD_FAILED", message.Code);
        Assert.Equal("Unable to load LaMA", message.Message);
    }

    [Fact]
    public void Parse_ReturnsMalformedJsonAsPlainLog()
    {
        const string malformed = "{not-json";

        var message = WatermarkWorkerProtocol.Parse(malformed);

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Log, message.Type);
        Assert.Equal(malformed, message.Message);
    }

    [Fact]
    public void Parse_ReturnsUnknownMessageTypeAsPlainLog()
    {
        const string unknown = """{"type":"future_message","value":42}""";

        var message = WatermarkWorkerProtocol.Parse(unknown);

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Log, message.Type);
        Assert.Equal(unknown, message.Message);
    }

    [Fact]
    public void Parse_IgnoresBlankLines()
    {
        Assert.Null(WatermarkWorkerProtocol.Parse("   "));
    }

    [Fact]
    public void Parse_ParsesPreviewDetectionsAsNormalizedRegions()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"preview","previewPath":"C:\\Temp\\preview.png","sourceFrame":50,"sourceWidth":1000,"sourceHeight":500,"noRegionDetected":false,"detections":[{"bbox":[100,50,300,150],"accepted":true,"label":"logo"},{"bbox":[0,0,900,500],"accepted":false}]}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.Preview, message.Type);
        Assert.Equal(50, message.SourceFrame);
        Assert.False(message.NoRegionDetected);
        Assert.Equal(2, message.Detections?.Count);
        Assert.Equal(0.1D, message.Detections![0].Region.X, precision: 8);
        Assert.Equal(0.1D, message.Detections[0].Region.Y, precision: 8);
        Assert.Equal(0.2D, message.Detections[0].Region.Width, precision: 8);
        Assert.Equal(0.2D, message.Detections[0].Region.Height, precision: 8);
        Assert.True(message.Detections[0].Accepted);
        Assert.False(message.Detections[1].Accepted);
    }

    [Fact]
    public void Parse_ParsesDetailedRuntimeStatus()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"runtime_status","installed":false,"dependenciesAvailable":true,"cudaAvailable":false,"ffmpegAvailable":true,"florenceModelAvailable":false,"lamaModelAvailable":true,"markerExists":true,"markerValid":false,"markerOutdated":true,"installationMode":"CPU","pythonVersion":"3.12.7","missingComponents":["outdated installation marker","Florence-2 model"]}""");

        Assert.NotNull(message);
        Assert.Equal(WatermarkWorkerMessageType.RuntimeStatus, message.Type);
        Assert.False(message.Installed);
        Assert.True(message.DependenciesAvailable);
        Assert.True(message.MarkerOutdated);
        Assert.False(message.FlorenceModelAvailable);
        Assert.Equal("CPU", message.InstallationMode);
        Assert.Equal(2, message.MissingComponents?.Count);
    }

    [Fact]
    public void Parse_SkipsMalformedPreviewBoundingBoxes()
    {
        var message = WatermarkWorkerProtocol.Parse(
            """{"type":"preview","sourceWidth":1920,"sourceHeight":1080,"detections":[{"bbox":[1,2,3]}]}""");

        Assert.NotNull(message);
        Assert.Empty(message.Detections!);
        Assert.True(message.NoRegionDetected);
    }
}
