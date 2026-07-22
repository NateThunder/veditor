using System.ComponentModel;

namespace VeditorWindow.UI;

internal enum ClaySurfaceKind
{
    Main,
    Raised,
    Elevated,
    Muted,
    Inset,
    Stage,
    Status
}

internal sealed class ClayPanel : Panel
{
    private ClaySurfaceKind _surfaceKind = ClaySurfaceKind.Main;
    private int _cornerRadius = StudioTheme.SurfaceRadius;
    private bool _showInnerHighlight = true;

    internal ClayPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = StudioTheme.Surface;
    }

    [DefaultValue(ClaySurfaceKind.Main)]
    internal ClaySurfaceKind SurfaceKind
    {
        get => _surfaceKind;
        set
        {
            _surfaceKind = value;
            BackColor = ResolveColors().Top;
            Invalidate();
        }
    }

    [DefaultValue(StudioTheme.SurfaceRadius)]
    internal int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(4, value);
            Invalidate();
        }
    }

    [DefaultValue(true)]
    internal bool ShowInnerHighlight
    {
        get => _showInnerHighlight;
        set
        {
            _showInnerHighlight = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color? SurfaceTopColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color? SurfaceBottomColor { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        //== matte panel surface ==============================================
        e.Graphics.Clear(ResolveOpaqueParentBackground());
        var colors = ResolveColors();
        ClayDrawing.DrawSurface(
            e.Graphics,
            ClientRectangle,
            CornerRadius,
            colors.Top,
            colors.Bottom,
            SurfaceKind == ClaySurfaceKind.Inset,
            drawInnerHighlight: ShowInnerHighlight);
        //=====================================================================
    }

    private Color ResolveOpaqueParentBackground()
    {
        //== transparent parent resolution ====================================
        for (Control? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.BackColor.A > 0)
            {
                return ancestor.BackColor;
            }
        }

        return StudioTheme.WindowBackground;
        //=====================================================================
    }

    private (Color Top, Color Bottom) ResolveColors()
    {
        if (SurfaceTopColor.HasValue || SurfaceBottomColor.HasValue)
        {
            return (
                SurfaceTopColor ?? StudioTheme.Surface,
                SurfaceBottomColor ?? SurfaceTopColor ?? StudioTheme.SurfaceMuted);
        }

        return SurfaceKind switch
        {
            ClaySurfaceKind.Raised => (StudioTheme.SurfaceRaised, StudioTheme.Surface),
            ClaySurfaceKind.Elevated => (StudioTheme.SurfaceElevated, StudioTheme.SurfaceRaised),
            ClaySurfaceKind.Muted => (StudioTheme.SurfaceMuted, StudioTheme.WindowBackgroundDeep),
            ClaySurfaceKind.Inset => (StudioTheme.SurfaceInput, StudioTheme.WindowBackgroundDeep),
            ClaySurfaceKind.Stage => (StudioTheme.StageSurface, StudioTheme.StageBackground),
            ClaySurfaceKind.Status => (StudioTheme.StatusSurface, StudioTheme.SurfaceMuted),
            _ => (StudioTheme.Surface, StudioTheme.SurfaceMuted)
        };
    }
}
