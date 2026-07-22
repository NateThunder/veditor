using System.ComponentModel;
using System.Drawing.Imaging;

namespace VeditorWindow.UI;

internal enum ClayButtonVariant
{
    Secondary,
    Primary,
    Selector,
    Navigation,
    Icon,
    Caption,
    Danger,
    Quiet
}

internal sealed class ClayButton : Button
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private ClayButtonVariant _variant = ClayButtonVariant.Secondary;
    private bool _selected;
    private bool _hovered;
    private bool _pressed;
    private float _hoverProgress;

    internal ClayButton()
    {
        //== control defaults =================================================
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        ForeColor = StudioTheme.TextPrimary;
        Cursor = Cursors.Hand;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        FlatAppearance.BorderSize = 0;
        //=====================================================================

        _animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    [DefaultValue(ClayButtonVariant.Secondary)]
    internal ClayButtonVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    internal bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            AccessibleDescription = value ? "Selected" : null;
            Invalidate();
        }
    }

    [DefaultValue(13)]
    internal int CornerRadius { get; set; } = 13;

    [DefaultValue(22)]
    internal int IconSize { get; set; } = 22;

    [DefaultValue(120)]
    internal int CompactIconOnlyThreshold { get; set; } = 120;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        StartInteractionTransition();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        StartInteractionTransition();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        //== matte button surface ============================================
        base.OnPaintBackground(pevent);
        var drawSurface = Variant != ClayButtonVariant.Caption || _hovered || _pressed;
        var inset = _pressed || Selected;
        var palette = ResolvePalette();

        if (drawSurface)
        {
            ClayDrawing.DrawSurface(
                pevent.Graphics,
                ClientRectangle,
                CornerRadius,
                palette.Top,
                palette.Bottom,
                inset,
                _hoverProgress);
        }

        DrawContent(pevent.Graphics, palette.Text);

        if (Focused && ShowFocusCues && Enabled)
        {
            ClayDrawing.DrawFocusRing(pevent.Graphics, ClientRectangle, CornerRadius);
        }
        //=====================================================================
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void StartInteractionTransition()
    {
        //== reduced motion ===================================================
        if (!SystemInformation.IsMenuAnimationEnabled)
        {
            _hoverProgress = _hovered ? 1F : 0F;
            Invalidate();
            return;
        }

        _animationTimer.Start();
        //=====================================================================
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        var target = _hovered ? 1F : 0F;
        var delta = target - _hoverProgress;
        if (Math.Abs(delta) < 0.08F)
        {
            _hoverProgress = target;
            _animationTimer.Stop();
        }
        else
        {
            _hoverProgress += Math.Sign(delta) * 0.09F;
        }

        Invalidate();
    }

    private (Color Top, Color Bottom, Color Text) ResolvePalette()
    {
        //== state palette ====================================================
        if (!Enabled)
        {
            return (
                StudioTheme.SurfaceMuted,
                StudioTheme.WindowBackgroundDeep,
                StudioTheme.TextDisabled);
        }

        if (Variant == ClayButtonVariant.Danger)
        {
            return (
                ClayDrawing.Blend(StudioTheme.Error, Color.White, _hoverProgress * 0.08F),
                Color.FromArgb(145, 45, 69),
                StudioTheme.TextPrimary);
        }

        var accentSurface = Variant == ClayButtonVariant.Primary || Selected;
        if (accentSurface)
        {
            return (
                ClayDrawing.Blend(StudioTheme.Accent, StudioTheme.AccentHover, _hoverProgress * 0.6F),
                Selected ? StudioTheme.AccentPressed : StudioTheme.AccentDeep,
                StudioTheme.TextPrimary);
        }

        if (Variant == ClayButtonVariant.Quiet)
        {
            return (
                ClayDrawing.Blend(StudioTheme.SurfaceMuted, StudioTheme.SurfaceRaised, _hoverProgress),
                StudioTheme.WindowBackgroundDeep,
                StudioTheme.TextSecondary);
        }

        return (
            ClayDrawing.Blend(StudioTheme.SurfaceRaised, StudioTheme.SurfaceElevated, _hoverProgress * 0.7F),
            ClayDrawing.Blend(StudioTheme.Surface, StudioTheme.SurfaceRaised, _hoverProgress * 0.45F),
            _hovered ? StudioTheme.TextPrimary : StudioTheme.TextSecondary);
        //=====================================================================
    }

    private void DrawContent(Graphics graphics, Color textColor)
    {
        var horizontalInset = Variant is ClayButtonVariant.Navigation or ClayButtonVariant.Selector ? 6 : 10;
        var contentBounds = Rectangle.Inflate(ClientRectangle, -horizontalInset, -8);
        if (_pressed || Selected)
        {
            contentBounds.Offset(0, 1);
        }
        else if (_hoverProgress > 0.55F)
        {
            contentBounds.Offset(0, -1);
        }

        var compactNavigation = Variant == ClayButtonVariant.Navigation && Width < CompactIconOnlyThreshold;
        var hasImage = Image is not null;
        var hasText = !compactNavigation && !string.IsNullOrWhiteSpace(Text);

        if (hasImage && hasText)
        {
            DrawImageAndText(graphics, contentBounds, textColor);
            return;
        }

        if (hasImage)
        {
            var iconBounds = CenterRectangle(contentBounds, IconSize, IconSize);
            DrawImage(graphics, Image!, iconBounds, Enabled ? 1F : 0.42F);
        }

        if (hasText)
        {
            var textBounds = contentBounds;
            if (Text.Contains(Environment.NewLine, StringComparison.Ordinal))
            {
                var measuredText = TextRenderer.MeasureText(
                    Text,
                    Font,
                    new Size(contentBounds.Width, int.MaxValue),
                    ResolveTextFlags());
                textBounds = new Rectangle(
                    contentBounds.Left,
                    contentBounds.Top + Math.Max(0, (contentBounds.Height - measuredText.Height) / 2),
                    contentBounds.Width,
                    Math.Min(contentBounds.Height, measuredText.Height));
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                textBounds,
                textColor,
                ResolveTextFlags());
        }
    }

    private void DrawImageAndText(Graphics graphics, Rectangle contentBounds, Color textColor)
    {
        var measured = TextRenderer.MeasureText(
            Text,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var spacing = Variant == ClayButtonVariant.Navigation ? 7 : 9;
        var groupWidth = IconSize + spacing + measured.Width;
        var groupLeft = Variant == ClayButtonVariant.Navigation || TextAlign == ContentAlignment.MiddleLeft
            ? contentBounds.Left + (Variant == ClayButtonVariant.Navigation ? 2 : 4)
            : contentBounds.Left + Math.Max(0, (contentBounds.Width - groupWidth) / 2);
        var iconBounds = new Rectangle(
            groupLeft,
            contentBounds.Top + Math.Max(0, (contentBounds.Height - IconSize) / 2),
            IconSize,
            IconSize);
        DrawImage(graphics, Image!, iconBounds, Enabled ? 1F : 0.42F);

        var textBounds = new Rectangle(
            iconBounds.Right + spacing,
            contentBounds.Top,
            Math.Max(1, contentBounds.Right - iconBounds.Right - spacing),
            contentBounds.Height);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private TextFormatFlags ResolveTextFlags()
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        flags |= Text.Contains(Environment.NewLine, StringComparison.Ordinal)
            ? TextFormatFlags.WordBreak
            : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        return flags;
    }

    private static Rectangle CenterRectangle(Rectangle bounds, int width, int height)
    {
        return new Rectangle(
            bounds.Left + Math.Max(0, (bounds.Width - width) / 2),
            bounds.Top + Math.Max(0, (bounds.Height - height) / 2),
            Math.Min(width, bounds.Width),
            Math.Min(height, bounds.Height));
    }

    private static void DrawImage(Graphics graphics, Image image, Rectangle bounds, float opacity)
    {
        using var attributes = new ImageAttributes();
        var colorMatrix = new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0F, 1F) };
        attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(
            image,
            bounds,
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
    }
}
