using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VeditorWindow.UI;

internal sealed class PurpleSlider : Control, ISupportInitialize
{
    private int _minimum;
    private int _maximum = 4;
    private int _value;
    private bool _dragging;
    private bool _hoveringThumb;

    internal PurpleSlider()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = true;
        Height = 30;
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "Video quality";
    }

    [DefaultValue(0)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = Math.Min(value, _maximum);
            Value = _value;
        }
    }

    [DefaultValue(4)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = _value;
        }
    }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, _minimum, _maximum);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange { get; set; } = 1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TickFrequency { get; set; } = 1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TickStyle TickStyle { get; set; } = TickStyle.None;

    public event EventHandler? ValueChanged;

    public event EventHandler? InteractionCompleted;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDragging => _dragging;

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Left or Keys.Down)
        {
            Value--;
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Right or Keys.Up)
        {
            Value++;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Home)
        {
            Value = Minimum;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.End)
        {
            Value = Maximum;
            e.Handled = true;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragging = true;
        Capture = true;
        UpdateValueFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            UpdateValueFromX(e.X);
            return;
        }

        var hoveringThumb = GetThumbBounds().Contains(e.Location);
        if (_hoveringThumb != hoveringThumb)
        {
            _hoveringThumb = hoveringThumb;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
        InteractionCompleted?.Invoke(this, EventArgs.Empty);
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

        //== output shaping ===================================================
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackBounds = new Rectangle(8, Math.Max(4, Height / 2 - 5), Math.Max(1, Width - 16), 10);
        var ratio = Maximum == Minimum ? 0D : (double)(Value - Minimum) / (Maximum - Minimum);
        var thumbCenterX = trackBounds.Left + (int)Math.Round(trackBounds.Width * ratio);

        using var trackPath = CreateRoundedPath(trackBounds, 5);
        using var trackBrush = new LinearGradientBrush(
            trackBounds,
            Enabled ? StudioTheme.WindowBackgroundDeep : StudioTheme.SurfaceMuted,
            Enabled ? StudioTheme.SurfaceInput : StudioTheme.WindowBackgroundDeep,
            LinearGradientMode.Vertical);
        e.Graphics.FillPath(trackBrush, trackPath);
        using var trackEdge = new Pen(StudioTheme.ClayShadowStrong, 1.4F);
        e.Graphics.DrawPath(trackEdge, trackPath);

        var fillBounds = new Rectangle(trackBounds.Left + 2, trackBounds.Top + 2, Math.Max(1, thumbCenterX - trackBounds.Left), trackBounds.Height - 4);
        using var fillPath = CreateRoundedPath(fillBounds, 3);
        using var fillBrush = StudioTheme.CreateAccentBrush(fillBounds);
        e.Graphics.FillPath(fillBrush, fillPath);

        var thumbSize = _hoveringThumb && !_dragging ? 20 : 18;
        var thumbBounds = new Rectangle(thumbCenterX - (thumbSize / 2), (Height - thumbSize) / 2, thumbSize, thumbSize);
        var thumbShadowBounds = thumbBounds;
        thumbShadowBounds.Offset(2, 3);
        using var thumbShadow = new SolidBrush(StudioTheme.ClayShadow);
        e.Graphics.FillEllipse(thumbShadow, thumbShadowBounds);
        using var thumbBrush = new SolidBrush(
            !Enabled
                ? StudioTheme.TextMuted
                : _dragging
                    ? StudioTheme.AccentPressed
                    : Color.FromArgb(221, 215, 247));
        using var thumbBorder = new Pen(_hoveringThumb ? StudioTheme.AccentBright : StudioTheme.Border, 1.5F);
        e.Graphics.FillEllipse(thumbBrush, thumbBounds);
        e.Graphics.DrawEllipse(thumbBorder, thumbBounds);

        if (Focused && ShowFocusCues)
        {
            ClayDrawing.DrawFocusRing(e.Graphics, ClientRectangle, 10);
        }
        //=====================================================================
    }

    private void UpdateValueFromX(int x)
    {
        var usableWidth = Math.Max(1, Width - 16);
        var ratio = Math.Clamp((double)(x - 8) / usableWidth, 0D, 1D);
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio, MidpointRounding.AwayFromZero);
    }

    private Rectangle GetThumbBounds()
    {
        var trackBounds = new Rectangle(8, Math.Max(4, Height / 2 - 5), Math.Max(1, Width - 16), 10);
        var ratio = Maximum == Minimum ? 0D : (double)(Value - Minimum) / (Maximum - Minimum);
        var thumbCenterX = trackBounds.Left + (int)Math.Round(trackBounds.Width * ratio);
        return new Rectangle(thumbCenterX - 11, (Height - 22) / 2, 22, 22);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
