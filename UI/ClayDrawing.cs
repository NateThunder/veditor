using System.Drawing.Drawing2D;

namespace VeditorWindow.UI;

internal static class ClayDrawing
{
    //== matte surface rendering =============================================
    internal static void DrawSurface(
        Graphics graphics,
        Rectangle bounds,
        int radius,
        Color topColor,
        Color bottomColor,
        bool inset,
        float emphasis = 0F,
        bool drawInnerHighlight = true)
    {
        if (bounds.Width <= 4 || bounds.Height <= 4)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var resolvedRadius = Math.Max(4, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        var surfaceBounds = Rectangle.Inflate(bounds, -4, -4);
        if (!inset)
        {
            surfaceBounds.Offset(0, emphasis > 0.5F ? -1 : 0);
        }

        using var surfacePath = CreateRoundedPath(surfaceBounds, resolvedRadius);

        if (inset)
        {
            using var recessPath = CreateRoundedPath(Rectangle.Inflate(surfaceBounds, 2, 2), resolvedRadius + 2);
            using var recessBrush = new SolidBrush(StudioTheme.ClayShadowStrong);
            graphics.FillPath(recessBrush, recessPath);
        }
        else
        {
            var shadowBounds = surfaceBounds;
            shadowBounds.Offset(3, 4);
            using var shadowPath = CreateRoundedPath(shadowBounds, resolvedRadius);
            using var shadowBrush = new SolidBrush(StudioTheme.ClayShadow);
            graphics.FillPath(shadowBrush, shadowPath);

            var softShadowBounds = surfaceBounds;
            softShadowBounds.Offset(1, 2);
            using var softShadowPath = CreateRoundedPath(softShadowBounds, resolvedRadius);
            using var softShadowBrush = new SolidBrush(Color.FromArgb(54, StudioTheme.ClayShadowStrong));
            graphics.FillPath(softShadowBrush, softShadowPath);
        }

        using var surfaceBrush = new LinearGradientBrush(
            surfaceBounds,
            topColor,
            bottomColor,
            LinearGradientMode.ForwardDiagonal);
        graphics.FillPath(surfaceBrush, surfacePath);

        var highlightAlpha = inset ? 28 : 48 + (int)Math.Round(emphasis * 18F);
        using var highlightPen = new Pen(Color.FromArgb(highlightAlpha, StudioTheme.ClayHighlight), 1.2F);
        graphics.DrawPath(highlightPen, surfacePath);

        //== optional inner edge =============================================
        if (drawInnerHighlight)
        {
            var innerBounds = Rectangle.Inflate(surfaceBounds, -1, -1);
            using var innerPath = CreateRoundedPath(innerBounds, Math.Max(3, resolvedRadius - 1));
            using var innerPen = new Pen(
                inset ? Color.FromArgb(66, StudioTheme.ClayShadowStrong) : StudioTheme.ClayInnerHighlight,
                inset ? 1.8F : 1F);
            graphics.DrawPath(innerPen, innerPath);
        }
        //=====================================================================
    }
    //=======================================================================

    //== focus rendering =====================================================
    internal static void DrawFocusRing(Graphics graphics, Rectangle bounds, int radius)
    {
        var outerBounds = Rectangle.Inflate(bounds, -1, -1);
        using var outerPath = CreateRoundedPath(outerBounds, radius + 2);
        using var outerPen = new Pen(StudioTheme.FocusSeparation, 3F);
        graphics.DrawPath(outerPen, outerPath);

        var innerBounds = Rectangle.Inflate(bounds, -3, -3);
        using var innerPath = CreateRoundedPath(innerBounds, radius);
        using var innerPen = new Pen(StudioTheme.Focus, 1.8F);
        graphics.DrawPath(innerPen, innerPath);
    }
    //=======================================================================

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Color Blend(Color from, Color to, float amount)
    {
        var normalized = Math.Clamp(amount, 0F, 1F);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * normalized)),
            (int)Math.Round(from.R + ((to.R - from.R) * normalized)),
            (int)Math.Round(from.G + ((to.G - from.G) * normalized)),
            (int)Math.Round(from.B + ((to.B - from.B) * normalized)));
    }
}
