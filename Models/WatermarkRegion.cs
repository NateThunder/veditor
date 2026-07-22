namespace VeditorWindow.Models;

public sealed record WatermarkRegion(
    double X,
    double Y,
    double Width,
    double Height)
{
    public bool IsValid =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height) &&
        X >= 0D &&
        Y >= 0D &&
        Width > 0D &&
        Height > 0D &&
        X + Width <= 1D + 0.000001D &&
        Y + Height <= 1D + 0.000001D;

    public WatermarkRegion Clamp()
    {
        //== normalization ====================================================
        var x = Math.Clamp(X, 0D, 1D);
        var y = Math.Clamp(Y, 0D, 1D);
        var width = Math.Clamp(Width, 0D, 1D - x);
        var height = Math.Clamp(Height, 0D, 1D - y);
        return new WatermarkRegion(x, y, width, height);
        //=====================================================================
    }
}
