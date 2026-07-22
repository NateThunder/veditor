namespace VeditorWindow.Models;

public sealed record WatermarkRemovalResult(
    bool Success,
    string? OutputPath,
    int? ExitCode,
    string? ErrorMessage,
    bool UsedGpu,
    TimeSpan Duration,
    bool WasCancelled,
    IReadOnlyList<WatermarkDetection>? Detections = null,
    int? SourceFrame = null,
    int? SourceWidth = null,
    int? SourceHeight = null,
    bool NoRegionDetected = false);
