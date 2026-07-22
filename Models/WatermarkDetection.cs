namespace VeditorWindow.Models;

public sealed record WatermarkDetection(
    WatermarkRegion Region,
    bool Accepted,
    string? Label = null);
