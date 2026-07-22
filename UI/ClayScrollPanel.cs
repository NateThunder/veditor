namespace VeditorWindow.UI;

internal sealed class ClayScrollPanel : UserControl
{
    private const int KeyboardScrollStep = 36;

    private readonly Panel _nativeViewport;
    private readonly ClayScrollBar _styledScrollBar;
    private int _requestedVerticalOffset;
    private bool _layoutInProgress;
    private bool _nativeScrollSyncPending;
    private bool _restorePending;
    private bool _restoringScrollPosition;
    private bool _synchronizingScrollBar;

    internal ClayScrollPanel()
    {
        //== hybrid scroll composition ========================================
        AutoScaleMode = AutoScaleMode.None;
        BackColor = StudioTheme.Surface;
        DoubleBuffered = true;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);

        _nativeViewport = new Panel
        {
            AutoScroll = true,
            AutoScrollMargin = Size.Empty,
            BackColor = StudioTheme.Surface,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _styledScrollBar = new ClayScrollBar
        {
            AccessibleRole = AccessibleRole.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            TabStop = false,
            Visible = false,
            Width = SystemInformation.VerticalScrollBarWidth
        };

        Controls.Add(_nativeViewport);
        Controls.Add(_styledScrollBar);
        _styledScrollBar.BringToFront();

        _nativeViewport.Scroll += NativeViewport_Scroll;
        _nativeViewport.MouseWheel += (_, _) => QueueNativeScrollSynchronization();
        _nativeViewport.Layout += NativeViewport_Layout;
        _nativeViewport.SizeChanged += (_, _) => QueueScrollPositionRestore();
        _nativeViewport.ControlAdded += NativeViewport_ControlAdded;
        _styledScrollBar.ValueChanged += StyledScrollBar_ValueChanged;
        //=====================================================================
    }

    internal Control.ControlCollection ContentControls => _nativeViewport.Controls;

    internal bool UsesNativeScrolling => _nativeViewport.AutoScroll;

    internal bool IsHorizontalScrollVisible => _nativeViewport.HorizontalScroll.Visible;

    internal bool IsVerticalScrollVisible => _nativeViewport.VerticalScroll.Visible;

    internal bool IsStyledScrollBarVisible => _styledScrollBar.Visible;

    internal int VerticalOffset => GetVerticalOffset();

    internal int MaximumVerticalOffset => GetMaximumVerticalOffset();

    internal int StyledScrollOffset => _styledScrollBar.Value;

    internal void ResetScroll()
    {
        //== scroll position reset ============================================
        _requestedVerticalOffset = 0;
        SetVerticalOffset(0);
        //=====================================================================
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        //== overlay placement ================================================
        base.OnLayout(e);
        if (_nativeViewport is null || _styledScrollBar is null)
        {
            return;
        }

        var viewportBounds = _nativeViewport.Bounds;
        _styledScrollBar.SetBounds(
            viewportBounds.Right - SystemInformation.VerticalScrollBarWidth,
            viewportBounds.Top,
            SystemInformation.VerticalScrollBarWidth,
            viewportBounds.Height);
        _styledScrollBar.BringToFront();
        QueueScrollPositionRestore();
        //=====================================================================
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return ApplyKeyboardScroll(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    internal bool ApplyKeyboardScroll(Keys keyData)
    {
        //== keyboard scrolling ===============================================
        switch (keyData)
        {
            case Keys.Up:
                SetVerticalOffset(GetVerticalOffset() - KeyboardScrollStep);
                return true;
            case Keys.Down:
                SetVerticalOffset(GetVerticalOffset() + KeyboardScrollStep);
                return true;
            case Keys.PageUp:
                SetVerticalOffset(GetVerticalOffset() - _nativeViewport.ClientSize.Height);
                return true;
            case Keys.PageDown:
                SetVerticalOffset(GetVerticalOffset() + _nativeViewport.ClientSize.Height);
                return true;
            case Keys.Home:
                SetVerticalOffset(0);
                return true;
            case Keys.End:
                SetVerticalOffset(GetMaximumVerticalOffset());
                return true;
            default:
                return false;
        }
        //=====================================================================
    }

    internal void SetVerticalOffset(int requestedOffset)
    {
        if (_restoringScrollPosition || IsDisposed)
        {
            return;
        }

        //== scroll range normalization =======================================
        var normalizedOffset = Math.Clamp(requestedOffset, 0, GetMaximumVerticalOffset());
        _requestedVerticalOffset = normalizedOffset;

        if (GetVerticalOffset() != normalizedOffset)
        {
            _restoringScrollPosition = true;
            try
            {
                _nativeViewport.AutoScrollPosition = new Point(0, normalizedOffset);
            }
            finally
            {
                _restoringScrollPosition = false;
            }

            _nativeViewport.Invalidate(invalidateChildren: true);
        }

        SynchronizeStyledScrollBar();
        //=====================================================================
    }

    private void NativeViewport_ControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            e.Control.SizeChanged += (_, _) => QueueScrollPositionRestore();
        }

        QueueScrollPositionRestore();
    }

    private void NativeViewport_Layout(object? sender, LayoutEventArgs e)
    {
        //== native range recalculation =======================================
        _layoutInProgress = true;
        try
        {
            SynchronizeStyledScrollBar();
        }
        finally
        {
            _layoutInProgress = false;
        }

        QueueScrollPositionRestore();
        //=====================================================================
    }

    private void NativeViewport_Scroll(object? sender, ScrollEventArgs e)
    {
        QueueNativeScrollSynchronization();
    }

    private void StyledScrollBar_ValueChanged(object? sender, EventArgs e)
    {
        if (!_synchronizingScrollBar)
        {
            SetVerticalOffset(_styledScrollBar.Value);
        }
    }

    private int GetVerticalOffset()
    {
        return Math.Max(0, -_nativeViewport.AutoScrollPosition.Y);
    }

    private int GetMaximumVerticalOffset()
    {
        if (!_nativeViewport.VerticalScroll.Visible)
        {
            return 0;
        }

        return Math.Max(
            0,
            _nativeViewport.VerticalScroll.Maximum - _nativeViewport.VerticalScroll.LargeChange + 1);
    }

    private void QueueScrollPositionRestore()
    {
        if (_restorePending || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        //== deferred native range synchronization ============================
        _restorePending = true;
        BeginInvoke(() =>
        {
            _restorePending = false;
            if (!IsDisposed)
            {
                SetVerticalOffset(_requestedVerticalOffset);
            }
        });
        //=====================================================================
    }

    private void QueueNativeScrollSynchronization()
    {
        if (_nativeScrollSyncPending || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        //== deferred native position read ====================================
        _nativeScrollSyncPending = true;
        BeginInvoke(() =>
        {
            _nativeScrollSyncPending = false;
            if (IsDisposed)
            {
                return;
            }

            var nativeOffset = GetVerticalOffset();
            if (!_layoutInProgress && !_restorePending && !_restoringScrollPosition)
            {
                _requestedVerticalOffset = nativeOffset;
            }

            SynchronizeStyledScrollBar(nativeOffset);
        });
        //=====================================================================
    }

    private void SynchronizeStyledScrollBar(int? reportedOffset = null)
    {
        if (_synchronizingScrollBar || IsDisposed)
        {
            return;
        }

        //== styled scrollbar synchronization ================================
        _synchronizingScrollBar = true;
        try
        {
            var maximumOffset = GetMaximumVerticalOffset();
            _styledScrollBar.ViewportSize = Math.Max(1, _nativeViewport.ClientSize.Height);
            _styledScrollBar.Maximum = maximumOffset;
            _styledScrollBar.Value = Math.Clamp(reportedOffset ?? GetVerticalOffset(), 0, maximumOffset);
            _styledScrollBar.Visible = maximumOffset > 0;
            if (_styledScrollBar.Visible)
            {
                _styledScrollBar.BringToFront();
            }
        }
        finally
        {
            _synchronizingScrollBar = false;
        }
        //=====================================================================
    }
}
