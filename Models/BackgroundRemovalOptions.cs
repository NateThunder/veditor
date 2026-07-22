namespace VeditorWindow.Models;

public enum BackgroundRemovalQuality
{
    Fast,
    Balanced,
    Best
}

public sealed record BackgroundRemovalOptions(
    BackgroundRemovalQuality Quality,
    bool RefineEdges)
{
    public string ModelName => Quality switch
    {
        BackgroundRemovalQuality.Fast => "u2netp",
        BackgroundRemovalQuality.Best => "birefnet-general",
        _ => "u2net"
    };
}
