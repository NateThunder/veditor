namespace VeditorWindow.Models;

public sealed record BackgroundRemovalResult(
    bool Success,
    bool Cancelled,
    string? OutputPath,
    string? ErrorMessage,
    TimeSpan Duration);
