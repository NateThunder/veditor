namespace VeditorWindow.Models;

public sealed record BackgroundRemovalInstallerProgress(
    double Percent,
    string Stage,
    string Message,
    long? BytesReceived,
    long? BytesTotal);
