using System.ComponentModel;

namespace VeditorWindow.UI;

internal sealed class ClayScrollBar : Control
{
    private int _maximum;
    private int _value;
    private int _viewportSize = 1;
    private bool _dragging;
    private int _dragOffset;
    private bool _hoveringThumb;

    internal ClayScrollBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Width = 12;
        AccessibleRole = AccessibleRole.None;
    }

    [DefaultValue(0)]
    internal int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(0, value);
            Value = _value;
            Invalidate();
        }
    }

    [DefaultValue(0)]
    internal int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, 0, Maximum);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
        }
    }

    [DefaultValue(1)]
    internal int ViewportSize
    {
        get => _viewportSize;
        set
        {
            _viewportSize = Math.Max(1, value);
            Invalidate();
        }
    }

    internal event EventHandler? ValueChanged;

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End ||
               base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up:
                Value -= 36;
                break;
            case Keys.Down:
                Value += 36;
                break;
            case Keys.PageUp:
                Value -= ViewportSize;
                break;
            case Keys.PageDown:
                Value += ViewportSize;
                break;
            case Keys.Home:
                Value = 0;
                break;
            case Keys.End:
                Value = Maximum;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var thumbBounds = GetThumbBounds();
        if (thumbBounds.Contains(e.Location))
        {
            _dragging = true;
            _dragOffset = e.Y - thumbBounds.Top;
            Capture = true;
        }
        else
        {
            Value += e.Y < thumbBounds.Top ? -ViewportSize : ViewportSize;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hoveringThumb = GetThumbBounds().Contains(e.Location);
        if (_hoveringThumb != hoveringThumb)
        {
            _hoveringThumb = hoveringThumb;
            Invalidate();
        }

        if (!_dragging)
        {
            return;
        }

        var trackBounds = GetTrackBounds();
        var thumbBounds = GetThumbBounds();
        var travel = Math.Max(1, trackBounds.Height - thumbBounds.Height);
        var thumbTop = Math.Clamp(e.Y - _dragOffset, trackBounds.Top, trackBounds.Bottom - thumbBounds.Height);
        Value = (int)Math.Round((double)(thumbTop - trackBounds.Top) / travel * Maximum);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveringThumb = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        //== blended track and raised thumb ==================================
        var parentBackground = ResolveOpaqueParentBackground();
        e.Graphics.Clear(parentBackground);
        var trackBounds = GetTrackBounds();
        ClayDrawing.DrawSurface(
            e.Graphics,
            trackBounds,
            6,
            StudioTheme.SurfaceInput,
            StudioTheme.SurfaceMuted,
            inset: true);

        var thumbBounds = GetThumbBounds();
        var thumbTop = _dragging
            ? StudioTheme.Accent
            : _hoveringThumb
                ? StudioTheme.AccentHover
                : StudioTheme.AccentSoft;
        ClayDrawing.DrawSurface(
            e.Graphics,
            thumbBounds,
            6,
            thumbTop,
            _dragging ? StudioTheme.AccentPressed : StudioTheme.AccentDeep,
            inset: _dragging,
            emphasis: _hoveringThumb ? 1F : 0F);

        if (Focused && ShowFocusCues)
        {
            ClayDrawing.DrawFocusRing(e.Graphics, ClientRectangle, 6);
        }
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

        return StudioTheme.Surface;
        //=====================================================================
    }

    private Rectangle GetTrackBounds()
    {
        return new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
    }

    private Rectangle GetThumbBounds()
    {
        var trackBounds = GetTrackBounds();
        var totalRange = Maximum + ViewportSize;
        var thumbHeight = Maximum == 0
            ? trackBounds.Height
            : Math.Max(38, (int)Math.Round((double)ViewportSize / totalRange * trackBounds.Height));
        thumbHeight = Math.Min(trackBounds.Height, thumbHeight);
        var travel = Math.Max(0, trackBounds.Height - thumbHeight);
        var thumbTop = Maximum == 0
            ? trackBounds.Top
            : trackBounds.Top + (int)Math.Round((double)Value / Maximum * travel);
        return new Rectangle(trackBounds.Left, thumbTop, trackBounds.Width, thumbHeight);
    }
}
