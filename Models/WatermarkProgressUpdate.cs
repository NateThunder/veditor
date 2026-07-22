namespace VeditorWindow.Models;

public sealed record WatermarkProgressUpdate(
    string Stage,
    double? Percent,
    string? Message);
