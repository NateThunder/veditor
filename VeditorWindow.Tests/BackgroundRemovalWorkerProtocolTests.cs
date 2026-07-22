using VeditorWindow.Models;
using VeditorWindow.Services;

namespace VeditorWindow.Tests;

public sealed class BackgroundRemovalWorkerProtocolTests
{
    [Fact]
    public void Parse_RuntimeStatus_NormalizesHealthInformation()
    {
        //== act ===============================================================
        var message = BackgroundRemovalWorkerProtocol.Parse(
            "{\"type\":\"runtime_status\",\"installed\":true,\"installationMode\":\"CPU\",\"cudaAvailable\":false,\"pythonVersion\":\"3.12.7\",\"rembgVersion\":\"2.0.77\",\"missingComponents\":[]}");
        //=====================================================================

        //== assertions =======================================================
        Assert.NotNull(message);
        Assert.Equal(BackgroundRemovalWorkerMessageType.RuntimeStatus, message.Type);
        Assert.True(message.Installed);
        Assert.Equal("CPU", message.InstallationMode);
        Assert.Equal("2.0.77", message.RembgVersion);
        Assert.Empty(message.MissingComponents!);
        //=====================================================================
    }

    [Fact]
    public void Parse_Progress_ReadsPercentageAndMessage()
    {
        //== act ===============================================================
        var message = BackgroundRemovalWorkerProtocol.Parse(
            "{\"type\":\"progress\",\"percent\":42.5,\"message\":\"Separating subject\"}");
        //=====================================================================

        //== assertions =======================================================
        Assert.NotNull(message);
        Assert.Equal(BackgroundRemovalWorkerMessageType.Progress, message.Type);
        Assert.Equal(42.5D, message.Percent);
        Assert.Equal("Separating subject", message.Message);
        //=====================================================================
    }

    [Fact]
    public void Parse_MalformedJson_BecomesSafeLogMessage()
    {
        //== act ===============================================================
        var message = BackgroundRemovalWorkerProtocol.Parse("not json");
        //=====================================================================

        //== assertions =======================================================
        Assert.NotNull(message);
        Assert.Equal(BackgroundRemovalWorkerMessageType.Log, message.Type);
        Assert.Equal("not json", message.Message);
        //=====================================================================
    }
}
