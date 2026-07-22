using VeditorWindow.Models;
using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class WatermarkRuntimeStatusTextTests
{
    [Theory]
    [InlineData(false, true, true, true, true, true, false, false, "not installed")]
    [InlineData(true, false, true, true, true, true, false, false, "worker is missing")]
    [InlineData(true, true, false, true, true, true, false, false, "packages are missing")]
    [InlineData(true, true, true, false, true, true, false, false, "Florence-2 detection model is missing")]
    [InlineData(true, true, true, true, false, true, false, false, "LaMA cleanup model is missing")]
    [InlineData(true, true, true, true, true, false, false, false, "FFmpeg is unavailable")]
    [InlineData(true, true, true, true, true, true, true, false, "needs an update")]
    [InlineData(true, true, true, true, true, true, false, true, "record is damaged")]
    public void GetPrimaryMessage_ExplainsRuntimeState(
        bool python,
        bool worker,
        bool dependencies,
        bool florence,
        bool lama,
        bool ffmpeg,
        bool outdated,
        bool invalidMarker,
        string expectedText)
    {
        var status = new WatermarkRuntimeStatus(
            false,
            python,
            worker,
            dependencies,
            ffmpeg,
            false,
            null,
            MarkerExists: true,
            MarkerValid: !outdated && !invalidMarker,
            MarkerOutdated: outdated,
            FlorenceModelAvailable: florence,
            LamaModelAvailable: lama,
            InstallationMode: "CPU");

        var message = WatermarkRuntimeStatusText.GetPrimaryMessage(status);

        Assert.Contains(expectedText, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPrimaryMessage_ReportsReadyCudaGpu()
    {
        var status = new WatermarkRuntimeStatus(
            true, true, true, true, true, true, null,
            MarkerExists: true,
            MarkerValid: true,
            FlorenceModelAvailable: true,
            LamaModelAvailable: true,
            InstallationMode: "CUDA",
            GpuName: "Test GPU");

        Assert.Equal(
            "WatermarkAI is ready with CUDA acceleration on Test GPU.",
            WatermarkRuntimeStatusText.GetPrimaryMessage(status));
    }
}
