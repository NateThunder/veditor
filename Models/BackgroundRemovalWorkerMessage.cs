namespace VeditorWindow.Models;

public enum BackgroundRemovalWorkerMessageType
{
    Log,
    Status,
    Progress,
    Completed,
    RuntimeStatus,
    Error
}

public sealed record BackgroundRemovalWorkerMessage(
    BackgroundRemovalWorkerMessageType Type,
    string? Message = null,
    double? Percent = null,
    string? OutputPath = null,
    bool? Installed = null,
    string? InstallationMode = null,
    bool? CudaAvailable = null,
    string? GpuName = null,
    string? PythonVersion = null,
    string? RembgVersion = null,
    IReadOnlyList<string>? MissingComponents = null,
    string? RawLine = null);
