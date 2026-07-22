using System.Drawing.Drawing2D;
using VeditorWindow.Services;
using VeditorWindow.UI;

namespace VeditorWindow;

public partial class Form1
{
    private const int WindowStyleThickFrame = 0x00040000;
    private const int WindowStyleMinimizeBox = 0x00020000;
    private const int WindowStyleMaximizeBox = 0x00010000;
    private const int WindowStyleSystemMenu = 0x00080000;

    private TableLayoutPanel? _studioShellLayout;
    private TableLayoutPanel? _studioWorkspaceLayout;
    private Panel? _studioEditorHost;
    private Control? _studioSidebar;
    private Control? _studioNavigationRail;
    private Control? _integratedTitleBar;
    private Button? _titleMinimizeButton;
    private Button? _titleMaximizeButton;
    private Button? _titleCloseButton;
    private Panel? _workspaceStickyActionHost;
    private Panel? _compressionActionPanel;
    private DropZonePanel? _emptyPreviewPanel;
    private Label? _emptyPreviewMessageLabel;
    private StudioLayoutMode _studioLayoutMode = StudioLayoutMode.Wide;
    private bool _windowWasMinimized;

    private Control BuildIntegratedTitleBar()
    {
        //== integrated window chrome =========================================
        var titleBar = new Panel
        {
            BackColor = StudioTheme.WindowBackground,
            Dock = DockStyle.Fill,
            Height = StudioTheme.TitleBarHeight,
            Margin = Padding.Empty,
            Padding = new Padding(14, 8, 4, 8)
        };
        _integratedTitleBar = titleBar;

        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 5,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));

        var icon = new PictureBox
        {
            AccessibleName = "VeditorWindow",
            Dock = DockStyle.Fill,
            Image = LoadUiAsset("veditor-brand-matte.png"),
            Margin = new Padding(0, 10, 8, 10),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };

        var title = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StudioTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "VeditorWindow",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _titleMinimizeButton = CreateCaptionButton("\u2014", "Minimize", () => WindowState = FormWindowState.Minimized);
        _titleMaximizeButton = CreateCaptionButton("\u25A1", "Maximize", ToggleMaximizeRestore);
        _titleCloseButton = CreateCaptionButton("\u00D7", "Close", Close, isCloseButton: true);

        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(title, 1, 0);
        layout.Controls.Add(_titleMinimizeButton, 2, 0);
        layout.Controls.Add(_titleMaximizeButton, 3, 0);
        layout.Controls.Add(_titleCloseButton, 4, 0);
        titleBar.Controls.Add(layout);

        //== title-bar movement fallback =====================================
        // Preserve the custom appearance while ensuring that nested controls
        // still initiate native movement if client hit testing reaches them.
        MouseEventHandler beginWindowDrag = (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                WindowChromeController.BeginWindowDrag(this);
            }
        };
        titleBar.MouseDown += beginWindowDrag;
        layout.MouseDown += beginWindowDrag;
        icon.MouseDown += beginWindowDrag;
        title.MouseDown += beginWindowDrag;
        //=====================================================================

        return titleBar;
        //=====================================================================
    }

    private static Button CreateCaptionButton(string glyph, string accessibleName, Action action, bool isCloseButton = false)
    {
        var button = new ClayButton
        {
            AccessibleName = accessibleName,
            BackColor = Color.Transparent,
            CornerRadius = 9,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.None,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Symbol", 11F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StudioTheme.TextSecondary,
            Margin = new Padding(4, 0, 4, 0),
            Size = new Size(50, 44),
            TabStop = false,
            Text = glyph,
            UseVisualStyleBackColor = false,
            Variant = isCloseButton ? ClayButtonVariant.Danger : ClayButtonVariant.Caption
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = isCloseButton
            ? Color.FromArgb(196, 53, 78)
            : Color.FromArgb(48, 56, 96);
        button.FlatAppearance.MouseDownBackColor = isCloseButton
            ? Color.FromArgb(164, 43, 66)
            : Color.FromArgb(59, 67, 112);
        button.Click += (_, _) => action();
        return button;
    }

    private Panel CreateEditorHost(Control editorArea, Control sidebar)
    {
        var host = new Panel
        {
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        host.Controls.Add(editorArea);

        _studioEditorHost = host;
        _studioSidebar = sidebar;
        return host;
    }

    private void InitializeAdaptiveShell()
    {
        //== responsive shell =================================================
        ClientSizeChanged -= StudioShell_ClientSizeChanged;
        ClientSizeChanged += StudioShell_ClientSizeChanged;
        ApplyResponsiveLayout(force: true);
        //=====================================================================
    }

    private void StudioShell_ClientSizeChanged(object? sender, EventArgs e)
    {
        ApplyResponsiveLayout(force: false);
        UpdateMaximizeGlyph();
    }

    private void ApplyResponsiveLayout(bool force)
    {
        if (_studioWorkspaceLayout is null ||
            _studioEditorHost is null ||
            _studioSidebar is null ||
            _studioNavigationRail is null)
        {
            return;
        }

        // WinForms exposes the autoscaled client width in layout units here.
        // Applying an additional DPI division would collapse a 1520-wide shell
        // at 125% scaling even though it still has the intended logical width.
        var logicalWidth = ClientSize.Width;
        var nextMode = StudioLayoutPolicy.Resolve(logicalWidth);
        if (!force && nextMode == _studioLayoutMode)
        {
            return;
        }

        _studioLayoutMode = nextMode;
        if (nextMode == StudioLayoutMode.Compact)
        {
            //== state transition: wide -> compact ============================
            _studioWorkspaceLayout.ColumnStyles[0].Width = StudioTheme.NavigationCompactWidth;
            _studioWorkspaceLayout.ColumnStyles[2].Width = 330F;
            ReparentControl(_studioSidebar, _studioWorkspaceLayout);
            _studioWorkspaceLayout.SetCellPosition(_studioSidebar, new TableLayoutPanelCellPosition(2, 0));
            _studioSidebar.Dock = DockStyle.Fill;
            _studioSidebar.Margin = new Padding(12, 0, 0, 0);
            if (_previewMetaLabel is not null)
            {
                _previewMetaLabel.Visible = false;
            }
            btnPreviewLast.Visible = true;
            lblPreviewPath.MinimumSize = Size.Empty;
            //=================================================================
        }
        else
        {
            //== state transition: compact -> wide ============================
            _studioWorkspaceLayout.ColumnStyles[0].Width = StudioTheme.NavigationWideWidth;
            _studioWorkspaceLayout.ColumnStyles[2].Width = StudioTheme.InspectorWidth;
            ReparentControl(_studioSidebar, _studioWorkspaceLayout);
            _studioWorkspaceLayout.SetCellPosition(_studioSidebar, new TableLayoutPanelCellPosition(2, 0));
            _studioSidebar.Dock = DockStyle.Fill;
            _studioSidebar.Margin = new Padding(16, 0, 0, 0);
            if (_previewMetaLabel is not null)
            {
                _previewMetaLabel.Visible = true;
            }
            btnPreviewLast.Visible = true;
            lblPreviewPath.MinimumSize = new Size(180, 0);
            //=================================================================
        }

        foreach (var button in _workspacePageButtons.Values)
        {
            button.Width = nextMode == StudioLayoutMode.Compact
                ? StudioTheme.NavigationCompactWidth - 16
                : StudioTheme.NavigationWideWidth - 20;
            button.Invalidate();
        }

        _studioWorkspaceLayout.PerformLayout();
    }

    private static void ReparentControl(Control control, Control targetParent)
    {
        if (control.Parent == targetParent)
        {
            return;
        }

        control.Parent?.Controls.Remove(control);
        targetParent.Controls.Add(control);
    }

    private DropZonePanel BuildEmptyPreviewState()
    {
        //== empty media state ================================================
        var dropZone = new DropZonePanel
        {
            AccessibleName = "Open media",
            AllowDrop = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(32)
        };
        _emptyPreviewPanel = dropZone;

        var content = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 6
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var artwork = new PictureBox
        {
            AccessibleName = "Media clapperboard illustration",
            Anchor = AnchorStyles.None,
            Image = LoadUiAsset("media-clapperboard-matte.png"),
            Margin = Padding.Empty,
            Size = new Size(190, 190),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };

        var title = new Label
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StudioTheme.TextPrimary,
            Margin = new Padding(0, 10, 0, 0),
            Text = "Drag & drop a file here",
            UseMnemonic = false
        };

        _emptyPreviewMessageLabel = new Label
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StudioTheme.TextSecondary,
            Margin = new Padding(0, 8, 0, 0),
            Text = $"or click Open to browse\r\n{MediaDropValidator.SupportedFormatHint}",
            TextAlign = ContentAlignment.MiddleCenter
        };

        var openButton = new ClayButton
        {
            AccessibleName = "Open media file",
            Anchor = AnchorStyles.None,
            AutoSize = false,
            BackColor = StudioTheme.Accent,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = StudioTheme.TextPrimary,
            Margin = new Padding(0, 18, 0, 0),
            Size = new Size(168, 48),
            Text = "Open File",
            UseVisualStyleBackColor = false,
            Variant = ClayButtonVariant.Primary
        };
        AssignMatteIcon(openButton, "open-folder", 22);
        openButton.FlatAppearance.BorderSize = 0;
        openButton.Click += (_, _) => btnOpenMediaFile.PerformClick();
        BindRoundedRegionToSize(openButton, 13);

        content.Controls.Add(new Panel { BackColor = Color.Transparent, Dock = DockStyle.Fill }, 0, 0);
        content.Controls.Add(artwork, 0, 1);
        content.Controls.Add(title, 0, 2);
        content.Controls.Add(_emptyPreviewMessageLabel, 0, 3);
        content.Controls.Add(openButton, 0, 4);
        content.Controls.Add(new Panel { BackColor = Color.Transparent, Dock = DockStyle.Fill }, 0, 5);
        dropZone.Controls.Add(content);

        EnableMediaDrop(dropZone);
        return dropZone;
        //=====================================================================
    }

    private void EnableMediaDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += Studio_DragEnter;
        control.DragLeave += Studio_DragLeave;
        control.DragDrop += Studio_DragDrop;

        foreach (Control child in control.Controls)
        {
            EnableMediaDrop(child);
        }
    }

    private void Studio_DragEnter(object? sender, DragEventArgs e)
    {
        if (_activeOperation != AppOperation.None)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (_currentWorkspacePage == WorkspacePage.Picture &&
            paths is { Length: > 0 } &&
            paths.All(path => File.Exists(path) && PictureCompressionService.IsSupported(path)))
        {
            e.Effect = DragDropEffects.Copy;
            if (_emptyPreviewPanel is not null)
            {
                _emptyPreviewPanel.IsDragActive = true;
                _emptyPreviewPanel.Invalidate();
            }
            return;
        }

        var result = MediaDropValidator.Validate(paths ?? []);
        e.Effect = result.IsValid ? DragDropEffects.Copy : DragDropEffects.None;
        if (_emptyPreviewPanel is not null)
        {
            _emptyPreviewPanel.IsDragActive = result.IsValid;
            _emptyPreviewPanel.Invalidate();
        }
    }

    private void Studio_DragLeave(object? sender, EventArgs e)
    {
        if (_emptyPreviewPanel is not null)
        {
            _emptyPreviewPanel.IsDragActive = false;
            _emptyPreviewPanel.Invalidate();
        }
    }

    private async void Studio_DragDrop(object? sender, DragEventArgs e)
    {
        //== input validation =================================================
        Studio_DragLeave(sender, EventArgs.Empty);
        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (_currentWorkspacePage == WorkspacePage.Picture &&
            paths is { Length: > 0 } &&
            paths.All(path => File.Exists(path) && PictureCompressionService.IsSupported(path)))
        {
            AddPictures(paths);
            await GenerateSelectedPicturePreviewAsync();
            return;
        }

        var result = MediaDropValidator.Validate(paths ?? []);
        if (!result.IsValid || string.IsNullOrWhiteSpace(result.MediaPath))
        {
            ShowEmptyPreviewState(result.Message, isError: true);
            return;
        }
        //=====================================================================

        await OpenMediaPathAsync(result.MediaPath);
    }

    private async Task OpenMediaPathAsync(string mediaPath)
    {
        SetCurrentMediaSource(mediaPath);
        if (await EnsurePreviewReadyAsync())
        {
            await LoadPreviewAsync(mediaPath, switchToPreview: true);
        }
        else
        {
            FocusPreviewStage();
        }
    }

    private void ShowEmptyPreviewState(string? message = null, bool isError = false)
    {
        if (_emptyPreviewPanel is null || _emptyPreviewMessageLabel is null)
        {
            return;
        }

        _emptyPreviewMessageLabel.Text = string.IsNullOrWhiteSpace(message)
            ? $"or click Open to browse\r\n{MediaDropValidator.SupportedFormatHint}"
            : message;
        _emptyPreviewMessageLabel.ForeColor = isError ? StudioTheme.Error : StudioTheme.TextSecondary;
        lblPreviewState.Visible = false;
        webPreview.Visible = false;
        _emptyPreviewPanel.Visible = true;
        _emptyPreviewPanel.BringToFront();
    }

    private void HideEmptyPreviewState()
    {
        if (_emptyPreviewPanel is not null)
        {
            _emptyPreviewPanel.Visible = false;
        }
    }

    private static Image? LoadUiAsset(string fileName)
    {
        try
        {
            var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            using var source = Image.FromFile(assetPath);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static void AssignMatteIcon(Button button, string iconName, int iconSize = 20)
    {
        //== generated icon assignment =======================================
        button.Image = LoadUiAsset(Path.Combine("MatteIcons", $"{iconName}.png"));
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        if (button is ClayButton clayButton)
        {
            clayButton.IconSize = iconSize;
        }
        //=====================================================================
    }

    private static Image? LoadUiAssetScaled(string fileName, Size size)
    {
        using var source = LoadUiAsset(fileName);
        return source is null ? null : new Bitmap(source, size);
    }

    private static void DrawGradientActionButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button || button.Width <= 0 || button.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(button.ClientRectangle, -1, -1);
        using var path = CreateRoundedRectanglePath(bounds, 13);
        if (button.Enabled)
        {
            using var brush = StudioTheme.CreateAccentBrush(bounds);
            e.Graphics.FillPath(brush, path);
        }
        else
        {
            using var disabledBrush = new SolidBrush(StudioTheme.Border);
            e.Graphics.FillPath(disabledBrush, path);
        }
        TextRenderer.DrawText(
            e.Graphics,
            button.Text,
            button.Font,
            bounds,
            button.Enabled ? StudioTheme.TextPrimary : StudioTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (_titleMaximizeButton is not null)
        {
            _titleMaximizeButton.Text = WindowState == FormWindowState.Maximized ? "\u2750" : "\u25A1";
            _titleMaximizeButton.AccessibleName = WindowState == FormWindowState.Maximized ? "Restore" : "Maximize";
        }
    }

    private Rectangle GetCaptionButtonBounds(Control? button)
    {
        if (button is null || !button.IsHandleCreated)
        {
            return Rectangle.Empty;
        }

        var screenBounds = button.RectangleToScreen(button.ClientRectangle);
        var clientPoint = PointToClient(screenBounds.Location);
        return new Rectangle(clientPoint, screenBounds.Size);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            //== native window capabilities ==================================
            // Keep the custom-painted frame, but advertise the standard
            // resizable window capabilities required by Windows Snap.
            var parameters = base.CreateParams;
            parameters.Style |= WindowStyleThickFrame |
                                WindowStyleMinimizeBox |
                                WindowStyleMaximizeBox |
                                WindowStyleSystemMenu;
            return parameters;
            //=================================================================
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChromeController.ApplyDwmAttributes(this);
    }

    protected override void OnActivated(EventArgs e)
    {
        //== activation repaint ==============================================
        base.OnActivated(e);
        RefreshStudioFrame();
        //=====================================================================
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = StudioTheme.CreateWindowBrush(ClientRectangle);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WindowChromeController.WmSize)
        {
            //== minimize and restore repaint ================================
            var isMinimized = m.WParam.ToInt32() == WindowChromeController.SizeMinimized;
            var isRestoring = _windowWasMinimized && !isMinimized;

            base.WndProc(ref m);
            _windowWasMinimized = isMinimized;

            if (isRestoring)
            {
                PerformLayout();
                Invalidate(invalidateChildren: true);
                Update();
                WindowChromeController.FlushComposedFrame();
            }

            return;
            //=================================================================
        }

        if (m.Msg == WindowChromeController.WmGetMinMaxInfo)
        {
            //== maximized working area =======================================
            base.WndProc(ref m);
            WindowChromeController.ApplyMaximizedWorkingArea(this, m.LParam);
            return;
            //=================================================================
        }

        if (m.Msg == WindowChromeController.WmNcHitTest)
        {
            base.WndProc(ref m);
            var screenPoint = WindowChromeController.GetPointFromMessageLParam(m.LParam);
            var clientPoint = PointToClient(screenPoint);
            var hitTarget = WindowChromeController.CalculateHitTarget(
                ClientSize,
                clientPoint,
                Math.Max(6, DeviceDpi / 16),
                StudioTheme.TitleBarHeight,
                GetCaptionButtonBounds(_titleMinimizeButton),
                GetCaptionButtonBounds(_titleMaximizeButton),
                GetCaptionButtonBounds(_titleCloseButton),
                WindowState == FormWindowState.Maximized);
            m.Result = (IntPtr)(int)hitTarget;
            return;
        }

        if (m.Msg == WindowChromeController.WmNcLButtonUp)
        {
            var target = (WindowChromeHitTarget)m.WParam.ToInt32();
            if (target == WindowChromeHitTarget.Close)
            {
                Close();
                return;
            }

            if (target == WindowChromeHitTarget.Minimize)
            {
                WindowState = FormWindowState.Minimized;
                return;
            }

            if (target == WindowChromeHitTarget.Maximize)
            {
                ToggleMaximizeRestore();
                return;
            }
        }

        if (m.Msg == WindowChromeController.WmNcRButtonUp &&
            (WindowChromeHitTarget)m.WParam.ToInt32() == WindowChromeHitTarget.Caption)
        {
            WindowChromeController.ShowSystemMenu(this, WindowChromeController.GetPointFromMessageLParam(m.LParam));
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Space))
        {
            WindowChromeController.ShowSystemMenu(this, PointToScreen(new Point(8, StudioTheme.TitleBarHeight)));
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
