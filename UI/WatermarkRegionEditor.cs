using System.Drawing.Drawing2D;
using System.ComponentModel;
using VeditorWindow.Models;

namespace VeditorWindow.UI;

public sealed class WatermarkRegionEditor : Control
{
    private const float MinimumRegionSize = 0.003F;
    private const float HandleSize = 10F;

    private enum DragMode
    {
        None,
        Create,
        Move,
        ResizeNorthWest,
        ResizeNorthEast,
        ResizeSouthWest,
        ResizeSouthEast
    }

    private sealed class EditableRegion
    {
        public required RectangleF Bounds { get; set; }

        public bool Included { get; set; }
    }

    private readonly List<EditableRegion> _regions = [];
    private Bitmap? _image;
    private int _selectedIndex = -1;
    private DragMode _dragMode;
    private PointF _dragStart;
    private RectangleF _dragStartBounds;
    private double _maskPaddingPercent = 0.5D;

    public WatermarkRegionEditor()
    {
        //== control configuration ===========================================
        DoubleBuffered = true;
        BackColor = Color.FromArgb(15, 23, 42);
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        //=====================================================================
    }

    public event EventHandler? SelectionChanged;

    public int RegionCount => _regions.Count(region => region.Included);

    public bool HasSelectedRegion => _selectedIndex >= 0 && _selectedIndex < _regions.Count;

    public bool SelectedRegionIncluded => HasSelectedRegion && _regions[_selectedIndex].Included;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double MaskPaddingPercent
    {
        get => _maskPaddingPercent;
        set
        {
            _maskPaddingPercent = Math.Clamp(value, 0D, 10D);
            Invalidate();
        }
    }

    public IReadOnlyList<WatermarkRegion> Regions => _regions
        .Where(region => region.Included)
        .Select(region => new WatermarkRegion(
            region.Bounds.X,
            region.Bounds.Y,
            region.Bounds.Width,
            region.Bounds.Height))
        .ToArray();

    public void SetImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        //== state changes ====================================================
        _image?.Dispose();
        _image = new Bitmap(image);
        Invalidate();
        //=====================================================================
    }

    public void SetDetections(IEnumerable<WatermarkDetection> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);

        //== state changes ====================================================
        _regions.Clear();
        foreach (var detection in detections)
        {
            if (!detection.Region.IsValid)
            {
                continue;
            }

            _regions.Add(new EditableRegion
            {
                Bounds = ToRectangle(detection.Region),
                Included = detection.Accepted
            });
        }

        _selectedIndex = _regions.Count > 0 ? 0 : -1;
        OnSelectionChanged();
        Invalidate();
        //=====================================================================
    }

    public void SetRegions(IEnumerable<WatermarkRegion> regions)
    {
        SetDetections(regions.Select(region => new WatermarkDetection(region, true)));
    }

    public void IncludeSelected()
    {
        if (!HasSelectedRegion)
        {
            return;
        }

        _regions[_selectedIndex].Included = true;
        OnSelectionChanged();
        Invalidate();
    }

    public void RemoveSelected()
    {
        if (!HasSelectedRegion)
        {
            return;
        }

        //== state changes ====================================================
        _regions.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Min(_selectedIndex, _regions.Count - 1);
        OnSelectionChanged();
        Invalidate();
        //=====================================================================
    }

    public void ClearRegions()
    {
        _regions.Clear();
        _selectedIndex = -1;
        OnSelectionChanged();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Delete or Keys.Back)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (_image is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        var normalizedPoint = ToNormalizedPoint(e.Location);
        if (normalizedPoint is null)
        {
            return;
        }

        //== input handling ===================================================
        _dragStart = normalizedPoint.Value;
        _dragMode = HitTestHandle(e.Location);
        if (_dragMode != DragMode.None)
        {
            _dragStartBounds = _regions[_selectedIndex].Bounds;
            return;
        }

        _selectedIndex = HitTestRegion(normalizedPoint.Value);
        if (_selectedIndex >= 0)
        {
            _dragMode = DragMode.Move;
            _dragStartBounds = _regions[_selectedIndex].Bounds;
        }
        else
        {
            _regions.Add(new EditableRegion
            {
                Bounds = new RectangleF(normalizedPoint.Value, SizeF.Empty),
                Included = true
            });
            _selectedIndex = _regions.Count - 1;
            _dragMode = DragMode.Create;
            _dragStartBounds = _regions[_selectedIndex].Bounds;
        }

        OnSelectionChanged();
        Invalidate();
        //=====================================================================
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMode == DragMode.None || !HasSelectedRegion)
        {
            Cursor = HitTestHandle(e.Location) == DragMode.None ? Cursors.Cross : Cursors.SizeNWSE;
            return;
        }

        var normalizedPoint = ToNormalizedPoint(e.Location, clamp: true);
        if (normalizedPoint is null)
        {
            return;
        }

        //== state changes ====================================================
        var current = normalizedPoint.Value;
        var updated = _dragMode switch
        {
            DragMode.Create => RectangleFromPoints(_dragStart, current),
            DragMode.Move => MoveRectangle(_dragStartBounds, current.X - _dragStart.X, current.Y - _dragStart.Y),
            DragMode.ResizeNorthWest => RectangleFromPoints(current, BottomRight(_dragStartBounds)),
            DragMode.ResizeNorthEast => RectangleFromPoints(new PointF(_dragStartBounds.Left, current.Y), new PointF(current.X, _dragStartBounds.Bottom)),
            DragMode.ResizeSouthWest => RectangleFromPoints(new PointF(current.X, _dragStartBounds.Top), new PointF(_dragStartBounds.Right, current.Y)),
            DragMode.ResizeSouthEast => RectangleFromPoints(new PointF(_dragStartBounds.Left, _dragStartBounds.Top), current),
            _ => _dragStartBounds
        };

        _regions[_selectedIndex].Bounds = ClampRectangle(updated);
        _regions[_selectedIndex].Included = true;
        OnSelectionChanged();
        Invalidate();
        //=====================================================================
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode == DragMode.None || !HasSelectedRegion)
        {
            return;
        }

        //== input validation =================================================
        var bounds = _regions[_selectedIndex].Bounds;
        if (bounds.Width < MinimumRegionSize || bounds.Height < MinimumRegionSize)
        {
            _regions.RemoveAt(_selectedIndex);
            _selectedIndex = Math.Min(_selectedIndex, _regions.Count - 1);
        }

        _dragMode = DragMode.None;
        OnSelectionChanged();
        Invalidate();
        //=====================================================================
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (_image is null)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "No preview frame is available.",
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        //== output shaping ===================================================
        var imageBounds = GetImageDisplayBounds();
        e.Graphics.DrawImage(_image, imageBounds);

        using var shadeBrush = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
        using var includedPen = new Pen(Color.FromArgb(50, 214, 146), 2F);
        using var excludedPen = new Pen(Color.FromArgb(248, 113, 113), 2F) { DashStyle = DashStyle.Dash };
        using var selectedPen = new Pen(Color.FromArgb(167, 139, 250), 3F);
        using var paddingPen = new Pen(Color.FromArgb(167, 139, 250), 1.4F) { DashStyle = DashStyle.Dot };
        using var handleBrush = new SolidBrush(Color.White);

        for (var index = 0; index < _regions.Count; index++)
        {
            var regionBounds = ToDisplayRectangle(_regions[index].Bounds, imageBounds);
            e.Graphics.FillRectangle(shadeBrush, regionBounds);
            e.Graphics.DrawRectangle(
                index == _selectedIndex ? selectedPen : _regions[index].Included ? includedPen : excludedPen,
                regionBounds.X,
                regionBounds.Y,
                regionBounds.Width,
                regionBounds.Height);

            if (index == _selectedIndex)
            {
                var paddingPixels = (float)(_maskPaddingPercent / 100D * Math.Min(imageBounds.Width, imageBounds.Height));
                var paddedBounds = RectangleF.Inflate(regionBounds, paddingPixels, paddingPixels);
                paddedBounds.Intersect(imageBounds);
                e.Graphics.DrawRectangle(
                    paddingPen,
                    paddedBounds.X,
                    paddedBounds.Y,
                    paddedBounds.Width,
                    paddedBounds.Height);

                foreach (var handle in GetHandleRectangles(regionBounds))
                {
                    e.Graphics.FillRectangle(handleBrush, handle);
                    e.Graphics.DrawRectangle(selectedPen, handle.X, handle.Y, handle.Width, handle.Height);
                }
            }
        }
        //=====================================================================
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private RectangleF GetImageDisplayBounds()
    {
        if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var scale = Math.Min(
            ClientSize.Width / (float)_image.Width,
            ClientSize.Height / (float)_image.Height);
        var width = _image.Width * scale;
        var height = _image.Height * scale;
        return new RectangleF(
            (ClientSize.Width - width) / 2F,
            (ClientSize.Height - height) / 2F,
            width,
            height);
    }

    private PointF? ToNormalizedPoint(Point point, bool clamp = false)
    {
        var imageBounds = GetImageDisplayBounds();
        if (imageBounds.Width <= 0F || imageBounds.Height <= 0F)
        {
            return null;
        }

        var x = (point.X - imageBounds.Left) / imageBounds.Width;
        var y = (point.Y - imageBounds.Top) / imageBounds.Height;
        if (!clamp && (x < 0F || x > 1F || y < 0F || y > 1F))
        {
            return null;
        }

        return new PointF(Math.Clamp(x, 0F, 1F), Math.Clamp(y, 0F, 1F));
    }

    private int HitTestRegion(PointF point)
    {
        for (var index = _regions.Count - 1; index >= 0; index--)
        {
            if (_regions[index].Bounds.Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private DragMode HitTestHandle(Point point)
    {
        if (!HasSelectedRegion)
        {
            return DragMode.None;
        }

        var displayBounds = ToDisplayRectangle(_regions[_selectedIndex].Bounds, GetImageDisplayBounds());
        var handles = GetHandleRectangles(displayBounds);
        DragMode[] modes =
        [
            DragMode.ResizeNorthWest,
            DragMode.ResizeNorthEast,
            DragMode.ResizeSouthWest,
            DragMode.ResizeSouthEast
        ];

        for (var index = 0; index < handles.Length; index++)
        {
            if (handles[index].Contains(point))
            {
                return modes[index];
            }
        }

        return DragMode.None;
    }

    private static RectangleF[] GetHandleRectangles(RectangleF bounds)
    {
        var offset = HandleSize / 2F;
        return
        [
            new RectangleF(bounds.Left - offset, bounds.Top - offset, HandleSize, HandleSize),
            new RectangleF(bounds.Right - offset, bounds.Top - offset, HandleSize, HandleSize),
            new RectangleF(bounds.Left - offset, bounds.Bottom - offset, HandleSize, HandleSize),
            new RectangleF(bounds.Right - offset, bounds.Bottom - offset, HandleSize, HandleSize)
        ];
    }

    private static RectangleF MoveRectangle(RectangleF bounds, float deltaX, float deltaY)
    {
        var x = Math.Clamp(bounds.X + deltaX, 0F, 1F - bounds.Width);
        var y = Math.Clamp(bounds.Y + deltaY, 0F, 1F - bounds.Height);
        return new RectangleF(x, y, bounds.Width, bounds.Height);
    }

    private static RectangleF RectangleFromPoints(PointF first, PointF second)
    {
        return new RectangleF(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
    }

    private static RectangleF ClampRectangle(RectangleF bounds)
    {
        var left = Math.Clamp(bounds.Left, 0F, 1F);
        var top = Math.Clamp(bounds.Top, 0F, 1F);
        var right = Math.Clamp(bounds.Right, left, 1F);
        var bottom = Math.Clamp(bounds.Bottom, top, 1F);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static PointF BottomRight(RectangleF bounds)
    {
        return new PointF(bounds.Right, bounds.Bottom);
    }

    private static RectangleF ToRectangle(WatermarkRegion region)
    {
        return new RectangleF(
            (float)region.X,
            (float)region.Y,
            (float)region.Width,
            (float)region.Height);
    }

    private static RectangleF ToDisplayRectangle(RectangleF normalizedBounds, RectangleF imageBounds)
    {
        return new RectangleF(
            imageBounds.Left + normalizedBounds.X * imageBounds.Width,
            imageBounds.Top + normalizedBounds.Y * imageBounds.Height,
            normalizedBounds.Width * imageBounds.Width,
            normalizedBounds.Height * imageBounds.Height);
    }
}
