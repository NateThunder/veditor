using System.Collections.Specialized;
using VeditorWindow.Services;
using VeditorWindow.UI;

namespace VeditorWindow;

public partial class Form1
{
    private readonly List<string> _pictureCompressionPaths = [];
    private readonly Dictionary<string, PictureCompressionResult> _picturePreviewResults = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _picturePreviewCts;
    private ListBox? _pictureFileList;
    private CheckBox? _pictureLosslessRadio;
    private CheckBox? _pictureLossyRadio;
    private bool _pictureModeSyncInFlight;
    private PurpleSlider? _pictureQualitySlider;
    private Label? _pictureQualityValue;
    private Label? _pictureQualitySummary;
    private Label? _pictureModeHint;
    private CheckBox? _pictureStripMetadata;
    private PictureBox? _pictureOriginalPreview;
    private PictureBox? _pictureCompressedPreview;
    private Panel? _pictureComparisonStage;
    private Label? _pictureSizeSummary;
    private Label? _pictureBatchSummary;
    private Button? _pictureSaveMenuButton;

    private Panel BuildPictureCompressionPage()
    {
        //== picture compressor workspace ====================================
        var section = CreateSectionPanel(Padding.Empty);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var title = CreateSectionTitle("Picture compression");
        var subtitle = CreateSectionSubtitle("Keep the original format and dimensions while reducing file size.");
        subtitle.Margin = new Padding(0, 7, 0, 16);

        var addButton = new ClayButton { Text = "Add pictures", Height = 42, Dock = DockStyle.Top };
        StyleActionButton(addButton, primary: false);
        addButton.Click += btnAddPictures_Click;

        _pictureFileList = new ListBox
        {
            BackColor = InputBackgroundColor,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 9F),
            ForeColor = PrimaryTextColor,
            Height = 24,
            HorizontalScrollbar = true,
            IntegralHeight = false,
            Margin = new Padding(0, 10, 0, 0)
        };
        _pictureFileList.SelectedIndexChanged += (_, _) => SchedulePicturePreview();

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(addButton, 0, 2);
        layout.Controls.Add(_pictureFileList, 0, 3);
        layout.Controls.Add(BuildPictureModePanel(), 0, 4);
        layout.Controls.Add(BuildPictureOutputPanel(), 0, 5);

        section.Controls.Add(layout);
        return section;
        //=====================================================================
    }

    private Control BuildPictureModePanel()
    {
        //== compression controls ============================================
        var panel = CreateInsetPanel(new Padding(14));
        panel.Dock = DockStyle.Fill;
        panel.Margin = new Padding(0, 14, 0, 0);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _pictureLossyRadio = CreatePictureModeRadio("Lossy — substantially smaller files", isChecked: true);
        _pictureLosslessRadio = CreatePictureModeRadio("Lossless — preserve every pixel", isChecked: false);
        _pictureLosslessRadio.CheckedChanged += PictureCompressionSettingChanged;
        _pictureLossyRadio.CheckedChanged += PictureCompressionSettingChanged;

        _pictureModeHint = CreateSectionSubtitle("Original dimensions are preserved. Lower quality produces smaller files.");
        _pictureModeHint.Margin = new Padding(0, 8, 0, 0);

        var qualityHeader = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 16, 0, 0),
            RowCount = 1
        };
        qualityHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        qualityHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var qualityCaption = CreateMicroCaption("Quality");
        _pictureQualityValue = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "80%",
            TextAlign = ContentAlignment.TopRight
        };
        qualityHeader.Controls.Add(qualityCaption, 0, 0);
        qualityHeader.Controls.Add(_pictureQualityValue, 1, 0);

        _pictureQualitySlider = new PurpleSlider
        {
            AccessibleName = "Picture quality",
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
            Height = 30,
            LargeChange = 10,
            Maximum = 100,
            Minimum = 1,
            Margin = new Padding(0, 10, 0, 0),
            Value = 80
        };
        _pictureQualitySlider.ValueChanged += PictureCompressionSettingChanged;
        _pictureQualitySlider.InteractionCompleted += (_, _) => SchedulePicturePreview();

        var qualityScale = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 7, 0, 0),
            RowCount = 1
        };
        qualityScale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        qualityScale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        qualityScale.Controls.Add(CreatePictureQualityScaleLabel("Smaller file", ContentAlignment.TopLeft), 0, 0);
        qualityScale.Controls.Add(CreatePictureQualityScaleLabel("Better quality", ContentAlignment.TopRight), 1, 0);

        _pictureQualitySummary = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI Semibold", 9F),
            Margin = new Padding(0, 7, 0, 0),
            Text = "Lossy · Quality 80",
            TextAlign = ContentAlignment.TopRight
        };

        _pictureStripMetadata = CreateCompressionOptionCheckBox(isChecked: true);
        _pictureStripMetadata.CheckedChanged += PictureCompressionSettingChanged;

        layout.Controls.Add(CreateMicroCaption("Mode"), 0, 0);
        layout.Controls.Add(_pictureLossyRadio, 0, 1);
        layout.Controls.Add(_pictureLosslessRadio, 0, 2);
        layout.Controls.Add(qualityHeader, 0, 3);
        layout.Controls.Add(_pictureQualitySlider, 0, 4);
        layout.Controls.Add(qualityScale, 0, 5);
        layout.Controls.Add(_pictureModeHint, 0, 6);
        layout.Controls.Add(_pictureQualitySummary, 0, 7);
        layout.Controls.Add(CreateCompressionOptionRow(_pictureStripMetadata, "Strip metadata", "Remove EXIF, GPS, and embedded tags."), 0, 8);
        panel.Controls.Add(layout);
        UpdatePictureCompressionControls();
        return panel;
        //=====================================================================
    }

    private Control BuildPictureComparisonStage()
    {
        //== central comparison stage ========================================
        var panel = new Panel
        {
            BackColor = StageBackgroundColor,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28)
        };
        _pictureComparisonStage = panel;

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

        var originalLabel = CreatePictureComparisonHeading("ORIGINAL");
        var compressedLabel = CreatePictureComparisonHeading("COMPRESSED");
        compressedLabel.Margin = new Padding(14, 0, 0, 12);
        _pictureOriginalPreview = CreateCentralPicturePreviewBox();
        _pictureCompressedPreview = CreateCentralPicturePreviewBox();
        _pictureOriginalPreview.Margin = new Padding(0, 0, 7, 0);
        _pictureCompressedPreview.Margin = new Padding(7, 0, 0, 0);

        _pictureSizeSummary = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 16, 0, 0),
            Padding = new Padding(0, 2, 0, 2),
            Text = "Add pictures to generate an exact side-by-side preview.",
            TextAlign = ContentAlignment.MiddleCenter
        };

        layout.Controls.Add(originalLabel, 0, 0);
        layout.Controls.Add(compressedLabel, 1, 0);
        layout.Controls.Add(_pictureOriginalPreview, 0, 1);
        layout.Controls.Add(_pictureCompressedPreview, 1, 1);
        layout.Controls.Add(_pictureSizeSummary, 0, 2);
        layout.SetColumnSpan(_pictureSizeSummary, 2);
        panel.Controls.Add(layout);
        return panel;
        //=====================================================================
    }

    private static Label CreatePictureComparisonHeading(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = PrimaryTextColor,
            Height = 28,
            Margin = new Padding(0, 0, 0, 12),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter
        };
    }

    private static PictureBox CreateCentralPicturePreviewBox()
    {
        return new PictureBox
        {
            AccessibleName = "Picture compression preview",
            BackColor = StageSurfaceColor,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(160, 220),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };
    }

    private Control BuildPictureOutputPanel()
    {
        //== output actions ===================================================
        var panel = CreateSectionPanel(new Padding(0, 16, 0, 0));
        panel.Dock = DockStyle.Fill;
        _pictureBatchSummary = CreateSectionSubtitle("No pictures selected.");
        _pictureBatchSummary.Margin = new Padding(0, 0, 0, 10);

        _pictureSaveMenuButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = "Save options"
        };
        StyleActionButton(_pictureSaveMenuButton, primary: true);

        var menu = new ContextMenuStrip
        {
            BackColor = CardBackgroundColor,
            Font = new Font("Segoe UI Semibold", 9.5F),
            ForeColor = PrimaryTextColor,
            ShowImageMargin = false
        };
        menu.Items.Add("Save as copy", null, async (_, _) => await RunPictureOutputAsync(PictureOutputAction.SaveAsCopy));
        menu.Items.Add("Save", null, async (_, _) => await RunPictureOutputAsync(PictureOutputAction.Save));
        menu.Items.Add("Copy to clipboard", null, async (_, _) => await RunPictureOutputAsync(PictureOutputAction.CopyToClipboard));
        _pictureSaveMenuButton.Click += (_, _) => menu.Show(_pictureSaveMenuButton, new Point(0, _pictureSaveMenuButton.Height));

        panel.Controls.Add(_pictureSaveMenuButton);
        panel.Controls.Add(_pictureBatchSummary);
        return panel;
        //=====================================================================
    }

    private enum PictureOutputAction
    {
        SaveAsCopy,
        Save,
        CopyToClipboard
    }

    private static CheckBox CreatePictureModeRadio(string text, bool isChecked)
    {
        return new ClayCheckBox
        {
            AutoSize = true,
            Checked = isChecked,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 9, 0, 0),
            Text = text
        };
    }

    private static Label CreatePictureQualityScaleLabel(string text, ContentAlignment alignment)
    {
        return new Label
        {
            AutoSize = true,
            Dock = alignment == ContentAlignment.TopLeft ? DockStyle.Left : DockStyle.Right,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = SecondaryTextColor,
            Margin = Padding.Empty,
            Text = text,
            TextAlign = alignment
        };
    }

    private async void btnAddPictures_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff",
            Multiselect = true,
            Title = "Add pictures"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPictures(dialog.FileNames);
            await GenerateSelectedPicturePreviewAsync();
        }
    }

    private void AddPictures(IEnumerable<string> paths)
    {
        //== input collection =================================================
        foreach (var path in paths.Where(File.Exists).Where(PictureCompressionService.IsSupported))
        {
            var fullPath = Path.GetFullPath(path);
            if (_pictureCompressionPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _pictureCompressionPaths.Add(fullPath);
            _pictureFileList?.Items.Add(Path.GetFileName(fullPath));
        }

        if (_pictureFileList is not null && _pictureFileList.Items.Count > 0 && _pictureFileList.SelectedIndex < 0)
        {
            _pictureFileList.SelectedIndex = 0;
        }
        //=====================================================================

        UpdatePictureCompressionControls();
    }

    private void UpdatePictureComparisonStageVisibility(WorkspacePage page)
    {
        //== state transition =================================================
        if (_pictureComparisonStage is null)
        {
            return;
        }

        _pictureComparisonStage.Visible = page == WorkspacePage.Picture;
        if (_pictureComparisonStage.Visible)
        {
            _pictureComparisonStage.BringToFront();
        }
        //=====================================================================
    }

    private void PictureCompressionSettingChanged(object? sender, EventArgs e)
    {
        //== mutually exclusive mode selection ===============================
        if (!_pictureModeSyncInFlight && sender is CheckBox modeSelector &&
            (ReferenceEquals(modeSelector, _pictureLosslessRadio) || ReferenceEquals(modeSelector, _pictureLossyRadio)))
        {
            _pictureModeSyncInFlight = true;
            try
            {
                if (modeSelector.Checked)
                {
                    var otherSelector = ReferenceEquals(modeSelector, _pictureLosslessRadio)
                        ? _pictureLossyRadio
                        : _pictureLosslessRadio;
                    if (otherSelector is not null)
                    {
                        otherSelector.Checked = false;
                    }
                }
                else if (_pictureLosslessRadio?.Checked != true && _pictureLossyRadio?.Checked != true)
                {
                    modeSelector.Checked = true;
                }
            }
            finally
            {
                _pictureModeSyncInFlight = false;
            }
        }
        //=====================================================================

        UpdatePictureCompressionControls();

        //== preview scheduling ===============================================
        if (ReferenceEquals(sender, _pictureQualitySlider) && _pictureQualitySlider?.IsDragging == true)
        {
            _picturePreviewCts?.Cancel();
            return;
        }

        SchedulePicturePreview();
        //=====================================================================
    }

    private void UpdatePictureCompressionControls()
    {
        //== state changes ====================================================
        var hasLosslessOnlyPicture = _pictureCompressionPaths.Any(PictureCompressionService.IsLosslessOnly);
        if (hasLosslessOnlyPicture && _pictureLossyRadio?.Checked == true)
        {
            _pictureLosslessRadio!.Checked = true;
        }

        if (_pictureLossyRadio is not null)
        {
            _pictureLossyRadio.Enabled = !hasLosslessOnlyPicture && _activeOperation == AppOperation.None;
        }

        var lossy = _pictureLossyRadio?.Checked == true;
        if (_pictureQualitySlider is not null)
        {
            _pictureQualitySlider.Enabled = lossy && _activeOperation == AppOperation.None;
        }

        if (_pictureQualityValue is not null)
        {
            _pictureQualityValue.Text = lossy ? $"{_pictureQualitySlider?.Value ?? 80}%" : "LOSSLESS";
        }

        if (_pictureQualitySummary is not null)
        {
            _pictureQualitySummary.Text = lossy
                ? $"Lossy · Quality {_pictureQualitySlider?.Value ?? 80}"
                : "Lossless · Balanced";
        }

        if (_pictureModeHint is not null)
        {
            _pictureModeHint.Text = hasLosslessOnlyPicture
                ? "Lossless is required because this batch contains BMP or TIFF pictures."
                : lossy
                    ? "Original dimensions are preserved. Lower quality produces smaller files."
                    : "Balanced lossless optimization preserves every decoded pixel.";
        }

        if (_pictureBatchSummary is not null)
        {
            _pictureBatchSummary.Text = _pictureCompressionPaths.Count == 0
                ? "No pictures selected."
                : $"{_pictureCompressionPaths.Count} picture{(_pictureCompressionPaths.Count == 1 ? string.Empty : "s")} ready.";
        }

        if (_pictureSaveMenuButton is not null)
        {
            _pictureSaveMenuButton.Enabled = _pictureCompressionPaths.Count > 0 && _activeOperation == AppOperation.None;
        }
        //=====================================================================
    }

    private void SchedulePicturePreview()
    {
        _picturePreviewCts?.Cancel();
        _picturePreviewCts?.Dispose();
        _picturePreviewCts = new CancellationTokenSource();
        _ = GenerateSelectedPicturePreviewAfterDelayAsync(_picturePreviewCts.Token);
    }

    private async Task GenerateSelectedPicturePreviewAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(320, cancellationToken);
            await GenerateSelectedPicturePreviewAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer selection or slider value superseded this preview.
        }
    }

    private async Task GenerateSelectedPicturePreviewAsync(CancellationToken cancellationToken = default)
    {
        //== precondition checks =============================================
        var selectedIndex = _pictureFileList?.SelectedIndex ?? -1;
        if (selectedIndex < 0 || selectedIndex >= _pictureCompressionPaths.Count)
        {
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out _,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));
        if (ffmpegPath is null)
        {
            _pictureSizeSummary!.Text = "FFmpeg is required to generate exact compressed previews.";
            return;
        }
        //=====================================================================

        var sourcePath = _pictureCompressionPaths[selectedIndex];
        var temporaryPath = BuildPictureTemporaryPath(sourcePath);
        _pictureSizeSummary!.Text = "Generating exact preview…";

        try
        {
            var result = await PictureCompressionService.CompressAsync(
                ffmpegPath,
                sourcePath,
                temporaryPath,
                GetPictureCompressionOptions(),
                cancellationToken);
            _picturePreviewResults[sourcePath] = result;
            SetPictureBoxImage(_pictureOriginalPreview, sourcePath);
            SetPictureBoxImage(_pictureCompressedPreview, result.OutputPath);
            var savings = result.SourceBytes == 0 ? 0D : (1D - result.OutputBytes / (double)result.SourceBytes) * 100D;
            _pictureSizeSummary.Text = $"{FormatFileSize(result.SourceBytes)} → {FormatFileSize(result.OutputBytes)} ({savings:0.#}% smaller)";
        }
        catch (OperationCanceledException)
        {
            TryDeletePictureTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeletePictureTemporaryFile(temporaryPath);
            _pictureSizeSummary.Text = $"Preview failed: {ex.Message}";
        }
    }

    private async Task RunPictureOutputAsync(PictureOutputAction action)
    {
        //== input validation =================================================
        if (_pictureCompressionPaths.Count == 0 || _activeOperation != AppOperation.None)
        {
            return;
        }

        if (action == PictureOutputAction.Save)
        {
            var confirmation = MessageBox.Show(
                this,
                $"Replace {_pictureCompressionPaths.Count} original picture{(_pictureCompressionPaths.Count == 1 ? string.Empty : "s")}? This cannot be undone.",
                "Replace Original Pictures",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var searchedPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));
        if (ffmpegPath is null)
        {
            MessageBox.Show(this, $"ffmpeg.exe was not found.\r\n\r\n{string.Join("\r\n", searchedPaths.Take(3))}", "ffmpeg Missing");
            return;
        }
        //=====================================================================

        SetUiBusy(AppOperation.PictureCompress);
        UpdatePictureCompressionControls();
        var clipboardPaths = new StringCollection();
        var completed = 0;

        try
        {
            //== batch processing ============================================
            foreach (var sourcePath in _pictureCompressionPaths.ToArray())
            {
                UpdateStatus($"Compressing picture {completed + 1} of {_pictureCompressionPaths.Count}…");
                string outputPath;
                var replaceOriginal = action == PictureOutputAction.Save;
                if (replaceOriginal || action == PictureOutputAction.CopyToClipboard)
                {
                    outputPath = BuildPictureTemporaryPath(sourcePath);
                }
                else
                {
                    outputPath = PictureCompressionService.CreateCopyPath(sourcePath);
                }

                var result = await PictureCompressionService.CompressAsync(
                    ffmpegPath,
                    sourcePath,
                    outputPath,
                    GetPictureCompressionOptions());

                if (replaceOriginal)
                {
                    File.Copy(result.OutputPath, sourcePath, overwrite: true);
                    TryDeletePictureTemporaryFile(result.OutputPath);
                }
                else if (action == PictureOutputAction.CopyToClipboard)
                {
                    clipboardPaths.Add(result.OutputPath);
                }

                completed++;
                SetProgressValue((int)Math.Round(completed / (double)_pictureCompressionPaths.Count * 100D));
            }
            //=================================================================

            if (action == PictureOutputAction.CopyToClipboard)
            {
                Clipboard.SetFileDropList(clipboardPaths);
            }

            UpdateStatus(action switch
            {
                PictureOutputAction.SaveAsCopy => $"Saved {completed} compressed picture copies",
                PictureOutputAction.Save => $"Saved {completed} compressed pictures",
                _ => $"Copied {completed} compressed pictures to the clipboard"
            });
            AddActivityEntry(ActivityFeedIconKind.Success, $"Picture compression complete: {completed} file{(completed == 1 ? string.Empty : "s")}", countsAsExport: true);
            SchedulePicturePreview();
        }
        catch (Exception ex)
        {
            UpdateStatus("Picture compression failed");
            AppendLog($"Picture compression failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Picture Compression Error");
        }
        finally
        {
            SetUiBusy(AppOperation.None);
            UpdatePictureCompressionControls();
        }
    }

    private PictureCompressionOptions GetPictureCompressionOptions()
    {
        return new PictureCompressionOptions(
            _pictureLossyRadio?.Checked == true ? PictureCompressionMode.Lossy : PictureCompressionMode.Lossless,
            _pictureQualitySlider?.Value ?? 80,
            _pictureStripMetadata?.Checked ?? true);
    }

    private static string BuildPictureTemporaryPath(string sourcePath)
    {
        //== output shaping ===================================================
        var directory = Path.Combine(Path.GetTempPath(), "Veditor", "picture-compression");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        //=====================================================================
    }

    private static void SetPictureBoxImage(PictureBox? pictureBox, string path)
    {
        if (pictureBox is null)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(stream);
            var replacement = new Bitmap(image);
            var previous = pictureBox.Image;
            pictureBox.Image = replacement;
            previous?.Dispose();
        }
        catch
        {
            var previous = pictureBox.Image;
            pictureBox.Image = null;
            previous?.Dispose();
        }
    }

    private static void TryDeletePictureTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary preview cleanup is best effort.
        }
    }
}
