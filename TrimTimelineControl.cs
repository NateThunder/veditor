using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VeditorWindow;

public sealed class TrimTimelineControl : Control
{
    private enum DragMode
    {
        None,
        InHandle,
        OutHandle,
        Playhead
    }

    //== timeline palette =====================================================
    private static readonly Color SurfaceColor = Color.FromArgb(6, 10, 18);
    private static readonly Color TrackColor = Color.FromArgb(12, 18, 28);
    private static readonly Color TrackBorderColor = Color.FromArgb(33, 44, 60);
    private static readonly Color WaveformColor = Color.FromArgb(30, 38, 52);
    private static readonly Color SelectionFillColor = Color.FromArgb(28, 129, 186, 255);
    private static readonly Color SelectionBorderColor = Color.FromArgb(40, 187, 255);
    private static readonly Color HandleColor = Color.FromArgb(40, 187, 255);
    private static readonly Color HandleAccentColor = Color.FromArgb(186, 238, 255);
    private static readonly Color PlayheadColor = Color.FromArgb(248, 250, 255);
    private static readonly Color TickTextColor = Color.FromArgb(95, 111, 136);
    //=========================================================================

    private TimeSpan _duration = TimeSpan.FromSeconds(1);
    private TimeSpan _selectionStart = TimeSpan.Zero;
    private TimeSpan _selectionEnd = TimeSpan.FromSeconds(1);
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private DragMode _dragMode;
    private int _waveSeed;

    public TrimTimelineControl()
    {
        //== control defaults ==================================================
        BackColor = SurfaceColor;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        MinimumSize = new Size(0, 126);
        Size = new Size(640, 126);
        //=========================================================================
    }

    public event EventHandler<TrimSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TrimCurrentPositionChangedEventArgs>? CurrentPositionChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            var resolvedDuration = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : value;
            if (_duration == resolvedDuration)
            {
                return;
            }

            _duration = resolvedDuration;
            CoerceMarkersIntoDuration();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan SelectionStart
    {
        get => _selectionStart;
        set
        {
            var resolvedValue = ClampToDuration(value);
            if (_selectionStart == resolvedValue)
            {
                return;
            }

            _selectionStart = resolvedValue;
            CoerceMarkersIntoDuration();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            var resolvedValue = ClampToDuration(value);
            if (_selectionEnd == resolvedValue)
            {
                return;
            }

            _selectionEnd = resolvedValue;
            CoerceMarkersIntoDuration();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            var resolvedValue = ClampToDuration(value);
            if (_currentPosition == resolvedValue)
            {
                return;
            }

            _currentPosition = resolvedValue;
            Invalidate();
        }
    }

    public void SetWaveSeed(string? sourceIdentity)
    {
        _waveSeed = string.IsNullOrWhiteSpace(sourceIdentity)
            ? 0
            : sourceIdentity.GetHashCode(StringComparison.OrdinalIgnoreCase);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        //== drawing setup =====================================================
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var timelineBounds = GetTimelineBounds();
        if (timelineBounds.Width <= 0 || timelineBounds.Height <= 0)
        {
            return;
        }
        //=========================================================================

        //== timeline background ===============================================
        using (var path = CreateRoundedRectanglePath(timelineBounds, 14))
        using (var brush = new SolidBrush(TrackColor))
        using (var pen = new Pen(TrackBorderColor))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }
        //=========================================================================

        //== tick labels ========================================================
        DrawTickLabels(e.Graphics, timelineBounds);
        //=========================================================================

        //== waveform and selection ============================================
        DrawWaveform(e.Graphics, timelineBounds);
        DrawSelection(e.Graphics, timelineBounds);
        DrawPlayhead(e.Graphics, timelineBounds);
        //=========================================================================
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        //== drag start =========================================================
        Focus();
        Capture = true;

        var timelineBounds = GetTimelineBounds();
        var startX = GetXFromTime(_selectionStart, timelineBounds);
        var endX = GetXFromTime(_selectionEnd, timelineBounds);
        var playheadX = GetXFromTime(_currentPosition, timelineBounds);

        if (Math.Abs(e.X - startX) <= 10)
        {
            _dragMode = DragMode.InHandle;
        }
        else if (Math.Abs(e.X - endX) <= 10)
        {
            _dragMode = DragMode.OutHandle;
        }
        else if (Math.Abs(e.X - playheadX) <= 10)
        {
            _dragMode = DragMode.Playhead;
        }
        else
        {
            _dragMode = DragMode.Playhead;
            UpdateCurrentPosition(e.X, raiseEvent: true);
        }
        //=========================================================================
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragMode == DragMode.None)
        {
            return;
        }

        //== drag update ========================================================
        switch (_dragMode)
        {
            case DragMode.InHandle:
                UpdateSelectionStart(e.X);
                break;
            case DragMode.OutHandle:
                UpdateSelectionEnd(e.X);
                break;
            case DragMode.Playhead:
                UpdateCurrentPosition(e.X, raiseEvent: true);
                break;
        }
        //=========================================================================
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        //== drag cleanup =======================================================
        _dragMode = DragMode.None;
        Capture = false;
        //=========================================================================
    }

    private Rectangle GetTimelineBounds()
    {
        return new Rectangle(72, 36, Math.Max(Width - 104, 0), Math.Max(Height - 58, 0));
    }

    private void DrawTickLabels(Graphics graphics, Rectangle timelineBounds)
    {
        //== tick label layout ==================================================
        using var brush = new SolidBrush(TickTextColor);
        using var font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);

        const int tickCount = 10;
        for (var index = 0; index <= tickCount; index++)
        {
            var fraction = tickCount == 0 ? 0D : (double)index / tickCount;
            var x = timelineBounds.Left + (int)Math.Round(timelineBounds.Width * fraction);
            var seconds = Duration.TotalSeconds * fraction;
            var text = FormatTickLabel(TimeSpan.FromSeconds(seconds));
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush, x - (size.Width / 2F), 8F);
        }
        //=========================================================================
    }

    private void DrawWaveform(Graphics graphics, Rectangle timelineBounds)
    {
        //== waveform bars ======================================================
        using var brush = new SolidBrush(WaveformColor);

        const int barWidth = 5;
        const int barSpacing = 3;
        var barCount = Math.Max(timelineBounds.Width / (barWidth + barSpacing), 1);
        var midY = timelineBounds.Top + (timelineBounds.Height / 2F);

        for (var index = 0; index < barCount; index++)
        {
            var amplitude = GetAmplitude(index);
            var height = (int)Math.Round((timelineBounds.Height - 16) * amplitude);
            var x = timelineBounds.Left + (index * (barWidth + barSpacing));
            var y = (int)Math.Round(midY - (height / 2F));
            graphics.FillRectangle(brush, x, y, barWidth, Math.Max(height, 8));
        }
        //=========================================================================
    }

    private void DrawSelection(Graphics graphics, Rectangle timelineBounds)
    {
        //== selected range =====================================================
        var selectionLeft = GetXFromTime(_selectionStart, timelineBounds);
        var selectionRight = GetXFromTime(_selectionEnd, timelineBounds);
        var selectionWidth = Math.Max(selectionRight - selectionLeft, 4);
        var selectionBounds = new Rectangle(selectionLeft, timelineBounds.Top, selectionWidth, timelineBounds.Height);

        using (var path = CreateRoundedRectanglePath(selectionBounds, 12))
        using (var brush = new SolidBrush(SelectionFillColor))
        using (var pen = new Pen(SelectionBorderColor, 2F))
        {
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
        }

        using var handleBrush = new SolidBrush(HandleColor);
        using var handleAccentBrush = new SolidBrush(HandleAccentColor);

        DrawHandle(graphics, handleBrush, handleAccentBrush, selectionLeft, timelineBounds);
        DrawHandle(graphics, handleBrush, handleAccentBrush, selectionRight, timelineBounds);
        //=========================================================================
    }

    private static void DrawHandle(
        Graphics graphics,
        Brush handleBrush,
        Brush handleAccentBrush,
        int x,
        Rectangle timelineBounds)
    {
        var handleBounds = new Rectangle(x - 4, timelineBounds.Top - 2, 8, timelineBounds.Height + 4);
        graphics.FillRectangle(handleBrush, handleBounds);
        graphics.FillEllipse(handleAccentBrush, x - 6, timelineBounds.Top + (timelineBounds.Height / 2) - 6, 12, 12);
    }

    private void DrawPlayhead(Graphics graphics, Rectangle timelineBounds)
    {
        //== current position ===================================================
        var playheadX = GetXFromTime(_currentPosition, timelineBounds);
        using var pen = new Pen(PlayheadColor, 2F);
        using var brush = new SolidBrush(PlayheadColor);

        graphics.DrawLine(pen, playheadX, timelineBounds.Top - 8, playheadX, timelineBounds.Bottom + 10);
        graphics.FillEllipse(brush, playheadX - 5, timelineBounds.Top - 14, 10, 10);
        //=========================================================================
    }

    private void UpdateSelectionStart(int mouseX)
    {
        var timelineBounds = GetTimelineBounds();
        var proposedTime = GetTimeFromX(mouseX, timelineBounds);
        var maxStart = _selectionEnd - TimeSpan.FromMilliseconds(100);
        var clampedTime = proposedTime > maxStart ? maxStart : proposedTime;

        if (clampedTime < TimeSpan.Zero)
        {
            clampedTime = TimeSpan.Zero;
        }

        if (_selectionStart == clampedTime)
        {
            return;
        }

        _selectionStart = clampedTime;
        if (_currentPosition < _selectionStart)
        {
            _currentPosition = _selectionStart;
            CurrentPositionChanged?.Invoke(this, new TrimCurrentPositionChangedEventArgs(_currentPosition));
        }

        SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_selectionStart, _selectionEnd));
        Invalidate();
    }

    private void UpdateSelectionEnd(int mouseX)
    {
        var timelineBounds = GetTimelineBounds();
        var proposedTime = GetTimeFromX(mouseX, timelineBounds);
        var minEnd = _selectionStart + TimeSpan.FromMilliseconds(100);
        var clampedTime = proposedTime < minEnd ? minEnd : proposedTime;

        if (clampedTime > Duration)
        {
            clampedTime = Duration;
        }

        if (_selectionEnd == clampedTime)
        {
            return;
        }

        _selectionEnd = clampedTime;
        if (_currentPosition > _selectionEnd)
        {
            _currentPosition = _selectionEnd;
            CurrentPositionChanged?.Invoke(this, new TrimCurrentPositionChangedEventArgs(_currentPosition));
        }

        SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_selectionStart, _selectionEnd));
        Invalidate();
    }

    private void UpdateCurrentPosition(int mouseX, bool raiseEvent)
    {
        var timelineBounds = GetTimelineBounds();
        var proposedTime = GetTimeFromX(mouseX, timelineBounds);
        if (_currentPosition == proposedTime)
        {
            return;
        }

        _currentPosition = proposedTime;
        if (raiseEvent)
        {
            CurrentPositionChanged?.Invoke(this, new TrimCurrentPositionChangedEventArgs(_currentPosition));
        }

        Invalidate();
    }

    private void CoerceMarkersIntoDuration()
    {
        var minimumGap = TimeSpan.FromMilliseconds(100);

        if (_selectionStart < TimeSpan.Zero)
        {
            _selectionStart = TimeSpan.Zero;
        }

        if (_selectionEnd > Duration)
        {
            _selectionEnd = Duration;
        }

        if (_selectionEnd - _selectionStart < minimumGap)
        {
            _selectionEnd = _selectionStart + minimumGap;
            if (_selectionEnd > Duration)
            {
                _selectionEnd = Duration;
                _selectionStart = Duration - minimumGap;
            }
        }

        if (_selectionStart < TimeSpan.Zero)
        {
            _selectionStart = TimeSpan.Zero;
        }

        if (_currentPosition < TimeSpan.Zero)
        {
            _currentPosition = TimeSpan.Zero;
        }

        if (_currentPosition > Duration)
        {
            _currentPosition = Duration;
        }
    }

    private TimeSpan ClampToDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > Duration ? Duration : value;
    }

    private int GetXFromTime(TimeSpan value, Rectangle timelineBounds)
    {
        if (timelineBounds.Width <= 0 || Duration <= TimeSpan.Zero)
        {
            return timelineBounds.Left;
        }

        var fraction = Clamp(value.TotalSeconds / Duration.TotalSeconds, 0D, 1D);
        return timelineBounds.Left + (int)Math.Round(timelineBounds.Width * fraction);
    }

    private TimeSpan GetTimeFromX(int mouseX, Rectangle timelineBounds)
    {
        if (timelineBounds.Width <= 0)
        {
            return TimeSpan.Zero;
        }

        var fraction = Clamp((double)(mouseX - timelineBounds.Left) / timelineBounds.Width, 0D, 1D);
        return TimeSpan.FromSeconds(Duration.TotalSeconds * fraction);
    }

    private double GetAmplitude(int index)
    {
        var hash = HashCode.Combine(index, _waveSeed, index * 31, 17);
        hash = Math.Abs(hash);
        return 0.16 + ((hash % 84) / 100D);
    }

    private static string FormatTickLabel(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{value.Minutes}:{value.Seconds:00}";
        }

        return $"{Math.Round(value.TotalSeconds):0}s";
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class TrimSelectionChangedEventArgs : EventArgs
{
    public TrimSelectionChangedEventArgs(TimeSpan start, TimeSpan end)
    {
        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }
}

public sealed class TrimCurrentPositionChangedEventArgs : EventArgs
{
    public TrimCurrentPositionChangedEventArgs(TimeSpan position)
    {
        Position = position;
    }

    public TimeSpan Position { get; }
}
