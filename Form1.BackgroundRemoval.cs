using System.Diagnostics;
using VeditorWindow.Models;
using VeditorWindow.Services;
using VeditorWindow.UI;

namespace VeditorWindow;

public partial class Form1
{
    private readonly BackgroundRemovalService _backgroundRemovalService;
    private CancellationTokenSource? _backgroundRemovalCts;
    private CancellationTokenSource? _backgroundRuntimeCts;
    private Process? _backgroundInstallerProcess;
    private BackgroundRemovalRuntimeStatus? _backgroundRuntimeStatus;
    private bool _backgroundInstallationInProgress;
    private bool _backgroundInstallerProgressReceived;
    private string? _backgroundSourcePath;
    private string? _backgroundResultPath;
    private string? _lastSavedBackgroundPath;
    private Panel? _backgroundComparisonStage;
    private PictureBox? _backgroundOriginalPreview;
    private PictureBox? _backgroundRemovedPreview;
    private Label? _backgroundComparisonStatus;
    private Label? _backgroundFileLabel;
    private Label? _backgroundQualityLabel;
    private Label? _backgroundQualityHint;
    private Label? _backgroundRuntimeLabel;
    private PurpleSlider? _backgroundQualitySlider;
    private CheckBox? _backgroundRefineEdgesCheckBox;
    private Button? _backgroundChooseButton;
    private Button? _backgroundRemoveButton;
    private Button? _backgroundSaveButton;
    private Button? _backgroundOpenFolderButton;
    private Button? _backgroundInstallButton;
    private Button? _backgroundInstallCancelButton;

    private Panel BuildBackgroundRemovalPage()
    {
        //== background-removal workspace ====================================
        var section = CreateSectionPanel(Padding.Empty);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var title = CreateSectionTitle("Background remover");
        var subtitle = CreateSectionSubtitle("Create a transparent PNG from one picture, entirely on this device.");
        subtitle.Margin = new Padding(0, 7, 0, 16);

        _backgroundChooseButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 44,
            Text = "Choose picture"
        };
        StyleActionButton(_backgroundChooseButton, primary: false);
        _backgroundChooseButton.Click += btnChooseBackgroundPicture_Click;

        _backgroundFileLabel = CreateSectionSubtitle("No picture selected.");
        _backgroundFileLabel.AutoEllipsis = true;
        _backgroundFileLabel.AutoSize = false;
        _backgroundFileLabel.Height = 44;
        _backgroundFileLabel.Margin = new Padding(0, 10, 0, 0);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(_backgroundChooseButton, 0, 2);
        layout.Controls.Add(_backgroundFileLabel, 0, 3);
        layout.Controls.Add(BuildBackgroundQualityPanel(), 0, 4);
        layout.Controls.Add(BuildBackgroundRuntimePanel(), 0, 5);
        layout.Controls.Add(BuildBackgroundActionPanel(), 0, 6);
        section.Controls.Add(layout);
        return section;
        //=====================================================================
    }

    private Control BuildBackgroundQualityPanel()
    {
        //== quality controls =================================================
        var panel = CreateInsetPanel(new Padding(14));
        panel.Dock = DockStyle.Fill;
        panel.Margin = new Padding(0, 14, 0, 0);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _backgroundQualityLabel = CreateSectionTitle("Balanced");
        _backgroundQualityLabel.Font = new Font("Segoe UI Semibold", 10F);
        _backgroundQualitySlider = new PurpleSlider
        {
            AccessibleName = "Background removal quality",
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 2,
            Value = 1,
            Margin = new Padding(0, 10, 0, 0)
        };
        _backgroundQualitySlider.ValueChanged += BackgroundQualityChanged;

        var scale = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 0),
            RowCount = 1
        };
        scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        scale.Controls.Add(CreateBackgroundScaleLabel("FAST", ContentAlignment.MiddleLeft), 0, 0);
        scale.Controls.Add(CreateBackgroundScaleLabel("BALANCED", ContentAlignment.MiddleCenter), 1, 0);
        scale.Controls.Add(CreateBackgroundScaleLabel("BEST", ContentAlignment.MiddleRight), 2, 0);

        _backgroundQualityHint = CreateSectionSubtitle("General-purpose separation with a balanced model.");
        _backgroundQualityHint.AutoSize = true;
        _backgroundQualityHint.MaximumSize = new Size(StudioTheme.InspectorWidth - 76, 0);
        _backgroundQualityHint.Margin = new Padding(0, 9, 0, 0);

        _backgroundRefineEdgesCheckBox = new ClayCheckBox
        {
            AutoSize = true,
            Checked = false,
            Font = new Font("Segoe UI", 9F),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 12, 0, 0),
            Text = "Refine hair and soft edges"
        };
        _backgroundRefineEdgesCheckBox.CheckedChanged += (_, _) => InvalidateBackgroundResult();

        layout.Controls.Add(_backgroundQualityLabel, 0, 0);
        layout.Controls.Add(_backgroundQualitySlider, 0, 1);
        layout.Controls.Add(scale, 0, 2);
        layout.Controls.Add(_backgroundQualityHint, 0, 3);
        layout.Controls.Add(_backgroundRefineEdgesCheckBox, 0, 4);
        panel.Controls.Add(layout);
        return panel;
        //=====================================================================
    }

    private Control BuildBackgroundRuntimePanel()
    {
        //== runtime controls =================================================
        var panel = CreateInsetPanel(new Padding(14));
        panel.Dock = DockStyle.Fill;
        panel.Margin = new Padding(0, 14, 0, 0);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var heading = CreateSectionTitle("Local runtime");
        heading.Font = new Font("Segoe UI Semibold", 10F);
        _backgroundRuntimeLabel = CreateSectionSubtitle("Checking installation…");
        _backgroundRuntimeLabel.AutoSize = true;
        _backgroundRuntimeLabel.MaximumSize = new Size(StudioTheme.InspectorWidth - 76, 0);
        _backgroundRuntimeLabel.Margin = new Padding(0, 8, 0, 10);
        _backgroundInstallButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 42,
            Text = "Install or repair runtime"
        };
        StyleActionButton(_backgroundInstallButton, primary: false);
        _backgroundInstallButton.Click += btnInstallBackgroundRuntime_Click;

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(_backgroundRuntimeLabel, 0, 1);
        layout.Controls.Add(_backgroundInstallButton, 0, 2);
        panel.Controls.Add(layout);
        return panel;
        //=====================================================================
    }

    private Control BuildBackgroundActionPanel()
    {
        //== output actions ===================================================
        var panel = CreateSectionPanel(new Padding(0, 16, 0, 0));
        panel.Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _backgroundRemoveButton = CreateBackgroundActionButton("Remove background", primary: true, btnRemoveBackground_Click);
        _backgroundSaveButton = CreateBackgroundActionButton("Save transparent PNG", primary: false, btnSaveBackgroundResult_Click);
        _backgroundOpenFolderButton = CreateBackgroundActionButton("Open output folder", primary: false, btnOpenBackgroundFolder_Click);
        _backgroundSaveButton.Margin = new Padding(0, 10, 0, 0);
        _backgroundOpenFolderButton.Margin = new Padding(0, 10, 0, 0);

        layout.Controls.Add(_backgroundRemoveButton, 0, 0);
        layout.Controls.Add(_backgroundSaveButton, 0, 1);
        layout.Controls.Add(_backgroundOpenFolderButton, 0, 2);
        panel.Controls.Add(layout);
        return panel;
        //=====================================================================
    }

    private Control BuildBackgroundComparisonStage()
    {
        //== central comparison stage ========================================
        var panel = new Panel
        {
            AllowDrop = true,
            BackColor = StageBackgroundColor,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28)
        };
        panel.DragEnter += BackgroundStage_DragEnter;
        panel.DragDrop += BackgroundStage_DragDrop;
        _backgroundComparisonStage = panel;

        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var originalHeading = CreatePictureComparisonHeading("ORIGINAL");
        var removedHeading = CreatePictureComparisonHeading("BACKGROUND REMOVED");
        removedHeading.Margin = new Padding(14, 0, 0, 12);
        var originalHost = CreateBackgroundPreviewHost(out _backgroundOriginalPreview, checkerboard: false);
        var removedHost = CreateBackgroundPreviewHost(out _backgroundRemovedPreview, checkerboard: true);
        originalHost.Margin = new Padding(0, 0, 7, 0);
        removedHost.Margin = new Padding(7, 0, 0, 0);

        _backgroundComparisonStatus = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 16, 0, 0),
            Padding = new Padding(0, 2, 0, 2),
            Text = "Choose or drop one picture to begin.",
            TextAlign = ContentAlignment.MiddleCenter
        };

        layout.Controls.Add(originalHeading, 0, 0);
        layout.Controls.Add(removedHeading, 1, 0);
        layout.Controls.Add(originalHost, 0, 1);
        layout.Controls.Add(removedHost, 1, 1);
        layout.Controls.Add(_backgroundComparisonStatus, 0, 2);
        layout.SetColumnSpan(_backgroundComparisonStatus, 2);
        panel.Controls.Add(layout);
        return panel;
        //=====================================================================
    }

    private static Panel CreateBackgroundPreviewHost(out PictureBox pictureBox, bool checkerboard)
    {
        var host = new Panel
        {
            BackColor = StageSurfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(160, 220),
            Padding = new Padding(1)
        };
        if (checkerboard)
        {
            host.Paint += DrawTransparencyCheckerboard;
        }

        pictureBox = new PictureBox
        {
            AccessibleName = checkerboard ? "Background removed preview" : "Original picture preview",
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };
        host.Controls.Add(pictureBox);
        return host;
    }

    private static void DrawTransparencyCheckerboard(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        //== transparency visualization ======================================
        const int tile = 14;
        var light = Color.FromArgb(64, 68, 82);
        var dark = Color.FromArgb(43, 47, 60);
        using var lightBrush = new SolidBrush(light);
        using var darkBrush = new SolidBrush(dark);
        for (var y = 0; y < control.ClientSize.Height; y += tile)
        {
            for (var x = 0; x < control.ClientSize.Width; x += tile)
            {
                e.Graphics.FillRectangle(((x / tile + y / tile) & 1) == 0 ? lightBrush : darkBrush, x, y, tile, tile);
            }
        }
        //=====================================================================
    }

    private static Label CreateBackgroundScaleLabel(string text, ContentAlignment alignment) => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 7.5F),
        ForeColor = SecondaryTextColor,
        Height = 22,
        Text = text,
        TextAlign = alignment
    };

    private Button CreateBackgroundActionButton(string text, bool primary, EventHandler handler)
    {
        var button = new ClayButton { Dock = DockStyle.Top, Height = 46, Text = text };
        StyleActionButton(button, primary);
        button.Click += handler;
        return button;
    }

    private async void btnChooseBackgroundPicture_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff",
            Multiselect = false,
            Title = "Choose picture for background removal"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SelectBackgroundSource(dialog.FileName);
        }
        await Task.CompletedTask;
    }

    private void BackgroundStage_DragEnter(object? sender, DragEventArgs e)
    {
        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        e.Effect = paths is { Length: 1 } && File.Exists(paths[0]) && BackgroundRemovalService.IsSupportedPicture(paths[0])
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void BackgroundStage_DragDrop(object? sender, DragEventArgs e)
    {
        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (paths is { Length: 1 } && BackgroundRemovalService.IsSupportedPicture(paths[0]))
        {
            SelectBackgroundSource(paths[0]);
        }
        else if (_backgroundComparisonStatus is not null)
        {
            _backgroundComparisonStatus.Text = "Drop exactly one JPEG, PNG, WebP, BMP, or TIFF picture.";
        }
    }

    private void SelectBackgroundSource(string path)
    {
        //== input validation =================================================
        if (!File.Exists(path) || !BackgroundRemovalService.IsSupportedPicture(path))
        {
            MessageBox.Show(this, "Choose a supported JPEG, PNG, WebP, BMP, or TIFF picture.", "Unsupported Picture");
            return;
        }
        //=====================================================================

        //== state transition =================================================
        _backgroundRemovalCts?.Cancel();
        CleanupBackgroundResult();
        _backgroundSourcePath = Path.GetFullPath(path);
        _lastSavedBackgroundPath = null;
        SetPictureBoxImage(_backgroundOriginalPreview, _backgroundSourcePath);
        ClearPictureBox(_backgroundRemovedPreview);
        if (_backgroundFileLabel is not null)
        {
            _backgroundFileLabel.Text = Path.GetFileName(_backgroundSourcePath);
        }
        if (_backgroundComparisonStatus is not null)
        {
            _backgroundComparisonStatus.Text = "Ready to remove the background at full resolution.";
        }
        UpdateBackgroundControls();
        //=====================================================================
    }

    private void AdoptCurrentPictureForBackgroundRemoval()
    {
        if (!string.IsNullOrWhiteSpace(_currentPreviewFilePath) &&
            BackgroundRemovalService.IsSupportedPicture(_currentPreviewFilePath) &&
            !string.Equals(_backgroundSourcePath, _currentPreviewFilePath, StringComparison.OrdinalIgnoreCase))
        {
            SelectBackgroundSource(_currentPreviewFilePath);
        }
    }

    private void BackgroundQualityChanged(object? sender, EventArgs e)
    {
        if (_backgroundQualitySlider is null)
        {
            return;
        }

        var quality = GetBackgroundQuality();
        if (_backgroundQualityLabel is not null)
        {
            _backgroundQualityLabel.Text = quality switch
            {
                BackgroundRemovalQuality.Fast => "Fast",
                BackgroundRemovalQuality.Best => "Best quality",
                _ => "Balanced"
            };
        }
        if (_backgroundQualityHint is not null)
        {
            _backgroundQualityHint.Text = quality switch
            {
                BackgroundRemovalQuality.Fast => "Lightweight model for quick, simpler cut-outs.",
                BackgroundRemovalQuality.Best => "Highest-quality model for detailed subjects; processing takes longer.",
                _ => "General-purpose separation with a balanced model."
            };
        }
        if (_backgroundRefineEdgesCheckBox is not null)
        {
            _backgroundRefineEdgesCheckBox.Checked = quality == BackgroundRemovalQuality.Best;
        }
        InvalidateBackgroundResult();
    }

    private BackgroundRemovalQuality GetBackgroundQuality() => (_backgroundQualitySlider?.Value ?? 1) switch
    {
        0 => BackgroundRemovalQuality.Fast,
        2 => BackgroundRemovalQuality.Best,
        _ => BackgroundRemovalQuality.Balanced
    };

    private async void btnRemoveBackground_Click(object? sender, EventArgs e)
    {
        if (_activeOperation == AppOperation.RemoveBackground)
        {
            _backgroundRemovalCts?.Cancel();
            UpdateStatus("Canceling background removal…");
            return;
        }

        if (_activeOperation == AppOperation.None)
        {
            await RunBackgroundRemovalAsync();
        }
    }

    private async Task RunBackgroundRemovalAsync()
    {
        //== precondition checks =============================================
        if (string.IsNullOrWhiteSpace(_backgroundSourcePath) || !File.Exists(_backgroundSourcePath))
        {
            MessageBox.Show(this, "Choose one picture before removing its background.", "Picture Required");
            return;
        }

        await CheckBackgroundRuntimeAsync();
        if (_backgroundRuntimeStatus?.IsInstalled != true)
        {
            var install = MessageBox.Show(
                this,
                "The local Background Removal runtime is not installed. Install it now?",
                "Runtime Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (install != DialogResult.Yes || !await InstallBackgroundRuntimeAsync())
            {
                return;
            }
        }
        //=====================================================================

        CleanupBackgroundResult();
        _backgroundRemovalCts = new CancellationTokenSource();
        SetUiBusy(AppOperation.RemoveBackground);
        UpdateBackgroundControls();
        if (_backgroundComparisonStatus is not null)
        {
            _backgroundComparisonStatus.Text = "Preparing full-resolution background removal…";
        }

        try
        {
            //== external service call ========================================
            var options = new BackgroundRemovalOptions(
                GetBackgroundQuality(),
                _backgroundRefineEdgesCheckBox?.Checked == true);
            var progress = new Progress<double>(value => SetProgressValue((int)Math.Round(value)));
            var status = new Progress<string>(message =>
            {
                UpdateStatus(message);
                if (_backgroundComparisonStatus is not null)
                {
                    _backgroundComparisonStatus.Text = message;
                }
            });
            var log = new Progress<string>(AppendLog);
            var result = await _backgroundRemovalService.RemoveAsync(
                _backgroundSourcePath,
                options,
                progress,
                status,
                log,
                _backgroundRemovalCts.Token);
            //=================================================================

            //== output shaping ===============================================
            if (result.Success && result.OutputPath is not null)
            {
                _backgroundResultPath = result.OutputPath;
                SetPictureBoxImage(_backgroundRemovedPreview, result.OutputPath);
                _backgroundRemovedPreview?.Parent?.Invalidate(true);
                if (_backgroundComparisonStatus is not null)
                {
                    _backgroundComparisonStatus.Text = $"Preview ready in {result.Duration.TotalSeconds:0.0} seconds. Inspect the edges, then save the transparent PNG.";
                }
                UpdateStatus("Background removed — preview ready");
            }
            else
            {
                if (_backgroundComparisonStatus is not null)
                {
                    _backgroundComparisonStatus.Text = result.Cancelled ? "Background removal canceled." : $"Background removal failed: {result.ErrorMessage}";
                }
                if (!result.Cancelled)
                {
                    MessageBox.Show(this, result.ErrorMessage ?? "Background removal failed.", "Background Removal Error");
                }
            }
            //=================================================================
        }
        finally
        {
            _backgroundRemovalCts?.Dispose();
            _backgroundRemovalCts = null;
            SetUiBusy(AppOperation.None);
            UpdateBackgroundControls();
        }
    }

    private async void btnSaveBackgroundResult_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_backgroundResultPath) || !File.Exists(_backgroundResultPath) ||
            string.IsNullOrWhiteSpace(_backgroundSourcePath))
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "png",
            Filter = "Transparent PNG|*.png",
            FileName = Path.GetFileName(BackgroundRemovalOutputPath.CreateDefault(_backgroundSourcePath)),
            InitialDirectory = Path.GetDirectoryName(_backgroundSourcePath),
            OverwritePrompt = true,
            Title = "Save transparent PNG"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        //== persistence ======================================================
        File.Copy(_backgroundResultPath, dialog.FileName, overwrite: true);
        _lastSavedBackgroundPath = Path.GetFullPath(dialog.FileName);
        if (_backgroundComparisonStatus is not null)
        {
            _backgroundComparisonStatus.Text = $"Saved {Path.GetFileName(_lastSavedBackgroundPath)}. The comparison remains available.";
        }
        AddActivityEntry(ActivityFeedIconKind.Success, $"Transparent PNG saved: {Path.GetFileName(_lastSavedBackgroundPath)}", countsAsExport: true);
        UpdateStatus("Transparent PNG saved");
        UpdateBackgroundControls();
        await Task.CompletedTask;
        //=====================================================================
    }

    private void btnOpenBackgroundFolder_Click(object? sender, EventArgs e)
    {
        var path = _lastSavedBackgroundPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        //== external service call ===========================================
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        //=====================================================================
    }

    private async void btnInstallBackgroundRuntime_Click(object? sender, EventArgs e)
    {
        if (_backgroundInstallationInProgress)
        {
            CancelBackgroundRuntimeInstallation();
            return;
        }
        await InstallBackgroundRuntimeAsync();
    }

    private async Task<bool> InstallBackgroundRuntimeAsync()
    {
        if (_activeOperation != AppOperation.None || _backgroundInstallationInProgress)
        {
            return false;
        }

        var mode = ShowBackgroundInstallModeDialog();
        if (mode is null)
        {
            return false;
        }

        var installerPath = ResolveToolPath(
            "install-background-removal.ps1",
            out _,
            Path.Combine("tools", "background-removal", "install-background-removal.ps1"));
        if (installerPath is null)
        {
            MessageBox.Show(this, "The bundled Background Removal installer could not be found.", "Installer Missing");
            return false;
        }

        _backgroundRuntimeCts = new CancellationTokenSource();
        _backgroundInstallationInProgress = true;
        _backgroundInstallerProgressReceived = false;
        SetBackgroundInstallationUi(isInstalling: true);
        UpdateStatus("Installing Background Removal runtime…");

        Process? installerProcess = null;
        Task? installerOutputTask = null;
        Task? installerErrorTask = null;
        try
        {
            //== external service call ========================================
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(installerPath);
            startInfo.ArgumentList.Add("-Mode");
            startInfo.ArgumentList.Add(mode);
            if (_backgroundRuntimeStatus is not null)
            {
                startInfo.ArgumentList.Add("-Repair");
            }

            installerProcess = new Process { StartInfo = startInfo };
            _backgroundInstallerProcess = installerProcess;
            installerProcess.Start();
            using var registration = _backgroundRuntimeCts.Token.Register(TryStopBackgroundInstaller);
            installerOutputTask = ReadBackgroundInstallerStreamAsync(installerProcess.StandardOutput, isError: false);
            installerErrorTask = ReadBackgroundInstallerStreamAsync(installerProcess.StandardError, isError: true);
            await installerProcess.WaitForExitAsync(_backgroundRuntimeCts.Token);
            await Task.WhenAll(installerOutputTask, installerErrorTask);
            var succeeded = installerProcess.ExitCode == 0;
            _backgroundInstallerProcess = null;
            //=================================================================

            await CheckBackgroundRuntimeAsync(allowDuringInstallation: true);
            if (succeeded && _backgroundRuntimeStatus?.IsInstalled == true)
            {
                SetProgressValue(100);
                UpdateStatus("Background Removal runtime ready");
                return true;
            }

            MessageBox.Show(this, _backgroundRuntimeStatus?.ErrorMessage ?? "Runtime installation failed. See the activity log for details.", "Installation Failed");
            return false;
        }
        catch (OperationCanceledException)
        {
            await StopAndWaitForBackgroundInstallerAsync();
            try
            {
                await Task.WhenAll(
                        installerOutputTask ?? Task.CompletedTask,
                        installerErrorTask ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Stream teardown can race process cancellation.
            }
            CleanupBackgroundInstallerPartialFiles();
            UpdateStatus("Background Removal installation canceled");
            return false;
        }
        finally
        {
            _backgroundInstallerProcess = null;
            installerProcess?.Dispose();
            _backgroundRuntimeCts?.Dispose();
            _backgroundRuntimeCts = null;
            _backgroundInstallationInProgress = false;
            _backgroundInstallerProgressReceived = false;
            SetBackgroundInstallationUi(isInstalling: false);
        }
    }

    private string? ShowBackgroundInstallModeDialog()
    {
        //== input collection =================================================
        using var dialog = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = CardBackgroundColor,
            ClientSize = new Size(460, 230),
            Font = new Font("Segoe UI", 9.5F),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = "Install Background Removal"
        };
        var message = new Label
        {
            AutoSize = false,
            ForeColor = PrimaryTextColor,
            Location = new Point(24, 20),
            Size = new Size(412, 58),
            Text = "Choose the local inference mode. CPU is the reliable default; incompatible CUDA setup falls back to CPU automatically."
        };
        var modeSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(24, 88),
            Size = new Size(412, 34)
        };
        modeSelector.Items.AddRange(["CPU", "CUDA"]);
        modeSelector.SelectedIndex = 0;
        var install = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(246, 166),
            Size = new Size(90, 36),
            Text = "Install"
        };
        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(346, 166),
            Size = new Size(90, 36),
            Text = "Cancel"
        };

        //== high-contrast dialog actions =====================================
        StyleActionButton(install, primary: true);
        StyleActionButton(cancel, primary: false);
        //=====================================================================

        dialog.Controls.AddRange([message, modeSelector, install, cancel]);
        dialog.AcceptButton = install;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? modeSelector.SelectedItem?.ToString() : null;
        //=====================================================================
    }

    private async Task ReadBackgroundInstallerStreamAsync(StreamReader reader, bool isError)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            AppendLog($"Background Removal install: {line}");
            if (!isError && BackgroundRemovalInstallerProtocol.ParseProgress(line) is { } progress)
            {
                UpdateBackgroundInstallerProgress(progress);
            }
            else if (!isError && !_backgroundInstallerProgressReceived && !line.StartsWith('{'))
            {
                UpdateStatus(line);
            }
        }
    }

    private void UpdateBackgroundInstallerProgress(BackgroundRemovalInstallerProgress progress)
    {
        //== output shaping ===================================================
        _backgroundInstallerProgressReceived = true;
        var roundedPercent = (int)Math.Round(progress.Percent);
        SetProgressValue(roundedPercent);
        var statusText = $"{progress.Message} ({roundedPercent}%)";
        UpdateStatus(statusText);
        RunOnUiThread(() =>
        {
            if (_backgroundRuntimeLabel is not null)
            {
                _backgroundRuntimeLabel.Text = statusText;
                _backgroundRuntimeLabel.ForeColor = PrimaryTextColor;
            }
        });
        //=====================================================================
    }

    private async Task CheckBackgroundRuntimeAsync(bool allowDuringInstallation = false)
    {
        if (_backgroundInstallationInProgress && !allowDuringInstallation)
        {
            return;
        }

        _backgroundRuntimeCts?.Cancel();
        _backgroundRuntimeCts?.Dispose();
        _backgroundRuntimeCts = new CancellationTokenSource();
        var token = _backgroundRuntimeCts.Token;
        _backgroundRuntimeStatus = await _backgroundRemovalService.CheckRuntimeAsync(new Progress<string>(AppendLog), token);
        UpdateBackgroundRuntimeText();
        UpdateBackgroundControls();
    }

    private void UpdateBackgroundRuntimeText()
    {
        if (_backgroundRuntimeLabel is null)
        {
            return;
        }

        _backgroundRuntimeLabel.Text = _backgroundRuntimeStatus?.IsInstalled == true
            ? $"Ready · {_backgroundRuntimeStatus.InstallationMode ?? "CPU"} · rembg {_backgroundRuntimeStatus.RembgVersion ?? "installed"}"
            : _backgroundRuntimeStatus?.ErrorMessage ?? "Runtime installation is required.";
        _backgroundRuntimeLabel.ForeColor = _backgroundRuntimeStatus?.IsInstalled == true ? SuccessColor : WarningTextColor;
    }

    private void UpdateBackgroundControls()
    {
        var backgroundBusy = _activeOperation == AppOperation.RemoveBackground;
        var appIdle = _activeOperation == AppOperation.None;
        if (_backgroundChooseButton is not null)
        {
            _backgroundChooseButton.Enabled = appIdle && !_backgroundInstallationInProgress;
        }
        if (_backgroundQualitySlider is not null)
        {
            _backgroundQualitySlider.Enabled = appIdle && !_backgroundInstallationInProgress;
        }
        if (_backgroundRefineEdgesCheckBox is not null)
        {
            _backgroundRefineEdgesCheckBox.Enabled = appIdle && !_backgroundInstallationInProgress;
        }
        if (_backgroundRemoveButton is not null)
        {
            _backgroundRemoveButton.Enabled = !_backgroundInstallationInProgress &&
                ((appIdle && !string.IsNullOrWhiteSpace(_backgroundSourcePath)) || backgroundBusy);
            _backgroundRemoveButton.Text = backgroundBusy ? "Cancel background removal" : "Remove background";
        }
        if (_backgroundSaveButton is not null)
        {
            _backgroundSaveButton.Enabled = appIdle && !string.IsNullOrWhiteSpace(_backgroundResultPath) && File.Exists(_backgroundResultPath);
        }
        if (_backgroundOpenFolderButton is not null)
        {
            _backgroundOpenFolderButton.Enabled = appIdle && !string.IsNullOrWhiteSpace(_lastSavedBackgroundPath) && File.Exists(_lastSavedBackgroundPath);
        }
        if (_backgroundInstallButton is not null)
        {
            _backgroundInstallButton.Enabled = appIdle && !_backgroundInstallationInProgress;
            _backgroundInstallButton.Text = _backgroundInstallationInProgress ? "Installation in progress" : "Install or repair runtime";
        }
    }

    private void SetBackgroundInstallationUi(bool isInstalling)
    {
        //== whole-application interaction lock ==============================
        RunOnUiThread(() =>
        {
            if (_studioWorkspaceLayout is not null)
            {
                _studioWorkspaceLayout.Enabled = !isInstalling;
            }
            AllowDrop = !isInstalling;

            if (_backgroundInstallCancelButton is not null)
            {
                _backgroundInstallCancelButton.Visible = isInstalling;
                _backgroundInstallCancelButton.Enabled = isInstalling;
                _backgroundInstallCancelButton.Text = "Cancel download";
            }

            progressDownload.Visible = isInstalling;
            progressDownload.MarqueeAnimationSpeed = 0;
            progressDownload.Style = ProgressBarStyle.Continuous;
            progressDownload.Value = progressDownload.Minimum;
            UpdateBackgroundControls();
        });
        //=====================================================================
    }

    private void CancelBackgroundRuntimeInstallation()
    {
        //== cancellation =====================================================
        if (!_backgroundInstallationInProgress)
        {
            return;
        }

        if (_backgroundInstallCancelButton is not null)
        {
            _backgroundInstallCancelButton.Enabled = false;
            _backgroundInstallCancelButton.Text = "Canceling…";
        }
        UpdateStatus("Canceling Background Removal runtime download…");
        _backgroundRuntimeCts?.Cancel();
        TryStopBackgroundInstaller();
        //=====================================================================
    }

    private void CleanupBackgroundInstallerPartialFiles()
    {
        //== partial download cleanup =========================================
        foreach (var directory in new[]
                 {
                     _veditorPaths.BackgroundRemovalModelsDirectory,
                     _veditorPaths.BackgroundRemovalDownloadCacheDirectory
                 })
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var partialPath in Directory.EnumerateFiles(directory, "*.partial", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(partialPath);
                }
            }
            catch
            {
                // Best effort cleanup; verified completed assets remain reusable.
            }
        }
        //=====================================================================
    }

    private void UpdateBackgroundComparisonStageVisibility(WorkspacePage page)
    {
        if (_backgroundComparisonStage is null)
        {
            return;
        }

        _backgroundComparisonStage.Visible = page == WorkspacePage.Background;
        if (_backgroundComparisonStage.Visible)
        {
            _backgroundComparisonStage.BringToFront();
            AdoptCurrentPictureForBackgroundRemoval();
        }
    }

    private void InvalidateBackgroundResult()
    {
        if (string.IsNullOrWhiteSpace(_backgroundResultPath))
        {
            return;
        }

        CleanupBackgroundResult();
        ClearPictureBox(_backgroundRemovedPreview);
        if (_backgroundComparisonStatus is not null)
        {
            _backgroundComparisonStatus.Text = "Quality settings changed. Remove the background again to refresh the preview.";
        }
        UpdateBackgroundControls();
    }

    private void CleanupBackgroundResult()
    {
        var resultPath = _backgroundResultPath;
        _backgroundResultPath = null;
        BackgroundRemovalService.CleanupResult(resultPath);
    }

    private static void ClearPictureBox(PictureBox? pictureBox)
    {
        if (pictureBox is null)
        {
            return;
        }
        var previous = pictureBox.Image;
        pictureBox.Image = null;
        previous?.Dispose();
        pictureBox.Parent?.Invalidate(true);
    }

    private void TryStopBackgroundInstaller()
    {
        try
        {
            if (_backgroundInstallerProcess is { HasExited: false })
            {
                _backgroundInstallerProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation of the installer process tree.
        }
    }

    private async Task StopAndWaitForBackgroundInstallerAsync()
    {
        //== cancellation synchronization ====================================
        var process = _backgroundInstallerProcess;
        if (process is null)
        {
            return;
        }

        TryStopBackgroundInstaller();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            // Best effort wait; the process tree has already received termination.
        }
        //=====================================================================
    }
}
