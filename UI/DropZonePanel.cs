using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VeditorWindow.UI;

internal sealed class DropZonePanel : Panel
{
    internal DropZonePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsDragActive { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        //== recessed drop workspace =========================================
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        ClayDrawing.DrawSurface(
            e.Graphics,
            bounds,
            28,
            IsDragActive ? Color.FromArgb(57, 48, 111) : StudioTheme.StageSurface,
            StudioTheme.StageBackground,
            inset: true,
            emphasis: IsDragActive ? 1F : 0F);

        var haloSize = Math.Max(120, Math.Min(bounds.Width, bounds.Height) / 2);
        var haloBounds = new Rectangle(
            bounds.Left + ((bounds.Width - haloSize) / 2),
            bounds.Top + ((bounds.Height - haloSize) / 2) - 28,
            haloSize,
            haloSize);
        using var haloPath = new GraphicsPath();
        haloPath.AddEllipse(haloBounds);
        using var haloBrush = new PathGradientBrush(haloPath)
        {
            CenterColor = Color.FromArgb(IsDragActive ? 58 : 34, StudioTheme.Accent),
            SurroundColors = [Color.FromArgb(0, StudioTheme.Accent)]
        };
        e.Graphics.FillEllipse(haloBrush, haloBounds);
        //=====================================================================
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(ClientRectangle, -2, -2);
        using var path = CreateRoundedPath(bounds, 28);
        using var border = new Pen(
            IsDragActive ? StudioTheme.AccentBright : Color.FromArgb(124, 142, 119, 206),
            IsDragActive ? 2.2F : 1.45F)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = [4F, 3F]
        };
        e.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
