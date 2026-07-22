namespace VeditorWindow.Models;

public enum WatermarkWorkerMessageType
{
    Log,
    Status,
    Progress,
    Completed,
    Error,
    RuntimeStatus,
    Preview
}

public sealed record WatermarkWorkerMessage(
    WatermarkWorkerMessageType Type,
    string? Stage = null,
    double? Percent = null,
    string? Message = null,
    string? OutputPath = null,
    bool? UsedGpu = null,
    string? Code = null,
    bool? DependenciesAvailable = null,
    bool? CudaAvailable = null,
    string? RawLine = null,
    IReadOnlyList<WatermarkDetection>? Detections = null,
    int? SourceFrame = null,
    int? SourceWidth = null,
    int? SourceHeight = null,
    bool? NoRegionDetected = null,
    bool? Installed = null,
    bool? MarkerExists = null,
    bool? MarkerValid = null,
    bool? MarkerOutdated = null,
    bool? FlorenceModelAvailable = null,
    bool? LamaModelAvailable = null,
    bool? FfmpegAvailable = null,
    string? InstallationMode = null,
    string? GpuName = null,
    string? PythonVersion = null,
    IReadOnlyList<string>? MissingComponents = null)
{
    public static WatermarkWorkerMessage FromLog(string line)
    {
        return new WatermarkWorkerMessage(
            WatermarkWorkerMessageType.Log,
            Message: line,
            RawLine: line);
    }
}
