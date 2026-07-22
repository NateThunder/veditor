namespace VeditorWindow.Models;

public sealed record WatermarkRuntimeStatus(
    bool IsInstalled,
    bool PythonExists,
    bool WorkerExists,
    bool DependenciesAvailable,
    bool FfmpegAvailable,
    bool CudaAvailable,
    string? ErrorMessage,
    bool MarkerExists = false,
    bool MarkerValid = false,
    bool MarkerOutdated = false,
    bool FlorenceModelAvailable = false,
    bool LamaModelAvailable = false,
    string? InstallationMode = null,
    string? GpuName = null,
    string? PythonVersion = null,
    IReadOnlyList<string>? MissingComponents = null);
