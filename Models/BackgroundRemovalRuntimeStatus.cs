namespace VeditorWindow.Models;

public sealed record BackgroundRemovalRuntimeStatus(
    bool IsInstalled,
    string? InstallationMode,
    bool CudaAvailable,
    string? GpuName,
    string? PythonVersion,
    string? RembgVersion,
    IReadOnlyList<string> MissingComponents,
    string? ErrorMessage);
