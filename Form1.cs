using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VeditorWindow.Models;
using VeditorWindow.Services;
using VeditorWindow.UI;

namespace VeditorWindow;

public partial class Form1 : Form
{
    private const string PreviewHostName = "preview.media";
    private const int SurfaceCornerRadius = StudioTheme.SurfaceRadius;
    private static readonly Color AppBackgroundColor = StudioTheme.WindowBackground;
    private static readonly Color CardBackgroundColor = StudioTheme.Surface;
    private static readonly Color CardBorderColor = StudioTheme.Border;
    private static readonly Color PrimaryTextColor = StudioTheme.TextPrimary;
    private static readonly Color SecondaryTextColor = StudioTheme.TextSecondary;
    private static readonly Color AccentColor = StudioTheme.Accent;
    private static readonly Color AccentSoftColor = StudioTheme.AccentSoft;
    private static readonly Color AccentTextOnSolidColor = PrimaryTextColor;
    private static readonly Color StageBackgroundColor = StudioTheme.StageBackground;
    private static readonly Color StageSurfaceColor = StudioTheme.StageSurface;
    private static readonly Color LogBackgroundColor = StudioTheme.SurfaceInput;
    private static readonly Color InputBackgroundColor = StudioTheme.SurfaceInput;
    private static readonly Color MutedSurfaceColor = StudioTheme.SurfaceMuted;
    private static readonly Color StatusBackgroundColor = StudioTheme.StatusSurface;
    private static readonly Color WarningTextColor = StudioTheme.Warning;
    private static readonly Color SuccessColor = StudioTheme.Success;
    private static readonly Color ErrorColor = StudioTheme.Error;

    private enum AppOperation
    {
        None,
        Download,
        Convert,
        PictureCompress,
        RemoveBackground,
        Trim,
        Crop,
        RemoveWatermark
    }

    private enum WorkspacePage
    {
        Source,
        Audio,
        Video,
        Picture,
        Background,
        Watermark,
        Trim,
        Crop,
        Activity
    }

    private enum RailIconKind
    {
        Download,
        Compress,
        Background,
        Watermark,
        Trim,
        Crop,
        Convert,
        Activity
    }

    private enum CropAspectPreset
    {
        Custom,
        Original,
        Square,
        Landscape16x9,
        Portrait9x16,
        Landscape4x3
    }

    private enum ActivityFeedIconKind
    {
        Download,
        Export,
        Success,
        Error,
        Neutral
    }

    private enum WatermarkActionMode
    {
        None,
        Remove,
        Preview,
        Selection
    }

    //== media metadata =======================================================
    private sealed record MediaMetadata(
        TimeSpan Duration,
        int? Width,
        int? Height,
        bool HasVideo);
    //=========================================================================

    //== activity feed ========================================================
    private sealed record ActivityFeedEntry(
        DateTime TimestampLocal,
        string Message,
        ActivityFeedIconKind IconKind,
        bool CountsAsDownload,
        bool CountsAsExport,
        bool CountsAsError);
    //=========================================================================

    //== video encoder selection ==============================================
    private sealed record VideoEncoderSelection(
        string EncoderName,
        string DisplayLabel,
        bool UsesHardwareAcceleration);
    //=========================================================================

    //== rail button state ====================================================
    private sealed class RailButtonVisualState
    {
        public required RailIconKind IconKind { get; init; }

        public required string Label { get; init; }

        public bool IsHovered { get; set; }

        public bool IsSelected { get; set; }
    }
    //=========================================================================

    //== compression profiles ==================================================
    private readonly record struct VideoQualityPreset(
        string Name,
        int Crf,
        string EncoderPreset,
        string AudioBitrate,
        int? MaxHeight,
        string HintText,
        bool WarnOfNoticeableLoss,
        double EstimatedOutputFactor,
        int QualityPercent);

    private readonly record struct VideoCompressionOptions(
        bool StripMetadata,
        bool UseTwoPassEncode,
        bool UseHardwareAcceleration);

    private static readonly VideoQualityPreset[] VideoQualityPresets =
    [
        new("Best", 20, "medium", "160k", null, "Full resolution with a faster default encode.", false, 0.90D, 92),
        new("High", 23, "medium", "160k", null, "Good visual quality with a faster everyday encode.", false, 0.80D, 78),
        new("Balanced", 26, "medium", "128k", null, "Balanced savings with quicker conversion times.", false, 0.60D, 60),
        new("Smaller", 28, "medium", "128k", 1080, "Caps video at 1080p for stronger size savings.", true, 0.46D, 44),
        new("Smallest", 30, "medium", "96k", 720, "Caps video at 720p and lowers audio bitrate the most.", true, 0.25D, 28)
    ];
    //=========================================================================

    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _watermarkCts;
    private CancellationTokenSource? _watermarkRuntimeCheckCts;
    private Process? _activeProcess;
    private AppOperation _activeOperation;
    private WatermarkActionMode _watermarkActionMode;
    private readonly VeditorPaths _veditorPaths;
    private readonly WatermarkRemovalService _watermarkRemovalService;
    private WatermarkRuntimeStatus? _watermarkRuntimeStatus;
    private bool _watermarkRuntimeCheckInProgress;
    private bool _watermarkInstallationInProgress;
    private bool _shutdownCleanupInProgress;
    private bool _shutdownConfirmed;
    private string? _watermarkAuthorizationMediaPath;
    private string? _lastWatermarkProgressStage;
    private bool _previewInitializationAttempted;
    private bool _previewReady;
    private string? _currentOutputFolder;
    private string? _lastDownloadedFilePath;
    private string? _currentPreviewFilePath;
    private bool _downloadAutoPreviewTriggered;
    private string? _downloadAutoPreviewLoadedPath;
    private DateTime _downloadStartedUtc;
    private HashSet<string> _outputFolderSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private WorkspacePage _currentWorkspacePage = WorkspacePage.Source;
    private bool _previewDocumentReady;
    private Panel? _workspacePageHost;
    private ClayScrollPanel? _workspacePageViewport;
    private Label? _previewMetaLabel;
    private Panel? _trimTimelineHost;
    private TrimTimelineControl? _trimTimelineControl;
    private MediaMetadata? _currentMediaMetadata;
    private Button? _btnTrimSetIn;
    private Button? _btnTrimSetOut;
    private Button? _btnTrimJumpToIn;
    private Button? _btnTrimPreviewSelection;
    private Button? _btnTrimJumpToOut;
    private Button? _btnTrimResetRange;
    private Button? _btnTrimExport;
    private Label? _lblTrimCurrentPositionValue;
    private Label? _lblTrimInPointValue;
    private Label? _lblTrimOutPointValue;
    private Label? _lblTrimSourceDurationValue;
    private Label? _lblTrimSelectionValue;
    private Label? _lblTrimTrimmedValue;
    private readonly System.Windows.Forms.Timer _trimPreviewTimer = new();
    private TimeSpan _trimCurrentPosition = TimeSpan.Zero;
    private TimeSpan _trimSelectionStart = TimeSpan.Zero;
    private TimeSpan _trimSelectionEnd = TimeSpan.Zero;
    private bool _trimSelectionPlaybackActive;
    private bool _trimPositionSyncInFlight;
    private Button? _cropAspectCustomButton;
    private Button? _cropAspectOriginalButton;
    private Button? _cropAspectSquareButton;
    private Button? _cropAspectLandscapeButton;
    private Button? _cropAspectPortraitButton;
    private Button? _cropAspectClassicButton;
    private TextBox? _txtCropX;
    private TextBox? _txtCropY;
    private TextBox? _txtCropWidth;
    private TextBox? _txtCropHeight;
    private Button? _btnCropRotateLeft;
    private Button? _btnCropRotateReset;
    private Button? _btnCropRotateRight;
    private Label? _lblCropSourceValue;
    private Label? _lblCropOutputValue;
    private Label? _lblCropRotationValue;
    private Button? _btnCropReset;
    private Button? _btnCropApply;
    private CropAspectPreset _selectedCropAspectPreset = CropAspectPreset.Original;
    private bool _cropUiSyncInFlight;
    private int _cropX;
    private int _cropY;
    private int _cropWidth;
    private int _cropHeight;
    private int _cropRotationDegrees;
    private Label? _compressionPreviewFileLabel;
    private Label? _compressionPreviewSizeBadgeLabel;
    private Button? _compressionPresetLightButton;
    private Button? _compressionPresetBalancedButton;
    private Button? _compressionPresetAggressiveButton;
    private Button? _conversionCodecH264Button;
    private Button? _conversionCodecH265Button;
    private Label? _conversionFormatValueLabel;
    private Label? _conversionEstimatedSizeValueLabel;
    private Label? _compressionQualityPercentLabel;
    private Label? _compressionOriginalSizeLabel;
    private Label? _compressionOutputSizeLabel;
    private Label? _compressionSavingsTitleLabel;
    private Label? _compressionSavingsDetailLabel;
    private Label? _compressionSavingsPercentLabel;
    private Label? _compressionOutputFooterLabel;
    private Panel? _compressionOriginalBarTrack;
    private Panel? _compressionOriginalBarFill;
    private Panel? _compressionOutputBarTrack;
    private Panel? _compressionOutputBarFill;
    private CheckBox? _compressionStripMetadataCheckBox;
    private CheckBox? _compressionTwoPassCheckBox;
    private CheckBox? _compressionHardwareAccelerationCheckBox;
    private Button? _compressionActionButton;
    private TextBox? _watermarkDetectionPromptTextBox;
    private NumericUpDown? _watermarkMaximumDetectionSizeInput;
    private NumericUpDown? _watermarkDetectionIntervalInput;
    private NumericUpDown? _watermarkFadeInInput;
    private NumericUpDown? _watermarkFadeOutInput;
    private CheckBox? _watermarkUseGpuCheckBox;
    private CheckBox? _watermarkAuthorizationCheckBox;
    private Label? _watermarkGpuStatusLabel;
    private Label? _watermarkRuntimeStatusLabel;
    private Button? _watermarkRemoveButton;
    private Button? _watermarkPreviewButton;
    private Button? _watermarkInstallButton;
    private Button? _watermarkManualSelectButton;
    private RadioButton? _watermarkAutoModeRadioButton;
    private RadioButton? _watermarkManualModeRadioButton;
    private Label? _watermarkSelectionStatusLabel;
    private NumericUpDown? _watermarkMaskPaddingInput;
    private Control? _watermarkAutomaticSettingsPanel;
    private IReadOnlyList<WatermarkRegion> _watermarkSelectedRegions = Array.Empty<WatermarkRegion>();
    private string? _watermarkSelectionMediaPath;
    private TableLayoutPanel? _activityFeedLayout;
    private Label? _activityEntryCountLabel;
    private Label? _activityDownloadsCountLabel;
    private Label? _activityExportsCountLabel;
    private Label? _activityErrorsCountLabel;
    private string? _lastActivityErrorSummary;
    private readonly Dictionary<string, HashSet<string>> _ffmpegEncoderCache = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedVideoTargetExtension = StudioDefaults.VideoExtension;
    private string _selectedAudioTargetExtension = "mp3";
    private bool _selectedConversionRequiresVideo = true;
    private string _selectedVideoCodec = StudioDefaults.VideoCodec;
    private readonly List<ActivityFeedEntry> _activityFeedEntries = [];
    private readonly Dictionary<WorkspacePage, Panel> _workspacePagePanels = [];
    private readonly Dictionary<WorkspacePage, Button> _workspacePageButtons = [];
    private readonly CheckBox chkExtractWavAudio = new ClayCheckBox();

    public Form1()
    {
        _veditorPaths = new VeditorPaths();
        _watermarkRemovalService = new WatermarkRemovalService(_veditorPaths);
        _backgroundRemovalService = new BackgroundRemovalService(_veditorPaths);
        InitializeComponent();
        ConfigureStudioLayout();

        txtOutputFolder.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "VeditorDownloads");
        lblStatus.Text = "Idle";
        RefreshPreviewSummary();
        Shown += Form1_Shown;
        webPreview.NavigationCompleted += webPreview_NavigationCompleted;
        _trimPreviewTimer.Interval = 220;
        _trimPreviewTimer.Tick += TrimPreviewTimer_Tick;
        _trimPreviewTimer.Start();
        SetUiBusy(AppOperation.None);
        UpdateVideoQualityUi();
    }

    private void ConfigureStudioLayout()
    {
        SuspendLayout();

        //== workspace state reset =============================================
        _workspacePageHost = null;
        _workspacePageViewport = null;
        _previewMetaLabel = null;
        _trimTimelineHost = null;
        _trimTimelineControl = null;
        _currentMediaMetadata = null;
        _btnTrimSetIn = null;
        _btnTrimSetOut = null;
        _btnTrimJumpToIn = null;
        _btnTrimPreviewSelection = null;
        _btnTrimJumpToOut = null;
        _btnTrimResetRange = null;
        _btnTrimExport = null;
        _lblTrimCurrentPositionValue = null;
        _lblTrimInPointValue = null;
        _lblTrimOutPointValue = null;
        _lblTrimSourceDurationValue = null;
        _lblTrimSelectionValue = null;
        _lblTrimTrimmedValue = null;
        _trimCurrentPosition = TimeSpan.Zero;
        _trimSelectionStart = TimeSpan.Zero;
        _trimSelectionEnd = TimeSpan.Zero;
        _trimSelectionPlaybackActive = false;
        _trimPositionSyncInFlight = false;
        _cropAspectCustomButton = null;
        _cropAspectOriginalButton = null;
        _cropAspectSquareButton = null;
        _cropAspectLandscapeButton = null;
        _cropAspectPortraitButton = null;
        _cropAspectClassicButton = null;
        _txtCropX = null;
        _txtCropY = null;
        _txtCropWidth = null;
        _txtCropHeight = null;
        _btnCropRotateLeft = null;
        _btnCropRotateReset = null;
        _btnCropRotateRight = null;
        _lblCropSourceValue = null;
        _lblCropOutputValue = null;
        _lblCropRotationValue = null;
        _btnCropReset = null;
        _btnCropApply = null;
        _selectedCropAspectPreset = CropAspectPreset.Original;
        _cropUiSyncInFlight = false;
        _cropX = 0;
        _cropY = 0;
        _cropWidth = 0;
        _cropHeight = 0;
        _cropRotationDegrees = 0;
        _compressionPreviewFileLabel = null;
        _compressionPreviewSizeBadgeLabel = null;
        _compressionPresetLightButton = null;
        _compressionPresetBalancedButton = null;
        _compressionPresetAggressiveButton = null;
        _conversionCodecH264Button = null;
        _conversionCodecH265Button = null;
        _conversionFormatValueLabel = null;
        _conversionEstimatedSizeValueLabel = null;
        _compressionQualityPercentLabel = null;
        _compressionOriginalSizeLabel = null;
        _compressionOutputSizeLabel = null;
        _compressionSavingsTitleLabel = null;
        _compressionSavingsDetailLabel = null;
        _compressionSavingsPercentLabel = null;
        _compressionOutputFooterLabel = null;
        _compressionOriginalBarTrack = null;
        _compressionOriginalBarFill = null;
        _compressionOutputBarTrack = null;
        _compressionOutputBarFill = null;
        _compressionStripMetadataCheckBox = null;
        _compressionTwoPassCheckBox = null;
        _compressionHardwareAccelerationCheckBox = null;
        _compressionActionButton = null;
        _watermarkDetectionPromptTextBox = null;
        _watermarkMaximumDetectionSizeInput = null;
        _watermarkDetectionIntervalInput = null;
        _watermarkFadeInInput = null;
        _watermarkFadeOutInput = null;
        _watermarkUseGpuCheckBox = null;
        _watermarkAuthorizationCheckBox = null;
        _watermarkGpuStatusLabel = null;
        _watermarkRuntimeStatusLabel = null;
        _watermarkRemoveButton = null;
        _watermarkPreviewButton = null;
        _watermarkInstallButton = null;
        _watermarkManualSelectButton = null;
        _watermarkAutoModeRadioButton = null;
        _watermarkManualModeRadioButton = null;
        _watermarkSelectionStatusLabel = null;
        _watermarkMaskPaddingInput = null;
        _watermarkAutomaticSettingsPanel = null;
        _watermarkSelectedRegions = Array.Empty<WatermarkRegion>();
        _watermarkSelectionMediaPath = null;
        _watermarkRuntimeStatus = null;
        _watermarkRuntimeCheckInProgress = false;
        _watermarkInstallationInProgress = false;
        _shutdownCleanupInProgress = false;
        _shutdownConfirmed = false;
        _watermarkAuthorizationMediaPath = null;
        _lastWatermarkProgressStage = null;
        _watermarkActionMode = WatermarkActionMode.None;
        _activityFeedLayout = null;
        _activityEntryCountLabel = null;
        _activityDownloadsCountLabel = null;
        _activityExportsCountLabel = null;
        _activityErrorsCountLabel = null;
        _lastActivityErrorSummary = null;
        _selectedVideoTargetExtension = StudioDefaults.VideoExtension;
        _selectedAudioTargetExtension = "mp3";
        _selectedConversionRequiresVideo = true;
        _selectedVideoCodec = StudioDefaults.VideoCodec;
        _activityFeedEntries.Clear();
        _workspacePagePanels.Clear();
        _workspacePageButtons.Clear();
        _currentWorkspacePage = WorkspacePage.Source;
        _previewDocumentReady = false;
        //=========================================================================

        //== shell defaults =====================================================
        BackColor = AppBackgroundColor;
        DoubleBuffered = true;
        ClientSize = new Size(1520, 860);
        MinimumSize = new Size(1260, 760);
        FormBorderStyle = FormBorderStyle.None;
        AllowDrop = true;
        Padding = Padding.Empty;
        Text = "VeditorWindow";
        //=========================================================================

        //== control copy =======================================================
        lblStatusCaption.Text = "\u25cf";
        lblUrl.Text = "URL";
        lblOutputFolder.Text = "SAVE TO";
        lblVideoQualityCaption.Text = "Compression";
        lblVideoQualityScaleLeft.Text = "Larger output";
        lblVideoQualityScaleRight.Text = "Smaller output";
        chkExtractAudio.Text = "Extract audio only (MP3)";
        chkExtractWavAudio.Text = "Extract audio only (WAV)";
        btnDownload.Text = "Download";
        btnOpenMediaFile.Text = "Open";
        btnPreviewLast.Text = "Latest";
        btnOpenExternal.Text = "Open Folder";
        btnConvertMp3.Text = "MP3";
        btnConvertWav.Text = "WAV";
        btnConvertM4a.Text = "M4A";
        btnConvertMp4.Text = "MP4";
        btnConvertMkv.Text = "MKV";
        btnConvertMov.Text = "MOV";
        lblPreviewState.Text = "Load a media file to begin the live preview.";
        trkVideoQuality.Value = StudioDefaults.QualityTrackValue;
        //=========================================================================

        //== input and action styling ==========================================
        StyleTextInput(txtUrl, "Paste a YouTube or direct video URL");
        StyleTextInput(txtOutputFolder, "Choose where downloaded files should go");
        StyleActionButton(btnDownload, primary: true);
        btnDownload.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        StyleActionButton(btnBrowseOutput, primary: false);
        StylePreviewToolbarButton(btnOpenMediaFile, "Open");
        StylePreviewToolbarButton(btnPreviewLast, "Latest");
        StylePreviewToolbarButton(btnOpenExternal, "Open Folder");
        btnPreviewLast.MinimumSize = new Size(100, 40);
        btnOpenMediaFile.Width = 104;
        btnOpenMediaFile.MinimumSize = new Size(104, 40);
        btnOpenExternal.Width = 148;
        btnOpenExternal.MinimumSize = new Size(148, 40);
        btnOpenExternal.Padding = Padding.Empty;
        ConfigureCompactActionButton(btnBrowseOutput, "Browse");
        ConfigureConvertTargetButton(btnConvertMp3, "MP3");
        ConfigureConvertTargetButton(btnConvertWav, "WAV");
        ConfigureConvertTargetButton(btnConvertM4a, "M4A");
        ConfigureConvertTargetButton(btnConvertMp4, "MP4");
        ConfigureConvertTargetButton(btnConvertMkv, "MKV");
        ConfigureConvertTargetButton(btnConvertMov, "MOV");
        AssignMatteIcon(btnDownload, "download", 22);
        AssignMatteIcon(btnBrowseOutput, "open-folder", 18);
        AssignMatteIcon(btnOpenMediaFile, "open-folder", 18);
        AssignMatteIcon(btnPreviewLast, "history", 18);
        AssignMatteIcon(btnOpenExternal, "open-folder", 18);
        //=========================================================================

        //== label and log styling =============================================
        lblUrl.AutoSize = true;
        lblOutputFolder.AutoSize = true;
        lblUrl.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblOutputFolder.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblUrl.ForeColor = SecondaryTextColor;
        lblOutputFolder.ForeColor = SecondaryTextColor;

        chkExtractAudio.AutoSize = true;
        chkExtractAudio.ForeColor = PrimaryTextColor;
        chkExtractAudio.BackColor = Color.Transparent;
        //== checked-state visibility ==========================================
        // Keep the selected tick visible against the matte application surface
        // at common Windows DPI scales, including the legacy fallback control.
        chkExtractAudio.FlatStyle = chkExtractAudio is ClayCheckBox
            ? FlatStyle.Flat
            : FlatStyle.Standard;
        chkExtractAudio.UseVisualStyleBackColor = false;
        if (chkExtractAudio is not ClayCheckBox)
        {
            chkExtractAudio.Paint += DrawExtractAudioSelectedState;
        }
        chkExtractAudio.CheckedChanged += (_, _) => chkExtractAudio.Invalidate();
        //=====================================================================
        chkExtractAudio.Padding = new Padding(0, 4, 0, 4);

        //== audio extraction options ===========================================
        chkExtractWavAudio.AutoSize = true;
        chkExtractWavAudio.ForeColor = PrimaryTextColor;
        chkExtractWavAudio.BackColor = Color.Transparent;
        chkExtractWavAudio.FlatStyle = FlatStyle.Flat;
        chkExtractWavAudio.UseVisualStyleBackColor = false;
        chkExtractWavAudio.Padding = new Padding(0, 4, 0, 4);
        chkExtractWavAudio.Margin = new Padding(0, 2, 0, 0);
        chkExtractWavAudio.Checked = false;
        chkExtractWavAudio.CheckedChanged += (_, _) => chkExtractWavAudio.Invalidate();
        //=======================================================================

        txtLog.BorderStyle = BorderStyle.None;
        txtLog.BackColor = LogBackgroundColor;
        txtLog.ForeColor = PrimaryTextColor;
        txtLog.Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        txtLog.WordWrap = false;

        lblPreviewState.BackColor = StageBackgroundColor;
        lblPreviewState.ForeColor = Color.WhiteSmoke;
        lblPreviewState.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewState.Padding = new Padding(48);
        lblPreviewState.TextAlign = ContentAlignment.MiddleCenter;

        webPreview.DefaultBackgroundColor = StageBackgroundColor;

        lblStatusCaption.ForeColor = SecondaryTextColor;
        lblStatus.ForeColor = PrimaryTextColor;
        lblStatus.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblStatusCaption.AutoSize = true;
        lblStatusCaption.Margin = Padding.Empty;
        lblStatus.AutoEllipsis = true;
        lblStatus.Margin = new Padding(12, 0, 0, 0);
        lblFileInfo.ForeColor = SecondaryTextColor;
        progressDownload.Height = 6;
        progressDownload.Minimum = 0;
        progressDownload.Maximum = 100;
        progressDownload.Style = ProgressBarStyle.Marquee;
        progressDownload.MarqueeAnimationSpeed = 30;
        progressDownload.Visible = false;
        //=========================================================================

        //== shell composition ==================================================
        Controls.Clear();

        var shellLayout = new TableLayoutPanel
        {
            BackColor = AppBackgroundColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 3
        };
        _studioShellLayout = shellLayout;
        shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, StudioTheme.TitleBarHeight));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var navigationRailWidth = StudioTheme.NavigationWideWidth;

        var workspaceLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(14, 20, 14, 10),
            RowCount = 1
        };
        _studioWorkspaceLayout = workspaceLayout;
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, navigationRailWidth));
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, StudioTheme.InspectorWidth));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var integratedTitleBar = BuildIntegratedTitleBar();
        var editorArea = BuildEditorArea();
        var sidebar = BuildSidebar();
        var navRail = BuildNavigationRail();
        var statusBar = BuildStatusBar();
        var editorHost = CreateEditorHost(editorArea, sidebar);
        _studioSidebar = sidebar;
        _studioNavigationRail = navRail;

        workspaceLayout.Controls.Add(navRail, 0, 0);
        workspaceLayout.Controls.Add(editorHost, 1, 0);
        workspaceLayout.Controls.Add(sidebar, 2, 0);

        shellLayout.Controls.Add(integratedTitleBar, 0, 0);
        shellLayout.Controls.Add(workspaceLayout, 0, 1);
        shellLayout.Controls.Add(statusBar, 0, 2);

        Controls.Add(shellLayout);
        ShowWorkspacePage(WorkspacePage.Source);
        InitializeAdaptiveShell();
        //=========================================================================

        ResumeLayout(performLayout: true);
    }

    private Control BuildNavigationRail()
    {
        //== workspace navigation ==============================================
        var rail = CreateCard(MutedSurfaceColor, CardBorderColor, SurfaceCornerRadius);
        rail.Dock = DockStyle.Fill;
        rail.Margin = Padding.Empty;
        rail.Padding = new Padding(8, 12, 8, 12);

        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            WrapContents = false
        };

        //== visible page order ================================================
        WorkspacePage[] visiblePages =
        [
            WorkspacePage.Source,
            WorkspacePage.Video,
            WorkspacePage.Picture,
            WorkspacePage.Background,
            WorkspacePage.Watermark,
            WorkspacePage.Trim,
            WorkspacePage.Crop,
            WorkspacePage.Activity
        ];
        //=========================================================================

        foreach (var page in visiblePages)
        {
            var info = GetWorkspacePageVisuals(page);
            var button = CreateRailButton(page, info.RailIconKind, info.RailLabel, () => ShowWorkspacePage(page));
            _workspacePageButtons[page] = button;
            ApplyRailButtonState(button, page == _currentWorkspacePage);
            stack.Controls.Add(button);
        }

        rail.Controls.Add(stack);
        return rail;
        //=========================================================================
    }

    private Control BuildSidebar()
    {
        //== inspector shell ====================================================
        var sidebar = CreateCard(CardBackgroundColor, CardBorderColor, SurfaceCornerRadius);
        sidebar.Dock = DockStyle.Fill;
        sidebar.Margin = new Padding(10, 0, 0, 0);
        sidebar.Padding = Padding.Empty;

        //== scrollable workspace pages ======================================
        var viewport = new ClayScrollPanel
        {
            BackColor = CardBackgroundColor,
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 14, 20)
        };
        _workspacePageViewport = viewport;

        var pageHost = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _workspacePageHost = pageHost;

        var sourcePage = BuildWorkspacePage(
            BuildSourceCard(),
            BuildOutputCard(),
            BuildCaptureCard());
        var videoPage = BuildWorkspacePage(BuildVideoConvertCard());
        var picturePage = BuildWorkspacePage(BuildPictureCompressionPage());
        var backgroundPage = BuildWorkspacePage(BuildBackgroundRemovalPage());
        var watermarkPage = BuildWorkspacePage(BuildWatermarkCleanupCard());
        var trimPage = BuildWorkspacePage(BuildTrimInspectorPage());
        var cropPage = BuildWorkspacePage(BuildCropInspectorPage());
        var activityPage = BuildWorkspacePage(BuildActivityPage());

        _workspacePagePanels[WorkspacePage.Source] = sourcePage;
        _workspacePagePanels[WorkspacePage.Video] = videoPage;
        _workspacePagePanels[WorkspacePage.Picture] = picturePage;
        _workspacePagePanels[WorkspacePage.Background] = backgroundPage;
        _workspacePagePanels[WorkspacePage.Watermark] = watermarkPage;
        _workspacePagePanels[WorkspacePage.Trim] = trimPage;
        _workspacePagePanels[WorkspacePage.Crop] = cropPage;
        _workspacePagePanels[WorkspacePage.Activity] = activityPage;

        pageHost.Controls.Add(activityPage);
        pageHost.Controls.Add(cropPage);
        pageHost.Controls.Add(trimPage);
        pageHost.Controls.Add(watermarkPage);
        pageHost.Controls.Add(backgroundPage);
        pageHost.Controls.Add(videoPage);
        pageHost.Controls.Add(picturePage);
        pageHost.Controls.Add(sourcePage);

        viewport.ContentControls.Add(pageHost);

        //== blended sticky action host =======================================
        var stickyActionHost = new Panel
        {
            BackColor = Color.Transparent,
            Dock = DockStyle.Bottom,
            Height = 82,
            Margin = Padding.Empty,
            Padding = new Padding(20, 12, 20, 16),
            Visible = true
        };
        //=====================================================================
        _workspaceStickyActionHost = stickyActionHost;
        if (_compressionActionPanel is not null)
        {
            _compressionActionPanel.Parent?.Controls.Remove(_compressionActionPanel);
            _compressionActionPanel.Dock = DockStyle.Fill;
            _compressionActionPanel.Padding = Padding.Empty;
            stickyActionHost.Controls.Add(_compressionActionPanel);
        }

        if (_watermarkInstallButton is not null)
        {
            _watermarkInstallButton.Parent?.Controls.Remove(_watermarkInstallButton);
            _watermarkInstallButton.Dock = DockStyle.Fill;
            _watermarkInstallButton.Margin = Padding.Empty;
            _watermarkInstallButton.Visible = false;
            stickyActionHost.Controls.Add(_watermarkInstallButton);
        }

        stickyActionHost.Visible = _currentWorkspacePage is WorkspacePage.Video or WorkspacePage.Watermark;

        sidebar.Controls.Add(viewport);
        sidebar.Controls.Add(stickyActionHost);
        return sidebar;
        //=========================================================================
    }

    private static Panel BuildWorkspacePage(params Control[] sections)
    {
        var page = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var stack = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 0
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            section.Dock = DockStyle.Top;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(section, 0, index);
        }

        page.Controls.Add(stack);
        return page;
    }

    private Panel BuildSourceCard()
    {
        //== input collection ===================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 20));

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

        txtUrl.Dock = DockStyle.Top;
        txtUrl.Margin = new Padding(0, 10, 0, 0);

        //== one-line URL field ===============================================
        var urlFieldHost = CreateClayFieldHost(txtUrl);
        urlFieldHost.AutoSize = false;
        urlFieldHost.Dock = DockStyle.Top;
        urlFieldHost.Height = 42;
        urlFieldHost.MaximumSize = new Size(0, 42);
        //=====================================================================

        layout.Controls.Add(CreateSectionTitle("Source URL"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Paste a video, playlist, or direct media link."), 0, 1);
        layout.Controls.Add(lblUrl, 0, 2);
        layout.Controls.Add(urlFieldHost, 0, 3);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Panel BuildOutputCard()
    {
        //== output folder ======================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 20));

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

        var folderRow = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Height = 42,
            Margin = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(0, 42),
            RowCount = 1
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        folderRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        //== one-line destination controls ===================================
        txtOutputFolder.Dock = DockStyle.Fill;
        txtOutputFolder.Margin = Padding.Empty;
        btnBrowseOutput.Dock = DockStyle.Fill;
        btnBrowseOutput.Margin = new Padding(10, 0, 0, 0);

        var outputFieldHost = CreateClayFieldHost(txtOutputFolder);
        outputFieldHost.AutoSize = false;
        outputFieldHost.MaximumSize = new Size(0, 42);

        folderRow.Controls.Add(outputFieldHost, 0, 0);
        folderRow.Controls.Add(btnBrowseOutput, 1, 0);
        //=====================================================================

        layout.Controls.Add(CreateSectionTitle("Destination"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Choose where new captures and conversions are written."), 0, 1);
        layout.Controls.Add(lblOutputFolder, 0, 2);
        layout.Controls.Add(folderRow, 0, 3);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Panel BuildCaptureCard()
    {
        //== download options ===================================================
        var section = CreateSectionPanel(Padding.Empty);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        btnDownload.Dock = DockStyle.Top;
        btnDownload.Margin = new Padding(0, 16, 0, 0);
        btnDownload.Height = 50;

        layout.Controls.Add(CreateSectionTitle("Capture"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Downloads default to the best merged output unless audio-only mode is enabled."), 0, 1);
        layout.Controls.Add(chkExtractAudio, 0, 2);
        layout.Controls.Add(chkExtractWavAudio, 0, 3);
        layout.Controls.Add(btnDownload, 0, 4);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Panel BuildAudioConvertCard()
    {
        //== audio export tools ================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 20));

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

        layout.Controls.Add(CreateSectionTitle("Audio Formats"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Convert the loaded media into lightweight listening files."), 0, 1);
        layout.Controls.Add(CreateButtonGrid(btnConvertMp3, btnConvertWav, btnConvertM4a), 0, 2);
        layout.Controls.Add(CreateSectionSubtitle("The current preview file is used as the conversion source."), 0, 3);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Panel BuildWatermarkCleanupCard()
    {
        //== watermark cleanup workspace ======================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 22));
        var card = CreateInsetPanel(new Padding(16));
        card.AutoSize = false;
        card.Dock = DockStyle.Top;
        card.Margin = Padding.Empty;

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
        for (var rowIndex = 0; rowIndex < layout.RowCount; rowIndex++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateSectionTitle("Watermark cleanup");
        var subtitle = CreateSectionSubtitle(
            "Detect an area automatically or draw one or more fixed rectangles. Processing stays on this PC.");

        _watermarkAutoModeRadioButton = new ClayRadioButton
        {
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 0, 18, 0),
            Text = "Auto detect"
        };
        _watermarkManualModeRadioButton = new ClayRadioButton
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "Selected areas"
        };
        _watermarkAutoModeRadioButton.CheckedChanged += (_, _) => UpdateWatermarkModeUi();
        _watermarkManualModeRadioButton.CheckedChanged += (_, _) => UpdateWatermarkModeUi();

        var modePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 0),
            WrapContents = true
        };
        modePanel.Controls.Add(_watermarkAutoModeRadioButton);
        modePanel.Controls.Add(_watermarkManualModeRadioButton);

        _watermarkDetectionPromptTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            MinimumSize = new Size(0, 32),
            Text = "watermark"
        };
        StyleTextInput(_watermarkDetectionPromptTextBox, "Describe the watermark to detect");

        var promptPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 0),
            RowCount = 2
        };
        promptPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        promptPanel.Controls.Add(CreateMicroCaption("Detection prompt"), 0, 0);
        promptPanel.Controls.Add(CreateClayFieldHost(_watermarkDetectionPromptTextBox), 0, 1);

        _watermarkMaximumDetectionSizeInput = CreateWatermarkNumericInput(
            minimum: 0.1M,
            maximum: 100M,
            value: 10M,
            increment: 0.1M,
            decimalPlaces: 1);
        _watermarkDetectionIntervalInput = CreateWatermarkNumericInput(
            minimum: 1M,
            maximum: 10M,
            value: 3M,
            increment: 1M,
            decimalPlaces: 0);
        _watermarkFadeInInput = CreateWatermarkNumericInput(
            minimum: 0M,
            maximum: 3600M,
            value: 0.25M,
            increment: 0.05M,
            decimalPlaces: 2);
        _watermarkFadeOutInput = CreateWatermarkNumericInput(
            minimum: 0M,
            maximum: 3600M,
            value: 0.25M,
            increment: 0.05M,
            decimalPlaces: 2);

        var settingsPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 4
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settingsPanel.Controls.Add(
            CreateWatermarkSettingRow("Maximum detection size", "%", _watermarkMaximumDetectionSizeInput),
            0,
            0);
        settingsPanel.Controls.Add(
            CreateWatermarkSettingRow("Detection interval", "frames", _watermarkDetectionIntervalInput),
            0,
            1);
        settingsPanel.Controls.Add(
            CreateWatermarkSettingRow("Fade-in", "seconds", _watermarkFadeInInput),
            0,
            2);
        settingsPanel.Controls.Add(
            CreateWatermarkSettingRow("Fade-out", "seconds", _watermarkFadeOutInput),
            0,
            3);

        var automaticSettingsPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        automaticSettingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        automaticSettingsPanel.Controls.Add(promptPanel, 0, 0);
        automaticSettingsPanel.Controls.Add(settingsPanel, 0, 1);
        _watermarkAutomaticSettingsPanel = automaticSettingsPanel;

        _watermarkMaskPaddingInput = CreateWatermarkNumericInput(
            minimum: 0M,
            maximum: 10M,
            value: 0.5M,
            increment: 0.1M,
            decimalPlaces: 1);
        var paddingRow = CreateWatermarkSettingRow(
            "Mask edge padding",
            "%",
            _watermarkMaskPaddingInput);

        _watermarkSelectionStatusLabel = CreateSectionSubtitle(
            "No fixed areas selected. Auto mode will detect across sampled frames.");
        _watermarkSelectionStatusLabel.Margin = new Padding(0, 8, 0, 0);

        _watermarkUseGpuCheckBox = CreateWatermarkCheckBox(
            "Use GPU when available",
            isChecked: true);

        _watermarkRuntimeStatusLabel = CreateWatermarkStatusValueLabel("Checking...");
        _watermarkGpuStatusLabel = CreateWatermarkStatusValueLabel("Checking...");

        var statusCard = CreateCard(InputBackgroundColor, CardBorderColor, 12);
        statusCard.AutoSize = true;
        statusCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        statusCard.Dock = DockStyle.Top;
        statusCard.Margin = new Padding(0, 12, 0, 0);
        statusCard.Padding = new Padding(12);

        var statusLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusLayout.Controls.Add(
            CreateWatermarkStatusRow("Runtime", _watermarkRuntimeStatusLabel),
            0,
            0);
        statusLayout.Controls.Add(
            CreateWatermarkStatusRow("GPU", _watermarkGpuStatusLabel),
            0,
            1);
        statusCard.Controls.Add(statusLayout);

        _watermarkAuthorizationCheckBox = CreateWatermarkCheckBox(
            "I confirm that I own this media or have permission to modify it.",
            isChecked: false);
        _watermarkAuthorizationCheckBox.AutoSize = false;
        _watermarkAuthorizationCheckBox.Dock = DockStyle.Top;
        _watermarkAuthorizationCheckBox.Height = 58;
        _watermarkAuthorizationCheckBox.MaximumSize = Size.Empty;
        _watermarkAuthorizationCheckBox.Margin = new Padding(0, 14, 0, 0);
        _watermarkAuthorizationCheckBox.CheckedChanged += (_, _) =>
        {
            _watermarkAuthorizationMediaPath = _watermarkAuthorizationCheckBox.Checked
                ? GetPreferredMediaPath()
                : null;
            UpdateWatermarkButtons();
        };

        _watermarkRemoveButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Text = "Remove watermark"
        };
        StyleActionButton(_watermarkRemoveButton, primary: true);
        AssignMatteIcon(_watermarkRemoveButton, "cleanup", 20);
        _watermarkRemoveButton.Click += btnWatermarkRemove_Click;

        _watermarkPreviewButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Preview detection"
        };
        StyleActionButton(_watermarkPreviewButton, primary: false);
        AssignMatteIcon(_watermarkPreviewButton, "play", 19);
        _watermarkPreviewButton.Click += btnWatermarkPreview_Click;

        _watermarkManualSelectButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Select areas manually"
        };
        StyleActionButton(_watermarkManualSelectButton, primary: false);
        AssignMatteIcon(_watermarkManualSelectButton, "watermark", 20);
        _watermarkManualSelectButton.Click += btnWatermarkManualSelect_Click;

        _watermarkInstallButton = new ClayButton
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Install or repair runtime"
        };
        StyleActionButton(_watermarkInstallButton, primary: false);
        AssignMatteIcon(_watermarkInstallButton, "download", 19);
        _watermarkInstallButton.Click += btnWatermarkInstall_Click;

        var actionPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 14, 0, 0),
            RowCount = 3
        };
        actionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var rowIndex = 0; rowIndex < actionPanel.RowCount; rowIndex++)
        {
            actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        actionPanel.Controls.Add(_watermarkRemoveButton, 0, 0);
        actionPanel.Controls.Add(_watermarkPreviewButton, 0, 1);
        actionPanel.Controls.Add(_watermarkManualSelectButton, 0, 2);

        //== action stack sizing =============================================
        var actionPanelMinimumHeight =
            _watermarkRemoveButton.Height + _watermarkRemoveButton.Margin.Vertical +
            _watermarkPreviewButton.Height + _watermarkPreviewButton.Margin.Vertical +
            _watermarkManualSelectButton.Height + _watermarkManualSelectButton.Margin.Vertical;
        actionPanel.MinimumSize = new Size(0, actionPanelMinimumHeight);
        //=====================================================================

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(modePanel, 0, 2);
        layout.Controls.Add(automaticSettingsPanel, 0, 3);
        layout.Controls.Add(paddingRow, 0, 4);
        layout.Controls.Add(_watermarkSelectionStatusLabel, 0, 5);
        layout.Controls.Add(_watermarkUseGpuCheckBox, 0, 6);
        layout.Controls.Add(statusCard, 0, 7);
        layout.Controls.Add(_watermarkAuthorizationCheckBox, 0, 8);
        layout.Controls.Add(actionPanel, 0, 9);

        //== card height synchronization =====================================
        card.Controls.Add(layout);
        layout.SizeChanged += (_, _) => SynchronizeCardHeight();
        SynchronizeCardHeight();

        void SynchronizeCardHeight()
        {
            const int bottomClearance = 28;
            var requiredHeight = layout.Height + card.Padding.Vertical + bottomClearance;
            if (requiredHeight > 0 && card.Height != requiredHeight)
            {
                card.Height = requiredHeight;
            }
        }
        //=====================================================================

        section.Controls.Add(card);
        return section;
        //=====================================================================
    }

    private static NumericUpDown CreateWatermarkNumericInput(
        decimal minimum,
        decimal maximum,
        decimal value,
        decimal increment,
        int decimalPlaces)
    {
        return new NumericUpDown
        {
            BackColor = InputBackgroundColor,
            BorderStyle = BorderStyle.None,
            DecimalPlaces = decimalPlaces,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Increment = increment,
            Maximum = maximum,
            Minimum = minimum,
            MinimumSize = new Size(92, 30),
            TextAlign = HorizontalAlignment.Right,
            Value = value,
            Width = 92
        };
    }

    private static Control CreateWatermarkSettingRow(
        string caption,
        string unit,
        NumericUpDown input)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8),
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var captionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 6, 10, 0),
            MaximumSize = new Size(150, 0),
            Text = caption
        };
        var unitLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(7, 7, 0, 0),
            Text = unit
        };

        input.Anchor = AnchorStyles.Right;
        input.Margin = Padding.Empty;
        row.Controls.Add(captionLabel, 0, 0);
        row.Controls.Add(CreateClayNumericHost(input), 1, 0);
        row.Controls.Add(unitLabel, 2, 0);
        return row;
    }

    private static Control CreateClayNumericHost(NumericUpDown input)
    {
        //== recessed numeric field ==========================================
        var host = new ClayPanel
        {
            BackColor = StudioTheme.SurfaceInput,
            CornerRadius = 10,
            Height = 36,
            Margin = Padding.Empty,
            Padding = new Padding(8, 7, 7, 5),
            SurfaceKind = ClaySurfaceKind.Inset,
            Width = 106
        };
        input.Dock = DockStyle.Fill;
        input.Margin = Padding.Empty;
        host.Controls.Add(input);
        return host;
        //=====================================================================
    }

    private static CheckBox CreateWatermarkCheckBox(string text, bool isChecked)
    {
        return new ClayCheckBox
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Checked = isChecked,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(285, 0),
            Padding = new Padding(0, 3, 0, 3),
            Text = text
        };
    }

    private static Label CreateWatermarkStatusValueLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            Font = new Font("Segoe UI Semibold", 8.6F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(12, 0, 0, 6),
            MaximumSize = new Size(180, 0),
            Text = text,
            TextAlign = ContentAlignment.TopRight
        };
    }

    private static Control CreateWatermarkStatusRow(string caption, Label valueLabel)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var captionLabel = CreateMicroCaption(caption);
        captionLabel.Margin = new Padding(0, 0, 10, 6);
        row.Controls.Add(captionLabel, 0, 0);
        row.Controls.Add(valueLabel, 1, 0);
        return row;
    }

    private Panel BuildVideoConvertCard()
    {
        //== convert workspace =================================================
        var section = CreateSectionPanel(Padding.Empty);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 14
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        AddFullWidthTableRow(layout, BuildConvertFormatSection("Video", btnConvertMp4, btnConvertMkv, btnConvertMov), 0);
        layout.Controls.Add(CreateConvertDivider(), 0, 1);
        AddFullWidthTableRow(layout, BuildConvertFormatSection("Audio", btnConvertMp3, btnConvertWav, btnConvertM4a), 2);
        layout.Controls.Add(CreateConvertDivider(), 0, 3);
        AddFullWidthTableRow(layout, BuildConvertQualitySection(), 4);
        AddFullWidthTableRow(layout, BuildCompressionPresetPanel(), 5);
        layout.Controls.Add(CreateConvertDivider(), 0, 6);
        AddFullWidthTableRow(layout, BuildConvertCodecSection(), 7);
        layout.Controls.Add(CreateConvertDivider(), 0, 8);
        AddFullWidthTableRow(layout, BuildCompressionOptionsPanel(), 9);
        layout.Controls.Add(CreateConvertDivider(), 0, 10);
        AddFullWidthTableRow(layout, BuildCompressionSizePanel(), 11);
        AddFullWidthTableRow(layout, BuildConvertSummarySection(), 12);
        AddFullWidthTableRow(layout, BuildCompressionActionPanel(), 13);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildConvertFormatSection(string title, params Button[] buttons)
    {
        //== format selection ===================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 0));

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = buttons.Length,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 1
        };

        for (var index = 0; index < buttons.Length; index++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / buttons.Length));

            var button = buttons[index];
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, index < buttons.Length - 1 ? 10 : 0, 0);
            grid.Controls.Add(button, index, 0);
        }

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption(title), 0, 0);
        layout.Controls.Add(grid, 0, 1);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildConvertQualitySection()
    {
        //== quality selection ==================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 0));

        var headerRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblVideoQualityCaption.ForeColor = SecondaryTextColor;
        lblVideoQualityCaption.Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityCaption.Margin = Padding.Empty;
        lblVideoQualityCaption.Text = "QUALITY";

        _compressionQualityPercentLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "78%"
        };

        headerRow.Controls.Add(lblVideoQualityCaption, 0, 0);
        headerRow.Controls.Add(_compressionQualityPercentLabel, 1, 0);

        trkVideoQuality.AutoSize = false;
        trkVideoQuality.Dock = DockStyle.Top;
        trkVideoQuality.Height = 30;
        trkVideoQuality.Margin = new Padding(0, 12, 0, 0);
        trkVideoQuality.BackColor = Color.Transparent;
        trkVideoQuality.TickStyle = TickStyle.None;

        var scaleRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1
        };
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        lblVideoQualityScaleLeft.AutoSize = true;
        lblVideoQualityScaleLeft.Dock = DockStyle.Left;
        lblVideoQualityScaleLeft.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityScaleLeft.ForeColor = SecondaryTextColor;
        lblVideoQualityScaleLeft.Margin = Padding.Empty;
        lblVideoQualityScaleLeft.Text = "Smaller file";

        lblVideoQualityScaleRight.AutoSize = true;
        lblVideoQualityScaleRight.Dock = DockStyle.Right;
        lblVideoQualityScaleRight.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityScaleRight.ForeColor = SecondaryTextColor;
        lblVideoQualityScaleRight.Margin = Padding.Empty;
        lblVideoQualityScaleRight.Text = "Better quality";

        scaleRow.Controls.Add(lblVideoQualityScaleLeft, 0, 0);
        scaleRow.Controls.Add(lblVideoQualityScaleRight, 1, 0);

        lblVideoQualityHint.AutoEllipsis = false;
        lblVideoQualityHint.AutoSize = true;
        lblVideoQualityHint.Dock = DockStyle.Top;
        lblVideoQualityHint.Font = new Font("Segoe UI", 8.7F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityHint.ForeColor = SecondaryTextColor;
        lblVideoQualityHint.Margin = new Padding(0, 10, 0, 0);
        lblVideoQualityHint.MaximumSize = new Size(0, 0);

        lblVideoQualityValue.AutoSize = true;
        lblVideoQualityValue.Dock = DockStyle.Top;
        lblVideoQualityValue.Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityValue.ForeColor = PrimaryTextColor;
        lblVideoQualityValue.Margin = new Padding(0, 6, 0, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(headerRow, 0, 0);
        layout.Controls.Add(trkVideoQuality, 0, 1);
        layout.Controls.Add(scaleRow, 0, 2);
        layout.Controls.Add(lblVideoQualityHint, 0, 3);
        layout.Controls.Add(lblVideoQualityValue, 0, 4);
        layout.SizeChanged += (_, _) =>
        {
            //== measurement-aware text wrapping ==============================
            lblVideoQualityHint.MaximumSize = new Size(Math.Max(120, layout.ClientSize.Width), 0);
            //=================================================================
        };

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildConvertCodecSection()
    {
        //== codec selection ====================================================
        var section = CreateSectionPanel(new Padding(0, 0, 0, 0));

        _conversionCodecH264Button = CreateConvertSelectorButton("H264", () => SelectVideoCodec("h264"));
        _conversionCodecH265Button = CreateConvertSelectorButton("H265", () => SelectVideoCodec("h265"));

        _conversionCodecH264Button.Margin = new Padding(0, 0, 10, 0);
        _conversionCodecH265Button.Margin = Padding.Empty;

        var buttonRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 1
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttonRow.Controls.Add(_conversionCodecH264Button, 0, 0);
        buttonRow.Controls.Add(_conversionCodecH265Button, 1, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption("Codec"), 0, 0);
        layout.Controls.Add(buttonRow, 0, 1);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildConvertSummarySection()
    {
        //== conversion summary ================================================
        var section = CreateSectionPanel(new Padding(0, 18, 0, 0));

        var summaryCard = CreateCard(InputBackgroundColor, CardBorderColor, 14);
        summaryCard.AutoSize = true;
        summaryCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        summaryCard.Dock = DockStyle.Top;
        summaryCard.Margin = Padding.Empty;
        summaryCard.Padding = new Padding(14);

        _conversionFormatValueLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = ".mp4"
        };

        _conversionEstimatedSizeValueLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = AccentColor,
            Margin = Padding.Empty,
            Text = "~ --"
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(CreateMicroCaption("Format"), 0, 0);
        layout.Controls.Add(_conversionFormatValueLabel, 1, 0);
        layout.Controls.Add(CreateMicroCaption("Est. size"), 0, 1);
        layout.Controls.Add(_conversionEstimatedSizeValueLabel, 1, 1);

        summaryCard.Controls.Add(layout);
        section.Controls.Add(summaryCard);
        return section;
        //=========================================================================
    }

    private static Control CreateConvertDivider()
    {
        return new Panel
        {
            BackColor = CardBorderColor,
            Dock = DockStyle.Top,
            Height = 1,
            Margin = new Padding(0, 18, 0, 18)
        };
    }

    private Button CreateConvertSelectorButton(string text, Action onClick)
    {
        var button = new ClayButton();
        ConfigureConvertTargetButton(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Panel BuildCompressionPreviewCard()
    {
        //== preview summary ====================================================
        var previewCard = CreateInsetPanel(new Padding(14));
        previewCard.Margin = new Padding(0, 2, 0, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var previewCaption = CreateMicroCaption("Preview");
        previewCaption.Margin = Padding.Empty;

        var previewSurface = CreateCard(Color.FromArgb(18, 24, 43), Color.FromArgb(34, 45, 72), 16);
        previewSurface.AutoSize = true;
        previewSurface.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        previewSurface.Dock = DockStyle.Top;
        previewSurface.MinimumSize = new Size(0, 148);
        previewSurface.Margin = new Padding(0, 12, 0, 0);
        previewSurface.Padding = new Padding(14);

        var previewSurfaceLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        previewSurfaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewSurfaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewSurfaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewSurfaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _compressionPreviewFileLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(214, 224, 248),
            Margin = Padding.Empty,
            MinimumSize = new Size(0, 24),
            Text = "No media loaded"
        };

        var previewGlyph = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Symbol", 50F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(255, 70, 98),
            Margin = Padding.Empty,
            Text = "\u25B6",
            TextAlign = ContentAlignment.MiddleCenter
        };

        _compressionPreviewSizeBadgeLabel = new Label
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            BackColor = AccentSoftColor,
            Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = AccentColor,
            Margin = Padding.Empty,
            Padding = new Padding(10, 6, 10, 6),
            Text = "--"
        };
        _compressionPreviewSizeBadgeLabel.SizeChanged += (_, _) => ApplyRoundedRegion(_compressionPreviewSizeBadgeLabel, 10);
        ApplyRoundedRegion(_compressionPreviewSizeBadgeLabel, 10);

        previewSurfaceLayout.Controls.Add(_compressionPreviewFileLabel, 0, 0);
        previewSurfaceLayout.Controls.Add(previewGlyph, 0, 1);
        previewSurfaceLayout.Controls.Add(_compressionPreviewSizeBadgeLabel, 0, 2);
        previewSurface.Controls.Add(previewSurfaceLayout);

        layout.Controls.Add(previewCaption, 0, 0);
        layout.Controls.Add(previewSurface, 0, 1);
        previewCard.Controls.Add(layout);
        return previewCard;
        //=========================================================================
    }

    private Panel BuildCompressionPresetPanel()
    {
        //== preset selection ===================================================
        var presetCard = CreateSectionPanel(new Padding(0, 14, 0, 0));
        presetCard.Margin = new Padding(0, 14, 0, 0);
        presetCard.Padding = new Padding(14);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var presetGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1
        };
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));

        _compressionPresetLightButton = CreateCompressionPresetButton("Light", "~20%", 4);
        _compressionPresetBalancedButton = CreateCompressionPresetButton("Balanced", "~40%", 2);
        _compressionPresetAggressiveButton = CreateCompressionPresetButton("Strong", "~75%", 1);

        _compressionPresetLightButton.Margin = new Padding(0, 0, 10, 0);
        _compressionPresetBalancedButton.Margin = new Padding(0, 0, 10, 0);
        _compressionPresetAggressiveButton.Margin = Padding.Empty;

        presetGrid.Controls.Add(_compressionPresetLightButton, 0, 0);
        presetGrid.Controls.Add(_compressionPresetBalancedButton, 1, 0);
        presetGrid.Controls.Add(_compressionPresetAggressiveButton, 2, 0);

        layout.Controls.Add(CreateMicroCaption("Preset"), 0, 0);
        layout.Controls.Add(presetGrid, 0, 1);
        presetCard.Controls.Add(layout);
        return presetCard;
        //=========================================================================
    }

    private Panel BuildCompressionQualityPanel()
    {
        //== quality tuning =====================================================
        var qualityPanel = CreateInsetPanel(new Padding(14));
        qualityPanel.Margin = new Padding(0, 14, 0, 0);

        var headerRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblVideoQualityCaption.ForeColor = SecondaryTextColor;
        lblVideoQualityCaption.Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityCaption.Text = "QUALITY";

        _compressionQualityPercentLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "60%"
        };

        headerRow.Controls.Add(lblVideoQualityCaption, 0, 0);
        headerRow.Controls.Add(_compressionQualityPercentLabel, 1, 0);

        trkVideoQuality.Dock = DockStyle.Top;
        trkVideoQuality.Margin = new Padding(0, 10, 0, 0);
        trkVideoQuality.BackColor = MutedSurfaceColor;
        trkVideoQuality.AutoSize = false;
        trkVideoQuality.Height = 34;

        var scaleRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1
        };
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        lblVideoQualityScaleLeft.AutoSize = true;
        lblVideoQualityScaleLeft.Dock = DockStyle.Left;
        lblVideoQualityScaleLeft.ForeColor = Color.FromArgb(255, 101, 122);
        lblVideoQualityScaleLeft.Text = "Tiny file";

        lblVideoQualityScaleRight.AutoSize = true;
        lblVideoQualityScaleRight.Dock = DockStyle.Right;
        lblVideoQualityScaleRight.ForeColor = Color.FromArgb(103, 255, 190);
        lblVideoQualityScaleRight.Text = "Full quality";

        scaleRow.Controls.Add(lblVideoQualityScaleLeft, 0, 0);
        scaleRow.Controls.Add(lblVideoQualityScaleRight, 1, 0);

        lblVideoQualityValue.AutoSize = true;
        lblVideoQualityValue.Dock = DockStyle.Top;
        lblVideoQualityValue.Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityValue.ForeColor = PrimaryTextColor;
        lblVideoQualityValue.Margin = new Padding(0, 12, 0, 0);

        lblVideoQualityHint.Dock = DockStyle.Top;
        lblVideoQualityHint.ForeColor = SecondaryTextColor;
        lblVideoQualityHint.Margin = new Padding(0, 6, 0, 0);
        lblVideoQualityHint.MaximumSize = new Size(0, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(headerRow, 0, 0);
        layout.Controls.Add(trkVideoQuality, 0, 1);
        layout.Controls.Add(scaleRow, 0, 2);
        layout.Controls.Add(lblVideoQualityValue, 0, 3);
        layout.Controls.Add(lblVideoQualityHint, 0, 4);

        qualityPanel.Controls.Add(layout);
        return qualityPanel;
        //=========================================================================
    }

    private Panel BuildCompressionSizePanel()
    {
        //== size summary =======================================================
        var sizePanel = CreateInsetPanel(new Padding(14));
        sizePanel.Margin = new Padding(0, 14, 0, 0);

        _compressionOriginalSizeLabel = CreateMetricValueLabel();
        _compressionOutputSizeLabel = CreateMetricValueLabel();
        _compressionOriginalBarTrack = CreateCompressionBarTrack(Color.FromArgb(88, 94, 110), out _compressionOriginalBarFill);
        _compressionOutputBarTrack = CreateCompressionBarTrack(AccentColor, out _compressionOutputBarFill);

        var originalRow = CreateMetricRow("ORIGINAL", _compressionOriginalSizeLabel, _compressionOriginalBarTrack);
        var outputRow = CreateMetricRow("OUTPUT", _compressionOutputSizeLabel, _compressionOutputBarTrack);

        const int savingsCardHeight = 118;
        var savingsCard = CreateCard(Color.FromArgb(15, 22, 31), Color.FromArgb(39, 89, 121), 16);
        savingsCard.Dock = DockStyle.Top;
        savingsCard.Height = savingsCardHeight;
        savingsCard.MinimumSize = new Size(0, savingsCardHeight);
        savingsCard.Margin = new Padding(0, 14, 0, 0);
        savingsCard.Padding = new Padding(14);

        var savingsLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        savingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        savingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        savingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var savingsIcon = new Label
        {
            AutoSize = false,
            BackColor = AccentSoftColor,
            Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = AccentColor,
            Margin = new Padding(0, 0, 12, 0),
            Size = new Size(46, 46),
            Text = "\u2198",
            TextAlign = ContentAlignment.MiddleCenter
        };
        savingsIcon.SizeChanged += (_, _) => ApplyRoundedRegion(savingsIcon, 12);
        ApplyRoundedRegion(savingsIcon, 12);

        var savingsCopy = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        savingsCopy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _compressionSavingsTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.8F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "Save --"
        };

        _compressionSavingsDetailLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 5, 0, 0),
            MaximumSize = new Size(170, 0),
            Text = "Load a video to estimate savings."
        };

        _compressionSavingsPercentLabel = new Label
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = AccentColor,
            Margin = Padding.Empty,
            Text = "--%"
        };

        savingsCopy.Controls.Add(_compressionSavingsTitleLabel, 0, 0);
        savingsCopy.Controls.Add(_compressionSavingsDetailLabel, 0, 1);

        savingsLayout.Controls.Add(savingsIcon, 0, 0);
        savingsLayout.Controls.Add(savingsCopy, 1, 0);
        savingsLayout.Controls.Add(_compressionSavingsPercentLabel, 2, 0);
        savingsCard.Controls.Add(savingsLayout);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, savingsCardHeight + savingsCard.Margin.Vertical));
        layout.Controls.Add(originalRow, 0, 0);
        layout.Controls.Add(outputRow, 0, 1);
        layout.Controls.Add(savingsCard, 0, 2);

        sizePanel.Controls.Add(layout);
        return sizePanel;
        //=========================================================================
    }

    private Panel BuildCompressionOptionsPanel()
    {
        //== compressor options =================================================
        var section = CreateSectionPanel(new Padding(0, 14, 0, 0));

        _compressionStripMetadataCheckBox = CreateCompressionOptionCheckBox(isChecked: true);
        _compressionTwoPassCheckBox = CreateCompressionOptionCheckBox(isChecked: false);
        _compressionHardwareAccelerationCheckBox = CreateCompressionOptionCheckBox(isChecked: true);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateMicroCaption("Options"), 0, 0);
        layout.Controls.Add(CreateCompressionOptionRow(_compressionStripMetadataCheckBox, "Strip metadata", "Remove EXIF, GPS, and embedded tags."), 0, 1);
        layout.Controls.Add(CreateCompressionOptionRow(_compressionTwoPassCheckBox, "2-pass encode", "Spend more time for steadier bitrate efficiency."), 0, 2);
        layout.Controls.Add(CreateCompressionOptionRow(_compressionHardwareAccelerationCheckBox, "Hardware accel.", "Prefer GPU video encoding when ffmpeg and the device support it."), 0, 3);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Panel BuildCompressionFormatPanel()
    {
        //== output format ======================================================
        var formatCard = CreateInsetPanel(new Padding(14));
        formatCard.Margin = new Padding(0, 14, 0, 0);

        var formatGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1
        };
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));

        btnConvertMp4.Margin = new Padding(0, 0, 10, 0);
        btnConvertMkv.Margin = new Padding(0, 0, 10, 0);
        btnConvertMov.Margin = Padding.Empty;
        btnConvertMp4.Dock = DockStyle.Fill;
        btnConvertMkv.Dock = DockStyle.Fill;
        btnConvertMov.Dock = DockStyle.Fill;

        formatGrid.Controls.Add(btnConvertMp4, 0, 0);
        formatGrid.Controls.Add(btnConvertMkv, 1, 0);
        formatGrid.Controls.Add(btnConvertMov, 2, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption("Format"), 0, 0);
        layout.Controls.Add(formatGrid, 0, 1);

        formatCard.Controls.Add(layout);
        return formatCard;
        //=========================================================================
    }

    private Panel BuildCompressionActionPanel()
    {
        //== convert action =====================================================
        var section = CreateSectionPanel(new Padding(0, 18, 0, 0));
        _compressionActionPanel = section;

        _compressionActionButton = new ClayButton();
        StyleActionButton(_compressionActionButton, primary: true);
        _compressionActionButton.Dock = DockStyle.Top;
        _compressionActionButton.Height = 46;
        _compressionActionButton.Margin = Padding.Empty;
        _compressionActionButton.Text = "Convert to MP4";
        AssignMatteIcon(_compressionActionButton, "convert", 21);
        _compressionActionButton.Click += btnConvertSelectedMedia_Click;

        section.Controls.Add(_compressionActionButton);
        return section;
        //=========================================================================
    }

    private Panel BuildTrimInspectorPage()
    {
        //== trim controls ======================================================
        var section = CreateSectionPanel(Padding.Empty);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 9
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var positionCard = CreateInsetPanel(new Padding(18, 16, 18, 16));
        positionCard.Margin = new Padding(0, 0, 0, 18);
        positionCard.MinimumSize = new Size(0, 110);

        var positionLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        positionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        positionLayout.Controls.Add(CreateMicroCaption("Current Position"), 0, 0);

        _lblTrimCurrentPositionValue = CreateTrimTimeDisplayLabel(24F);
        _lblTrimCurrentPositionValue.Dock = DockStyle.Top;
        _lblTrimCurrentPositionValue.Margin = new Padding(0, 12, 0, 0);
        positionLayout.Controls.Add(_lblTrimCurrentPositionValue, 0, 1);
        positionCard.Controls.Add(positionLayout);

        var pointLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        pointLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pointLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pointLayout.Controls.Add(CreateMicroCaption("In Point"), 0, 0);
        pointLayout.Controls.Add(CreateMicroCaption("Out Point"), 1, 0);

        _lblTrimInPointValue = CreateTrimTimeDisplayLabel(16F);
        _lblTrimOutPointValue = CreateTrimTimeDisplayLabel(16F);

        var inPointCard = CreateInsetPanel(new Padding(12));
        var outPointCard = CreateInsetPanel(new Padding(12));
        inPointCard.Margin = new Padding(0, 10, 8, 0);
        outPointCard.Margin = new Padding(8, 10, 0, 0);
        inPointCard.Controls.Add(_lblTrimInPointValue);
        outPointCard.Controls.Add(_lblTrimOutPointValue);
        pointLayout.Controls.Add(inPointCard, 0, 1);
        pointLayout.Controls.Add(outPointCard, 1, 1);

        var pointButtonRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 14, 0, 18),
            RowCount = 1
        };
        pointButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pointButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        _btnTrimSetIn = CreateTrimUtilityButton("Set In", "previous", btnTrimSetIn_Click);
        _btnTrimSetOut = CreateTrimUtilityButton("Set Out", "next", btnTrimSetOut_Click);
        _btnTrimSetIn.Margin = new Padding(0, 0, 8, 0);
        _btnTrimSetOut.Margin = new Padding(8, 0, 0, 0);
        pointButtonRow.Controls.Add(_btnTrimSetIn, 0, 0);
        pointButtonRow.Controls.Add(_btnTrimSetOut, 1, 0);

        var statsLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 3
        };
        statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblTrimSourceDurationValue = CreateTrimStatValueLabel();
        _lblTrimSelectionValue = CreateTrimStatValueLabel();
        _lblTrimTrimmedValue = CreateTrimStatValueLabel();
        _lblTrimSelectionValue.ForeColor = AccentColor;

        statsLayout.Controls.Add(CreateTrimStatCaption("Source duration"), 0, 0);
        statsLayout.Controls.Add(_lblTrimSourceDurationValue, 1, 0);
        statsLayout.Controls.Add(CreateTrimStatCaption("Selection"), 0, 1);
        statsLayout.Controls.Add(_lblTrimSelectionValue, 1, 1);
        statsLayout.Controls.Add(CreateTrimStatCaption("Trimmed"), 0, 2);
        statsLayout.Controls.Add(_lblTrimTrimmedValue, 1, 2);

        var divider = new Panel
        {
            BackColor = CardBorderColor,
            Dock = DockStyle.Top,
            Height = 1,
            Margin = new Padding(0, 18, 0, 18)
        };

        var previewButtonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false
        };

        _btnTrimJumpToIn = CreateTrimTransportButton("previous", "Jump to in point", btnTrimJumpToIn_Click);
        _btnTrimPreviewSelection = CreateTrimTransportButton("play", "Preview selection", btnTrimPreviewSelection_Click);
        _btnTrimJumpToOut = CreateTrimTransportButton("next", "Jump to out point", btnTrimJumpToOut_Click);
        _btnTrimJumpToIn.Margin = new Padding(0, 0, 10, 0);
        _btnTrimPreviewSelection.Margin = new Padding(0, 0, 10, 0);
        previewButtonRow.Controls.Add(_btnTrimJumpToIn);
        previewButtonRow.Controls.Add(_btnTrimPreviewSelection);
        previewButtonRow.Controls.Add(_btnTrimJumpToOut);

        _btnTrimResetRange = CreateResetTextButton();
        _btnTrimExport = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 46,
            Margin = new Padding(0, 24, 0, 0),
            Text = "Export Trimmed Clip"
        };
        StyleActionButton(_btnTrimExport, primary: true);
        AssignMatteIcon(_btnTrimExport, "trim", 21);
        _btnTrimExport.Click += btnTrimExport_Click;

        layout.Controls.Add(positionCard, 0, 0);
        layout.Controls.Add(pointLayout, 0, 1);
        layout.Controls.Add(pointButtonRow, 0, 2);
        layout.Controls.Add(statsLayout, 0, 3);
        layout.Controls.Add(divider, 0, 4);
        layout.Controls.Add(CreateMicroCaption("Preview Range"), 0, 5);
        layout.Controls.Add(previewButtonRow, 0, 6);
        layout.Controls.Add(_btnTrimResetRange, 0, 7);
        layout.Controls.Add(_btnTrimExport, 0, 8);

        section.Controls.Add(layout);
        UpdateTrimUi();
        return section;
        //=========================================================================
    }

    private Panel BuildCropInspectorPage()
    {
        //== crop controls ======================================================
        var section = CreateSectionPanel(Padding.Empty);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateSectionTitle("Crop & Rotate"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Frame the visible region, fine-tune the crop box, and export a cleaned-up clip."), 0, 1);
        layout.Controls.Add(BuildCropAspectSection(), 0, 2);
        layout.Controls.Add(BuildCropRegionSection(), 0, 3);
        layout.Controls.Add(BuildCropRotationSection(), 0, 4);
        layout.Controls.Add(BuildCropSummarySection(), 0, 5);

        _btnCropReset = CreateCropResetTextButton();
        _btnCropApply = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 48,
            Margin = new Padding(0, 18, 0, 0),
            Text = "Apply Crop & Rotation"
        };
        StyleActionButton(_btnCropApply, primary: true);
        AssignMatteIcon(_btnCropApply, "crop", 21);
        _btnCropApply.Click += btnCropApply_Click;

        layout.Controls.Add(_btnCropReset, 0, 6);
        layout.Controls.Add(_btnCropApply, 0, 7);

        section.Controls.Add(layout);
        UpdateCropUi();
        return section;
        //=========================================================================
    }

    private Control BuildCropAspectSection()
    {
        //== aspect ratio selection =============================================
        var section = CreateSectionPanel(new Padding(0, 12, 0, 0));

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 2
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _cropAspectCustomButton = CreateCropSelectorButton("Custom", () => SelectCropAspectPreset(CropAspectPreset.Custom));
        _cropAspectOriginalButton = CreateCropSelectorButton("Original", () => SelectCropAspectPreset(CropAspectPreset.Original));
        _cropAspectSquareButton = CreateCropSelectorButton("Square", () => SelectCropAspectPreset(CropAspectPreset.Square));
        _cropAspectLandscapeButton = CreateCropSelectorButton("16:9", () => SelectCropAspectPreset(CropAspectPreset.Landscape16x9));
        _cropAspectPortraitButton = CreateCropSelectorButton("9:16", () => SelectCropAspectPreset(CropAspectPreset.Portrait9x16));
        _cropAspectClassicButton = CreateCropSelectorButton("4:3", () => SelectCropAspectPreset(CropAspectPreset.Landscape4x3));

        grid.Controls.Add(WrapCropGridButton(_cropAspectCustomButton, 0, 0), 0, 0);
        grid.Controls.Add(WrapCropGridButton(_cropAspectOriginalButton, 1, 0), 1, 0);
        grid.Controls.Add(WrapCropGridButton(_cropAspectSquareButton, 2, 0), 2, 0);
        grid.Controls.Add(WrapCropGridButton(_cropAspectLandscapeButton, 0, 1), 0, 1);
        grid.Controls.Add(WrapCropGridButton(_cropAspectPortraitButton, 1, 1), 1, 1);
        grid.Controls.Add(WrapCropGridButton(_cropAspectClassicButton, 2, 1), 2, 1);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption("Aspect Ratio"), 0, 0);
        layout.Controls.Add(grid, 0, 1);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildCropRegionSection()
    {
        //== crop region inputs =================================================
        var section = CreateSectionPanel(new Padding(0, 18, 0, 0));

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 2
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateCropInputCard("X", out _txtCropX, () => _cropX, value =>
        {
            _cropX = value;
            _selectedCropAspectPreset = CropAspectPreset.Custom;
            NormalizeCropSelection();
        }), 0, 0);
        grid.Controls.Add(CreateCropInputCard("Y", out _txtCropY, () => _cropY, value =>
        {
            _cropY = value;
            _selectedCropAspectPreset = CropAspectPreset.Custom;
            NormalizeCropSelection();
        }), 1, 0);
        grid.Controls.Add(CreateCropInputCard("Width", out _txtCropWidth, () => _cropWidth, value =>
        {
            _cropWidth = value;
            _selectedCropAspectPreset = CropAspectPreset.Custom;
            NormalizeCropSelection();
        }), 0, 1);
        grid.Controls.Add(CreateCropInputCard("Height", out _txtCropHeight, () => _cropHeight, value =>
        {
            _cropHeight = value;
            _selectedCropAspectPreset = CropAspectPreset.Custom;
            NormalizeCropSelection();
        }), 1, 1);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption("Crop Region"), 0, 0);
        layout.Controls.Add(grid, 0, 1);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildCropRotationSection()
    {
        //== rotation controls ==================================================
        var section = CreateSectionPanel(new Padding(0, 18, 0, 0));

        var buttonRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 1
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));

        _btnCropRotateLeft = CreateCropSelectorButton("-90\u00B0", () => SetCropRotation(-90));
        _btnCropRotateReset = CreateCropSelectorButton("0\u00B0", () => SetCropRotation(0));
        _btnCropRotateRight = CreateCropSelectorButton("+90\u00B0", () => SetCropRotation(90));
        AssignMatteIcon(_btnCropRotateLeft, "rotate-left", 18);
        AssignMatteIcon(_btnCropRotateReset, "reset", 18);
        AssignMatteIcon(_btnCropRotateRight, "rotate-right", 18);

        buttonRow.Controls.Add(WrapCropGridButton(_btnCropRotateLeft, 0, 0), 0, 0);
        buttonRow.Controls.Add(WrapCropGridButton(_btnCropRotateReset, 1, 0), 1, 0);
        buttonRow.Controls.Add(WrapCropGridButton(_btnCropRotateRight, 2, 0), 2, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption("Rotation"), 0, 0);
        layout.Controls.Add(buttonRow, 0, 1);

        section.Controls.Add(layout);
        return section;
        //=========================================================================
    }

    private Control BuildCropSummarySection()
    {
        //== crop summary =======================================================
        var section = CreateSectionPanel(new Padding(0, 18, 0, 0));

        var summaryCard = CreateCard(InputBackgroundColor, CardBorderColor, 14);
        summaryCard.AutoSize = true;
        summaryCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        summaryCard.Dock = DockStyle.Top;
        summaryCard.Margin = Padding.Empty;
        summaryCard.Padding = new Padding(14);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblCropSourceValue = CreateCropMetricValueLabel();
        _lblCropOutputValue = CreateCropMetricValueLabel();
        _lblCropRotationValue = CreateCropMetricValueLabel();
        _lblCropOutputValue.ForeColor = AccentColor;

        layout.Controls.Add(CreateTrimStatCaption("Source"), 0, 0);
        layout.Controls.Add(_lblCropSourceValue, 1, 0);
        layout.Controls.Add(CreateTrimStatCaption("Output"), 0, 1);
        layout.Controls.Add(_lblCropOutputValue, 1, 1);
        layout.Controls.Add(CreateTrimStatCaption("Rotation"), 0, 2);
        layout.Controls.Add(_lblCropRotationValue, 1, 2);

        summaryCard.Controls.Add(layout);
        section.Controls.Add(summaryCard);
        return section;
        //=========================================================================
    }

    private Button CreateCropSelectorButton(string text, Action onClick)
    {
        var button = new ClayButton();
        ConfigureConvertTargetButton(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control WrapCropGridButton(Button button, int columnIndex, int rowIndex)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(
            columnIndex < 2 ? 0 : 0,
            0,
            columnIndex < 2 ? 10 : 0,
            rowIndex == 0 ? 10 : 0);
        return button;
    }

    private Control CreateCropInputCard(string caption, out TextBox? textBox, Func<int> getValue, Action<int> applyValue)
    {
        //== input collection ===================================================
        var card = CreateCard(MutedSurfaceColor, CardBorderColor, 12);
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Dock = DockStyle.Top;
        card.Margin = Padding.Empty;
        card.Padding = new Padding(12);

        textBox = CreateCropValueInput(getValue, applyValue);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateMicroCaption(caption), 0, 0);
        layout.Controls.Add(CreateClayFieldHost(textBox, 46), 0, 1);

        card.Controls.Add(layout);
        return card;
        //=========================================================================
    }

    private TextBox CreateCropValueInput(Func<int> getValue, Action<int> applyValue)
    {
        var textBox = new TextBox
        {
            BackColor = InputBackgroundColor,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Top,
            Font = new Font("Consolas", 13F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 8, 0, 0)
        };

        void CommitValue()
        {
            if (_cropUiSyncInFlight)
            {
                return;
            }

            if (!int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                textBox.Text = getValue().ToString(CultureInfo.InvariantCulture);
                return;
            }

            applyValue(parsedValue);
            UpdateCropUi();
        }

        textBox.Leave += (_, _) => CommitValue();
        textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            CommitValue();
            e.SuppressKeyPress = true;
            e.Handled = true;
        };

        return textBox;
    }

    private static Label CreateCropMetricValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 10.2F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(12, 0, 0, 10),
            Text = "--"
        };
    }

    private Button CreateCropResetTextButton()
    {
        var button = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 40,
            MinimumSize = new Size(0, 40),
            Margin = new Padding(0, 14, 0, 0),
            Text = "Reset crop",
            Variant = ClayButtonVariant.Quiet
        };
        AssignMatteIcon(button, "reset", 17);

        button.BackColor = Color.Transparent;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = SecondaryTextColor;
        button.Padding = Padding.Empty;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.Transparent;
        button.FlatAppearance.MouseDownBackColor = Color.Transparent;
        button.Click += btnCropReset_Click;
        return button;
    }

    private Panel BuildActivityPage()
    {
        //== activity inspector ================================================
        var section = CreateSectionPanel(Padding.Empty);

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

        var headerRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleLabel = CreateSectionTitle("ACTIVITY");
        _activityEntryCountLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(12, 2, 0, 0),
            Text = "0 entries",
            TextAlign = ContentAlignment.TopRight
        };

        headerRow.Controls.Add(titleLabel, 0, 0);
        headerRow.Controls.Add(_activityEntryCountLabel, 1, 0);

        var activitySurface = CreateInsetPanel(new Padding(14));
        activitySurface.MinimumSize = new Size(0, 430);
        activitySurface.Margin = new Padding(0, 8, 0, 0);

        _activityFeedLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 0
        };
        _activityFeedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        activitySurface.Controls.Add(_activityFeedLayout);

        var footerDivider = new Panel
        {
            BackColor = CardBorderColor,
            Dock = DockStyle.Top,
            Height = 1,
            Margin = new Padding(0, 12, 0, 12)
        };

        var summaryRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));

        summaryRow.Controls.Add(CreateActivitySummaryGroup("Downloads", AccentColor, out _activityDownloadsCountLabel), 0, 0);
        summaryRow.Controls.Add(CreateActivitySummaryGroup("Exports", PrimaryTextColor, out _activityExportsCountLabel), 1, 0);
        summaryRow.Controls.Add(CreateActivitySummaryGroup("Errors", ErrorColor, out _activityErrorsCountLabel), 2, 0);

        layout.Controls.Add(headerRow, 0, 0);
        layout.Controls.Add(activitySurface, 0, 1);
        layout.Controls.Add(footerDivider, 0, 2);
        layout.Controls.Add(summaryRow, 0, 3);

        section.Controls.Add(layout);
        RefreshActivityFeedUi();
        return section;
        //=========================================================================
    }

    private static TableLayoutPanel CreateActivitySummaryGroup(string title, Color countColor, out Label? valueLabel)
    {
        //== output shaping ======================================================
        var group = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var captionLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = Padding.Empty,
            Text = title
        };

        valueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = countColor,
            Margin = new Padding(0, 4, 0, 0),
            Text = "0"
        };

        group.Controls.Add(captionLabel, 0, 0);
        group.Controls.Add(valueLabel, 0, 1);
        return group;
        //=========================================================================
    }

    private void RefreshActivityFeedUi()
    {
        //== output shaping ======================================================
        if (_activityFeedLayout is null)
        {
            return;
        }

        _activityFeedLayout.SuspendLayout();
        _activityFeedLayout.Controls.Clear();
        _activityFeedLayout.RowStyles.Clear();

        if (_activityFeedEntries.Count == 0)
        {
            _activityFeedLayout.RowCount = 1;
            _activityFeedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _activityFeedLayout.Controls.Add(CreateActivityEmptyState(), 0, 0);
        }
        else
        {
            _activityFeedLayout.RowCount = _activityFeedEntries.Count;
            for (var index = 0; index < _activityFeedEntries.Count; index++)
            {
                _activityFeedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _activityFeedLayout.Controls.Add(CreateActivityFeedItem(_activityFeedEntries[index]), 0, index);
            }
        }

        _activityFeedLayout.ResumeLayout(performLayout: true);

        if (_activityEntryCountLabel is not null)
        {
            _activityEntryCountLabel.Text = FormatActivityEntryCount(_activityFeedEntries.Count);
        }

        if (_activityDownloadsCountLabel is not null)
        {
            _activityDownloadsCountLabel.Text = _activityFeedEntries.Count(entry => entry.CountsAsDownload).ToString(CultureInfo.InvariantCulture);
        }

        if (_activityExportsCountLabel is not null)
        {
            _activityExportsCountLabel.Text = _activityFeedEntries.Count(entry => entry.CountsAsExport).ToString(CultureInfo.InvariantCulture);
        }

        if (_activityErrorsCountLabel is not null)
        {
            _activityErrorsCountLabel.Text = _activityFeedEntries.Count(entry => entry.CountsAsError).ToString(CultureInfo.InvariantCulture);
        }

        //=========================================================================
    }

    private Control CreateActivityEmptyState()
    {
        //== output shaping ======================================================
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "No activity yet"
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 6, 0, 0),
            MaximumSize = new Size(260, 0),
            Text = "Start a download, trim, or conversion to populate the session timeline."
        };

        panel.Controls.Add(titleLabel, 0, 0);
        panel.Controls.Add(subtitleLabel, 0, 1);
        return panel;
        //=========================================================================
    }

    private Control CreateActivityFeedItem(ActivityFeedEntry entry)
    {
        //== output shaping ======================================================
        var (glyph, accentColor) = GetActivityFeedVisuals(entry.IconKind);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 18),
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var iconLabel = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = accentColor,
            Margin = new Padding(0, 1, 0, 0),
            Size = new Size(22, 22),
            Text = glyph,
            TextAlign = ContentAlignment.TopCenter
        };

        var copyLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 0, 0, 0),
            RowCount = 2
        };
        copyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var messageLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            MaximumSize = new Size(252, 0),
            Text = entry.Message
        };

        var timeLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 8.45F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 5, 0, 0),
            Text = entry.TimestampLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        };

        copyLayout.Controls.Add(messageLabel, 0, 0);
        copyLayout.Controls.Add(timeLabel, 0, 1);
        row.Controls.Add(iconLabel, 0, 0);
        row.Controls.Add(copyLayout, 1, 0);
        return row;
        //=========================================================================
    }

    private static (string Glyph, Color AccentColor) GetActivityFeedVisuals(ActivityFeedIconKind iconKind)
    {
        return iconKind switch
        {
            ActivityFeedIconKind.Download => ("\u2193", AccentColor),
            ActivityFeedIconKind.Export => ("\u2702", PrimaryTextColor),
            ActivityFeedIconKind.Success => ("\u25CB", SuccessColor),
            ActivityFeedIconKind.Error => ("\u0021", ErrorColor),
            _ => ("\u2022", SecondaryTextColor)
        };
    }

    private void AddActivityEntry(
        ActivityFeedIconKind iconKind,
        string message,
        bool countsAsDownload = false,
        bool countsAsExport = false,
        bool countsAsError = false)
    {
        //== state changes ======================================================
        RunOnUiThread(() =>
        {
            var normalizedMessage = message.Trim();
            if (string.IsNullOrWhiteSpace(normalizedMessage))
            {
                return;
            }

            if (_activityFeedEntries.Count > 0)
            {
                var lastEntry = _activityFeedEntries[^1];
                if (lastEntry.IconKind == iconKind &&
                    lastEntry.Message.Equals(normalizedMessage, StringComparison.Ordinal))
                {
                    return;
                }
            }

            _activityFeedEntries.Add(new ActivityFeedEntry(
                DateTime.Now,
                normalizedMessage,
                iconKind,
                countsAsDownload,
                countsAsExport,
                countsAsError));

            const int maxEntries = 48;
            if (_activityFeedEntries.Count > maxEntries)
            {
                _activityFeedEntries.RemoveAt(0);
            }

            RefreshActivityFeedUi();
        });
        //=========================================================================
    }

    private void CaptureActivityFromLog(string message)
    {
        //== output shaping ======================================================
        var normalizedMessage = message.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        if (normalizedMessage.Contains("Extracting URL", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("Downloading webpage", StringComparison.OrdinalIgnoreCase))
        {
            AddActivityEntry(ActivityFeedIconKind.Download, "[yt-dlp] Extracting URL info...");
            return;
        }

        var downloadFormatIndex = normalizedMessage.IndexOf("Downloading format", StringComparison.OrdinalIgnoreCase);
        if (downloadFormatIndex >= 0)
        {
            AddActivityEntry(ActivityFeedIconKind.Download, $"[yt-dlp] {normalizedMessage[downloadFormatIndex..]}");
            return;
        }

        if (TrySummarizeActivityError(normalizedMessage, out var errorSummary))
        {
            _lastActivityErrorSummary = errorSummary;
        }
        //=========================================================================
    }

    private static bool TrySummarizeActivityError(string message, out string summary)
    {
        //== output shaping ======================================================
        var normalizedMessage = message.Trim();
        if (normalizedMessage.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
        {
            summary = string.Empty;
            return false;
        }

        if (normalizedMessage.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("No such file", StringComparison.OrdinalIgnoreCase))
        {
            summary = normalizedMessage;
            return true;
        }

        summary = string.Empty;
        return false;
        //=========================================================================
    }

    private static string FormatActivityEntryCount(int count)
    {
        return count == 1
            ? "1 entry"
            : $"{count.ToString(CultureInfo.InvariantCulture)} entries";
    }

    private static string FormatActivityUrl(string url)
    {
        //== normalization =======================================================
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var builder = new StringBuilder(uri.Host);
        builder.Append(uri.AbsolutePath);
        builder.Append(uri.Query);
        return builder.ToString().TrimEnd('/');
        //=========================================================================
    }

    private static string BuildActivityFileSummary(string mediaPath)
    {
        //== output shaping ======================================================
        var fileName = Path.GetFileName(mediaPath);
        if (!File.Exists(mediaPath))
        {
            return fileName;
        }

        var fileSize = new FileInfo(mediaPath).Length;
        return $"{fileName} ({FormatFileSize(fileSize)})";
        //=========================================================================
    }

    private static string BuildActivityCommandSnippet(string toolName, params string[] arguments)
    {
        //== output shaping ======================================================
        var filteredArguments = arguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .Select(argument => argument.Trim())
            .Take(8);

        return $"[{toolName}] {string.Join(" ", filteredArguments)}";
        //=========================================================================
    }

    private string BuildActivityFailureMessage(string prefix, string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(_lastActivityErrorSummary)
            ? fallbackMessage
            : $"{prefix}: {_lastActivityErrorSummary}";
    }

    private Control BuildEditorArea()
    {
        //== preview stage ======================================================
        var stageCard = CreateCard(StageSurfaceColor, CardBorderColor, SurfaceCornerRadius);
        stageCard.Dock = DockStyle.Fill;
        stageCard.Margin = Padding.Empty;
        stageCard.Padding = Padding.Empty;

        var stageLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        stageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        stageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigurePreviewToolbar();

        var stageShell = new ClayPanel
        {
            BackColor = StageSurfaceColor,
            CornerRadius = 20,
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            SurfaceKind = ClaySurfaceKind.Stage
        };
        stageShell.Paint += DrawStageGrid;

        var previewStage = CreateCard(StageBackgroundColor, CardBorderColor, 24);
        previewStage.Dock = DockStyle.Fill;
        previewStage.Margin = Padding.Empty;

        webPreview.Dock = DockStyle.Fill;
        lblPreviewState.Dock = DockStyle.Fill;
        var emptyPreviewState = BuildEmptyPreviewState();
        var pictureComparisonStage = BuildPictureComparisonStage();
        var backgroundComparisonStage = BuildBackgroundComparisonStage();

        previewStage.Controls.Add(webPreview);
        previewStage.Controls.Add(emptyPreviewState);
        previewStage.Controls.Add(lblPreviewState);
        previewStage.Controls.Add(pictureComparisonStage);
        previewStage.Controls.Add(backgroundComparisonStage);
        emptyPreviewState.BringToFront();
        pictureComparisonStage.Visible = false;
        backgroundComparisonStage.Visible = false;
        lblPreviewState.Visible = false;
        webPreview.Visible = false;
        stageShell.Controls.Add(previewStage);

        var trimTimelineCard = CreateCard(Color.FromArgb(9, 12, 19), CardBorderColor, 20);
        trimTimelineCard.Dock = DockStyle.Top;
        trimTimelineCard.Height = 150;
        trimTimelineCard.Margin = new Padding(18, 0, 18, 18);
        trimTimelineCard.Padding = new Padding(12);
        trimTimelineCard.Visible = false;
        _trimTimelineHost = trimTimelineCard;

        _trimTimelineControl = new TrimTimelineControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _trimTimelineControl.SelectionChanged += TrimTimelineControl_SelectionChanged;
        _trimTimelineControl.CurrentPositionChanged += TrimTimelineControl_CurrentPositionChanged;
        trimTimelineCard.Controls.Add(_trimTimelineControl);

        stageLayout.Controls.Add(panelPreviewToolbar, 0, 0);
        stageLayout.Controls.Add(stageShell, 0, 1);
        stageLayout.Controls.Add(trimTimelineCard, 0, 2);
        stageCard.Controls.Add(stageLayout);
        return stageCard;
        //=========================================================================
    }

    private void ConfigurePreviewToolbar()
    {
        //== preview context header ============================================
        panelPreviewToolbar.Controls.Clear();
        panelPreviewToolbar.AutoSize = false;
        panelPreviewToolbar.Dock = DockStyle.Fill;
        panelPreviewToolbar.Height = 68;
        panelPreviewToolbar.MinimumSize = new Size(0, 68);
        panelPreviewToolbar.Padding = new Padding(18, 14, 18, 14);
        panelPreviewToolbar.BackColor = Color.Transparent;
        if (panelPreviewToolbar is ClayPanel toolbarSurface)
        {
            toolbarSurface.CornerRadius = 18;
            toolbarSurface.SurfaceKind = ClaySurfaceKind.Raised;
            toolbarSurface.ShowInnerHighlight = false;
        }

        lblPreviewCaption.AutoSize = true;
        lblPreviewCaption.BackColor = AccentSoftColor;
        lblPreviewCaption.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewCaption.ForeColor = AccentColor;
        lblPreviewCaption.Margin = Padding.Empty;
        lblPreviewCaption.Padding = new Padding(10, 6, 10, 6);
        BindRoundedRegionToSize(lblPreviewCaption, 10);

        lblPreviewPath.AutoEllipsis = true;
        lblPreviewPath.AutoSize = false;
        lblPreviewPath.Dock = DockStyle.Fill;
        lblPreviewPath.Font = new Font("Consolas", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        lblPreviewPath.ForeColor = PrimaryTextColor;
        lblPreviewPath.Margin = new Padding(12, 0, 12, 0);
        lblPreviewPath.MinimumSize = new Size(180, 0);
        lblPreviewPath.TextAlign = ContentAlignment.MiddleLeft;

        _previewMetaLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var actionBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false
        };

        btnPreviewLast.Margin = new Padding(0, 0, 8, 0);
        btnOpenMediaFile.Margin = new Padding(0, 0, 8, 0);
        btnOpenExternal.Margin = Padding.Empty;

        actionBar.Controls.Add(btnPreviewLast);
        actionBar.Controls.Add(btnOpenMediaFile);
        actionBar.Controls.Add(btnOpenExternal);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(lblPreviewCaption, 0, 0);
        layout.Controls.Add(lblPreviewPath, 1, 0);
        layout.Controls.Add(_previewMetaLabel, 2, 0);
        layout.Controls.Add(actionBar, 3, 0);

        panelPreviewToolbar.Controls.Add(layout);
        UpdateWorkspaceChrome(_currentWorkspacePage);
        //=========================================================================
    }

    private Control BuildStatusBar()
    {
        //== runtime status =====================================================
        var statusBar = CreateCard(StatusBackgroundColor, CardBorderColor, 14);
        statusBar.AutoSize = true;
        statusBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        statusBar.Dock = DockStyle.Top;
        statusBar.Margin = new Padding(14, 0, 14, 12);
        statusBar.Padding = new Padding(16, 10, 16, 10);

        lblStatusCaption.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        lblStatusCaption.ForeColor = AccentColor;
        lblStatusCaption.Margin = new Padding(0, 0, 10, 0);
        lblStatusCaption.TextAlign = ContentAlignment.MiddleCenter;

        lblStatus.Dock = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblFileInfo.AutoEllipsis = true;
        lblFileInfo.Dock = DockStyle.Fill;
        lblFileInfo.Font = new Font("Consolas", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
        lblFileInfo.TextAlign = ContentAlignment.MiddleRight;

        progressDownload.Dock = DockStyle.Top;
        progressDownload.Margin = new Padding(0, 0, 0, 8);

        //== installation cancellation =======================================
        _backgroundInstallCancelButton = new ClayButton
        {
            AccessibleName = "Cancel Background Removal runtime download",
            AutoSize = false,
            Height = 38,
            Margin = new Padding(16, 0, 0, 0),
            MinimumSize = new Size(142, 38),
            Text = "Cancel download",
            Visible = false,
            Width = 142
        };
        StyleActionButton(_backgroundInstallCancelButton, primary: false);
        _backgroundInstallCancelButton.Height = 38;
        _backgroundInstallCancelButton.Click += (_, _) => CancelBackgroundRuntimeInstallation();
        //=====================================================================

        var statusRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusRow.Controls.Add(lblStatusCaption, 0, 0);
        statusRow.Controls.Add(lblStatus, 1, 0);
        statusRow.Controls.Add(lblFileInfo, 2, 0);
        statusRow.Controls.Add(_backgroundInstallCancelButton, 3, 0);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(progressDownload, 0, 0);
        layout.Controls.Add(statusRow, 0, 1);

        statusBar.Controls.Add(layout);
        return statusBar;
        //=========================================================================
    }

    private static Panel CreateCard(
        Color? backgroundColor = null,
        Color? borderColor = null,
        int radius = SurfaceCornerRadius)
    {
        //== reusable matte panel ============================================
        var resolvedBackground = backgroundColor ?? CardBackgroundColor;
        var panel = new ClayPanel
        {
            CornerRadius = radius,
            SurfaceKind = resolvedBackground == StageBackgroundColor
                ? ClaySurfaceKind.Stage
                : resolvedBackground == StatusBackgroundColor
                    ? ClaySurfaceKind.Status
                    : resolvedBackground == MutedSurfaceColor
                        ? ClaySurfaceKind.Muted
                        : ClaySurfaceKind.Main,
            SurfaceBottomColor = ClayDrawing.Blend(resolvedBackground, StudioTheme.WindowBackgroundDeep, 0.22F),
            SurfaceTopColor = resolvedBackground,
            BackColor = resolvedBackground
        };
        return panel;
        //=====================================================================
    }

    private static Panel CreateSectionPanel(Padding margin)
    {
        return new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = margin,
            Padding = Padding.Empty
        };
    }

    private static Panel CreateInsetPanel(Padding padding)
    {
        var panel = new ClayPanel
        {
            BackColor = StudioTheme.SurfaceInput,
            CornerRadius = 14,
            SurfaceKind = ClaySurfaceKind.Inset
        };
        panel.AutoSize = true;
        panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        panel.Padding = padding;
        return panel;
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.75F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = text
        };
    }

    private static Label CreateSectionSubtitle(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 4, 0, 10),
            MaximumSize = new Size(288, 0),
            Text = text
        };
    }

    private static void DrawExtractAudioSelectedState(object? sender, PaintEventArgs e)
    {
        //== selected-state indicator =========================================
        if (sender is not CheckBox { Checked: true } checkBox)
        {
            return;
        }

        var scale = checkBox.DeviceDpi / 96F;
        var glyphSize = Math.Max(13, (int)Math.Round(13F * scale));
        var glyphTop = Math.Max(0, (checkBox.ClientSize.Height - glyphSize) / 2);
        var fillColor = checkBox.Enabled
            ? AccentColor
            : Color.FromArgb(82, 87, 106);
        var interiorInset = Math.Max(2, (int)Math.Round(2F * scale));
        var interiorBounds = new Rectangle(
            interiorInset,
            glyphTop + interiorInset,
            Math.Max(1, glyphSize - (interiorInset * 2)),
            Math.Max(1, glyphSize - (interiorInset * 2)));

        using var fillBrush = new SolidBrush(fillColor);
        e.Graphics.FillRectangle(fillBrush, interiorBounds);
        //=====================================================================
    }

    private static void AddFullWidthTableRow(TableLayoutPanel layout, Control control, int rowIndex)
    {
        //== layout normalization ==============================================
        if (control.Dock == DockStyle.None)
        {
            control.Dock = DockStyle.Fill;
        }

        while (layout.RowStyles.Count <= rowIndex)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(control, 0, rowIndex);
        //=========================================================================
    }

    private void ShowWorkspacePage(WorkspacePage page)
    {
        //== atomic workspace transition =====================================
        _studioSidebar?.SuspendLayout();
        _workspacePageViewport?.SuspendLayout();
        _workspacePageHost?.SuspendLayout();

        try
        {
            _currentWorkspacePage = page;
            UpdateWorkspaceChrome(page);

            foreach (var workspacePage in _workspacePagePanels)
            {
                var isActive = workspacePage.Key == page;
                workspacePage.Value.Visible = isActive;

                if (isActive)
                {
                    workspacePage.Value.BringToFront();
                }
            }

            foreach (var workspaceButton in _workspacePageButtons)
            {
                ApplyRailButtonState(workspaceButton.Value, workspaceButton.Key == page);
            }

            if (_workspaceStickyActionHost is not null)
            {
                //== sticky page actions ======================================
                _workspaceStickyActionHost.Visible = page is WorkspacePage.Video or WorkspacePage.Watermark;

                if (_compressionActionPanel is not null)
                {
                    _compressionActionPanel.Visible = page == WorkspacePage.Video;
                }

                if (_watermarkInstallButton is not null)
                {
                    _watermarkInstallButton.Visible = page == WorkspacePage.Watermark;
                }
                //=============================================================
            }

            UpdatePictureComparisonStageVisibility(page);
            UpdateBackgroundComparisonStageVisibility(page);

            _workspacePageViewport?.ResetScroll();
        }
        finally
        {
            _workspacePageHost?.ResumeLayout(performLayout: true);
            _workspacePageViewport?.ResumeLayout(performLayout: true);
            _studioSidebar?.ResumeLayout(performLayout: true);
        }

        RefreshWorkspaceTransitionFrame();
        //=====================================================================
    }

    private void RefreshWorkspaceTransitionFrame()
    {
        //== targeted transition repaint =====================================
        _studioNavigationRail?.Invalidate(invalidateChildren: true);
        _studioNavigationRail?.Update();

        lblPreviewCaption.Invalidate();
        lblPreviewCaption.Update();

        _studioSidebar?.Invalidate(invalidateChildren: true);
        _studioSidebar?.Update();
        WindowChromeController.FlushComposedFrame();
        //=====================================================================
    }

    private void RefreshStudioFrame()
    {
        //== composed frame refresh ==========================================
        if (_studioShellLayout is null || !_studioShellLayout.IsHandleCreated)
        {
            return;
        }

        _studioShellLayout.Invalidate(invalidateChildren: true);
        _studioShellLayout.Update();
        WindowChromeController.FlushComposedFrame();
        //=====================================================================
    }

    private void UpdateWorkspaceChrome(WorkspacePage page)
    {
        var info = GetWorkspacePageVisuals(page);

        lblPreviewCaption.Text = info.StageTag;
        if (_trimTimelineHost is not null)
        {
            _trimTimelineHost.Visible = page == WorkspacePage.Trim;
        }

        _ = SyncCropPreviewOverlayAsync();
    }

    private static (RailIconKind RailIconKind, string RailLabel, string StageTag)
        GetWorkspacePageVisuals(WorkspacePage page)
    {
        return page switch
        {
            WorkspacePage.Source => (RailIconKind.Download, "DOWNLOAD", "DOWNLOAD"),
            WorkspacePage.Audio => (RailIconKind.Convert, "AUDIO", "AUDIO"),
            WorkspacePage.Video => (RailIconKind.Compress, "COMPRESS", "CONVERT"),
            WorkspacePage.Picture => (RailIconKind.Compress, "PICTURES", "PICTURE COMPRESSION"),
            WorkspacePage.Background => (RailIconKind.Background, "BACKGROUND", "BACKGROUND REMOVER"),
            WorkspacePage.Watermark => (RailIconKind.Watermark, "WATERMARK", "WATERMARK"),
            WorkspacePage.Trim => (RailIconKind.Trim, "TRIM", "TRIM"),
            WorkspacePage.Crop => (RailIconKind.Crop, "CROP", "CROP"),
            WorkspacePage.Activity => (RailIconKind.Activity, "ACTIVITY", "ACTIVITY"),
            _ => (RailIconKind.Activity, "WORKSPACE", "WORKSPACE")
        };
    }

    private Button CreateRailButton(WorkspacePage page, RailIconKind iconKind, string text, Action onClick)
    {
        var buttonFont = new Font("Segoe UI Semibold", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
        var buttonSize = MeasureRailButtonSize(text, buttonFont);

        var button = new ClayButton
        {
            AccessibleName = text,
            BackColor = Color.Transparent,
            CompactIconOnlyThreshold = 120,
            CornerRadius = 14,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = buttonFont,
            ForeColor = SecondaryTextColor,
            IconSize = 24,
            Image = LoadUiAsset(Path.Combine("MatteIcons", $"{GetRailIconAssetName(iconKind)}.png")),
            Margin = new Padding(2, 0, 2, 10),
            Padding = Padding.Empty,
            Size = buttonSize,
            Tag = new RailButtonVisualState
            {
                IconKind = iconKind,
                Label = text
            },
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            UseVisualStyleBackColor = false,
            Variant = ClayButtonVariant.Navigation
        };

        button.FlatAppearance.BorderColor = MutedSurfaceColor;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = MutedSurfaceColor;
        button.FlatAppearance.MouseOverBackColor = MutedSurfaceColor;
        button.Click += (_, _) => onClick();
        return button;
    }

    private static string GetRailIconAssetName(RailIconKind iconKind)
    {
        return iconKind switch
        {
            RailIconKind.Download => "download",
            RailIconKind.Compress => "compress",
            RailIconKind.Background => "cleanup",
            RailIconKind.Watermark => "watermark",
            RailIconKind.Trim => "trim",
            RailIconKind.Crop => "crop",
            RailIconKind.Convert => "convert",
            _ => "activity"
        };
    }

    private static int GetNavigationRailWidth()
    {
        return StudioTheme.NavigationWideWidth;
    }

    private static Size MeasureRailButtonSize(string text, Font font)
    {
        //== measurement-aware sizing ==========================================
        var textSize = TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var width = Math.Max(156, textSize.Width + 66);
        var height = 64;
        return new Size(width, height);
        //=======================================================================
    }

    private static void ApplyRailButtonState(Button button, bool selected)
    {
        //== state transition ==================================================
        if (button.Tag is RailButtonVisualState state)
        {
            state.IsSelected = selected;
        }

        if (button is ClayButton clayButton)
        {
            clayButton.Selected = selected;
            clayButton.ForeColor = selected ? PrimaryTextColor : SecondaryTextColor;
            clayButton.Invalidate();
            return;
        }

        button.BackColor = MutedSurfaceColor;
        button.ForeColor = selected ? PrimaryTextColor : SecondaryTextColor;
        button.Invalidate();
        //======================================================================
    }

    private static void UpdateRailButtonHoverState(Button button, bool isHovered)
    {
        //== state transition ==================================================
        if (button.Tag is not RailButtonVisualState state)
        {
            return;
        }

        state.IsHovered = isHovered;
        button.Invalidate();
        //======================================================================
    }

    private static void DrawRailButton(Graphics graphics, Button button, WorkspacePage page)
    {
        if (button.Tag is not RailButtonVisualState state)
        {
            return;
        }

        //== output shaping ====================================================
        var bounds = button.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var backgroundColor = state.IsSelected
            ? AccentColor
            : state.IsHovered
                ? StudioTheme.SurfaceRaised
                : MutedSurfaceColor;

        var surfaceBounds = Rectangle.Inflate(bounds, -2, -2);
        using var surfacePath = CreateRoundedRectanglePath(surfaceBounds, 13);
        if (state.IsSelected)
        {
            using var accentBrush = StudioTheme.CreateAccentBrush(surfaceBounds);
            graphics.FillPath(accentBrush, surfacePath);
        }
        else
        {
            using var backgroundBrush = new SolidBrush(backgroundColor);
            graphics.FillPath(backgroundBrush, surfacePath);
        }

        var compact = bounds.Width < 120;
        var iconColor = state.IsSelected ? PrimaryTextColor : SecondaryTextColor;
        var iconBounds = compact
            ? new Rectangle(0, 0, bounds.Width, bounds.Height)
            : new Rectangle(10, 0, 42, bounds.Height);
        DrawRailIcon(graphics, state.IconKind, iconBounds, iconColor, state.IsSelected);

        if (compact)
        {
            return;
        }

        // Keep the longest rail label readable at normal and scaled desktop DPIs.
        // The icon already has visual breathing room, so the label can safely use
        // the remaining right edge instead of losing its final character.
        var labelRectangle = new Rectangle(54, 0, Math.Max(1, bounds.Width - 58), bounds.Height);
        TextRenderer.DrawText(
            graphics,
            state.Label,
            button.Font,
            labelRectangle,
            iconColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        //======================================================================
    }

    private static void DrawRailIcon(
        Graphics graphics,
        RailIconKind iconKind,
        Rectangle bounds,
        Color color,
        bool isSelected)
    {
        //== output shaping ====================================================
        var centerX = bounds.Width / 2F;
        var iconTop = 17F;
        using var iconPen = new Pen(color, isSelected ? 2.05F : 1.85F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (iconKind)
        {
            case RailIconKind.Download:
                graphics.DrawLine(iconPen, centerX, iconTop + 4, centerX, iconTop + 18);
                graphics.DrawLine(iconPen, centerX - 5, iconTop + 13, centerX, iconTop + 18);
                graphics.DrawLine(iconPen, centerX + 5, iconTop + 13, centerX, iconTop + 18);
                graphics.DrawLine(iconPen, centerX - 8, iconTop + 24, centerX + 8, iconTop + 24);
                break;

            case RailIconKind.Compress:
                graphics.DrawArc(iconPen, centerX - 10, iconTop + 2, 20, 20, 205, 258);
                graphics.DrawLine(iconPen, centerX + 5, iconTop + 5, centerX + 9, iconTop + 9);
                graphics.DrawLine(iconPen, centerX + 5, iconTop + 5, centerX + 4, iconTop + 11);
                break;

            case RailIconKind.Background:
                graphics.DrawEllipse(iconPen, centerX - 9, iconTop + 4, 18, 18);
                graphics.DrawLine(iconPen, centerX + 4, iconTop + 2, centerX + 4, iconTop + 9);
                graphics.DrawLine(iconPen, centerX, iconTop + 5, centerX + 8, iconTop + 5);
                graphics.DrawLine(iconPen, centerX - 7, iconTop + 24, centerX + 7, iconTop + 24);
                break;

            case RailIconKind.Watermark:
                graphics.DrawRectangle(iconPen, centerX - 10, iconTop + 4, 20, 20);
                graphics.DrawLine(iconPen, centerX - 5, iconTop + 19, centerX + 5, iconTop + 9);
                graphics.DrawEllipse(iconPen, centerX + 2, iconTop + 6, 4, 4);
                break;

            case RailIconKind.Trim:
                //== output shaping: scissors icon =============================
                graphics.DrawEllipse(iconPen, centerX - 13, iconTop + 7, 8, 8);
                graphics.DrawEllipse(iconPen, centerX - 13, iconTop + 19, 8, 8);
                graphics.DrawLine(iconPen, centerX - 4, iconTop + 11, centerX + 9, iconTop + 5);
                graphics.DrawLine(iconPen, centerX - 4, iconTop + 23, centerX + 9, iconTop + 28);
                graphics.DrawLine(iconPen, centerX + 1, iconTop + 14, centerX + 10, iconTop + 22);
                graphics.DrawLine(iconPen, centerX + 1, iconTop + 20, centerX + 10, iconTop + 12);
                //==============================================================
                break;

            case RailIconKind.Crop:
                graphics.DrawLine(iconPen, centerX - 8, iconTop + 6, centerX - 8, iconTop + 20);
                graphics.DrawLine(iconPen, centerX - 8, iconTop + 20, centerX + 4, iconTop + 20);
                graphics.DrawLine(iconPen, centerX - 1, iconTop + 2, centerX - 1, iconTop + 16);
                graphics.DrawLine(iconPen, centerX - 1, iconTop + 2, centerX + 11, iconTop + 2);
                break;

            case RailIconKind.Convert:
                graphics.DrawRectangle(iconPen, centerX - 9, iconTop + 4, 18, 18);
                graphics.DrawLine(iconPen, centerX - 3, iconTop + 4, centerX - 3, iconTop + 22);
                graphics.DrawLine(iconPen, centerX + 3, iconTop + 4, centerX + 3, iconTop + 22);
                graphics.DrawLine(iconPen, centerX - 9, iconTop + 10, centerX + 9, iconTop + 10);
                graphics.DrawLine(iconPen, centerX - 9, iconTop + 16, centerX + 9, iconTop + 16);
                break;

            case RailIconKind.Activity:
                PointF[] points =
                [
                    new PointF(centerX - 10, iconTop + 18),
                    new PointF(centerX - 5, iconTop + 18),
                    new PointF(centerX - 1, iconTop + 10),
                    new PointF(centerX + 3, iconTop + 24),
                    new PointF(centerX + 7, iconTop + 16),
                    new PointF(centerX + 11, iconTop + 16)
                ];
                graphics.DrawLines(iconPen, points);
                break;
        }
        //======================================================================
    }

    private static TableLayoutPanel CreateButtonGrid(params Button[] buttons)
    {
        var rowCount = (int)Math.Ceiling(buttons.Length / 2D);
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 0),
            RowCount = rowCount
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        for (var row = 0; row < rowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        for (var index = 0; index < buttons.Length; index++)
        {
            var row = index / 2;
            var column = index % 2;
            buttons[index].Dock = DockStyle.Fill;
            buttons[index].Margin = new Padding(0, 0, column == 0 ? 10 : 0, row < rowCount - 1 ? 10 : 0);
            layout.Controls.Add(buttons[index], column, row);
        }

        return layout;
    }

    private static void StyleActionButton(Button button, bool primary)
    {
        //== reusable clay button ============================================
        button.AutoSize = false;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        button.Height = 42;
        button.Padding = new Padding(14, 0, 14, 0);
        button.UseVisualStyleBackColor = false;

        if (button is ClayButton clayButton)
        {
            clayButton.BackColor = Color.Transparent;
            clayButton.CornerRadius = 14;
            clayButton.Variant = primary ? ClayButtonVariant.Primary : ClayButtonVariant.Secondary;
            return;
        }
        //=====================================================================

        void ApplyPalette()
        {
            if (primary)
            {
                button.BackColor = button.Enabled ? AccentColor : CardBorderColor;
                button.ForeColor = button.Enabled ? AccentTextOnSolidColor : SecondaryTextColor;
                button.FlatAppearance.BorderColor = button.Enabled ? AccentColor : CardBorderColor;
            }
            else
            {
                button.BackColor = button.Enabled ? InputBackgroundColor : MutedSurfaceColor;
                button.ForeColor = button.Enabled ? PrimaryTextColor : SecondaryTextColor;
                button.FlatAppearance.BorderColor = CardBorderColor;
            }
        }

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary
            ? StudioTheme.AccentBright
            : AccentSoftColor;
        button.FlatAppearance.MouseDownBackColor = primary
            ? StudioTheme.AccentDeep
            : AccentSoftColor;
        button.EnabledChanged += (_, _) => ApplyPalette();
        button.SizeChanged += (_, _) => ApplyRoundedRegion(button, 14);
        if (primary)
        {
            button.Paint += DrawGradientActionButton;
        }
        ApplyPalette();
        ApplyRoundedRegion(button, 14);
    }

    private static void StylePreviewToolbarButton(Button button, string text)
    {
        //== visual shell ======================================================
        button.AutoSize = false;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        button.Height = 40;
        button.MinimumSize = new Size(90, 40);
        button.Padding = new Padding(16, 0, 16, 0);
        button.Text = text;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
        //=========================================================================

        //== measurement-aware sizing ==========================================
        var measuredText = TextRenderer.MeasureText(text, button.Font, Size.Empty, TextFormatFlags.NoPadding);
        button.Width = Math.Max(button.MinimumSize.Width, measuredText.Width + 36);
        //=========================================================================

        if (button is ClayButton clayButton)
        {
            //== surface-free toolbar presentation ==================================
            clayButton.BackColor = Color.Transparent;
            clayButton.CornerRadius = 11;
            clayButton.Variant = ClayButtonVariant.Caption;
            //=========================================================================
            return;
        }

        //== palette ===========================================================
        void ApplyPalette()
        {
            if (button.Enabled)
            {
                button.BackColor = StudioTheme.AccentSoft;
                button.ForeColor = PrimaryTextColor;
                button.FlatAppearance.BorderColor = StudioTheme.Border;
                return;
            }

            button.BackColor = MutedSurfaceColor;
            button.ForeColor = SecondaryTextColor;
            button.FlatAppearance.BorderColor = CardBorderColor;
        }
        //=========================================================================

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(83, 67, 171);
        button.FlatAppearance.MouseDownBackColor = StudioTheme.AccentDeep;
        button.EnabledChanged += (_, _) => ApplyPalette();
        button.SizeChanged += (_, _) => ApplyRoundedRegion(button, 11);
        ApplyPalette();
        ApplyRoundedRegion(button, 11);
    }

    private static void ConfigureCompactActionButton(Button button, string text)
    {
        button.Text = text;
        button.Height = 40;
        button.MinimumSize = new Size(0, 40);
        button.Padding = new Padding(12, 0, 12, 0);
        button.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static void ConfigureOptionButton(Button button, string title, string subtitle)
    {
        button.Height = 80;
        button.MinimumSize = new Size(0, 80);
        button.Padding = new Padding(14, 10, 14, 10);
        button.Text = $"{title}{Environment.NewLine}{subtitle}";
        button.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static void ConfigureConvertTargetButton(Button button, string text)
    {
        //== visual shell ======================================================
        button.AutoSize = false;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = SecondaryTextColor;
        button.Height = 40;
        button.MinimumSize = new Size(0, 40);
        button.Padding = new Padding(10, 0, 10, 0);
        button.Text = text;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;

        if (button is ClayButton clayButton)
        {
            clayButton.BackColor = Color.Transparent;
            clayButton.CornerRadius = 10;
            clayButton.Variant = ClayButtonVariant.Selector;
            return;
        }

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
        button.FlatAppearance.MouseDownBackColor = AccentSoftColor;
        button.SizeChanged += (_, _) => ApplyRoundedRegion(button, 10);
        ApplyRoundedRegion(button, 10);
        //=======================================================================
    }

    private static void ConfigureSelectorButton(Button button, string title, string subtitle)
    {
        //== visual shell ======================================================
        button.AutoSize = false;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 8.9F, FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = SecondaryTextColor;
        button.Height = 62;
        button.MinimumSize = new Size(0, 62);
        button.Padding = new Padding(12, 8, 12, 8);
        button.Text = $"{title}{Environment.NewLine}{subtitle}";
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;

        if (button is ClayButton clayButton)
        {
            clayButton.BackColor = Color.Transparent;
            clayButton.CornerRadius = 14;
            clayButton.Variant = ClayButtonVariant.Selector;
            return;
        }

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
        button.FlatAppearance.MouseDownBackColor = AccentSoftColor;
        button.SizeChanged += (_, _) => ApplyRoundedRegion(button, 14);
        ApplyRoundedRegion(button, 14);
        //=======================================================================
    }

    private static void ApplySelectorButtonState(Button button, bool selected)
    {
        //== active vs inactive text state =====================================
        if (button is ClayButton clayButton)
        {
            clayButton.Selected = selected;
            clayButton.ForeColor = selected ? PrimaryTextColor : SecondaryTextColor;
            return;
        }

        if (!button.Enabled)
        {
            button.BackColor = MutedSurfaceColor;
            button.ForeColor = SecondaryTextColor;
            button.FlatAppearance.BorderColor = CardBorderColor;
            return;
        }

        button.BackColor = selected ? StudioTheme.AccentSoft : InputBackgroundColor;
        button.ForeColor = selected ? PrimaryTextColor : SecondaryTextColor;
        button.FlatAppearance.BorderColor = selected ? AccentColor : CardBorderColor;
        //=======================================================================
    }

    private static Label CreateMicroCaption(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = Padding.Empty,
            Text = text.ToUpperInvariant()
        };
    }

    private static Label CreateTrimTimeDisplayLabel(float fontSize)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = new Font("Consolas", fontSize, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Height = (int)Math.Ceiling(fontSize * 1.8F),
            Margin = Padding.Empty,
            Text = "00:00:00.000",
            TextAlign = ContentAlignment.MiddleCenter
        };
    }

    private static Label CreateTrimStatCaption(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 0, 12, 10),
            Text = text
        };
    }

    private static Label CreateTrimStatValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 9.6F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(12, 0, 0, 10),
            Text = "--"
        };
    }

    private Button CreateTrimUtilityButton(string text, string iconName, EventHandler onClick)
    {
        var button = new ClayButton
        {
            Dock = DockStyle.Fill,
            Height = 42,
            MinimumSize = new Size(0, 42),
            Margin = Padding.Empty,
            Text = text
        };

        StyleActionButton(button, primary: false);
        AssignMatteIcon(button, iconName, 18);
        button.Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        button.Click += onClick;
        return button;
    }

    private Button CreateTrimTransportButton(string iconName, string accessibleName, EventHandler onClick)
    {
        var button = new ClayButton
        {
            Size = new Size(52, 40),
            Margin = Padding.Empty,
            AccessibleName = accessibleName,
            Text = string.Empty,
            Variant = ClayButtonVariant.Icon
        };

        StyleActionButton(button, primary: false);
        button.Variant = ClayButtonVariant.Icon;
        AssignMatteIcon(button, iconName, 22);
        button.Font = new Font("Segoe UI Symbol", 13F, FontStyle.Regular, GraphicsUnit.Point);
        button.Click += onClick;
        return button;
    }

    private Button CreateResetTextButton()
    {
        var button = new ClayButton
        {
            Dock = DockStyle.Top,
            Height = 40,
            MinimumSize = new Size(0, 40),
            Margin = new Padding(0, 12, 0, 0),
            Text = "Reset to full duration",
            Variant = ClayButtonVariant.Quiet
        };
        AssignMatteIcon(button, "reset", 17);

        button.BackColor = Color.Transparent;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
        button.ForeColor = SecondaryTextColor;
        button.Padding = Padding.Empty;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.Transparent;
        button.FlatAppearance.MouseDownBackColor = Color.Transparent;
        button.Click += btnTrimResetRange_Click;
        return button;
    }

    private Button CreateCompressionPresetButton(string title, string subtitle, int trackValue)
    {
        //== compact preset selection ========================================
        var button = new ClayButton();
        ConfigureSelectorButton(button, title, subtitle);
        button.Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
        button.Height = 68;
        button.MinimumSize = new Size(0, 68);
        button.Click += (_, _) => trkVideoQuality.Value = trackValue;
        return button;
        //=====================================================================
    }

    private static Label CreateMetricValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "--"
        };
    }

    private static Panel CreateCompressionBarTrack(Color fillColor, out Panel fillPanel)
    {
        var track = new Panel
        {
            BackColor = Color.FromArgb(34, 38, 48),
            Dock = DockStyle.Top,
            Height = 10,
            Margin = new Padding(0, 8, 0, 0)
        };

        fillPanel = new Panel
        {
            BackColor = fillColor,
            Dock = DockStyle.Left,
            Margin = Padding.Empty,
            Width = 0
        };
        var localFillPanel = fillPanel;

        track.Controls.Add(localFillPanel);
        track.SizeChanged += (_, _) =>
        {
            ApplyRoundedRegion(track, 5);
            ApplyRoundedRegion(localFillPanel, 5);
        };

        ApplyRoundedRegion(track, 5);
        ApplyRoundedRegion(localFillPanel, 5);
        return track;
    }

    private static Panel CreateMetricRow(string caption, Label valueLabel, Panel barTrack)
    {
        var row = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var headerRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var captionLabel = CreateMicroCaption(caption);
        captionLabel.Dock = DockStyle.Left;

        headerRow.Controls.Add(captionLabel, 0, 0);
        headerRow.Controls.Add(valueLabel, 1, 0);

        row.Controls.Add(barTrack);
        row.Controls.Add(headerRow);
        return row;
    }

    private CheckBox CreateCompressionOptionCheckBox(bool isChecked)
    {
        var checkBox = new ClayCheckBox
        {
            AutoSize = true,
            Checked = isChecked,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            ForeColor = AccentColor,
            Margin = new Padding(0, 2, 12, 0)
        };

        checkBox.CheckedChanged += (_, _) => UpdateVideoQualityUi();
        return checkBox;
    }

    private static Control CreateCompressionOptionRow(CheckBox checkBox, string title, string subtitle)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var copy = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        copy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        copy.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = title
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 3, 0, 0),
            MaximumSize = new Size(220, 0),
            Text = subtitle
        }, 0, 1);

        row.Controls.Add(checkBox, 0, 0);
        row.Controls.Add(copy, 1, 0);
        return row;
    }

    private static void StyleTextInput(TextBox textBox, string placeholder)
    {
        textBox.BackColor = InputBackgroundColor;
        textBox.BorderStyle = BorderStyle.None;
        textBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        textBox.ForeColor = PrimaryTextColor;
        textBox.PlaceholderText = placeholder;
    }

    private static Control CreateClayFieldHost(TextBox textBox, int minimumHeight = 42)
    {
        //== recessed form field =============================================
        var host = new ClayPanel
        {
            BackColor = StudioTheme.SurfaceInput,
            CornerRadius = 12,
            Dock = DockStyle.Fill,
            Margin = textBox.Margin,
            MinimumSize = new Size(0, minimumHeight),
            Padding = new Padding(12, 10, 12, 8),
            SurfaceKind = ClaySurfaceKind.Inset
        };
        textBox.BackColor = StudioTheme.SurfaceInput;
        textBox.BorderStyle = BorderStyle.None;
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = Padding.Empty;
        host.Controls.Add(textBox);
        return host;
        //=====================================================================
    }

    private static void DrawStageGrid(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        using var minorPen = new Pen(Color.FromArgb(22, 70, 84, 106));
        using var majorPen = new Pen(Color.FromArgb(34, 70, 84, 106));

        const int minorStep = 48;
        const int majorStep = minorStep * 4;

        for (var x = 0; x < control.Width; x += minorStep)
        {
            e.Graphics.DrawLine(x % majorStep == 0 ? majorPen : minorPen, x, 0, x, control.Height);
        }

        for (var y = 0; y < control.Height; y += minorStep)
        {
            e.Graphics.DrawLine(y % majorStep == 0 ? majorPen : minorPen, 0, y, control.Width, y);
        }
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

    private static GraphicsPath CreateChamferedRectanglePath(Rectangle bounds, int cutSize)
    {
        var path = new GraphicsPath();

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var maximumCutSize = Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2);
        var chamferSize = Math.Clamp(cutSize, 1, maximumCutSize);
        var right = bounds.Right - 1;
        var bottom = bounds.Bottom - 1;

        path.AddPolygon(
        [
            new Point(bounds.Left + chamferSize, bounds.Top),
            new Point(right, bounds.Top),
            new Point(right, bottom - chamferSize),
            new Point(right - chamferSize, bottom),
            new Point(bounds.Left, bottom),
            new Point(bounds.Left, bounds.Top + chamferSize)
        ]);
        path.CloseFigure();
        return path;
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectanglePath(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region = new Region(path);
    }

    private static void BindRoundedRegionToSize(Control control, int radius)
    {
        //== layout normalization ==============================================
        control.SizeChanged += (_, _) => ApplyRoundedRegion(control, radius);
        ApplyRoundedRegion(control, radius);
        //=======================================================================
    }

    private static void ApplyChamferedRegion(Control control, int cutSize)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        using var path = CreateChamferedRectanglePath(new Rectangle(0, 0, control.Width, control.Height), cutSize);
        control.Region = new Region(path);
    }

    private void FocusPreviewStage()
    {
        if (webPreview.Visible && webPreview.CanFocus)
        {
            webPreview.Focus();
            return;
        }

        if (lblPreviewState.CanFocus)
        {
            lblPreviewState.Focus();
        }
    }

    private void btnBrowseOutput_Click(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(txtOutputFolder.Text)
                ? txtOutputFolder.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtOutputFolder.Text = dialog.SelectedPath;
        }
    }

    private async void btnDownload_Click(object sender, EventArgs e)
    {
        if (_activeOperation == AppOperation.Download && _downloadCts is not null)
        {
            btnDownload.Enabled = false;
            _downloadCts.Cancel();
            return;
        }

        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        await StartDownloadAsync();
    }

    private void btnConvertMp3_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("mp3", requiresVideo: false);
    }

    private void btnConvertWav_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("wav", requiresVideo: false);
    }

    private void btnConvertM4a_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("m4a", requiresVideo: false);
    }

    private void btnConvertMp4_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("mp4", requiresVideo: true);
    }

    private void btnConvertMkv_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("mkv", requiresVideo: true);
    }

    private void btnConvertMov_Click(object sender, EventArgs e)
    {
        SelectConversionTarget("mov", requiresVideo: true);
    }

    private async void btnConvertSelectedMedia_Click(object? sender, EventArgs e)
    {
        var targetExtension = GetSelectedConversionTargetExtension();
        await StartConversionAsync(targetExtension.ToUpperInvariant(), targetExtension, _selectedConversionRequiresVideo);
    }

    private async void btnTrimSetIn_Click(object? sender, EventArgs e)
    {
        await ApplyTrimMarkerAsync(setStartMarker: true);
    }

    private async void btnTrimSetOut_Click(object? sender, EventArgs e)
    {
        await ApplyTrimMarkerAsync(setStartMarker: false);
    }

    private async void btnTrimJumpToIn_Click(object? sender, EventArgs e)
    {
        await SeekPreviewToTrimMarkerAsync(_trimSelectionStart);
    }

    private async void btnTrimJumpToOut_Click(object? sender, EventArgs e)
    {
        await SeekPreviewToTrimMarkerAsync(_trimSelectionEnd);
    }

    private async void btnTrimPreviewSelection_Click(object? sender, EventArgs e)
    {
        if (_trimSelectionPlaybackActive)
        {
            await StopTrimPreviewPlaybackAsync();
            return;
        }

        await StartTrimPreviewPlaybackAsync();
    }

    private void btnTrimResetRange_Click(object? sender, EventArgs e)
    {
        _trimSelectionPlaybackActive = false;
        ResetTrimSelectionToFullDuration();
        UpdateTrimUi();
    }

    private async void btnTrimExport_Click(object? sender, EventArgs e)
    {
        await StartTrimExportAsync();
    }

    private void TrimTimelineControl_SelectionChanged(object? sender, TrimSelectionChangedEventArgs e)
    {
        //== state transition ===================================================
        _trimSelectionStart = e.Start;
        _trimSelectionEnd = e.End;
        if (_trimCurrentPosition < _trimSelectionStart)
        {
            _trimCurrentPosition = _trimSelectionStart;
        }

        if (_trimCurrentPosition > _trimSelectionEnd)
        {
            _trimCurrentPosition = _trimSelectionEnd;
        }

        UpdateTrimUi();
        //=========================================================================
    }

    private async void TrimTimelineControl_CurrentPositionChanged(object? sender, TrimCurrentPositionChangedEventArgs e)
    {
        _trimCurrentPosition = e.Position;
        UpdateTrimUi();
        await SeekPreviewAsync(e.Position);
    }

    private async void TrimPreviewTimer_Tick(object? sender, EventArgs e)
    {
        await SyncTrimPreviewStateAsync();
    }

    private async Task ApplyTrimMarkerAsync(bool setStartMarker)
    {
        //== input validation ===================================================
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        var mediaPath = GetPreferredMediaPath();
        if (mediaPath is null || !HasTrimCapableVideo())
        {
            return;
        }
        //=========================================================================

        //== current position sync ==============================================
        var currentPosition = await GetCurrentOrObservedPlayerPositionAsync();
        if (!currentPosition.HasValue)
        {
            return;
        }

        var duration = GetTrimDuration();
        if (duration <= TimeSpan.Zero)
        {
            return;
        }
        //=========================================================================

        //== marker update ======================================================
        if (setStartMarker)
        {
            _trimSelectionStart = ClampTrimTime(currentPosition.Value, duration);
            if (_trimSelectionStart >= _trimSelectionEnd)
            {
                _trimSelectionStart = _trimSelectionEnd - TimeSpan.FromMilliseconds(100);
            }
        }
        else
        {
            _trimSelectionEnd = ClampTrimTime(currentPosition.Value, duration);
            if (_trimSelectionEnd <= _trimSelectionStart)
            {
                _trimSelectionEnd = _trimSelectionStart + TimeSpan.FromMilliseconds(100);
            }
        }

        NormalizeTrimSelection(duration);
        UpdateTrimUi();
        //=========================================================================
    }

    private async Task SeekPreviewToTrimMarkerAsync(TimeSpan marker)
    {
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        _trimCurrentPosition = marker;
        UpdateTrimUi();
        await SeekPreviewAsync(marker);
    }

    private async Task StartTrimPreviewPlaybackAsync()
    {
        //== precondition checks ================================================
        if (_activeOperation != AppOperation.None || !HasTrimCapableVideo())
        {
            return;
        }

        var selectionDuration = _trimSelectionEnd - _trimSelectionStart;
        if (selectionDuration <= TimeSpan.Zero)
        {
            return;
        }
        //=========================================================================

        //== playback setup =====================================================
        var playbackStart = _trimCurrentPosition < _trimSelectionStart || _trimCurrentPosition >= _trimSelectionEnd
            ? _trimSelectionStart
            : _trimCurrentPosition;

        _trimSelectionPlaybackActive = await SeekPreviewAsync(playbackStart) && await PlayPreviewAsync();
        if (_trimSelectionPlaybackActive)
        {
            _trimCurrentPosition = playbackStart;
            UpdateTrimUi();
        }
        //=========================================================================
    }

    private async Task StopTrimPreviewPlaybackAsync()
    {
        _trimSelectionPlaybackActive = false;
        await PausePreviewAsync();
        UpdateTrimUi();
    }

    private async Task SyncTrimPreviewStateAsync()
    {
        //== precondition checks ================================================
        if (_trimPositionSyncInFlight)
        {
            return;
        }

        if (!_previewReady || !_previewDocumentReady)
        {
            return;
        }

        if (_currentWorkspacePage != WorkspacePage.Trim && !_trimSelectionPlaybackActive)
        {
            return;
        }
        //=========================================================================

        _trimPositionSyncInFlight = true;

        try
        {
            //== preview sampling ================================================
            var observedDuration = await TryGetPlayerDurationAsync();
            var observedPosition = await TryGetPlayerCurrentTimeAsync();
            //=========================================================================

            var shouldStopPlayback = false;

            RunOnUiThread(() =>
            {
                //== state transition ===========================================
                if (observedDuration.HasValue)
                {
                    UpdateObservedTrimDuration(TimeSpan.FromSeconds(observedDuration.Value));
                }

                if (observedPosition.HasValue)
                {
                    _trimCurrentPosition = ClampTrimTime(TimeSpan.FromSeconds(observedPosition.Value), GetTrimDuration());
                }

                shouldStopPlayback = _trimSelectionPlaybackActive &&
                                     _trimCurrentPosition >= _trimSelectionEnd - TimeSpan.FromMilliseconds(40);

                UpdateTrimUi();
                //=================================================================
            });

            if (shouldStopPlayback)
            {
                await StopTrimPreviewPlaybackAsync();
                await SeekPreviewAsync(_trimSelectionEnd);
            }
        }
        finally
        {
            _trimPositionSyncInFlight = false;
        }
    }

    private async Task<TimeSpan?> GetCurrentOrObservedPlayerPositionAsync()
    {
        var observedPosition = await TryGetPlayerCurrentTimeAsync();
        if (observedPosition.HasValue)
        {
            return TimeSpan.FromSeconds(observedPosition.Value);
        }

        return _trimCurrentPosition;
    }

    private async void Form1_Shown(object? sender, EventArgs e)
    {
        //== first composed frame =============================================
        RefreshStudioFrame();
        await Task.Yield();
        //=====================================================================

        await EnsurePreviewReadyAsync();
        await CheckWatermarkRuntimeAsync(showInstallerUnavailableMessage: false);
        await CheckBackgroundRuntimeAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        //== precondition checks ==============================================
        var operationActive = _activeOperation != AppOperation.None ||
                              _watermarkInstallationInProgress ||
                              _backgroundInstallationInProgress;
        if (!_shutdownConfirmed && operationActive)
        {
            if (_shutdownCleanupInProgress)
            {
                e.Cancel = true;
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                "An operation is still running. Cancel it, clean temporary files, and close Veditor?",
                "Cancel Operation and Close",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            _shutdownCleanupInProgress = true;
            _ = CompleteShutdownCleanupAsync();
            return;
        }
        //=====================================================================

        _trimPreviewTimer.Stop();

        if (_downloadCts is not null)
        {
            _downloadCts.Cancel();
        }

        _watermarkCts?.Cancel();
        _watermarkRuntimeCheckCts?.Cancel();
        _backgroundRemovalCts?.Cancel();
        _backgroundRuntimeCts?.Cancel();
        TryStopBackgroundInstaller();
        CleanupBackgroundResult();

        try
        {
            if (_activeProcess is not null &&
                !_activeProcess.HasExited)
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup during shutdown.
        }

        base.OnFormClosing(e);
    }

    private async Task CompleteShutdownCleanupAsync()
    {
        //== cleanup ==========================================================
        UpdateStatus("Canceling active operation before closing...");
        _downloadCts?.Cancel();
        _watermarkCts?.Cancel();
        _watermarkRuntimeCheckCts?.Cancel();
        _backgroundRemovalCts?.Cancel();
        _backgroundRuntimeCts?.Cancel();
        TryStopBackgroundInstaller();
        try
        {
            if (_activeProcess is not null && !_activeProcess.HasExited)
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup while the owning operation unwinds.
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((_activeOperation != AppOperation.None || _watermarkInstallationInProgress || _backgroundInstallationInProgress) &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        _shutdownConfirmed = true;
        _shutdownCleanupInProgress = false;
        if (!IsDisposed && !Disposing)
        {
            Close();
        }
        //=====================================================================
    }

    private async void btnWatermarkRemove_Click(object? sender, EventArgs e)
    {
        //== cancel active cleanup ============================================
        if (_activeOperation == AppOperation.RemoveWatermark &&
            _watermarkActionMode == WatermarkActionMode.Remove)
        {
            CancelWatermarkOperation("Canceling watermark cleanup...");
            return;
        }
        //=====================================================================

        if (_activeOperation == AppOperation.None)
        {
            await StartWatermarkRemovalAsync();
        }
    }

    private async void btnWatermarkPreview_Click(object? sender, EventArgs e)
    {
        //== cancel active preview ============================================
        if (_activeOperation == AppOperation.RemoveWatermark &&
            _watermarkActionMode == WatermarkActionMode.Preview)
        {
            CancelWatermarkOperation("Canceling detection preview...");
            return;
        }
        //=====================================================================

        if (_activeOperation == AppOperation.None)
        {
            await PreviewWatermarkDetectionAsync();
        }
    }

    private async void btnWatermarkManualSelect_Click(object? sender, EventArgs e)
    {
        //== cancel active selection preview ==================================
        if (_activeOperation == AppOperation.RemoveWatermark &&
            _watermarkActionMode == WatermarkActionMode.Selection)
        {
            CancelWatermarkOperation("Canceling area selection...");
            return;
        }
        //=====================================================================

        if (_activeOperation == AppOperation.None)
        {
            await RunWatermarkOperationAsync(previewOnly: false, selectionPreviewOnly: true);
        }
    }

    private async void btnWatermarkInstall_Click(object? sender, EventArgs e)
    {
        if (_watermarkInstallationInProgress && _watermarkRuntimeCheckCts is not null)
        {
            UpdateStatus("Canceling WatermarkAI installation...");
            AppendLog("Canceling WatermarkAI runtime installation...");
            _watermarkRuntimeCheckCts.Cancel();
            UpdateWatermarkButtons();
            return;
        }

        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        await InstallOrRepairWatermarkRuntimeAsync();
    }

    private string? PromptWatermarkInstallationMode(bool repair)
    {
        //== configuration load ==============================================
        var recordedMode = _watermarkRuntimeStatus?.InstallationMode;
        var defaultMode = repair && recordedMode is "CPU" or "CUDA"
            ? recordedMode
            : "Auto";
        //=====================================================================

        //== dialog composition ==============================================
        using var dialog = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = AppBackgroundColor,
            ClientSize = new Size(540, 350),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = repair ? "Repair WatermarkAI Runtime" : "Install WatermarkAI Runtime"
        };

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = CreateSectionTitle(repair ? "Repair local AI runtime" : "Install local AI runtime");
        var explanation = CreateSectionSubtitle(
            "This downloads portable Python, pinned packages, Florence-2, and LaMA. " +
            "Allow at least 6 GB of free space. Models stay under LocalAppData and media remains local.");
        explanation.Margin = new Padding(0, 8, 0, 16);

        var modeLabel = CreateMicroCaption("Processing mode");
        var modeSelector = new ComboBox
        {
            BackColor = InputBackgroundColor,
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            IntegralHeight = false,
            Margin = new Padding(0, 6, 0, 0),
            MinimumSize = new Size(0, 34)
        };
        modeSelector.Items.AddRange(["Auto", "CPU", "CUDA"]);
        modeSelector.SelectedItem = defaultMode;

        var modeHelp = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = WarningTextColor,
            Margin = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(490, 0),
            Text = "Auto chooses CPU unless a compatible NVIDIA GPU and CUDA 12.4+ driver are detected. AMD processors and Radeon graphics use CPU mode."
        };

        var modePanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 3
        };
        modePanel.Controls.Add(modeLabel, 0, 0);
        modePanel.Controls.Add(modeSelector, 0, 1);
        modePanel.Controls.Add(modeHelp, 0, 2);

        var continueButton = new ClayButton { Text = repair ? "Repair runtime" : "Install runtime", Width = 138 };
        var cancelButton = new ClayButton { Text = "Cancel", Width = 96, DialogResult = DialogResult.Cancel };
        StyleActionButton(continueButton, primary: true);
        StyleActionButton(cancelButton, primary: false);
        continueButton.Click += (_, _) =>
        {
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 18, 0, 0),
            WrapContents = false
        };
        footer.Controls.Add(continueButton);
        footer.Controls.Add(cancelButton);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(explanation, 0, 1);
        layout.Controls.Add(modePanel, 0, 2);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(footer, 0, 4);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = continueButton;
        dialog.CancelButton = cancelButton;
        //=====================================================================

        return dialog.ShowDialog(this) == DialogResult.OK
            ? modeSelector.SelectedItem?.ToString() ?? "Auto"
            : null;
    }

    private static bool HasSufficientFreeSpace(string path, long requiredBytes, out long availableBytes)
    {
        availableBytes = 0L;
        try
        {
            //== precondition checks ==========================================
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            availableBytes = new DriveInfo(root).AvailableFreeSpace;
            return availableBytes >= requiredBytes;
            //=================================================================
        }
        catch
        {
            return false;
        }
    }

    private async Task InstallOrRepairWatermarkRuntimeAsync()
    {
        //== precondition checks ==============================================
        if (_watermarkRuntimeCheckInProgress)
        {
            return;
        }

        var installerPath = ResolveToolPath(
            "install-watermark-ai.ps1",
            out var installerSearchPaths,
            Path.Combine("tools", "watermark-ai", "install-watermark-ai.ps1"));
        if (installerPath is null)
        {
            MessageBox.Show(
                this,
                "The WatermarkAI installer script was not found.\r\n\r\n" +
                string.Join("\r\n", installerSearchPaths.Take(3)),
                "Watermark Installer Missing");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var ffmpegSearchPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));
        if (ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "WatermarkAI installation needs ffmpeg.exe.\r\n\r\n" +
                string.Join("\r\n", ffmpegSearchPaths.Take(3)),
                "ffmpeg Missing");
            return;
        }

        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershellPath))
        {
            MessageBox.Show(
                this,
                "Windows PowerShell is required to install the local WatermarkAI runtime.",
                "PowerShell Missing");
            return;
        }

        var shouldRepair = Directory.Exists(_veditorPaths.WatermarkAiRuntimeDirectory);
        var selectedMode = PromptWatermarkInstallationMode(shouldRepair);
        if (selectedMode is null)
        {
            return;
        }

        if (!HasSufficientFreeSpace(_veditorPaths.VeditorRoot, 6L * 1024 * 1024 * 1024, out var availableBytes))
        {
            MessageBox.Show(
                this,
                $"WatermarkAI needs at least 6 GB of free space for Python, packages, models, and installation files.\r\n\r\n" +
                $"Available: {FormatFileSize(availableBytes)}",
                "Not Enough Disk Space");
            return;
        }
        //=====================================================================

        var installationSucceeded = false;

        //== state transition =================================================
        _watermarkRuntimeCheckInProgress = true;
        _watermarkInstallationInProgress = true;
        _watermarkRuntimeCheckCts = new CancellationTokenSource();
        _watermarkRuntimeStatus = null;
        SetWatermarkRuntimeLabels("Installing...", "Detecting...", WarningTextColor);
        if (_watermarkInstallButton is not null)
        {
            _watermarkInstallButton.Text = "Installing runtime...";
        }
        UpdateWatermarkButtons();
        UpdateStatus("Installing WatermarkAI runtime...");
        AppendLog(string.Empty);
        AppendLog("WatermarkAI runtime installation started.");
        //=====================================================================

        try
        {
            //== external service call ========================================
            var startInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(installerPath);
            startInfo.ArgumentList.Add("-Mode");
            startInfo.ArgumentList.Add(selectedMode);
            startInfo.ArgumentList.Add("-FfmpegPath");
            startInfo.ArgumentList.Add(ffmpegPath);
            if (shouldRepair)
            {
                startInfo.ArgumentList.Add("-Repair");
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = _watermarkRuntimeCheckCts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort process-tree cleanup during shutdown.
                }
            });

            var outputTask = ReadLinesAsync(process.StandardOutput, AppendLog);
            var errorTask = ReadLinesAsync(process.StandardError, AppendLog);
            await AwaitRedirectedProcessAsync(
                process,
                outputTask,
                errorTask,
                _watermarkRuntimeCheckCts.Token);

            if (_watermarkRuntimeCheckCts.IsCancellationRequested)
            {
                UpdateStatus("WatermarkAI installation canceled");
                AppendLog("WatermarkAI runtime installation was canceled.");
                return;
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The WatermarkAI installer exited with code {process.ExitCode}. See the log for details.");
            }

            installationSucceeded = true;
            UpdateStatus("WatermarkAI runtime installed");
            AppendLog("WatermarkAI runtime installation completed.");
            //=================================================================
        }
        catch (Exception ex)
        {
            //== error handling ===============================================
            UpdateStatus("WatermarkAI installation failed");
            AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "WatermarkAI Installation Error");
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            _watermarkRuntimeCheckCts?.Dispose();
            _watermarkRuntimeCheckCts = null;
            _watermarkRuntimeCheckInProgress = false;
            _watermarkInstallationInProgress = false;
            if (_watermarkInstallButton is not null)
            {
                _watermarkInstallButton.Text = "Install or repair runtime";
            }
            UpdateWatermarkButtons();
            //=================================================================
        }

        //== runtime verification =============================================
        await CheckWatermarkRuntimeAsync(showInstallerUnavailableMessage: false);
        if (installationSucceeded && _watermarkRuntimeStatus?.IsInstalled == true)
        {
            MessageBox.Show(
                this,
                _watermarkRuntimeStatus.CudaAvailable
                    ? "WatermarkAI is ready and CUDA acceleration is available."
                    : "WatermarkAI is ready in CPU mode.",
                "WatermarkAI Ready");
        }
        //=====================================================================
    }

    private void CancelWatermarkOperation(string statusMessage)
    {
        //== cancellation request =============================================
        if (_watermarkCts is null || _watermarkCts.IsCancellationRequested)
        {
            return;
        }

        UpdateStatus(statusMessage);
        AppendLog(statusMessage);
        _watermarkCts.Cancel();
        UpdateWatermarkButtons();
        //=====================================================================
    }

    private Task StartWatermarkRemovalAsync()
    {
        return RunWatermarkOperationAsync(previewOnly: false, selectionPreviewOnly: false);
    }

    private Task PreviewWatermarkDetectionAsync()
    {
        return RunWatermarkOperationAsync(previewOnly: true, selectionPreviewOnly: false);
    }

    private async Task RunWatermarkOperationAsync(bool previewOnly, bool selectionPreviewOnly)
    {
        //== precondition checks ==============================================
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        var sourcePath = GetPreferredMediaPath();
        if (sourcePath is null || !IsSupportedWatermarkMedia(sourcePath))
        {
            MessageBox.Show(
                this,
                "Open a supported image or video before using watermark cleanup.",
                "Image or Video Required");
            return;
        }

        if (!IsWatermarkAuthorizationConfirmed(sourcePath))
        {
            MessageBox.Show(
                this,
                "Confirm that you own this media or have permission to modify it before continuing.",
                "Permission Confirmation Required");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var ffmpegSearchPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));

        if (ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "Watermark cleanup needs ffmpeg.exe.\r\n\r\n" +
                "Place it in one of these locations:\r\n" +
                string.Join("\r\n", ffmpegSearchPaths.Take(3)) +
                "\r\n\r\nOr install it on PATH.",
                "ffmpeg Missing");
            return;
        }

        await CheckWatermarkRuntimeAsync(showInstallerUnavailableMessage: false);
        if (_watermarkRuntimeStatus?.IsInstalled != true)
        {
            MessageBox.Show(
                this,
                "The local watermark runtime is not installed or needs repair. " +
                "Use Install or repair runtime to see the missing components.",
                "Watermark Runtime Unavailable");
            return;
        }

        var options = BuildWatermarkRemovalOptions(previewOnly, selectionPreviewOnly, sourcePath);
        if (!previewOnly && !selectionPreviewOnly)
        {
            var outputPath = SelectWatermarkOutputPath(sourcePath);
            if (outputPath is null)
            {
                return;
            }

            var requiredWorkingSpace = Math.Max(
                512L * 1024 * 1024,
                new FileInfo(sourcePath).Length * 3L);
            if (!HasSufficientFreeSpace(
                    Path.GetDirectoryName(outputPath) ?? sourcePath,
                    requiredWorkingSpace,
                    out var processingFreeBytes))
            {
                MessageBox.Show(
                    this,
                    $"There is not enough free space to process this file safely.\r\n\r\n" +
                    $"Required working space: {FormatFileSize(requiredWorkingSpace)}\r\n" +
                    $"Available: {FormatFileSize(processingFreeBytes)}",
                    "Not Enough Disk Space");
                return;
            }

            options = options with { OutputPath = outputPath };
        }
        if (!previewOnly &&
            !selectionPreviewOnly &&
            _watermarkManualModeRadioButton?.Checked == true &&
            options.Regions.Count == 0)
        {
            MessageBox.Show(
                this,
                "Select at least one watermark area before starting fixed-area cleanup.",
                "Select a Watermark Area");
            return;
        }
        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, validationErrors),
                "Invalid Watermark Settings");
            return;
        }
        //=====================================================================

        //== state transition ================================================
        _watermarkActionMode = selectionPreviewOnly
            ? WatermarkActionMode.Selection
            : previewOnly
                ? WatermarkActionMode.Preview
                : WatermarkActionMode.Remove;
        _lastWatermarkProgressStage = null;
        _watermarkCts = new CancellationTokenSource();
        SetUiBusy(AppOperation.RemoveWatermark);

        var actionName = selectionPreviewOnly
            ? "Area selection"
            : previewOnly
                ? "Detection preview"
                : "Watermark cleanup";
        UpdateStatus(selectionPreviewOnly
            ? "Preparing selection frame..."
            : previewOnly
                ? "Preparing detection preview..."
                : "Preparing watermark cleanup...");
        AppendLog(string.Empty);
        AppendLog($"{actionName}: {Path.GetFileName(sourcePath)}");
        AppendLog($"ffmpeg: {ffmpegPath}");
        AddActivityEntry(
            ActivityFeedIconKind.Export,
            $"{actionName} started: {Path.GetFileName(sourcePath)}");
        //=====================================================================

        try
        {
            //== external service call ========================================
            var progress = new Progress<WatermarkProgressUpdate>(HandleWatermarkProgress);
            var status = new Progress<string>(HandleWatermarkStatus);
            var log = new Progress<string>(AppendLog);

            var result = await _watermarkRemovalService.RemoveAsync(
                sourcePath,
                ffmpegPath,
                options,
                progress,
                status,
                log,
                _watermarkCts.Token);
            //=================================================================

            //== result handling ==============================================
            if (result.WasCancelled)
            {
                UpdateStatus(previewOnly || selectionPreviewOnly ? "Watermark preview canceled" : "Watermark cleanup canceled");
                AppendLog(result.ErrorMessage ?? "Watermark processing was canceled.");
                AddActivityEntry(ActivityFeedIconKind.Neutral, $"{actionName} canceled.");
                return;
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath))
            {
                var errorMessage = result.ErrorMessage ?? "The watermark worker did not produce an output file.";
                UpdateStatus(previewOnly || selectionPreviewOnly ? "Watermark preview failed" : "Watermark cleanup failed");
                AppendLog(errorMessage);
                AddActivityEntry(
                    ActivityFeedIconKind.Error,
                    BuildActivityFailureMessage($"{actionName} failed", errorMessage),
                    countsAsError: true);
                MessageBox.Show(this, errorMessage, $"{actionName} Error");
                return;
            }

            SetProgressValue(100);
            AppendLog($"{actionName} completed in {FormatOperationDuration(result.Duration)}.");
            AppendLog(result.UsedGpu
                ? "Watermark processing used GPU acceleration."
                : "Watermark processing used the CPU.");

            if (previewOnly || selectionPreviewOnly)
            {
                UpdateStatus(selectionPreviewOnly ? "Selection frame ready" : "Detection preview ready");
                AddActivityEntry(
                    ActivityFeedIconKind.Success,
                    $"Watermark selection ready for {Path.GetFileName(sourcePath)}");
                ShowWatermarkRegionEditorDialog(result, sourcePath, options, selectionPreviewOnly);
                return;
            }

            _lastDownloadedFilePath = result.OutputPath;
            SetCurrentMediaSource(result.OutputPath);

            if (await EnsurePreviewReadyAsync())
            {
                await LoadPreviewAsync(result.OutputPath, switchToPreview: true);
            }
            else
            {
                FocusPreviewStage();
            }

            ShowWorkspacePage(WorkspacePage.Watermark);
            UpdateStatus("Watermark cleanup completed");
            AppendLog($"Watermark-cleaned video: {result.OutputPath}");
            AppendLog(BuildWatermarkSizeSummary(sourcePath, result.OutputPath));
            AddActivityEntry(
                ActivityFeedIconKind.Success,
                $"Watermark cleanup complete \u2192 {BuildActivityFileSummary(result.OutputPath)}",
                countsAsExport: true);
            UpdateConversionButtons();
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            _watermarkCts?.Dispose();
            _watermarkCts = null;
            _watermarkActionMode = WatermarkActionMode.None;
            SetUiBusy(AppOperation.None);
            //=================================================================
        }
    }

    private WatermarkRemovalOptions BuildWatermarkRemovalOptions(
        bool previewOnly,
        bool selectionPreviewOnly,
        string sourcePath)
    {
        //== input collection =================================================
        return new WatermarkRemovalOptions
        {
            DetectionPrompt = _watermarkDetectionPromptTextBox?.Text ?? string.Empty,
            MaxBoundingBoxPercent = (double)(_watermarkMaximumDetectionSizeInput?.Value ?? 10M),
            DetectionSkip = decimal.ToInt32(_watermarkDetectionIntervalInput?.Value ?? 3M),
            FadeInSeconds = (double)(_watermarkFadeInInput?.Value ?? 0.25M),
            FadeOutSeconds = (double)(_watermarkFadeOutInput?.Value ?? 0.25M),
            MaskPaddingPercent = (double)(_watermarkMaskPaddingInput?.Value ?? 0.5M),
            UseGpuWhenAvailable = _watermarkUseGpuCheckBox?.Checked ?? true,
            PreviewOnly = previewOnly,
            SelectionPreviewOnly = selectionPreviewOnly,
            Regions = !previewOnly &&
                      !selectionPreviewOnly &&
                      _watermarkManualModeRadioButton?.Checked == true &&
                      string.Equals(_watermarkSelectionMediaPath, sourcePath, StringComparison.OrdinalIgnoreCase)
                ? _watermarkSelectedRegions
                : Array.Empty<WatermarkRegion>(),
            Overwrite = false
        };
        //=====================================================================
    }

    private string? SelectWatermarkOutputPath(string sourcePath)
    {
        //== output shaping ===================================================
        var proposedPath = WatermarkOutputPathGenerator.CreateProcessedOutputPath(sourcePath);
        var proposedDirectory = Path.GetDirectoryName(proposedPath);
        if (!string.IsNullOrWhiteSpace(proposedDirectory) && CanWriteToDirectory(proposedDirectory))
        {
            return proposedPath;
        }

        var extension = Path.GetExtension(proposedPath);
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = extension.TrimStart('.'),
            FileName = Path.GetFileName(proposedPath),
            Filter = IsSupportedWatermarkImage(sourcePath)
                ? $"{extension.ToUpperInvariant()} image|*{extension}"
                : "MP4 video|*.mp4",
            InitialDirectory = Directory.Exists(_currentOutputFolder)
                ? _currentOutputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            OverwritePrompt = false,
            RestoreDirectory = true,
            Title = "Choose Watermark Output Location"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        var selectedPath = Path.GetFullPath(Path.ChangeExtension(dialog.FileName, extension));
        if (string.Equals(selectedPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "The output cannot overwrite the original media. Choose a different file name.",
                "Original Media Is Protected");
            return null;
        }

        return File.Exists(selectedPath)
            ? WatermarkOutputPathGenerator.CreateUniquePath(
                Path.GetDirectoryName(selectedPath) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(selectedPath),
                Path.GetExtension(selectedPath))
            : selectedPath;
        //=====================================================================
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            //== precondition checks ==========================================
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".veditor-write-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
            //=================================================================
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedWatermarkImage(string mediaPath)
    {
        return Path.GetExtension(mediaPath).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff";
    }

    private async Task CheckWatermarkRuntimeAsync(bool showInstallerUnavailableMessage)
    {
        if (_watermarkRuntimeCheckInProgress)
        {
            return;
        }

        //== state transition ================================================
        _watermarkRuntimeCheckInProgress = true;
        SetWatermarkRuntimeLabels("Checking...", "Checking...", SecondaryTextColor);
        UpdateWatermarkButtons();
        _watermarkRuntimeCheckCts = new CancellationTokenSource();
        //=====================================================================

        try
        {
            //== runtime inspection ===========================================
            var ffmpegPath = ResolveToolPath(
                "ffmpeg.exe",
                out _,
                Path.Combine("tools", "ffmpeg.exe"),
                Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));
            var log = new Progress<string>(AppendLog);
            _watermarkRuntimeStatus = await _watermarkRemovalService.CheckRuntimeAsync(
                ffmpegPath,
                log,
                _watermarkRuntimeCheckCts.Token);
            ApplyWatermarkRuntimeStatus(_watermarkRuntimeStatus);
            //=================================================================

            //== user feedback ===============================================
            if (_watermarkRuntimeStatus.IsInstalled)
            {
                if (showInstallerUnavailableMessage)
                {
                    AppendLog("Watermark runtime check completed: ready.");
                }

                return;
            }

            if (showInstallerUnavailableMessage && !Disposing && !IsDisposed)
            {
                var details = DescribeMissingWatermarkRuntimeComponents(_watermarkRuntimeStatus);
                AppendLog($"Watermark runtime needs attention: {details}");
                MessageBox.Show(
                    this,
                    WatermarkRuntimeStatusText.GetPrimaryMessage(_watermarkRuntimeStatus) + "\r\n\r\n" +
                    $"Expected runtime location:\r\n{_veditorPaths.WatermarkAiRuntimeDirectory}",
                    "Watermark Runtime Status");
            }
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            _watermarkRuntimeCheckCts?.Dispose();
            _watermarkRuntimeCheckCts = null;
            _watermarkRuntimeCheckInProgress = false;
            UpdateWatermarkButtons();
            //=================================================================
        }
    }

    private void ApplyWatermarkRuntimeStatus(WatermarkRuntimeStatus runtimeStatus)
    {
        //== output shaping ===================================================
        var anyRuntimeFiles = runtimeStatus.PythonExists ||
                              runtimeStatus.WorkerExists ||
                              File.Exists(_veditorPaths.InstallationMarkerPath);
        var runtimeText = runtimeStatus.IsInstalled
            ? "Ready"
            : runtimeStatus.MarkerOutdated
                ? "Update required"
            : anyRuntimeFiles
                ? "Repair required"
                : "Not installed";
        var gpuText = runtimeStatus.DependenciesAvailable
            ? runtimeStatus.CudaAvailable
                ? "CUDA available"
                : "CPU only"
            : "Unknown";
        var color = runtimeStatus.IsInstalled
            ? SuccessColor
            : anyRuntimeFiles
                ? WarningTextColor
                : ErrorColor;

        SetWatermarkRuntimeLabels(runtimeText, gpuText, color);
        UpdateWatermarkButtons();
        //=====================================================================
    }

    private void SetWatermarkRuntimeLabels(string runtimeText, string gpuText, Color runtimeColor)
    {
        RunOnUiThread(() =>
        {
            if (_watermarkRuntimeStatusLabel is not null)
            {
                _watermarkRuntimeStatusLabel.Text = runtimeText;
                _watermarkRuntimeStatusLabel.ForeColor = runtimeColor;
            }

            if (_watermarkGpuStatusLabel is not null)
            {
                _watermarkGpuStatusLabel.Text = gpuText;
                _watermarkGpuStatusLabel.ForeColor = gpuText == "CUDA available"
                    ? SuccessColor
                    : gpuText == "CPU only"
                        ? WarningTextColor
                        : SecondaryTextColor;
            }
        });
    }

    private static string DescribeMissingWatermarkRuntimeComponents(WatermarkRuntimeStatus runtimeStatus)
    {
        return WatermarkRuntimeStatusText.DescribeMissingComponents(runtimeStatus);
    }

    private void HandleWatermarkProgress(WatermarkProgressUpdate update)
    {
        RunOnUiThread(() =>
        {
            //== progress shaping =============================================
            if (_activeOperation != AppOperation.RemoveWatermark)
            {
                return;
            }

            var stageText = MapWatermarkStage(update.Stage, update.Message);
            if (!string.Equals(_lastWatermarkProgressStage, stageText, StringComparison.Ordinal))
            {
                _lastWatermarkProgressStage = stageText;
                AppendLog($"Watermark: {stageText}");
            }

            if (update.Percent.HasValue)
            {
                if (progressDownload.Style != ProgressBarStyle.Continuous)
                {
                    progressDownload.MarqueeAnimationSpeed = 0;
                    progressDownload.Style = ProgressBarStyle.Continuous;
                }

                progressDownload.Value = Math.Clamp(
                    (int)Math.Round(update.Percent.Value, MidpointRounding.AwayFromZero),
                    progressDownload.Minimum,
                    progressDownload.Maximum);
                lblStatus.Text = $"{stageText} · {update.Percent.Value:0.#}%";
            }
            else
            {
                if (progressDownload.Style != ProgressBarStyle.Marquee)
                {
                    progressDownload.Style = ProgressBarStyle.Marquee;
                    progressDownload.MarqueeAnimationSpeed = 30;
                }

                lblStatus.Text = stageText;
            }
            //=================================================================
        });
    }

    private void HandleWatermarkStatus(string statusMessage)
    {
        if (_activeOperation != AppOperation.RemoveWatermark || string.IsNullOrWhiteSpace(statusMessage))
        {
            return;
        }

        UpdateStatus(MapWatermarkStage(statusMessage, statusMessage));
    }

    private static string MapWatermarkStage(string? stage, string? message)
    {
        //== normalization ====================================================
        var normalizedStage = (stage ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        var normalizedMessage = message?.Trim();
        //=====================================================================

        //== output shaping ===================================================
        if (normalizedStage is "loading_models" or "loading_model")
        {
            if (normalizedMessage?.Contains("lama", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Loading LaMA";
            }

            if (normalizedMessage?.Contains("florence", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Loading Florence-2";
            }

            return "Loading AI models";
        }

        return normalizedStage switch
        {
            "checking_runtime" or "runtime_check" => "Checking runtime",
            "loading_florence" or "loading_florence2" or "loading_florence_2" or "florence" => "Loading Florence-2",
            "loading_lama" or "lama" => "Loading LaMA",
            "detection" or "detecting" or "detecting_watermark" => "Detecting watermark",
            "mask" or "masks" or "preparing_masks" or "mask_preparation" => "Preparing masks",
            "inpainting" or "removal" or "removing_watermark" => "Removing watermark",
            "audio" or "restoring_audio" or "audio_restore" => "Restoring audio",
            "finalizing" or "finalising" or "finalizing_output" or "finalising_output" => "Finalising output",
            "completed" or "complete" => "Completed",
            _ when !string.IsNullOrWhiteSpace(normalizedMessage) => normalizedMessage,
            _ when !string.IsNullOrWhiteSpace(normalizedStage) =>
                CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalizedStage.Replace('_', ' ')),
            _ => "Processing watermark"
        };
        //=====================================================================
    }

    private bool IsWatermarkAuthorizationConfirmed(string mediaPath)
    {
        return _watermarkAuthorizationCheckBox?.Checked == true &&
               !string.IsNullOrWhiteSpace(_watermarkAuthorizationMediaPath) &&
               string.Equals(
                   _watermarkAuthorizationMediaPath,
                   mediaPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void ResetWatermarkAuthorizationForMedia(string mediaPath)
    {
        //== state transition ================================================
        if (string.Equals(
                _watermarkAuthorizationMediaPath,
                mediaPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _watermarkAuthorizationMediaPath = null;
        if (_watermarkAuthorizationCheckBox is not null)
        {
            _watermarkAuthorizationCheckBox.Checked = false;
        }

        _watermarkSelectedRegions = Array.Empty<WatermarkRegion>();
        _watermarkSelectionMediaPath = null;
        if (_watermarkAutoModeRadioButton is not null)
        {
            _watermarkAutoModeRadioButton.Checked = true;
        }
        UpdateWatermarkSelectionStatus();
        //=====================================================================
    }

    private void ShowWatermarkRegionEditorDialog(
        WatermarkRemovalResult result,
        string sourcePath,
        WatermarkRemovalOptions options,
        bool manualSelection)
    {
        if (string.IsNullOrWhiteSpace(result.OutputPath))
        {
            return;
        }

        using var dialogCts = new CancellationTokenSource();
        try
        {
            //== preview load =================================================
            using var stream = new FileStream(
                result.OutputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var loadedImage = Image.FromStream(stream);
            //=================================================================

            //== dialog composition ==========================================
            using var dialog = new Form
            {
                AutoScaleMode = AutoScaleMode.Dpi,
                BackColor = AppBackgroundColor,
                ClientSize = new Size(1040, 760),
                FormBorderStyle = FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                MinimumSize = new Size(760, 560),
                ShowIcon = false,
                StartPosition = FormStartPosition.CenterParent,
                Text = "Select Watermark Areas"
            };

            var layout = new TableLayoutPanel
            {
                BackColor = AppBackgroundColor,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(18),
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var summaryLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = result.NoRegionDetected && !manualSelection ? WarningTextColor : PrimaryTextColor,
                Margin = new Padding(0, 0, 0, 12),
                MaximumSize = new Size(980, 0),
                Text = result.NoRegionDetected && !manualSelection
                    ? "No watermark region was detected in the preview frame. Draw a box to select it manually."
                    : "Drag to draw areas. Select a box to move or resize it; press Delete to remove it. The dotted outline shows mask padding."
            };

            var editor = new WatermarkRegionEditor
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = PrimaryTextColor,
                MaskPaddingPercent = options.MaskPaddingPercent,
                MinimumSize = new Size(560, 360)
            };
            editor.SetImage(loadedImage);
            if (!manualSelection && result.Detections is { Count: > 0 })
            {
                editor.SetDetections(result.Detections);
            }
            else if (string.Equals(_watermarkSelectionMediaPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                editor.SetRegions(_watermarkSelectedRegions);
            }

            var imageSurface = CreateCard(StageBackgroundColor, CardBorderColor, 16);
            imageSurface.Dock = DockStyle.Fill;
            imageSurface.Margin = Padding.Empty;
            imageSurface.Padding = new Padding(10);
            imageSurface.Controls.Add(editor);

            var frameLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = SecondaryTextColor,
                Margin = new Padding(8, 9, 8, 0),
                Text = result.SourceFrame.HasValue ? $"Frame {result.SourceFrame.Value:N0}" : "Still image"
            };
            var previousButton = new ClayButton { Text = "Previous frame", Width = 126 };
            var nextButton = new ClayButton { Text = "Next frame", Width = 108 };
            var includeButton = new ClayButton { Text = "Include candidate", Width = 132 };
            var removeButton = new ClayButton { Text = "Remove selected", Width = 128 };
            var clearButton = new ClayButton { Text = "Clear all", Width = 92 };
            foreach (var button in new[] { previousButton, nextButton, includeButton, removeButton, clearButton })
            {
                StyleActionButton(button, primary: false);
                button.Margin = new Padding(0, 0, 8, 0);
            }

            var frameControls = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 12, 0, 0),
                WrapContents = true
            };
            frameControls.Controls.Add(previousButton);
            frameControls.Controls.Add(nextButton);
            frameControls.Controls.Add(frameLabel);
            frameControls.Controls.Add(includeButton);
            frameControls.Controls.Add(removeButton);
            frameControls.Controls.Add(clearButton);

            var saveButton = new ClayButton { Text = "Use selected areas", Width = 154 };
            var closeButton = new ClayButton { Text = "Close", Width = 100 };
            StyleActionButton(saveButton, primary: true);
            StyleActionButton(closeButton, primary: false);

            var selectionCountLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = SecondaryTextColor,
                Margin = new Padding(0, 10, 12, 0)
            };
            var footer = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 12, 0, 0),
                WrapContents = true
            };
            footer.Controls.Add(saveButton);
            footer.Controls.Add(closeButton);
            footer.Controls.Add(selectionCountLabel);

            void RefreshEditorActions()
            {
                selectionCountLabel.Text = editor.RegionCount == 1
                    ? "1 area selected"
                    : $"{editor.RegionCount:N0} areas selected";
                saveButton.Enabled = editor.RegionCount > 0;
                includeButton.Enabled = editor.HasSelectedRegion && !editor.SelectedRegionIncluded;
                removeButton.Enabled = editor.HasSelectedRegion;
                clearButton.Enabled = editor.RegionCount > 0 || editor.HasSelectedRegion;
            }

            var currentFrame = result.SourceFrame;
            previousButton.Enabled = currentFrame > 0;
            nextButton.Enabled = currentFrame.HasValue;
            editor.SelectionChanged += (_, _) => RefreshEditorActions();
            includeButton.Click += (_, _) => editor.IncludeSelected();
            removeButton.Click += (_, _) => editor.RemoveSelected();
            clearButton.Click += (_, _) => editor.ClearRegions();
            closeButton.Click += (_, _) => dialog.Close();
            saveButton.Click += (_, _) =>
            {
                _watermarkSelectedRegions = editor.Regions;
                _watermarkSelectionMediaPath = sourcePath;
                if (_watermarkManualModeRadioButton is not null)
                {
                    _watermarkManualModeRadioButton.Checked = true;
                }
                UpdateWatermarkSelectionStatus();
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            async Task LoadAdjacentFrameAsync(int frameIndex)
            {
                //== external service call ====================================
                previousButton.Enabled = false;
                nextButton.Enabled = false;
                frameLabel.Text = "Loading frame...";
                var ffmpegPath = ResolveToolPath(
                    "ffmpeg.exe",
                    out _,
                    Path.Combine("tools", "ffmpeg.exe"),
                    Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));
                if (ffmpegPath is null)
                {
                    frameLabel.Text = "FFmpeg unavailable";
                    return;
                }

                var frameOptions = options with
                {
                    PreviewOnly = false,
                    SelectionPreviewOnly = true,
                    PreviewFrameIndex = frameIndex,
                    Regions = Array.Empty<WatermarkRegion>()
                };
                var frameResult = await _watermarkRemovalService.RemoveAsync(
                    sourcePath,
                    ffmpegPath,
                    frameOptions,
                    cancellationToken: dialogCts.Token);
                if (!frameResult.Success || string.IsNullOrWhiteSpace(frameResult.OutputPath))
                {
                    frameLabel.Text = frameResult.ErrorMessage ?? "Frame unavailable";
                    previousButton.Enabled = currentFrame > 0;
                    nextButton.Enabled = currentFrame.HasValue;
                    return;
                }

                try
                {
                    using var frameStream = new FileStream(
                        frameResult.OutputPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var frameImage = Image.FromStream(frameStream);
                    editor.SetImage(frameImage);
                    currentFrame = frameResult.SourceFrame ?? frameIndex;
                    frameLabel.Text = $"Frame {currentFrame.Value:N0}";
                }
                finally
                {
                    TryDeleteWatermarkPreview(frameResult.OutputPath);
                }

                previousButton.Enabled = currentFrame > 0;
                nextButton.Enabled = currentFrame.HasValue;
                //=================================================================
            }

            previousButton.Click += async (_, _) =>
            {
                if (currentFrame > 0)
                {
                    await LoadAdjacentFrameAsync(currentFrame.Value - 1);
                }
            };
            nextButton.Click += async (_, _) =>
            {
                if (currentFrame.HasValue)
                {
                    await LoadAdjacentFrameAsync(currentFrame.Value + 1);
                }
            };
            dialog.FormClosing += (_, _) => dialogCts.Cancel();

            RefreshEditorActions();
            layout.Controls.Add(summaryLabel, 0, 0);
            layout.Controls.Add(imageSurface, 0, 1);
            layout.Controls.Add(frameControls, 0, 2);
            layout.Controls.Add(footer, 0, 3);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = saveButton;
            dialog.CancelButton = closeButton;
            dialog.ShowDialog(this);
            //=================================================================
        }
        catch (Exception ex)
        {
            //== error handling ===============================================
            AppendLog($"Watermark area editor could not be displayed: {ex.Message}");
            MessageBox.Show(
                this,
                $"The watermark area editor could not be displayed.\r\n\r\n{ex.Message}",
                "Watermark Area Editor Error");
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            TryDeleteWatermarkPreview(result.OutputPath);
            //=================================================================
        }
    }

    private void TryDeleteWatermarkPreview(string? previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return;
        }

        try
        {
            if (File.Exists(previewPath))
            {
                File.Delete(previewPath);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Could not delete the temporary watermark preview: {ex.Message}");
        }
    }

    private void ShowWatermarkDetectionPreviewDialog(
        string previewPath,
        string sourcePath,
        WatermarkRemovalOptions options)
    {
        try
        {
            //== preview load =================================================
            using var stream = new FileStream(
                previewPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var loadedImage = Image.FromStream(stream);
            using var previewImage = new Bitmap(loadedImage);
            //=================================================================

            //== dialog composition ==========================================
            using var dialog = new Form
            {
                AutoScaleMode = AutoScaleMode.Dpi,
                BackColor = AppBackgroundColor,
                ClientSize = new Size(900, 640),
                FormBorderStyle = FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                MinimumSize = new Size(640, 480),
                ShowIcon = false,
                StartPosition = FormStartPosition.CenterParent,
                Text = "Watermark Detection Preview"
            };

            var layout = new TableLayoutPanel
            {
                BackColor = AppBackgroundColor,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(18),
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var summaryLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = PrimaryTextColor,
                Margin = new Padding(0, 0, 0, 12),
                Text = $"{Path.GetFileName(sourcePath)}{Environment.NewLine}" +
                       $"Prompt: {options.DetectionPrompt.Trim()} · Maximum detection size: {options.MaxBoundingBoxPercent:0.#}%"
            };

            var imageSurface = CreateCard(StageBackgroundColor, CardBorderColor, 16);
            imageSurface.Dock = DockStyle.Fill;
            imageSurface.Margin = Padding.Empty;
            imageSurface.Padding = new Padding(10);

            var pictureBox = new PictureBox
            {
                BackColor = StageBackgroundColor,
                Dock = DockStyle.Fill,
                Image = previewImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                TabStop = false
            };
            imageSurface.Controls.Add(pictureBox);

            var closeButton = new ClayButton
            {
                Text = "Close",
                Width = 110
            };
            StyleActionButton(closeButton, primary: true);
            closeButton.Click += (_, _) => dialog.Close();

            var footer = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 12, 0, 0),
                WrapContents = false
            };
            footer.Controls.Add(closeButton);

            layout.Controls.Add(summaryLabel, 0, 0);
            layout.Controls.Add(imageSurface, 0, 1);
            layout.Controls.Add(footer, 0, 2);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = closeButton;
            dialog.ShowDialog(this);
            pictureBox.Image = null;
            //=================================================================
        }
        catch (Exception ex)
        {
            //== error handling ===============================================
            AppendLog($"Detection preview could not be displayed: {ex.Message}");
            MessageBox.Show(
                this,
                $"The detection preview could not be displayed.\r\n\r\n{ex.Message}",
                "Detection Preview Error");
            //=================================================================
        }
        finally
        {
            //== cleanup ======================================================
            try
            {
                if (File.Exists(previewPath))
                {
                    File.Delete(previewPath);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not delete the temporary detection preview: {ex.Message}");
            }
            //=================================================================
        }
    }

    private static string BuildWatermarkSizeSummary(string sourcePath, string outputPath)
    {
        //== output shaping ===================================================
        if (!File.Exists(sourcePath) || !File.Exists(outputPath))
        {
            return "Watermark output size comparison unavailable.";
        }

        var sourceSize = new FileInfo(sourcePath).Length;
        var outputSize = new FileInfo(outputPath).Length;
        var difference = outputSize - sourceSize;
        var percentChange = sourceSize == 0
            ? 0D
            : Math.Abs(difference) * 100D / sourceSize;

        if (difference == 0)
        {
            return $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | No size change.";
        }

        return difference < 0
            ? $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | {FormatFileSize(-difference)} smaller ({percentChange:0.#}%)."
            : $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | {FormatFileSize(difference)} larger ({percentChange:0.#}%).";
        //=====================================================================
    }

    private static string FormatOperationDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1D
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private async Task StartDownloadAsync()
    {
        //== input collection ==================================================
        var url = txtUrl.Text.Trim();
        var outputFolder = txtOutputFolder.Text.Trim();
        var extractMp3 = chkExtractAudio.Checked;
        var extractWav = chkExtractWavAudio.Checked;
        var extractAudio = extractMp3 || extractWav;
        var primaryAudioFormat = extractWav ? "wav" : "mp3";
        //=======================================================================

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Enter a video URL first.", "Missing URL");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "Enter a valid absolute URL.", "Invalid URL");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            MessageBox.Show(this, "Choose an output folder first.", "Missing folder");
            return;
        }

        Directory.CreateDirectory(outputFolder);
        _currentOutputFolder = outputFolder;
        _lastDownloadedFilePath = null;
        _downloadAutoPreviewTriggered = false;
        _downloadAutoPreviewLoadedPath = null;
        _downloadStartedUtc = DateTime.UtcNow;
        _outputFolderSnapshot = CaptureOutputFolderSnapshot(outputFolder);

        var ytDlpPath = ResolveToolPath(
            "yt-dlp.exe",
            out var ytDlpSearchPaths,
            Path.Combine("tools", "yt-dlp.exe"));

        if (ytDlpPath is null)
        {
            MessageBox.Show(
                this,
                "yt-dlp.exe was not found.\r\n\r\n" +
                "Place it in one of these locations and rebuild if needed:\r\n" +
                string.Join("\r\n", ytDlpSearchPaths.Take(3)) +
                "\r\n\r\nOr install it on PATH.",
                "yt-dlp Missing");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out _,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));

        if (extractAudio && ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "Audio extraction needs ffmpeg.\r\n\r\nPlace ffmpeg.exe and ffprobe.exe in tools\\ffmpeg\\",
                "ffmpeg Missing");
            return;
        }

        var denoPath = ResolveToolPath(
            "deno.exe",
            out _,
            Path.Combine("tools", "deno.exe"),
            Path.Combine("tools", "runtimes", "deno.exe"));

        txtLog.Clear();
        AppendLog($"yt-dlp: {ytDlpPath}");
        AppendLog(ffmpegPath is null
            ? "ffmpeg: not found. Video downloads may fall back to single-file formats only."
            : $"ffmpeg: {ffmpegPath}");

        if (LooksLikeYouTubeUrl(url) && denoPath is null)
        {
            AppendLog("warning: Deno was not found. Modern YouTube downloads may be incomplete or fail.");
        }
        else if (denoPath is not null)
        {
            AppendLog($"deno: {denoPath}");
        }

        _lastActivityErrorSummary = null;
        SetUiBusy(AppOperation.Download);
        UpdateStatus("Starting download...");
        AddActivityEntry(ActivityFeedIconKind.Download, $"Download started: {FormatActivityUrl(url)}");

        _downloadCts = new CancellationTokenSource();

        try
        {
            var startInfo = BuildStartInfo(
                ytDlpPath,
                ffmpegPath,
                denoPath,
                url,
                outputFolder,
                extractAudio,
                primaryAudioFormat);

            using var process = new Process { StartInfo = startInfo };
            _activeProcess = process;

            process.Start();

            var outputTask = ReadLinesAsync(process.StandardOutput, HandleOutputLine);
            var errorTask = ReadLinesAsync(process.StandardError, AppendLog);

            using var registration = _downloadCts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort cleanup during cancellation.
                }
            });

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);

            if (_downloadCts.IsCancellationRequested)
            {
                UpdateStatus("Canceled");
                AppendLog("Download canceled.");
                AddActivityEntry(ActivityFeedIconKind.Neutral, "Download canceled.");
            }
            else if (process.ExitCode == 0)
            {
                SetProgressValue(100);
                var completedMediaPath = ResolveCompletedMediaPath();
                if (!string.IsNullOrWhiteSpace(completedMediaPath))
                {
                    completedMediaPath = NormalizeMediaPath(completedMediaPath);

                    //== secondary audio extraction =============================
                    if (extractMp3 && extractWav && ffmpegPath is not null)
                    {
                        UpdateStatus("Creating MP3 copy...");
                        var mp3Path = BuildSiblingAudioOutputPath(completedMediaPath, "mp3");
                        var mp3ExitCode = await RunLoggedProcessAsync(
                            BuildConversionStartInfo(ffmpegPath, completedMediaPath, mp3Path, "mp3", default));
                        if (mp3ExitCode != 0)
                        {
                            throw new InvalidOperationException($"WAV downloaded, but MP3 creation failed (ffmpeg exit code {mp3ExitCode}).");
                        }

                        AppendLog($"Additional MP3 created: {mp3Path}");
                    }
                    //=================================================================

                    _lastDownloadedFilePath = completedMediaPath;
                    if (!_downloadAutoPreviewTriggered ||
                        !string.Equals(_downloadAutoPreviewLoadedPath, completedMediaPath, StringComparison.OrdinalIgnoreCase))
                    {
                        await TryLoadDownloadedMediaPreviewAsync(completedMediaPath);
                    }
                }
                else
                {
                    AppendLog("Download completed, but the final media file could not be detected automatically.");
                }

                UpdateStatus("Completed");
                AppendLog("Download completed successfully.");
                AddActivityEntry(
                    ActivityFeedIconKind.Success,
                    !string.IsNullOrWhiteSpace(completedMediaPath)
                        ? $"Download complete \u2192 {BuildActivityFileSummary(completedMediaPath)}"
                        : "Download complete.",
                    countsAsDownload: true);
            }
            else
            {
                UpdateStatus($"Failed (exit code {process.ExitCode})");
                AppendLog($"yt-dlp exited with code {process.ExitCode}.");
                AddActivityEntry(
                    ActivityFeedIconKind.Error,
                    BuildActivityFailureMessage("Download failed", $"Download failed (exit code {process.ExitCode})."),
                    countsAsError: true);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Error");
            AppendLog(ex.Message);
            AddActivityEntry(
                ActivityFeedIconKind.Error,
                BuildActivityFailureMessage("Download failed", $"Download failed: {ex.Message}"),
                countsAsError: true);
            MessageBox.Show(this, ex.Message, "Download Error");
        }
        finally
        {
            _activeProcess = null;
            _downloadCts.Dispose();
            _downloadCts = null;
            SetUiBusy(AppOperation.None);
        }
    }

    private async Task StartConversionAsync(string formatLabel, string targetExtension, bool requiresVideo)
    {
        //== input validation ==================================================
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        var sourcePath = GetPreferredMediaPath();
        if (sourcePath is null)
        {
            MessageBox.Show(
                this,
                "Open a media file or download one first, then choose a conversion button.",
                "No Media Selected");
            return;
        }

        if (requiresVideo && IsAudioOnlyMedia(sourcePath))
        {
            MessageBox.Show(
                this,
                "Video conversion needs a file that contains video. Open a video file first.",
                "Video Required");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var ffmpegSearchPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));

        if (ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "ffmpeg.exe was not found.\r\n\r\n" +
                "Place it in one of these locations and rebuild if needed:\r\n" +
                string.Join("\r\n", ffmpegSearchPaths.Take(3)) +
                "\r\n\r\nOr install it on PATH.",
                "ffmpeg Missing");
            return;
        }
        //=========================================================================

        //== output setup =======================================================
        var outputPath = BuildConvertedOutputPath(sourcePath, targetExtension);
        var requestedCompressionOptions = requiresVideo
            ? GetSelectedVideoCompressionOptions()
            : default;
        var selectedVideoEncoder = requiresVideo
            ? ResolveVideoEncoderSelection(ffmpegPath, requestedCompressionOptions.UseHardwareAcceleration)
            : null;
        var selectedCompressionOptions = requiresVideo && selectedVideoEncoder is not null
            ? GetEffectiveVideoCompressionOptions(requestedCompressionOptions, selectedVideoEncoder)
            : requestedCompressionOptions;

        txtLog.AppendText(Environment.NewLine);
        AppendLog($"ffmpeg: {ffmpegPath}");
        AppendLog($"Converting to {formatLabel}: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)}");
        if (requiresVideo)
        {
            var selectedVideoQuality = GetSelectedVideoQualityPreset();
            AppendLog($"Compression profile: {DescribeVideoQualityPreset(selectedVideoQuality)}");
            AppendLog($"Video codec: {selectedVideoEncoder!.DisplayLabel}");
            AppendLog($"Compression options: {DescribeVideoCompressionOptions(selectedCompressionOptions)}");
        }
        //=========================================================================

        SetUiBusy(AppOperation.Convert);
        UpdateStatus($"Converting to {formatLabel}...");
        AddActivityEntry(ActivityFeedIconKind.Export, $"Convert started: {Path.GetFileName(sourcePath)} -> {formatLabel}");
        _lastActivityErrorSummary = null;

        try
        {
            //== external process =================================================
            var exitCode = requiresVideo && selectedVideoEncoder is not null
                ? await RunVideoConversionAsync(
                    ffmpegPath,
                    sourcePath,
                    outputPath,
                    targetExtension,
                    formatLabel,
                    selectedCompressionOptions,
                    selectedVideoEncoder)
                : await RunLoggedProcessAsync(
                    BuildConversionStartInfo(
                        ffmpegPath,
                        sourcePath,
                        outputPath,
                        targetExtension,
                        requestedCompressionOptions));

            if (exitCode != 0 &&
                requiresVideo &&
                selectedVideoEncoder is not null &&
                selectedVideoEncoder.UsesHardwareAcceleration)
            {
                //== hardware fallback ==========================================
                var softwareVideoEncoder = ResolveVideoEncoderSelection(ffmpegPath, preferHardwareAcceleration: false);
                if (!string.Equals(softwareVideoEncoder.EncoderName, selectedVideoEncoder.EncoderName, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"Hardware encode failed with {selectedVideoEncoder.DisplayLabel}. Retrying with {softwareVideoEncoder.DisplayLabel}.");
                    selectedCompressionOptions = GetEffectiveVideoCompressionOptions(requestedCompressionOptions, softwareVideoEncoder);
                    AppendLog($"Retry compression options: {DescribeVideoCompressionOptions(selectedCompressionOptions)}");
                    UpdateStatus($"Retrying {formatLabel} with software encode...");
                    exitCode = await RunVideoConversionAsync(
                        ffmpegPath,
                        sourcePath,
                        outputPath,
                        targetExtension,
                        formatLabel,
                        selectedCompressionOptions,
                        softwareVideoEncoder);
                }
            }
            //=========================================================================

            //== output handling ===================================================
            if (exitCode == 0)
            {
                SetProgressValue(100);
                _lastDownloadedFilePath = outputPath;
                SetCurrentMediaSource(outputPath);

                if (await EnsurePreviewReadyAsync())
                {
                    await LoadPreviewAsync(outputPath, switchToPreview: true);
                }
                else
                {
                    FocusPreviewStage();
                }

                UpdateStatus($"Converted to {formatLabel}");
                AppendLog($"Conversion completed: {outputPath}");
                AppendLog(BuildConversionSizeSummary(sourcePath, outputPath));
                AddActivityEntry(
                    ActivityFeedIconKind.Success,
                    $"Convert complete \u2192 {BuildActivityFileSummary(outputPath)}",
                    countsAsExport: true);
            }
            else
            {
                UpdateStatus($"Conversion failed (exit code {exitCode})");
                AppendLog($"ffmpeg exited with code {exitCode}.");
                AddActivityEntry(
                    ActivityFeedIconKind.Error,
                    BuildActivityFailureMessage("Convert failed", $"{formatLabel} export failed (exit code {exitCode})."),
                    countsAsError: true);
            }
            //=========================================================================
        }
        catch (Exception ex)
        {
            //== error handling =====================================================
            UpdateStatus("Conversion error");
            AppendLog(ex.Message);
            AddActivityEntry(
                ActivityFeedIconKind.Error,
                BuildActivityFailureMessage("Convert failed", $"Convert failed: {ex.Message}"),
                countsAsError: true);
            MessageBox.Show(this, ex.Message, "Conversion Error");
            //=========================================================================
        }
        finally
        {
            //== cleanup ==========================================================
            _activeProcess = null;
            SetUiBusy(AppOperation.None);
            //=========================================================================
        }
    }

    private VideoCompressionOptions GetSelectedVideoCompressionOptions()
    {
        return new VideoCompressionOptions(
            _compressionStripMetadataCheckBox?.Checked ?? false,
            _compressionTwoPassCheckBox?.Checked ?? false,
            _compressionHardwareAccelerationCheckBox?.Checked ?? false);
    }

    private static VideoCompressionOptions GetEffectiveVideoCompressionOptions(
        VideoCompressionOptions requestedCompressionOptions,
        VideoEncoderSelection selectedVideoEncoder)
    {
        //== normalization =====================================================
        return new VideoCompressionOptions(
            requestedCompressionOptions.StripMetadata,
            requestedCompressionOptions.UseTwoPassEncode && !selectedVideoEncoder.UsesHardwareAcceleration,
            requestedCompressionOptions.UseHardwareAcceleration && selectedVideoEncoder.UsesHardwareAcceleration);
        //======================================================================
    }

    private static string DescribeVideoCompressionOptions(VideoCompressionOptions compressionOptions)
    {
        //== output shaping ======================================================
        var options = new List<string>();

        if (compressionOptions.StripMetadata)
        {
            options.Add("strip metadata");
        }

        if (compressionOptions.UseTwoPassEncode)
        {
            options.Add("2-pass encode");
        }

        if (compressionOptions.UseHardwareAcceleration)
        {
            options.Add("hardware accel.");
        }

        return options.Count == 0
            ? "default video encode"
            : string.Join(", ", options);
        //=========================================================================
    }

    private VideoEncoderSelection ResolveVideoEncoderSelection(string ffmpegPath, bool preferHardwareAcceleration)
    {
        //== encoder selection =================================================
        if (!preferHardwareAcceleration)
        {
            return BuildSoftwareVideoEncoderSelection();
        }

        var availableEncoders = GetAvailableFfmpegEncoders(ffmpegPath);
        var hardwareEncoderName = ResolveHardwareVideoEncoderName(_selectedVideoCodec, availableEncoders);

        if (hardwareEncoderName is null)
        {
            return BuildSoftwareVideoEncoderSelection();
        }

        return new VideoEncoderSelection(
            hardwareEncoderName,
            $"{GetSelectedVideoCodecLabel()} ({hardwareEncoderName})",
            UsesHardwareAcceleration: true);
        //======================================================================
    }

    private VideoEncoderSelection BuildSoftwareVideoEncoderSelection()
    {
        //== encoder selection =================================================
        return _selectedVideoCodec == "h265"
            ? new VideoEncoderSelection("libx265", "H265 (libx265)", UsesHardwareAcceleration: false)
            : new VideoEncoderSelection("libx264", "H264 (libx264)", UsesHardwareAcceleration: false);
        //======================================================================
    }

    private HashSet<string> GetAvailableFfmpegEncoders(string ffmpegPath)
    {
        //== external process ==================================================
        if (_ffmpegEncoderCache.TryGetValue(ffmpegPath, out var cachedEncoders))
        {
            return cachedEncoders;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-encoders");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var encoderNames = ParseEncoderNames(output);
        foreach (var encoderName in ParseEncoderNames(error))
        {
            encoderNames.Add(encoderName);
        }

        _ffmpegEncoderCache[ffmpegPath] = encoderNames;
        return encoderNames;
        //======================================================================
    }

    private static HashSet<string> ParseEncoderNames(string encoderOutput)
    {
        //== normalization =====================================================
        var encoderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in encoderOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.TrimStart();
            if (trimmedLine.Length < 8 ||
                trimmedLine.StartsWith("Encoders:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var lineParts = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (lineParts.Length < 2 || lineParts[0].Length != 6)
            {
                continue;
            }

            var candidate = lineParts[1];
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                encoderNames.Add(candidate);
            }
        }

        return encoderNames;
        //======================================================================
    }

    private static string? ResolveHardwareVideoEncoderName(string selectedVideoCodec, HashSet<string> availableEncoders)
    {
        //== encoder selection =================================================
        var encoderCandidates = selectedVideoCodec == "h265"
            ? new[] { "hevc_nvenc", "hevc_qsv", "hevc_amf" }
            : new[] { "h264_nvenc", "h264_qsv", "h264_amf" };

        foreach (var encoderCandidate in encoderCandidates)
        {
            if (availableEncoders.Contains(encoderCandidate))
            {
                return encoderCandidate;
            }
        }

        return null;
        //======================================================================
    }

    private async Task<int> RunVideoConversionAsync(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        string targetExtension,
        string formatLabel,
        VideoCompressionOptions selectedCompressionOptions,
        VideoEncoderSelection selectedVideoEncoder)
    {
        //== external process ==================================================
        string? passLogFilePrefix = null;

        try
        {
            if (selectedCompressionOptions.UseTwoPassEncode)
            {
                passLogFilePrefix = BuildPassLogFilePrefix(outputPath);
                UpdateStatus($"Analyzing {formatLabel} (pass 1 of 2)...");
                AppendLog("Running pass 1 of 2...");

                var firstPassStartInfo = BuildConversionStartInfo(
                    ffmpegPath,
                    sourcePath,
                    "NUL",
                    targetExtension,
                    selectedCompressionOptions,
                    selectedVideoEncoder,
                    isFirstPass: true,
                    passLogFilePrefix);
                var firstPassExitCode = await RunLoggedProcessAsync(firstPassStartInfo);

                if (firstPassExitCode != 0)
                {
                    return firstPassExitCode;
                }

                SetProgressValue(50);
                UpdateStatus($"Encoding {formatLabel} (pass 2 of 2)...");
                AppendLog("Running pass 2 of 2...");

                var secondPassStartInfo = BuildConversionStartInfo(
                    ffmpegPath,
                    sourcePath,
                    outputPath,
                    targetExtension,
                    selectedCompressionOptions,
                    selectedVideoEncoder,
                    isFirstPass: false,
                    passLogFilePrefix);
                return await RunLoggedProcessAsync(secondPassStartInfo);
            }

            var startInfo = BuildConversionStartInfo(
                ffmpegPath,
                sourcePath,
                outputPath,
                targetExtension,
                selectedCompressionOptions,
                selectedVideoEncoder);
            return await RunLoggedProcessAsync(startInfo);
        }
        finally
        {
            //== cleanup ========================================================
            if (!string.IsNullOrWhiteSpace(passLogFilePrefix))
            {
                DeletePassLogFiles(passLogFilePrefix);
            }
            //==================================================================
        }
        //======================================================================
    }

    private static string BuildPassLogFilePrefix(string outputPath)
    {
        //== output shaping ======================================================
        var directory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, $"{fileName}_passlog");
        //=========================================================================
    }

    private static void DeletePassLogFiles(string passLogFilePrefix)
    {
        //== cleanup ============================================================
        var directory = Path.GetDirectoryName(passLogFilePrefix) ?? Environment.CurrentDirectory;
        var prefix = Path.GetFileName(passLogFilePrefix);

        foreach (var candidateFile in Directory.EnumerateFiles(directory, $"{prefix}*"))
        {
            try
            {
                File.Delete(candidateFile);
            }
            catch
            {
                // Best effort cleanup for temporary pass-log files.
            }
        }
        //=========================================================================
    }

    private async Task<int> RunLoggedProcessAsync(ProcessStartInfo startInfo)
    {
        //== external process ===================================================
        using var process = new Process { StartInfo = startInfo };
        _activeProcess = process;

        process.Start();

        var outputTask = ReadLinesAsync(process.StandardOutput, AppendLog);
        var errorTask = ReadLinesAsync(process.StandardError, AppendLog);

        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
        //=========================================================================
    }

    private async Task StartTrimExportAsync()
    {
        //== input validation ===================================================
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        var sourcePath = GetPreferredMediaPath();
        if (sourcePath is null || !HasTrimCapableVideo())
        {
            MessageBox.Show(this, "Open a video file before exporting a trimmed clip.", "Video Required");
            return;
        }

        var trimDuration = _trimSelectionEnd - _trimSelectionStart;
        if (trimDuration <= TimeSpan.FromMilliseconds(100))
        {
            MessageBox.Show(this, "Choose a longer trim range before exporting.", "Range Too Small");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var ffmpegSearchPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));

        if (ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "ffmpeg.exe was not found.\r\n\r\n" +
                "Place it in one of these locations and rebuild if needed:\r\n" +
                string.Join("\r\n", ffmpegSearchPaths.Take(3)) +
                "\r\n\r\nOr install it on PATH.",
                "ffmpeg Missing");
            return;
        }
        //=========================================================================

        //== output setup =======================================================
        var targetExtension = GetTrimOutputExtension(sourcePath);
        var outputPath = BuildTrimmedOutputPath(sourcePath, targetExtension);
        txtLog.AppendText(Environment.NewLine);
        AppendLog($"ffmpeg: {ffmpegPath}");
        AppendLog($"Trim export: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)}");
        AppendLog($"Trim range: {FormatTrimTimeCode(_trimSelectionStart)} -> {FormatTrimTimeCode(_trimSelectionEnd)} ({FormatTrimTimeCode(trimDuration)})");
        //=========================================================================

        SetUiBusy(AppOperation.Trim);
        UpdateStatus("Exporting trimmed clip...");
        AddActivityEntry(
            ActivityFeedIconKind.Export,
            $"Trim started: {FormatTrimTimeCode(_trimSelectionStart)} \u2192 {FormatTrimTimeCode(_trimSelectionEnd)}");
        _lastActivityErrorSummary = null;

        try
        {
            //== external process ===============================================
            var startInfo = BuildTrimExportStartInfo(ffmpegPath, sourcePath, outputPath, _trimSelectionStart, trimDuration, targetExtension);
            AddActivityEntry(
                ActivityFeedIconKind.Export,
                BuildActivityCommandSnippet(
                    "ffmpeg",
                    "-ss",
                    _trimSelectionStart.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "-t",
                    trimDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "-c:v",
                    "libx264",
                    "-c:a",
                    "aac"));
            var exitCode = await RunLoggedProcessAsync(startInfo);
            //=========================================================================

            //== output handling =================================================
            if (exitCode == 0)
            {
                SetProgressValue(100);
                _lastDownloadedFilePath = outputPath;
                SetCurrentMediaSource(outputPath);

                if (await EnsurePreviewReadyAsync())
                {
                    await LoadPreviewAsync(outputPath, switchToPreview: true);
                }
                else
                {
                    FocusPreviewStage();
                }

                ShowWorkspacePage(WorkspacePage.Trim);
                UpdateStatus("Trim export completed");
                AppendLog($"Trimmed clip exported: {outputPath}");
                AppendLog(BuildConversionSizeSummary(sourcePath, outputPath));
                AddActivityEntry(
                    ActivityFeedIconKind.Success,
                    $"Trim complete \u2192 {BuildActivityFileSummary(outputPath)}",
                    countsAsExport: true);
            }
            else
            {
                UpdateStatus($"Trim export failed (exit code {exitCode})");
                AppendLog($"ffmpeg exited with code {exitCode}.");
                AddActivityEntry(
                    ActivityFeedIconKind.Error,
                    BuildActivityFailureMessage("Trim failed", $"Trim export failed (exit code {exitCode})."),
                    countsAsError: true);
            }
            //=========================================================================
        }
        catch (Exception ex)
        {
            //== error handling ===================================================
            UpdateStatus("Trim export error");
            AppendLog(ex.Message);
            AddActivityEntry(
                ActivityFeedIconKind.Error,
                BuildActivityFailureMessage("Trim failed", $"Trim export failed: {ex.Message}"),
                countsAsError: true);
            MessageBox.Show(this, ex.Message, "Trim Export Error");
            //=========================================================================
        }
        finally
        {
            //== cleanup ==========================================================
            _activeProcess = null;
            SetUiBusy(AppOperation.None);
            //=========================================================================
        }
    }

    private ProcessStartInfo BuildTrimExportStartInfo(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        TimeSpan trimStart,
        TimeSpan trimDuration,
        string targetExtension)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(trimStart.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(trimDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0?");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("medium");
        startInfo.ArgumentList.Add("-crf");
        startInfo.ArgumentList.Add("18");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add("192k");

        if (targetExtension is "mp4" or "mov")
        {
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");
        }

        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private ProcessStartInfo BuildStartInfo(
        string ytDlpPath,
        string? ffmpegPath,
        string? denoPath,
        string url,
        string outputFolder,
        bool extractAudio,
        string audioFormat)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            WorkingDirectory = outputFolder,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("--ignore-config");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("--progress-template");
        startInfo.ArgumentList.Add("download:%(progress._percent_str)s|%(progress._eta_str)s|%(progress._speed_str)s");
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("after_move:FILE:%(filepath)s");
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(outputFolder);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("%(title)s.%(ext)s");

        if (ffmpegPath is not null)
        {
            startInfo.ArgumentList.Add("--ffmpeg-location");
            startInfo.ArgumentList.Add(Path.GetDirectoryName(ffmpegPath)!);
        }

        if (extractAudio)
        {
            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add("--audio-format");
            startInfo.ArgumentList.Add(audioFormat);
        }
        else
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("bv*+ba/b");
            startInfo.ArgumentList.Add("--merge-output-format");
            startInfo.ArgumentList.Add("mp4");
        }

        startInfo.ArgumentList.Add(url);

        var pathEntries = new List<string>();
        if (denoPath is not null)
        {
            pathEntries.Add(Path.GetDirectoryName(denoPath)!);
        }

        if (ffmpegPath is not null)
        {
            pathEntries.Add(Path.GetDirectoryName(ffmpegPath)!);
        }

        if (pathEntries.Count > 0)
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, pathEntries) +
                                            Path.PathSeparator +
                                            existingPath;
        }

        return startInfo;
    }

    private ProcessStartInfo BuildConversionStartInfo(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        string targetExtension,
        VideoCompressionOptions selectedCompressionOptions,
        VideoEncoderSelection? encoderSelection = null,
        bool isFirstPass = false,
        string? passLogFilePrefix = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");

        if (IsVideoConversionTarget(targetExtension) && selectedCompressionOptions.UseHardwareAcceleration)
        {
            startInfo.ArgumentList.Add("-hwaccel");
            startInfo.ArgumentList.Add("auto");
        }

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);

        foreach (var argument in GetConversionArguments(
                     targetExtension,
                     selectedCompressionOptions,
                     encoderSelection,
                     isFirstPass,
                     passLogFilePrefix))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private IReadOnlyList<string> GetConversionArguments(
        string targetExtension,
        VideoCompressionOptions selectedCompressionOptions,
        VideoEncoderSelection? encoderSelection = null,
        bool isFirstPass = false,
        string? passLogFilePrefix = null)
    {
        //== output format selection ============================================
        return targetExtension.ToLowerInvariant() switch
        {
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "160k"],
            "mp4" => BuildVideoConversionArguments(includeFastStart: true, selectedCompressionOptions, encoderSelection, isFirstPass, passLogFilePrefix),
            "mkv" => BuildVideoConversionArguments(includeFastStart: false, selectedCompressionOptions, encoderSelection, isFirstPass, passLogFilePrefix),
            "mov" => BuildVideoConversionArguments(includeFastStart: true, selectedCompressionOptions, encoderSelection, isFirstPass, passLogFilePrefix),
            _ => throw new InvalidOperationException($"Unsupported conversion target: {targetExtension}")
        };
        //=========================================================================
    }

    private IReadOnlyList<string> BuildVideoConversionArguments(
        bool includeFastStart,
        VideoCompressionOptions selectedCompressionOptions,
        VideoEncoderSelection? encoderSelection,
        bool isFirstPass,
        string? passLogFilePrefix)
    {
        //== compression profile =================================================
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        var resolvedEncoderSelection = encoderSelection ?? BuildSoftwareVideoEncoderSelection();
        var arguments = new List<string>
        {
            "-map", "0:v:0",
            "-c:v", resolvedEncoderSelection.EncoderName
        };

        AddVideoEncoderArguments(arguments, resolvedEncoderSelection, selectedVideoQuality);

        if (selectedCompressionOptions.StripMetadata)
        {
            arguments.Add("-map_metadata");
            arguments.Add("-1");
        }

        if (selectedCompressionOptions.UseTwoPassEncode)
        {
            arguments.Add("-pass");
            arguments.Add(isFirstPass ? "1" : "2");

            if (!string.IsNullOrWhiteSpace(passLogFilePrefix))
            {
                arguments.Add("-passlogfile");
                arguments.Add(passLogFilePrefix);
            }
        }

        if (isFirstPass)
        {
            arguments.Add("-an");
            arguments.Add("-f");
            arguments.Add("null");
        }
        else
        {
            arguments.Add("-map");
            arguments.Add("0:a:0?");
            arguments.Add("-c:a");
            arguments.Add("aac");
            arguments.Add("-b:a");
            arguments.Add(selectedVideoQuality.AudioBitrate);
        }
        //=========================================================================

        //== optional downscaling ================================================
        var scaleFilter = BuildScaleFilter(selectedVideoQuality);
        if (scaleFilter is not null)
        {
            arguments.Add("-vf");
            arguments.Add(scaleFilter);
        }
        //=========================================================================

        //== container-specific flags ============================================
        if (includeFastStart && !isFirstPass)
        {
            if (_selectedVideoCodec == "h265")
            {
                arguments.Add("-tag:v");
                arguments.Add("hvc1");
            }

            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }
        //=========================================================================

        return arguments;
    }

    private void AddVideoEncoderArguments(
        ICollection<string> arguments,
        VideoEncoderSelection selectedVideoEncoder,
        VideoQualityPreset selectedVideoQuality)
    {
        //== compression profile ===============================================
        if (!selectedVideoEncoder.UsesHardwareAcceleration)
        {
            arguments.Add("-preset");
            arguments.Add(selectedVideoQuality.EncoderPreset);
            arguments.Add("-crf");
            arguments.Add(selectedVideoQuality.Crf.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (selectedVideoEncoder.EncoderName.Contains("_nvenc", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-preset");
            arguments.Add("p5");
            arguments.Add("-cq");
            arguments.Add(MapCrfToHardwareQuality(selectedVideoQuality.Crf).ToString(CultureInfo.InvariantCulture));
            arguments.Add("-b:v");
            arguments.Add("0");
            return;
        }

        if (selectedVideoEncoder.EncoderName.Contains("_qsv", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-preset");
            arguments.Add("medium");
            arguments.Add("-global_quality");
            arguments.Add(MapCrfToHardwareQuality(selectedVideoQuality.Crf).ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (selectedVideoEncoder.EncoderName.Contains("_amf", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-quality");
            arguments.Add("balanced");
            arguments.Add("-qp_i");
            arguments.Add(MapCrfToHardwareQuality(selectedVideoQuality.Crf).ToString(CultureInfo.InvariantCulture));
            arguments.Add("-qp_p");
            arguments.Add(MapCrfToHardwareQuality(selectedVideoQuality.Crf).ToString(CultureInfo.InvariantCulture));
            return;
        }

        arguments.Add("-preset");
        arguments.Add(selectedVideoQuality.EncoderPreset);
        arguments.Add("-crf");
        arguments.Add(selectedVideoQuality.Crf.ToString(CultureInfo.InvariantCulture));
        //======================================================================
    }

    private static int MapCrfToHardwareQuality(int crf)
    {
        //== normalization =====================================================
        return Math.Clamp(crf - 4, 18, 30);
        //======================================================================
    }

    private static bool IsVideoConversionTarget(string targetExtension)
    {
        return targetExtension.Equals("mp4", StringComparison.OrdinalIgnoreCase) ||
               targetExtension.Equals("mkv", StringComparison.OrdinalIgnoreCase) ||
               targetExtension.Equals("mov", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildScaleFilter(VideoQualityPreset selectedVideoQuality)
    {
        //== optional downscaling ================================================
        if (!selectedVideoQuality.MaxHeight.HasValue)
        {
            return null;
        }

        return $"scale=-2:'min({selectedVideoQuality.MaxHeight.Value},ih)'";
        //=========================================================================
    }

    private static string BuildSiblingAudioOutputPath(string sourcePath, string targetExtension)
    {
        //== output shaping =====================================================
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = targetExtension.StartsWith(".", StringComparison.Ordinal)
            ? targetExtension
            : $".{targetExtension}";
        var candidatePath = Path.Combine(directory, $"{baseName}{extension}");
        var counter = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{baseName}_{counter}{extension}");
            counter++;
        }

        return candidatePath;
        //=======================================================================
    }

    private static string BuildConvertedOutputPath(string sourcePath, string targetExtension)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = targetExtension.StartsWith(".", StringComparison.Ordinal)
            ? targetExtension
            : $".{targetExtension}";

        var candidatePath = Path.Combine(directory, $"{baseName}_converted{extension}");
        var counter = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{baseName}_converted_{counter}{extension}");
            counter++;
        }

        return candidatePath;
    }

    private static async Task AwaitRedirectedProcessAsync(
        Process process,
        Task outputTask,
        Task errorTask,
        CancellationToken cancellationToken)
    {
        //== process lifecycle ===============================================
        var exitTask = process.WaitForExitAsync();
        if (cancellationToken.CanBeCanceled && !exitTask.IsCompleted)
        {
            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () => cancellationSignal.TrySetResult(true));
            if (await Task.WhenAny(exitTask, cancellationSignal.Task) == cancellationSignal.Task)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort process-tree cleanup during cancellation.
                }

                if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(10))) != exitTask)
                {
                    throw new TimeoutException("The external process did not exit after cancellation.");
                }
            }
        }

        await exitTask;
        var readTask = Task.WhenAll(outputTask, errorTask);
        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(10))) != readTask)
        {
            throw new TimeoutException("The external process output streams did not close after exit.");
        }
        await readTask;
        //=====================================================================
    }

    private async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            onLine(line);
        }
    }

    private void HandleOutputLine(string line)
    {
        if (line.StartsWith("download:", StringComparison.Ordinal))
        {
            var status = line["download:".Length..];
            var parts = status.Split('|', StringSplitOptions.None);
            var percentValue = TryParsePercent(parts.FirstOrDefault());
            var eta = parts.Length > 1 ? parts[1].Trim() : "n/a";
            var speed = parts.Length > 2 ? parts[2].Trim() : "n/a";

            RunOnUiThread(() =>
            {
                if (percentValue.HasValue)
                {
                    SetProgressValue(percentValue.Value);
                }

                lblStatus.Text = $"Downloading {parts.FirstOrDefault()?.Trim()} | ETA {eta} | Speed {speed}";
            });

            return;
        }

        if (line.StartsWith("FILE:", StringComparison.Ordinal))
        {
            var savedPath = NormalizeMediaPath(line["FILE:".Length..]);
            _lastDownloadedFilePath = savedPath;
            var shouldAutoPreview = !_downloadAutoPreviewTriggered;
            if (shouldAutoPreview)
            {
                _downloadAutoPreviewTriggered = true;
            }

            RunOnUiThread(() =>
            {
                RefreshPreviewSummary();
                UpdatePreviewButtons();

                if (shouldAutoPreview)
                {
                    _ = TryLoadDownloadedMediaPreviewAsync(savedPath);
                }
            });
            AppendLog($"Saved to: {savedPath}");
            return;
        }

        AppendLog(line);
    }

    private void AppendLog(string message)
    {
        CaptureActivityFromLog(message);
        RunOnUiThread(() =>
        {
            txtLog.AppendText($"{message}{Environment.NewLine}");
        });
    }

    private void UpdateStatus(string message)
    {
        RunOnUiThread(() => lblStatus.Text = message);
    }

    private void SetProgressValue(int percent)
    {
        RunOnUiThread(() =>
        {
            if (progressDownload.Style != ProgressBarStyle.Continuous)
            {
                progressDownload.Style = ProgressBarStyle.Continuous;
                progressDownload.MarqueeAnimationSpeed = 0;
            }

            progressDownload.Value = Math.Clamp(percent, progressDownload.Minimum, progressDownload.Maximum);
        });
    }

    private void SetUiBusy(AppOperation operation)
    {
        RunOnUiThread(() =>
        {
            _activeOperation = operation;
            var isBusy = operation != AppOperation.None;
            txtUrl.Enabled = !isBusy;
            txtOutputFolder.Enabled = !isBusy;
            btnBrowseOutput.Enabled = !isBusy;
            chkExtractAudio.Enabled = !isBusy;
            chkExtractWavAudio.Enabled = !isBusy;
            btnDownload.Enabled = operation is AppOperation.None or AppOperation.Download;
            btnDownload.Text = operation == AppOperation.Download ? "Cancel Download" : "Download";
            progressDownload.Visible = isBusy;

            if (isBusy)
            {
                progressDownload.Style = ProgressBarStyle.Marquee;
                progressDownload.MarqueeAnimationSpeed = 30;
                progressDownload.Value = progressDownload.Minimum;
            }
            else
            {
                progressDownload.MarqueeAnimationSpeed = 0;
                progressDownload.Style = ProgressBarStyle.Continuous;
                progressDownload.Value = progressDownload.Minimum;
            }

            UpdateVideoQualityUi();
            UpdatePreviewButtons();
            UpdateBackgroundControls();
        });
    }

    private void trkVideoQuality_ValueChanged(object sender, EventArgs e)
    {
        UpdateVideoQualityUi();
    }

    private async void btnPreviewLast_Click(object sender, EventArgs e)
    {
        var mediaPath = GetPreferredMediaPath();
        if (mediaPath is null)
        {
            MessageBox.Show(this, "No downloaded media file is available yet.", "Preview");
            return;
        }

        await LoadPreviewAsync(mediaPath, switchToPreview: true);
    }

    private async void btnOpenMediaFile_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open Media File",
            Filter = "Media files|*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.flv;*.wmv;*.webm;*.mp3;*.m4a;*.wav;*.aac;*.ogg;*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await OpenMediaPathAsync(dialog.FileName);
    }

    private void btnOpenExternal_Click(object sender, EventArgs e)
    {
        //== output folder resolution =========================================
        var mediaPath = GetPreferredMediaPath();
        var folderPath = mediaPath is not null
            ? Path.GetDirectoryName(mediaPath)
            : txtOutputFolder.Text.Trim();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ShowEmptyPreviewState("Choose an output folder or open a media file first.", isError: true);
            return;
        }
        //=====================================================================

        //== external service call ============================================
        try
        {
            var arguments = mediaPath is not null && File.Exists(mediaPath)
                ? $"/select,\"{mediaPath}\""
                : $"\"{folderPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"Open folder failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Open Folder Error");
        }
        //=====================================================================
    }

    private async Task<bool> TryLoadDownloadedMediaPreviewAsync(string mediaPath)
    {
        var normalizedPath = NormalizeMediaPath(mediaPath);
        normalizedPath = await ResolveAutoPreviewMediaPathAsync(normalizedPath) ?? normalizedPath;

        if (!File.Exists(normalizedPath))
        {
            AppendLog($"Auto-preview skipped because the media file is not available yet: {normalizedPath}");
            return false;
        }

        await LoadPreviewAsync(normalizedPath, switchToPreview: true);

        var previewLoaded = !string.IsNullOrWhiteSpace(_currentPreviewFilePath) &&
                            string.Equals(_currentPreviewFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(normalizedPath);

        if (previewLoaded)
        {
            _downloadAutoPreviewLoadedPath = normalizedPath;
            AppendLog("Opened downloaded file in Preview.");
        }

        return previewLoaded;
    }

    private async Task<string?> ResolveAutoPreviewMediaPathAsync(string preferredPath)
    {
        var normalizedPath = NormalizeMediaPath(preferredPath);
        if (File.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        const int retryCount = 6;
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            await Task.Delay(250);
            if (File.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        var fallbackPath = ResolveCompletedMediaPath();
        if (!string.IsNullOrWhiteSpace(fallbackPath))
        {
            fallbackPath = NormalizeMediaPath(fallbackPath);
            if (File.Exists(fallbackPath))
            {
                return fallbackPath;
            }
        }

        return null;
    }

    private async Task<bool> EnsurePreviewReadyAsync()
    {
        if (_previewReady)
        {
            return true;
        }

        if (_previewInitializationAttempted)
        {
            return false;
        }

        _previewInitializationAttempted = true;

        try
        {
            await webPreview.EnsureCoreWebView2Async();

            _previewReady = true;
            webPreview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webPreview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webPreview.CoreWebView2.Settings.IsZoomControlEnabled = true;
            webPreview.CoreWebView2.WebMessageReceived -= webPreview_WebMessageReceived;
            webPreview.CoreWebView2.WebMessageReceived += webPreview_WebMessageReceived;
            ShowEmptyPreviewState();
            RefreshPreviewSummary();
            UpdatePreviewButtons();
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowPreviewState("WebView2 Runtime was not found.\r\n\r\nInstall the Microsoft Edge WebView2 Runtime to enable the in-app player.");
            UpdatePreviewButtons();
            return false;
        }
        catch (Exception ex)
        {
            AppendLog($"Preview initialization failed: {ex.Message}");
            ShowPreviewState($"Preview could not be initialized.\r\n\r\n{ex.Message}");
            UpdatePreviewButtons();
            return false;
        }
    }

    private async Task LoadPreviewAsync(string mediaFilePath, bool switchToPreview)
    {
        var fullPath = NormalizeMediaPath(mediaFilePath);
        if (!File.Exists(fullPath))
        {
            MessageBox.Show(this, $"The media file was not found:\r\n{fullPath}", "Preview");
            return;
        }

        if (!await EnsurePreviewReadyAsync())
        {
            return;
        }

        try
        {
            _previewDocumentReady = false;
            _trimSelectionPlaybackActive = false;
            SetCurrentMediaSource(fullPath);
            RefreshPreviewSummary();
            ShowPreviewState($"Loading preview for {Path.GetFileName(fullPath)}...", keepPreviewVisible: true);
            NavigatePreviewToMedia(fullPath);
            UpdatePreviewButtons();

            if (switchToPreview)
            {
                FocusPreviewStage();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Preview load failed: {ex.Message}");
            ShowPreviewState($"Preview could not load this file.\r\n\r\n{ex.Message}");
        }
    }

    private void SetCurrentMediaSource(string mediaFilePath)
    {
        var normalizedMediaPath = NormalizeMediaPath(mediaFilePath);
        HideEmptyPreviewState();
        ResetWatermarkAuthorizationForMedia(normalizedMediaPath);
        _currentPreviewFilePath = normalizedMediaPath;
        if (_currentWorkspacePage == WorkspacePage.Background && BackgroundRemovalService.IsSupportedPicture(normalizedMediaPath))
        {
            SelectBackgroundSource(normalizedMediaPath);
        }
        _currentMediaMetadata = BuildFallbackMediaMetadata(_currentPreviewFilePath);
        ResetTrimSelectionToFullDuration();
        ResetCropSelectionToSourceBounds();
        RefreshPreviewSummary();
        UpdatePreviewButtons();
        _ = RefreshCurrentMediaMetadataAsync(_currentPreviewFilePath);
    }

    private void webPreview_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _previewDocumentReady = true;
            HidePreviewState();
            _ = SyncTrimPreviewStateAsync();
            _ = SyncCropPreviewOverlayAsync();
            return;
        }

        _previewDocumentReady = false;
        AppendLog($"Preview navigation failed: {e.WebErrorStatus}");
        ShowPreviewState("This file could not be rendered in the embedded player.\r\n\r\nTry Open externally for formats the browser runtime does not support.");
        UpdatePreviewButtons();
    }

    private void webPreview_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        //== input collection ===================================================
        JsonDocument? document = null;

        try
        {
            document = JsonDocument.Parse(e.WebMessageAsJson);
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out var typeElement) ||
                !string.Equals(typeElement.GetString(), "cropSelectionChanged", StringComparison.Ordinal))
            {
                return;
            }

            ApplyCropOverlaySelection(payload);
        }
        catch (JsonException)
        {
            // Ignore malformed preview messages.
        }
        finally
        {
            document?.Dispose();
        }
        //======================================================================
    }

    private void ApplyCropOverlaySelection(JsonElement payload)
    {
        //== input validation ===================================================
        if (!HasCropSourceBounds() || _activeOperation != AppOperation.None)
        {
            return;
        }

        if (!TryReadRoundedIntProperty(payload, "x", out var cropX) ||
            !TryReadRoundedIntProperty(payload, "y", out var cropY) ||
            !TryReadRoundedIntProperty(payload, "width", out var cropWidth) ||
            !TryReadRoundedIntProperty(payload, "height", out var cropHeight))
        {
            return;
        }
        //======================================================================

        //== state transition ===================================================
        _cropX = cropX;
        _cropY = cropY;
        _cropWidth = cropWidth;
        _cropHeight = cropHeight;
        _selectedCropAspectPreset = CropAspectPreset.Custom;
        NormalizeCropSelection();
        UpdateCropUi();
        //======================================================================
    }

    private static bool TryReadRoundedIntProperty(JsonElement payload, string propertyName, out int value)
    {
        //== normalization ======================================================
        value = 0;
        if (!payload.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var numericValue))
        {
            value = (int)Math.Round(numericValue, MidpointRounding.AwayFromZero);
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            value = parsedValue;
            return true;
        }

        return false;
        //======================================================================
    }

    private void ShowPreviewState(string message, bool keepPreviewVisible = false)
    {
        HideEmptyPreviewState();
        lblPreviewState.Text = message;
        lblPreviewState.Visible = true;
        webPreview.Visible = keepPreviewVisible;
        lblPreviewState.BringToFront();
    }

    private void HidePreviewState()
    {
        HideEmptyPreviewState();
        lblPreviewState.Visible = false;
        webPreview.Visible = true;
        webPreview.BringToFront();
    }

    private void NavigatePreviewToMedia(string fullPath)
    {
        var mediaDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(mediaDirectory))
        {
            throw new InvalidOperationException("The media file does not have a valid parent directory.");
        }

        var fileName = Path.GetFileName(fullPath);
        var mediaUrl = $"https://{PreviewHostName}/{Uri.EscapeDataString(fileName)}";
        var isAudioOnly = IsAudioOnlyMedia(fullPath);
        var isStillImage = IsSupportedWatermarkImage(fullPath);
        _previewDocumentReady = false;

        webPreview.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PreviewHostName,
            mediaDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        webPreview.CoreWebView2.NavigateToString(
            BuildMediaPreviewHtml(mediaUrl, fileName, isAudioOnly, isStillImage));
    }

    private void RefreshPreviewSummary()
    {
        var mediaPath = _currentPreviewFilePath ?? _lastDownloadedFilePath;
        lblPreviewPath.Text = string.IsNullOrWhiteSpace(mediaPath)
            ? "Nothing loaded yet."
            : Path.GetFileName(mediaPath);

        if (_compressionPreviewFileLabel is not null)
        {
            _compressionPreviewFileLabel.Text = string.IsNullOrWhiteSpace(mediaPath)
                ? "No media loaded"
                : Path.GetFileName(mediaPath);
        }

        var fileInfoText = BuildFileInfoText(mediaPath);
        lblFileInfo.Text = fileInfoText;

        if (_previewMetaLabel is not null)
        {
            //== toolbar metadata visibility ========================================
            var hasLoadedMedia = !string.IsNullOrWhiteSpace(mediaPath);
            _previewMetaLabel.Text = hasLoadedMedia ? fileInfoText : string.Empty;
            _previewMetaLabel.Margin = hasLoadedMedia
                ? new Padding(0, 0, 12, 0)
                : Padding.Empty;
            //=========================================================================
        }

        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            ShowEmptyPreviewState();
        }

        UpdateTrimUi();
        UpdateVideoQualityUi();
    }

    private async Task RefreshCurrentMediaMetadataAsync(string mediaPath)
    {
        var normalizedPath = NormalizeMediaPath(mediaPath);
        var metadata = await TryReadMediaMetadataAsync(normalizedPath);

        RunOnUiThread(() =>
        {
            //== state transition ===============================================
            if (!string.Equals(_currentPreviewFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_lastDownloadedFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentMediaMetadata = metadata;
            ResetTrimSelectionToFullDuration();
            ResetCropSelectionToSourceBounds();
            RefreshPreviewSummary();
            UpdatePreviewButtons();
            //=====================================================================
        });
    }

    private async Task<MediaMetadata> TryReadMediaMetadataAsync(string mediaPath)
    {
        //== input validation ===================================================
        if (!File.Exists(mediaPath))
        {
            return BuildFallbackMediaMetadata(mediaPath);
        }
        //=========================================================================

        var ffprobePath = ResolveToolPath(
            "ffprobe.exe",
            out _,
            Path.Combine("tools", "ffprobe.exe"),
            Path.Combine("tools", "ffmpeg", "ffprobe.exe"));

        if (ffprobePath is null)
        {
            return BuildFallbackMediaMetadata(mediaPath);
        }

        try
        {
            //== external process ===============================================
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_format");
            startInfo.ArgumentList.Add("-show_streams");
            startInfo.ArgumentList.Add(mediaPath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            //=====================================================================

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return BuildFallbackMediaMetadata(mediaPath);
            }

            return ParseMediaMetadata(output, mediaPath);
        }
        catch
        {
            return BuildFallbackMediaMetadata(mediaPath);
        }
    }

    private static MediaMetadata ParseMediaMetadata(string ffprobeJson, string mediaPath)
    {
        using var document = JsonDocument.Parse(ffprobeJson);
        var root = document.RootElement;

        //== duration parsing ===================================================
        var duration = TimeSpan.Zero;
        if (root.TryGetProperty("format", out var formatElement) &&
            formatElement.TryGetProperty("duration", out var durationElement) &&
            durationElement.ValueKind == JsonValueKind.String &&
            double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds))
        {
            duration = TimeSpan.FromSeconds(Math.Max(durationSeconds, 0D));
        }
        //=========================================================================

        //== stream parsing =====================================================
        var width = default(int?);
        var height = default(int?);
        var isStillImage = IsSupportedWatermarkImage(mediaPath);
        var hasVideo = !IsAudioOnlyMedia(mediaPath) && !isStillImage;

        if (root.TryGetProperty("streams", out var streamsElement) &&
            streamsElement.ValueKind == JsonValueKind.Array)
        {
            hasVideo = false;
            foreach (var streamElement in streamsElement.EnumerateArray())
            {
                if (!streamElement.TryGetProperty("codec_type", out var codecTypeElement) ||
                    codecTypeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!string.Equals(codecTypeElement.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasVideo = !isStillImage;
                if (streamElement.TryGetProperty("width", out var widthElement) && widthElement.TryGetInt32(out var parsedWidth))
                {
                    width = parsedWidth;
                }

                if (streamElement.TryGetProperty("height", out var heightElement) && heightElement.TryGetInt32(out var parsedHeight))
                {
                    height = parsedHeight;
                }

                break;
            }
        }
        //=========================================================================

        return new MediaMetadata(duration, width, height, hasVideo);
    }

    private static MediaMetadata BuildFallbackMediaMetadata(string mediaPath)
    {
        return new MediaMetadata(
            TimeSpan.Zero,
            null,
            null,
            !IsAudioOnlyMedia(mediaPath) && !IsSupportedWatermarkImage(mediaPath));
    }

    private string BuildFileInfoText(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return "No media loaded";
        }

        try
        {
            var normalizedPath = NormalizeMediaPath(mediaPath);

            if (!File.Exists(normalizedPath))
            {
                return "Unavailable";
            }

            var fileInfo = new FileInfo(normalizedPath);
            var segments = new List<string>();

            if (_currentMediaMetadata is not null)
            {
                if (_currentMediaMetadata.Width.HasValue && _currentMediaMetadata.Height.HasValue)
                {
                    segments.Add($"{_currentMediaMetadata.Width.Value}x{_currentMediaMetadata.Height.Value}");
                }

                if (_currentMediaMetadata.Duration > TimeSpan.Zero)
                {
                    segments.Add(FormatTrimDurationShort(_currentMediaMetadata.Duration));
                }
            }

            segments.Add(FormatFileSize(fileInfo.Length));
            return string.Join(" \u2022 ", segments);
        }
        catch
        {
            return "File details unavailable";
        }
    }

    private static string GetFileFormatLabel(string mediaPath)
    {
        var extension = Path.GetExtension(mediaPath);
        return string.IsNullOrWhiteSpace(extension)
            ? "Unknown"
            : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatFileSize(long byteCount)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = byteCount;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{byteCount:N0} {units[unitIndex]}";
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private bool HasTrimCapableVideo()
    {
        var mediaPath = GetPreferredMediaPath();
        if (mediaPath is null)
        {
            return false;
        }

        return _currentMediaMetadata?.HasVideo ?? !IsAudioOnlyMedia(mediaPath);
    }

    private TimeSpan GetTrimDuration()
    {
        if (_currentMediaMetadata is not null && _currentMediaMetadata.Duration > TimeSpan.Zero)
        {
            return _currentMediaMetadata.Duration;
        }

        return _trimSelectionEnd > TimeSpan.Zero ? _trimSelectionEnd : TimeSpan.Zero;
    }

    private void ResetTrimSelectionToFullDuration()
    {
        var duration = GetTrimDuration();
        if (duration <= TimeSpan.Zero)
        {
            _trimSelectionStart = TimeSpan.Zero;
            _trimSelectionEnd = TimeSpan.Zero;
            _trimCurrentPosition = TimeSpan.Zero;
            return;
        }

        _trimSelectionStart = TimeSpan.Zero;
        _trimSelectionEnd = duration;
        _trimCurrentPosition = ClampTrimTime(_trimCurrentPosition, duration);
    }

    private void NormalizeTrimSelection(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            _trimSelectionStart = TimeSpan.Zero;
            _trimSelectionEnd = TimeSpan.Zero;
            _trimCurrentPosition = TimeSpan.Zero;
            return;
        }

        _trimSelectionStart = ClampTrimTime(_trimSelectionStart, duration);
        _trimSelectionEnd = ClampTrimTime(_trimSelectionEnd, duration);

        if (_trimSelectionEnd <= _trimSelectionStart)
        {
            _trimSelectionEnd = _trimSelectionStart + TimeSpan.FromMilliseconds(100);
            if (_trimSelectionEnd > duration)
            {
                _trimSelectionEnd = duration;
                _trimSelectionStart = duration - TimeSpan.FromMilliseconds(100);
            }
        }

        if (_trimSelectionStart < TimeSpan.Zero)
        {
            _trimSelectionStart = TimeSpan.Zero;
        }

        _trimCurrentPosition = ClampTrimTime(_trimCurrentPosition, duration);
    }

    private static TimeSpan ClampTrimTime(TimeSpan value, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || value <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value >= duration ? duration : value;
    }

    private void UpdateObservedTrimDuration(TimeSpan observedDuration)
    {
        if (observedDuration <= TimeSpan.Zero)
        {
            return;
        }

        //== state transition ===================================================
        var hasVideo = HasTrimCapableVideo();
        _currentMediaMetadata = _currentMediaMetadata is null
            ? new MediaMetadata(observedDuration, null, null, hasVideo)
            : _currentMediaMetadata with { Duration = observedDuration };

        if (_trimSelectionEnd <= TimeSpan.Zero)
        {
            _trimSelectionStart = TimeSpan.Zero;
            _trimSelectionEnd = observedDuration;
        }

        NormalizeTrimSelection(observedDuration);
        //=========================================================================
    }

    private void UpdateTrimUi()
    {
        if (_lblTrimCurrentPositionValue is null ||
            _lblTrimInPointValue is null ||
            _lblTrimOutPointValue is null ||
            _lblTrimSourceDurationValue is null ||
            _lblTrimSelectionValue is null ||
            _lblTrimTrimmedValue is null)
        {
            return;
        }

        //== state shaping ======================================================
        var mediaPath = GetPreferredMediaPath();
        var canInteract = _activeOperation == AppOperation.None;
        var hasVideo = HasTrimCapableVideo();
        var duration = GetTrimDuration();

        if (duration > TimeSpan.Zero)
        {
            NormalizeTrimSelection(duration);
        }

        var selectionDuration = duration > TimeSpan.Zero
            ? _trimSelectionEnd - _trimSelectionStart
            : TimeSpan.Zero;
        if (selectionDuration < TimeSpan.Zero)
        {
            selectionDuration = TimeSpan.Zero;
        }

        var trimmedDuration = duration > selectionDuration
            ? duration - selectionDuration
            : TimeSpan.Zero;

        _lblTrimCurrentPositionValue.Text = FormatTrimTimeCode(_trimCurrentPosition);
        _lblTrimInPointValue.Text = FormatTrimTimeCode(_trimSelectionStart);
        _lblTrimOutPointValue.Text = FormatTrimTimeCode(_trimSelectionEnd);
        _lblTrimSourceDurationValue.Text = duration > TimeSpan.Zero ? FormatTrimTimeCode(duration) : "--";
        _lblTrimSelectionValue.Text = selectionDuration > TimeSpan.Zero ? FormatTrimTimeCode(selectionDuration) : "--";
        _lblTrimTrimmedValue.Text = duration > TimeSpan.Zero ? FormatTrimTimeCode(trimmedDuration) : "--";

        if (_btnTrimPreviewSelection is not null)
        {
            _btnTrimPreviewSelection.Text = _trimSelectionPlaybackActive ? "\u23F8" : "\u25B6";
        }

        var hasPreviewPlayback = canInteract && hasVideo && _previewDocumentReady;

        if (_btnTrimSetIn is not null)
        {
            _btnTrimSetIn.Enabled = hasPreviewPlayback;
        }

        if (_btnTrimSetOut is not null)
        {
            _btnTrimSetOut.Enabled = hasPreviewPlayback;
        }

        if (_btnTrimJumpToIn is not null)
        {
            _btnTrimJumpToIn.Enabled = hasPreviewPlayback && duration > TimeSpan.Zero;
        }

        if (_btnTrimPreviewSelection is not null)
        {
            _btnTrimPreviewSelection.Enabled = hasPreviewPlayback && selectionDuration > TimeSpan.Zero;
        }

        if (_btnTrimJumpToOut is not null)
        {
            _btnTrimJumpToOut.Enabled = hasPreviewPlayback && duration > TimeSpan.Zero;
        }

        if (_btnTrimResetRange is not null)
        {
            _btnTrimResetRange.Enabled = canInteract && hasVideo && duration > TimeSpan.Zero;
        }

        if (_btnTrimExport is not null)
        {
            _btnTrimExport.Enabled = canInteract && hasVideo && selectionDuration > TimeSpan.Zero;
        }

        if (_trimTimelineControl is not null)
        {
            var timelineDuration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1);
            var timelineSelectionEnd = duration > TimeSpan.Zero ? _trimSelectionEnd : timelineDuration;
            _trimTimelineControl.Duration = timelineDuration;
            _trimTimelineControl.SelectionStart = duration > TimeSpan.Zero ? _trimSelectionStart : TimeSpan.Zero;
            _trimTimelineControl.SelectionEnd = timelineSelectionEnd;
            _trimTimelineControl.CurrentPosition = duration > TimeSpan.Zero ? _trimCurrentPosition : TimeSpan.Zero;
            _trimTimelineControl.SetWaveSeed(mediaPath);
        }
        //=========================================================================
    }

    private static string FormatTrimTimeCode(TimeSpan value)
    {
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private static string FormatTrimDurationShort(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        }

        return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    private static string GetTrimOutputExtension(string sourcePath)
    {
        return Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".mov" => "mov",
            ".mkv" => "mkv",
            _ => "mp4"
        };
    }

    private static string BuildTrimmedOutputPath(string sourcePath, string targetExtension)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = targetExtension.StartsWith(".", StringComparison.Ordinal)
            ? targetExtension
            : $".{targetExtension}";

        var candidatePath = Path.Combine(directory, $"{baseName}_trimmed{extension}");
        var counter = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{baseName}_trimmed_{counter}{extension}");
            counter++;
        }

        return candidatePath;
    }

    private async Task<double?> TryGetPlayerCurrentTimeAsync()
    {
        return await ExecutePreviewNumberScriptAsync(
            "(() => { const player = document.getElementById('player'); return player ? player.currentTime : null; })();");
    }

    private async Task<double?> TryGetPlayerDurationAsync()
    {
        return await ExecutePreviewNumberScriptAsync(
            "(() => { const player = document.getElementById('player'); return player ? player.duration : null; })();");
    }

    private async Task<double?> ExecutePreviewNumberScriptAsync(string script)
    {
        var result = await ExecutePreviewScriptAsync(script);
        if (!result.HasValue || result.Value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return result.Value.TryGetDouble(out var parsedValue) ? parsedValue : null;
    }

    private async Task<bool> SeekPreviewAsync(TimeSpan position)
    {
        var seconds = Math.Max(position.TotalSeconds, 0D).ToString("0.###", CultureInfo.InvariantCulture);
        return await ExecutePreviewBooleanScriptAsync(
            $"(() => {{ const player = document.getElementById('player'); if (!player) return false; player.currentTime = {seconds}; return true; }})();");
    }

    private async Task<bool> PlayPreviewAsync()
    {
        return await ExecutePreviewBooleanScriptAsync(
            "(() => { const player = document.getElementById('player'); if (!player) return false; player.play(); return true; })();");
    }

    private async Task<bool> PausePreviewAsync()
    {
        return await ExecutePreviewBooleanScriptAsync(
            "(() => { const player = document.getElementById('player'); if (!player) return false; player.pause(); return true; })();");
    }

    private async Task<bool> ExecutePreviewBooleanScriptAsync(string script)
    {
        var result = await ExecutePreviewScriptAsync(script);
        return result.HasValue && result.Value.ValueKind == JsonValueKind.True;
    }

    private async Task<JsonElement?> ExecutePreviewScriptAsync(string script)
    {
        //== precondition checks ================================================
        if (!_previewReady || !_previewDocumentReady || webPreview.CoreWebView2 is null)
        {
            return null;
        }
        //=========================================================================

        try
        {
            //== external browser call ==========================================
            var json = await webPreview.CoreWebView2.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(json) ||
                string.Equals(json, "null", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(json, "undefined", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
            //=====================================================================
        }
        catch
        {
            return null;
        }
    }

    private void btnCropReset_Click(object? sender, EventArgs e)
    {
        ResetCropSelectionToSourceBounds();
        UpdateCropUi();
    }

    private async void btnCropApply_Click(object? sender, EventArgs e)
    {
        await StartCropExportAsync();
    }

    private void SelectCropAspectPreset(CropAspectPreset preset)
    {
        //== state transition ===================================================
        _selectedCropAspectPreset = preset;
        if (!HasCropSourceBounds())
        {
            UpdateCropUi();
            return;
        }

        if (preset == CropAspectPreset.Custom)
        {
            UpdateCropUi();
            return;
        }

        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        var targetAspectRatio = GetCropAspectRatio(preset, sourceWidth, sourceHeight);
        if (!targetAspectRatio.HasValue)
        {
            UpdateCropUi();
            return;
        }
        //=========================================================================

        var cropWidth = NormalizeCropDimension(sourceWidth, sourceWidth);
        var cropHeight = NormalizeCropDimension((int)Math.Round(cropWidth / targetAspectRatio.Value), sourceHeight);

        if (cropHeight > sourceHeight || cropHeight <= 0)
        {
            cropHeight = NormalizeCropDimension(sourceHeight, sourceHeight);
            cropWidth = NormalizeCropDimension((int)Math.Round(cropHeight * targetAspectRatio.Value), sourceWidth);
        }

        _cropWidth = cropWidth;
        _cropHeight = cropHeight;
        _cropX = NormalizeCropCoordinate((sourceWidth - _cropWidth) / 2, sourceWidth - _cropWidth);
        _cropY = NormalizeCropCoordinate((sourceHeight - _cropHeight) / 2, sourceHeight - _cropHeight);

        UpdateCropUi();
        //=======================================================================
    }

    private void SetCropRotation(int rotationDegrees)
    {
        //== state transition ===================================================
        _cropRotationDegrees = rotationDegrees switch
        {
            <= -90 => -90,
            >= 90 => 90,
            _ => 0
        };

        UpdateCropUi();
        //=======================================================================
    }

    private void ResetCropSelectionToSourceBounds()
    {
        //== state transition ===================================================
        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        _selectedCropAspectPreset = CropAspectPreset.Original;
        _cropRotationDegrees = 0;
        _cropX = 0;
        _cropY = 0;
        _cropWidth = sourceWidth;
        _cropHeight = sourceHeight;
        NormalizeCropSelection();
        //=======================================================================
    }

    private bool HasCropSourceBounds()
    {
        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        return sourceWidth > 0 && sourceHeight > 0 && HasTrimCapableVideo();
    }

    private (int Width, int Height) GetCropSourceSize()
    {
        //== output shaping =====================================================
        if (_currentMediaMetadata is not null &&
            _currentMediaMetadata.HasVideo &&
            _currentMediaMetadata.Width is > 0 &&
            _currentMediaMetadata.Height is > 0)
        {
            return (_currentMediaMetadata.Width.Value, _currentMediaMetadata.Height.Value);
        }

        return (0, 0);
        //=======================================================================
    }

    private (int Width, int Height) GetCropOutputSize()
    {
        //== output shaping =====================================================
        if (!HasCropSourceBounds())
        {
            return (0, 0);
        }
        //=========================================================================

        return Math.Abs(_cropRotationDegrees) == 90
            ? (_cropHeight, _cropWidth)
            : (_cropWidth, _cropHeight);
        //=======================================================================
    }

    private string BuildCropStatusText()
    {
        //== output shaping =====================================================
        var (outputWidth, outputHeight) = GetCropOutputSize();
        return outputWidth > 0 && outputHeight > 0
            ? $"{outputWidth} \u00D7 {outputHeight}"
            : "Video Only";
        //=======================================================================
    }

    private void NormalizeCropSelection()
    {
        //== normalization ======================================================
        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            _cropX = 0;
            _cropY = 0;
            _cropWidth = 0;
            _cropHeight = 0;
            return;
        }

        _cropWidth = NormalizeCropDimension(_cropWidth <= 0 ? sourceWidth : _cropWidth, sourceWidth);
        _cropHeight = NormalizeCropDimension(_cropHeight <= 0 ? sourceHeight : _cropHeight, sourceHeight);
        _cropX = NormalizeCropCoordinate(_cropX, sourceWidth - _cropWidth);
        _cropY = NormalizeCropCoordinate(_cropY, sourceHeight - _cropHeight);
        //=======================================================================
    }

    private static int NormalizeCropCoordinate(int value, int maxCoordinate)
    {
        //== normalization ======================================================
        var clampedValue = Math.Clamp(value, 0, Math.Max(maxCoordinate, 0));
        return clampedValue > 0
            ? clampedValue - (clampedValue % 2)
            : 0;
        //=======================================================================
    }

    private static int NormalizeCropDimension(int value, int sourceDimension)
    {
        //== normalization ======================================================
        if (sourceDimension <= 0)
        {
            return 0;
        }

        if (sourceDimension <= 2)
        {
            return sourceDimension;
        }

        var clampedValue = Math.Clamp(value, 2, sourceDimension);
        if (clampedValue == sourceDimension)
        {
            return sourceDimension % 2 == 0
                ? sourceDimension
                : sourceDimension - 1;
        }

        return clampedValue % 2 == 0
            ? clampedValue
            : clampedValue - 1;
        //=======================================================================
    }

    private static double? GetCropAspectRatio(CropAspectPreset preset, int sourceWidth, int sourceHeight)
    {
        //== output shaping =====================================================
        return preset switch
        {
            CropAspectPreset.Original when sourceWidth > 0 && sourceHeight > 0 => (double)sourceWidth / sourceHeight,
            CropAspectPreset.Square => 1D,
            CropAspectPreset.Landscape16x9 => 16D / 9D,
            CropAspectPreset.Portrait9x16 => 9D / 16D,
            CropAspectPreset.Landscape4x3 => 4D / 3D,
            _ => null
        };
        //=======================================================================
    }

    private void UpdateCropUi()
    {
        //== output shaping =====================================================
        var hasCropBounds = HasCropSourceBounds();
        var canInteract = _activeOperation == AppOperation.None;
        if (hasCropBounds)
        {
            NormalizeCropSelection();
        }

        var inputsEnabled = hasCropBounds && canInteract;
        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        var (outputWidth, outputHeight) = GetCropOutputSize();

        _cropUiSyncInFlight = true;
        try
        {
            if (_txtCropX is not null)
            {
                _txtCropX.Enabled = inputsEnabled;
                _txtCropX.Text = hasCropBounds ? _cropX.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }

            if (_txtCropY is not null)
            {
                _txtCropY.Enabled = inputsEnabled;
                _txtCropY.Text = hasCropBounds ? _cropY.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }

            if (_txtCropWidth is not null)
            {
                _txtCropWidth.Enabled = inputsEnabled;
                _txtCropWidth.Text = hasCropBounds ? _cropWidth.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }

            if (_txtCropHeight is not null)
            {
                _txtCropHeight.Enabled = inputsEnabled;
                _txtCropHeight.Text = hasCropBounds ? _cropHeight.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }
        }
        finally
        {
            _cropUiSyncInFlight = false;
        }

        UpdateCropSelectorState(_cropAspectCustomButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Custom);
        UpdateCropSelectorState(_cropAspectOriginalButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Original);
        UpdateCropSelectorState(_cropAspectSquareButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Square);
        UpdateCropSelectorState(_cropAspectLandscapeButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Landscape16x9);
        UpdateCropSelectorState(_cropAspectPortraitButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Portrait9x16);
        UpdateCropSelectorState(_cropAspectClassicButton, inputsEnabled, _selectedCropAspectPreset == CropAspectPreset.Landscape4x3);
        UpdateCropSelectorState(_btnCropRotateLeft, inputsEnabled, _cropRotationDegrees == -90);
        UpdateCropSelectorState(_btnCropRotateReset, inputsEnabled, _cropRotationDegrees == 0);
        UpdateCropSelectorState(_btnCropRotateRight, inputsEnabled, _cropRotationDegrees == 90);

        if (_lblCropSourceValue is not null)
        {
            _lblCropSourceValue.Text = hasCropBounds
                ? $"{sourceWidth} \u00D7 {sourceHeight}"
                : "--";
        }

        if (_lblCropOutputValue is not null)
        {
            _lblCropOutputValue.Text = hasCropBounds
                ? $"{outputWidth} \u00D7 {outputHeight}"
                : "--";
        }

        if (_lblCropRotationValue is not null)
        {
            _lblCropRotationValue.Text = $"{_cropRotationDegrees}\u00B0";
        }

        if (_btnCropReset is not null)
        {
            _btnCropReset.Enabled = inputsEnabled;
        }

        if (_btnCropApply is not null)
        {
            _btnCropApply.Enabled = inputsEnabled;
        }

        _ = SyncCropPreviewOverlayAsync();
        //=======================================================================
    }

    private static void UpdateCropSelectorState(Button? button, bool enabled, bool selected)
    {
        if (button is null)
        {
            return;
        }

        button.Enabled = enabled;
        ApplySelectorButtonState(button, selected);
    }

    private async Task SyncCropPreviewOverlayAsync()
    {
        //== external service call ==============================================
        if (!_previewReady || !_previewDocumentReady)
        {
            return;
        }

        var (sourceWidth, sourceHeight) = GetCropSourceSize();
        var overlayState = JsonSerializer.Serialize(new
        {
            enabled = _currentWorkspacePage == WorkspacePage.Crop && HasCropSourceBounds(),
            x = _cropX,
            y = _cropY,
            width = _cropWidth,
            height = _cropHeight,
            sourceWidth,
            sourceHeight,
            rotation = _cropRotationDegrees
        });

        await ExecutePreviewScriptAsync($"(() => window.cropOverlayApi?.setState({overlayState}) ?? null)();");
        //=======================================================================
    }

    private async Task StartCropExportAsync()
    {
        //== input validation ===================================================
        if (_activeOperation != AppOperation.None)
        {
            return;
        }

        var sourcePath = GetPreferredMediaPath();
        if (sourcePath is null || !HasCropSourceBounds())
        {
            MessageBox.Show(this, "Open a video file with readable dimensions before cropping.", "Video Required");
            return;
        }

        var ffmpegPath = ResolveToolPath(
            "ffmpeg.exe",
            out var ffmpegSearchPaths,
            Path.Combine("tools", "ffmpeg.exe"),
            Path.Combine("tools", "ffmpeg", "ffmpeg.exe"));

        if (ffmpegPath is null)
        {
            MessageBox.Show(
                this,
                "ffmpeg.exe was not found.\r\n\r\n" +
                "Place it in one of these locations and rebuild if needed:\r\n" +
                string.Join("\r\n", ffmpegSearchPaths.Take(3)) +
                "\r\n\r\nOr install it on PATH.",
                "ffmpeg Missing");
            return;
        }
        //=======================================================================

        //== output setup =======================================================
        var targetExtension = GetCropOutputExtension(sourcePath);
        var outputPath = BuildCroppedOutputPath(sourcePath, targetExtension);
        var requestedCompressionOptions = GetSelectedVideoCompressionOptions();
        var selectedVideoEncoder = ResolveVideoEncoderSelection(ffmpegPath, requestedCompressionOptions.UseHardwareAcceleration);
        var selectedCompressionOptions = GetEffectiveVideoCompressionOptions(requestedCompressionOptions, selectedVideoEncoder);
        txtLog.AppendText(Environment.NewLine);
        AppendLog($"ffmpeg: {ffmpegPath}");
        AppendLog($"Crop export: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)}");
        AppendLog($"Crop region: x={_cropX}, y={_cropY}, width={_cropWidth}, height={_cropHeight}");
        AppendLog($"Rotation: {_cropRotationDegrees}\u00B0");
        AppendLog($"Video codec: {selectedVideoEncoder.DisplayLabel}");
        AppendLog($"Compression options: {DescribeVideoCompressionOptions(selectedCompressionOptions)}");
        //=======================================================================

        SetUiBusy(AppOperation.Crop);
        UpdateStatus("Applying crop & rotation...");
        AddActivityEntry(ActivityFeedIconKind.Export, $"Crop started: {Path.GetFileName(sourcePath)}");
        _lastActivityErrorSummary = null;

        try
        {
            //== external process ===============================================
            var startInfo = BuildCropExportStartInfo(
                ffmpegPath,
                sourcePath,
                outputPath,
                targetExtension,
                selectedCompressionOptions,
                selectedVideoEncoder);
            var exitCode = await RunLoggedProcessAsync(startInfo);

            if (exitCode != 0 && selectedVideoEncoder.UsesHardwareAcceleration)
            {
                //== hardware fallback ==========================================
                var softwareVideoEncoder = ResolveVideoEncoderSelection(ffmpegPath, preferHardwareAcceleration: false);
                if (!string.Equals(softwareVideoEncoder.EncoderName, selectedVideoEncoder.EncoderName, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"Hardware crop encode failed with {selectedVideoEncoder.DisplayLabel}. Retrying with {softwareVideoEncoder.DisplayLabel}.");
                    selectedCompressionOptions = GetEffectiveVideoCompressionOptions(requestedCompressionOptions, softwareVideoEncoder);
                    AppendLog($"Retry compression options: {DescribeVideoCompressionOptions(selectedCompressionOptions)}");
                    UpdateStatus("Retrying crop export with software encode...");
                    startInfo = BuildCropExportStartInfo(
                        ffmpegPath,
                        sourcePath,
                        outputPath,
                        targetExtension,
                        selectedCompressionOptions,
                        softwareVideoEncoder);
                    exitCode = await RunLoggedProcessAsync(startInfo);
                }
            }
            //===================================================================

            //== output handling ================================================
            if (exitCode == 0)
            {
                SetProgressValue(100);
                _lastDownloadedFilePath = outputPath;
                SetCurrentMediaSource(outputPath);

                if (await EnsurePreviewReadyAsync())
                {
                    await LoadPreviewAsync(outputPath, switchToPreview: true);
                }
                else
                {
                    FocusPreviewStage();
                }

                ShowWorkspacePage(WorkspacePage.Crop);
                UpdateStatus("Crop export completed");
                AppendLog($"Cropped clip exported: {outputPath}");
                AppendLog(BuildConversionSizeSummary(sourcePath, outputPath));
                AddActivityEntry(
                    ActivityFeedIconKind.Success,
                    $"Crop complete \u2192 {BuildActivityFileSummary(outputPath)}",
                    countsAsExport: true);
            }
            else
            {
                UpdateStatus($"Crop export failed (exit code {exitCode})");
                AppendLog($"ffmpeg exited with code {exitCode}.");
                AddActivityEntry(
                    ActivityFeedIconKind.Error,
                    BuildActivityFailureMessage("Crop failed", $"Crop export failed (exit code {exitCode})."),
                    countsAsError: true);
            }
            //===================================================================
        }
        catch (Exception ex)
        {
            //== error handling =================================================
            UpdateStatus("Crop export error");
            AppendLog(ex.Message);
            AddActivityEntry(
                ActivityFeedIconKind.Error,
                BuildActivityFailureMessage("Crop failed", $"Crop export failed: {ex.Message}"),
                countsAsError: true);
            MessageBox.Show(this, ex.Message, "Crop Export Error");
            //===================================================================
        }
        finally
        {
            //== cleanup ========================================================
            _activeProcess = null;
            SetUiBusy(AppOperation.None);
            //===================================================================
        }
    }

    private ProcessStartInfo BuildCropExportStartInfo(
        string ffmpegPath,
        string sourcePath,
        string outputPath,
        string targetExtension,
        VideoCompressionOptions selectedCompressionOptions,
        VideoEncoderSelection selectedVideoEncoder)
    {
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");

        if (selectedCompressionOptions.UseHardwareAcceleration)
        {
            startInfo.ArgumentList.Add("-hwaccel");
            startInfo.ArgumentList.Add("auto");
        }

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0?");

        if (selectedCompressionOptions.StripMetadata)
        {
            startInfo.ArgumentList.Add("-map_metadata");
            startInfo.ArgumentList.Add("-1");
        }

        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(BuildCropFilterExpression());
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add(selectedVideoEncoder.EncoderName);
        AddVideoEncoderArguments(startInfo.ArgumentList, selectedVideoEncoder, selectedVideoQuality);

        if (_selectedVideoCodec == "h265" && targetExtension is "mp4" or "mov")
        {
            startInfo.ArgumentList.Add("-tag:v");
            startInfo.ArgumentList.Add("hvc1");
        }

        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add(selectedVideoQuality.AudioBitrate);

        if (targetExtension is "mp4" or "mov")
        {
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");
        }

        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private string BuildCropFilterExpression()
    {
        //== output shaping =====================================================
        var filterParts = new List<string>
        {
            $"crop={_cropWidth}:{_cropHeight}:{_cropX}:{_cropY}"
        };

        if (_cropRotationDegrees == -90)
        {
            filterParts.Add("transpose=2");
        }
        else if (_cropRotationDegrees == 90)
        {
            filterParts.Add("transpose=1");
        }

        return string.Join(",", filterParts);
        //=======================================================================
    }

    private string GetSelectedVideoCodecEncoder()
    {
        return _selectedVideoCodec == "h265"
            ? "libx265"
            : "libx264";
    }

    private static string GetCropOutputExtension(string sourcePath)
    {
        return Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".mov" => "mov",
            ".mkv" => "mkv",
            _ => "mp4"
        };
    }

    private static string BuildCroppedOutputPath(string sourcePath, string targetExtension)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = targetExtension.StartsWith(".", StringComparison.Ordinal)
            ? targetExtension
            : $".{targetExtension}";

        var candidatePath = Path.Combine(directory, $"{baseName}_cropped{extension}");
        var counter = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{baseName}_cropped_{counter}{extension}");
            counter++;
        }

        return candidatePath;
    }

    private void UpdatePreviewButtons()
    {
        var canInteract = _activeOperation == AppOperation.None;
        var hasLastDownload = !string.IsNullOrWhiteSpace(_lastDownloadedFilePath) &&
                              File.Exists(_lastDownloadedFilePath);

        btnOpenMediaFile.Enabled = canInteract;
        btnPreviewLast.Enabled = _previewReady && hasLastDownload && canInteract;
        btnOpenExternal.Enabled = canInteract &&
                                  (GetPreferredMediaPath() is not null || Directory.Exists(txtOutputFolder.Text.Trim()));
        UpdateWatermarkButtons();
        UpdateConversionButtons();
        UpdateTrimUi();
        UpdateCropUi();
        UpdateVideoQualityUi();
    }

    private void UpdateWatermarkButtons()
    {
        if (_watermarkRemoveButton is null ||
            _watermarkPreviewButton is null ||
            _watermarkInstallButton is null ||
            _watermarkManualSelectButton is null)
        {
            return;
        }

        //== state shaping ====================================================
        var mediaPath = GetPreferredMediaPath();
        var hasSupportedMedia = mediaPath is not null && IsSupportedWatermarkMedia(mediaPath);
        var canInteract = _activeOperation == AppOperation.None;
        var runtimeReady = _watermarkRuntimeStatus?.IsInstalled == true;
        var authorizationConfirmed = mediaPath is not null &&
                                     IsWatermarkAuthorizationConfirmed(mediaPath);
        var manualMode = _watermarkManualModeRadioButton?.Checked == true;
        var hasSelectedRegions = mediaPath is not null &&
                                 string.Equals(_watermarkSelectionMediaPath, mediaPath, StringComparison.OrdinalIgnoreCase) &&
                                 _watermarkSelectedRegions.Count > 0;
        var canCancel = _activeOperation == AppOperation.RemoveWatermark &&
                        _watermarkCts is not null &&
                        !_watermarkCts.IsCancellationRequested;
        //=====================================================================

        //== input state ======================================================
        if (_watermarkDetectionPromptTextBox is not null)
        {
            _watermarkDetectionPromptTextBox.Enabled = canInteract && !manualMode;
        }

        if (_watermarkMaximumDetectionSizeInput is not null)
        {
            _watermarkMaximumDetectionSizeInput.Enabled = canInteract && !manualMode;
        }

        if (_watermarkDetectionIntervalInput is not null)
        {
            _watermarkDetectionIntervalInput.Enabled = canInteract && !manualMode;
        }

        if (_watermarkFadeInInput is not null)
        {
            _watermarkFadeInInput.Enabled = canInteract && !manualMode;
        }

        if (_watermarkFadeOutInput is not null)
        {
            _watermarkFadeOutInput.Enabled = canInteract && !manualMode;
        }

        if (_watermarkUseGpuCheckBox is not null)
        {
            _watermarkUseGpuCheckBox.Enabled = canInteract;
        }

        if (_watermarkMaskPaddingInput is not null)
        {
            _watermarkMaskPaddingInput.Enabled = canInteract;
        }

        if (_watermarkAutoModeRadioButton is not null)
        {
            _watermarkAutoModeRadioButton.Enabled = canInteract;
        }

        if (_watermarkManualModeRadioButton is not null)
        {
            _watermarkManualModeRadioButton.Enabled = canInteract && hasSelectedRegions;
        }

        if (_watermarkAuthorizationCheckBox is not null)
        {
            _watermarkAuthorizationCheckBox.Enabled = canInteract && hasSupportedMedia;
        }
        //=====================================================================

        //== action state =====================================================
        _watermarkRemoveButton.Text = _watermarkActionMode == WatermarkActionMode.Remove
            ? "Cancel cleanup"
            : "Remove watermark";
        _watermarkPreviewButton.Text = _watermarkActionMode == WatermarkActionMode.Preview
            ? "Cancel preview"
            : "Preview detection";
        _watermarkManualSelectButton.Text = _watermarkActionMode == WatermarkActionMode.Selection
            ? "Cancel area selection"
            : hasSelectedRegions
                ? "Edit selected areas"
                : "Select areas manually";

        _watermarkRemoveButton.Enabled = _watermarkActionMode == WatermarkActionMode.Remove
            ? canCancel
            : canInteract &&
              hasSupportedMedia &&
              authorizationConfirmed &&
              runtimeReady &&
              (!manualMode || hasSelectedRegions);
        _watermarkPreviewButton.Enabled = _watermarkActionMode == WatermarkActionMode.Preview
            ? canCancel
            : canInteract && hasSupportedMedia && authorizationConfirmed && runtimeReady;
        _watermarkManualSelectButton.Enabled = _watermarkActionMode == WatermarkActionMode.Selection
            ? canCancel
            : canInteract && hasSupportedMedia && authorizationConfirmed && runtimeReady;
        _watermarkInstallButton.Text = _watermarkInstallationInProgress
            ? "Cancel installation"
            : _watermarkRuntimeCheckInProgress
                ? "Checking runtime..."
            : "Install or repair runtime";
        _watermarkInstallButton.Enabled = canInteract &&
                                          (!_watermarkRuntimeCheckInProgress || _watermarkInstallationInProgress);
        //=====================================================================
    }

    private void UpdateWatermarkModeUi()
    {
        //== state shaping ====================================================
        var manualMode = _watermarkManualModeRadioButton?.Checked == true;
        if (_watermarkAutomaticSettingsPanel is not null)
        {
            _watermarkAutomaticSettingsPanel.Visible = !manualMode;
        }

        UpdateWatermarkSelectionStatus();
        UpdateWatermarkButtons();
        //=====================================================================
    }

    private void UpdateWatermarkSelectionStatus()
    {
        if (_watermarkSelectionStatusLabel is null)
        {
            return;
        }

        //== output shaping ===================================================
        var mediaPath = GetPreferredMediaPath();
        var hasCurrentSelection = mediaPath is not null &&
                                  string.Equals(_watermarkSelectionMediaPath, mediaPath, StringComparison.OrdinalIgnoreCase) &&
                                  _watermarkSelectedRegions.Count > 0;
        _watermarkSelectionStatusLabel.Text = hasCurrentSelection
            ? _watermarkSelectedRegions.Count == 1
                ? "1 fixed area selected. It will be applied across every frame."
                : $"{_watermarkSelectedRegions.Count:N0} fixed areas selected. They will be applied across every frame."
            : "No fixed areas selected. Auto mode will detect across sampled frames.";
        _watermarkSelectionStatusLabel.ForeColor = hasCurrentSelection
            ? SuccessColor
            : SecondaryTextColor;
        //=====================================================================
    }

    private static bool IsSupportedWatermarkMedia(string mediaPath)
    {
        return Path.GetExtension(mediaPath).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff" or
            ".mp4" or ".avi" or ".mov" or ".mkv" or ".flv" or ".wmv" or ".webm" or ".m4v";
    }

    private void SelectConversionTarget(string targetExtension, bool requiresVideo)
    {
        //== state transition ===================================================
        if (requiresVideo)
        {
            _selectedVideoTargetExtension = targetExtension;
        }
        else
        {
            _selectedAudioTargetExtension = targetExtension;
        }

        _selectedConversionRequiresVideo = requiresVideo;
        UpdateConversionTargetSelectionUi();
        UpdateVideoQualityUi();
        //=========================================================================
    }

    private void SelectVideoCodec(string videoCodec)
    {
        //== state transition ===================================================
        _selectedVideoCodec = videoCodec;
        UpdateVideoCodecSelectionUi();
        UpdateVideoQualityUi();
        //=========================================================================
    }

    private string GetSelectedConversionTargetExtension()
    {
        return _selectedConversionRequiresVideo
            ? _selectedVideoTargetExtension
            : _selectedAudioTargetExtension;
    }

    private string GetSelectedVideoCodecLabel()
    {
        return _selectedVideoCodec == "h265" ? "H265" : "H264";
    }

    private void UpdateConversionButtons()
    {
        var canInteract = _activeOperation == AppOperation.None;
        var mediaPath = GetPreferredMediaPath();
        var hasMedia = mediaPath is not null;
        var hasVideo = hasMedia &&
                       !IsAudioOnlyMedia(mediaPath!) &&
                       !IsSupportedWatermarkImage(mediaPath!);
        var canConvertSelectedTarget = canInteract &&
                                       hasMedia &&
                                       (!_selectedConversionRequiresVideo || hasVideo);

        btnConvertMp3.Enabled = canInteract;
        btnConvertWav.Enabled = canInteract;
        btnConvertM4a.Enabled = canInteract;
        btnConvertMp4.Enabled = canInteract;
        btnConvertMkv.Enabled = canInteract;
        btnConvertMov.Enabled = canInteract;
        trkVideoQuality.Enabled = canInteract && _selectedConversionRequiresVideo;

        if (_compressionPresetLightButton is not null)
        {
            _compressionPresetLightButton.Enabled = canInteract && _selectedConversionRequiresVideo;
        }

        if (_compressionPresetBalancedButton is not null)
        {
            _compressionPresetBalancedButton.Enabled = canInteract && _selectedConversionRequiresVideo;
        }

        if (_compressionPresetAggressiveButton is not null)
        {
            _compressionPresetAggressiveButton.Enabled = canInteract && _selectedConversionRequiresVideo;
        }

        if (_conversionCodecH264Button is not null)
        {
            _conversionCodecH264Button.Enabled = canInteract && _selectedConversionRequiresVideo;
        }

        if (_conversionCodecH265Button is not null)
        {
            _conversionCodecH265Button.Enabled = canInteract && _selectedConversionRequiresVideo;
        }

        if (_compressionStripMetadataCheckBox is not null)
        {
            _compressionStripMetadataCheckBox.Enabled = canInteract;
        }

        if (_compressionTwoPassCheckBox is not null)
        {
            _compressionTwoPassCheckBox.Enabled = canInteract;
        }

        if (_compressionHardwareAccelerationCheckBox is not null)
        {
            _compressionHardwareAccelerationCheckBox.Enabled = canInteract;
        }

        if (_compressionActionButton is not null)
        {
            _compressionActionButton.Enabled = canConvertSelectedTarget;
        }

        UpdateConversionTargetSelectionUi();
        UpdateVideoCodecSelectionUi();
    }

    private VideoQualityPreset GetSelectedVideoQualityPreset()
    {
        var presetIndex = Math.Clamp(
            VideoQualityPresets.Length - 1 - trkVideoQuality.Value,
            0,
            VideoQualityPresets.Length - 1);
        return VideoQualityPresets[presetIndex];
    }

    private void UpdateVideoQualityUi()
    {
        //== output shaping ======================================================
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        var isVideoTargetSelected = _selectedConversionRequiresVideo;
        lblVideoQualityValue.Text = isVideoTargetSelected
            ? $"{selectedVideoQuality.Name} \u00B7 CRF {selectedVideoQuality.Crf}"
            : "Video-only setting";
        lblVideoQualityValue.ForeColor = isVideoTargetSelected && selectedVideoQuality.WarnOfNoticeableLoss
            ? WarningTextColor
            : isVideoTargetSelected
                ? PrimaryTextColor
                : SecondaryTextColor;
        lblVideoQualityHint.ForeColor = isVideoTargetSelected && selectedVideoQuality.WarnOfNoticeableLoss
            ? WarningTextColor
            : SecondaryTextColor;
        lblVideoQualityHint.Text = isVideoTargetSelected
            ? selectedVideoQuality.HintText
            : "Switch to MP4, MKV, or MOV to apply quality and codec controls.";
        if (_compressionQualityPercentLabel is not null)
        {
            _compressionQualityPercentLabel.Text = isVideoTargetSelected
                ? $"{selectedVideoQuality.QualityPercent}%"
                : "--";
            _compressionQualityPercentLabel.ForeColor = isVideoTargetSelected && selectedVideoQuality.WarnOfNoticeableLoss
                ? WarningTextColor
                : isVideoTargetSelected
                    ? PrimaryTextColor
                    : SecondaryTextColor;
        }

        UpdateCompressionPresetSelectionUi();
        UpdateCompressionEstimateUi(selectedVideoQuality);
        UpdateConversionTargetSelectionUi();
        UpdateVideoCodecSelectionUi();
        //=========================================================================
    }

    private void UpdateCompressionPresetSelectionUi()
    {
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        var presetGroup = selectedVideoQuality.QualityPercent switch
        {
            >= 75 => "light",
            >= 45 => "balanced",
            _ => "aggressive"
        };

        if (_compressionPresetLightButton is not null)
        {
            ApplySelectorButtonState(_compressionPresetLightButton, presetGroup == "light");
        }

        if (_compressionPresetBalancedButton is not null)
        {
            ApplySelectorButtonState(_compressionPresetBalancedButton, presetGroup == "balanced");
        }

        if (_compressionPresetAggressiveButton is not null)
        {
            ApplySelectorButtonState(_compressionPresetAggressiveButton, presetGroup == "aggressive");
        }
    }

    private void UpdateConversionTargetSelectionUi()
    {
        ApplySelectorButtonState(btnConvertMp4, _selectedConversionRequiresVideo && _selectedVideoTargetExtension == "mp4");
        ApplySelectorButtonState(btnConvertMkv, _selectedConversionRequiresVideo && _selectedVideoTargetExtension == "mkv");
        ApplySelectorButtonState(btnConvertMov, _selectedConversionRequiresVideo && _selectedVideoTargetExtension == "mov");
        ApplySelectorButtonState(btnConvertMp3, !_selectedConversionRequiresVideo && _selectedAudioTargetExtension == "mp3");
        ApplySelectorButtonState(btnConvertWav, !_selectedConversionRequiresVideo && _selectedAudioTargetExtension == "wav");
        ApplySelectorButtonState(btnConvertM4a, !_selectedConversionRequiresVideo && _selectedAudioTargetExtension == "m4a");
    }

    private void UpdateVideoCodecSelectionUi()
    {
        if (_conversionCodecH264Button is not null)
        {
            ApplySelectorButtonState(_conversionCodecH264Button, _selectedVideoCodec == "h264");
        }

        if (_conversionCodecH265Button is not null)
        {
            ApplySelectorButtonState(_conversionCodecH265Button, _selectedVideoCodec == "h265");
        }
    }

    private void UpdateCompressionEstimateUi(VideoQualityPreset selectedVideoQuality)
    {
        var mediaPath = GetPreferredMediaPath();
        var selectedTargetExtension = GetSelectedConversionTargetExtension();
        var hasVideo = mediaPath is not null && !IsAudioOnlyMedia(mediaPath);

        if (_conversionFormatValueLabel is not null)
        {
            _conversionFormatValueLabel.Text = $".{selectedTargetExtension}";
        }

        //== output shaping ======================================================
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            SetCompressionEstimatePlaceholders("Load media to estimate output size.");
            return;
        }

        var fileName = Path.GetFileName(mediaPath);
        if (_compressionPreviewFileLabel is not null)
        {
            _compressionPreviewFileLabel.Text = fileName;
        }

        if (_selectedConversionRequiresVideo && !hasVideo)
        {
            SetCompressionEstimatePlaceholders("Video conversion needs a file with video.");
            return;
        }

        try
        {
            var normalizedMediaPath = NormalizeMediaPath(mediaPath);
            if (!File.Exists(normalizedMediaPath))
            {
                SetCompressionEstimatePlaceholders("Media file details are unavailable.");
                return;
            }

            var sourceSize = new FileInfo(normalizedMediaPath).Length;
            var estimatedOutputSize = EstimateOutputSize(sourceSize, selectedVideoQuality, selectedTargetExtension);
            var savedBytes = Math.Max(0L, sourceSize - estimatedOutputSize);
            var savedPercent = sourceSize > 0
                ? Math.Clamp((int)Math.Round((double)savedBytes / sourceSize * 100D, MidpointRounding.AwayFromZero), 0, 99)
                : 0;

            if (_compressionPreviewSizeBadgeLabel is not null)
            {
                _compressionPreviewSizeBadgeLabel.Text = FormatFileSize(estimatedOutputSize);
            }

            if (_conversionEstimatedSizeValueLabel is not null)
            {
                _conversionEstimatedSizeValueLabel.Text = $"~{FormatFileSize(estimatedOutputSize)}";
            }

            if (_compressionOriginalSizeLabel is not null)
            {
                _compressionOriginalSizeLabel.Text = FormatFileSize(sourceSize);
            }

            if (_compressionOutputSizeLabel is not null)
            {
                _compressionOutputSizeLabel.Text = FormatFileSize(estimatedOutputSize);
            }

            if (_compressionSavingsTitleLabel is not null)
            {
                _compressionSavingsTitleLabel.Text = $"Save {FormatFileSize(savedBytes)}";
            }

            if (_compressionSavingsDetailLabel is not null)
            {
                _compressionSavingsDetailLabel.Text = savedPercent > 0
                    ? $"{savedPercent}% reduction from the current source."
                    : "Minimal savings at this quality level.";
            }

            if (_compressionSavingsPercentLabel is not null)
            {
                _compressionSavingsPercentLabel.Text = $"{savedPercent}%";
            }

            if (_compressionActionButton is not null)
            {
                _compressionActionButton.Text = $"Convert to {selectedTargetExtension.ToUpperInvariant()}";
            }

            if (_compressionOutputFooterLabel is not null)
            {
                _compressionOutputFooterLabel.Text =
                    $"Output: {FormatFileSize(estimatedOutputSize)} \u00B7 Codec: {GetSelectedVideoCodecLabel()} \u00B7 Format: .{selectedTargetExtension}";
            }

            UpdateCompressionBarFill(_compressionOriginalBarTrack, _compressionOriginalBarFill, 1D);
            UpdateCompressionBarFill(
                _compressionOutputBarTrack,
                _compressionOutputBarFill,
                sourceSize > 0 ? (double)estimatedOutputSize / sourceSize : 0D);
        }
        catch
        {
            SetCompressionEstimatePlaceholders("Output estimate is unavailable.");
        }
        //=========================================================================
    }

    private long EstimateOutputSize(long sourceSize, VideoQualityPreset selectedVideoQuality, string targetExtension)
    {
        //== output shaping ======================================================
        if (_selectedConversionRequiresVideo)
        {
            var codecFactor = _selectedVideoCodec == "h265" ? 0.84D : 1D;
            return Math.Max(
                1L,
                (long)Math.Round(
                    sourceSize * selectedVideoQuality.EstimatedOutputFactor * codecFactor,
                    MidpointRounding.AwayFromZero));
        }

        var duration = _currentMediaMetadata?.Duration ?? TimeSpan.Zero;
        if (duration > TimeSpan.Zero)
        {
            var bitrateKbps = targetExtension switch
            {
                "mp3" => 192D,
                "m4a" => 160D,
                "wav" => 1411.2D,
                _ => 160D
            };
            var estimatedBytes = duration.TotalSeconds * bitrateKbps * 1000D / 8D;
            return Math.Max(1L, (long)Math.Round(estimatedBytes, MidpointRounding.AwayFromZero));
        }

        var fallbackFactor = targetExtension switch
        {
            "mp3" => 0.18D,
            "m4a" => 0.15D,
            "wav" => 1.45D,
            _ => 0.25D
        };
        return Math.Max(1L, (long)Math.Round(sourceSize * fallbackFactor, MidpointRounding.AwayFromZero));
        //=========================================================================
    }

    private void SetCompressionEstimatePlaceholders(string detailText)
    {
        if (_compressionPreviewFileLabel is not null && string.IsNullOrWhiteSpace(GetPreferredMediaPath()))
        {
            _compressionPreviewFileLabel.Text = "No media loaded";
        }

        if (_compressionPreviewSizeBadgeLabel is not null)
        {
            _compressionPreviewSizeBadgeLabel.Text = "--";
        }

        if (_conversionEstimatedSizeValueLabel is not null)
        {
            _conversionEstimatedSizeValueLabel.Text = "--";
        }

        if (_compressionOriginalSizeLabel is not null)
        {
            _compressionOriginalSizeLabel.Text = "--";
        }

        if (_compressionOutputSizeLabel is not null)
        {
            _compressionOutputSizeLabel.Text = "--";
        }

        if (_compressionSavingsTitleLabel is not null)
        {
            _compressionSavingsTitleLabel.Text = "Save --";
        }

        if (_compressionSavingsDetailLabel is not null)
        {
            _compressionSavingsDetailLabel.Text = detailText;
        }

        if (_compressionSavingsPercentLabel is not null)
        {
            _compressionSavingsPercentLabel.Text = "--%";
        }

        if (_compressionActionButton is not null)
        {
            _compressionActionButton.Text = $"Convert to {GetSelectedConversionTargetExtension().ToUpperInvariant()}";
        }

        if (_compressionOutputFooterLabel is not null)
        {
            _compressionOutputFooterLabel.Text = detailText;
        }

        UpdateCompressionBarFill(_compressionOriginalBarTrack, _compressionOriginalBarFill, 0D);
        UpdateCompressionBarFill(_compressionOutputBarTrack, _compressionOutputBarFill, 0D);
    }

    private static void UpdateCompressionBarFill(Panel? trackPanel, Panel? fillPanel, double ratio)
    {
        if (trackPanel is null || fillPanel is null)
        {
            return;
        }

        var clampedRatio = Math.Clamp(ratio, 0D, 1D);
        var targetWidth = Math.Clamp(
            (int)Math.Round(trackPanel.ClientSize.Width * clampedRatio, MidpointRounding.AwayFromZero),
            0,
            trackPanel.ClientSize.Width);

        fillPanel.Width = targetWidth;
        fillPanel.Visible = targetWidth > 0;
        ApplyRoundedRegion(trackPanel, 5);
        ApplyRoundedRegion(fillPanel, 5);
    }

    private static string DescribeVideoQualityPreset(VideoQualityPreset selectedVideoQuality)
    {
        //== output shaping ======================================================
        var parts = new List<string>
        {
            $"{selectedVideoQuality.Name} (CRF {selectedVideoQuality.Crf})",
            $"{selectedVideoQuality.EncoderPreset} preset",
            $"{selectedVideoQuality.AudioBitrate} audio"
        };

        if (selectedVideoQuality.MaxHeight.HasValue)
        {
            parts.Add($"max {selectedVideoQuality.MaxHeight.Value}p");
        }

        return string.Join(", ", parts);
        //=========================================================================
    }

    private static string BuildConversionSizeSummary(string sourcePath, string outputPath)
    {
        //== output shaping ======================================================
        if (!File.Exists(sourcePath) || !File.Exists(outputPath))
        {
            return "Size comparison unavailable.";
        }

        var sourceSize = new FileInfo(sourcePath).Length;
        var outputSize = new FileInfo(outputPath).Length;
        var delta = outputSize - sourceSize;

        if (delta == 0)
        {
            return $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | Size unchanged.";
        }

        var percentChange = sourceSize > 0
            ? Math.Abs((double)delta / sourceSize) * 100
            : 0;

        if (delta < 0)
        {
            return $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | Saved {FormatFileSize(-delta)} ({percentChange:0.#}% smaller).";
        }

        return $"Source size: {FormatFileSize(sourceSize)} | Output size: {FormatFileSize(outputSize)} | Grew by {FormatFileSize(delta)} ({percentChange:0.#}% larger). Try a smaller preset.";
        //=========================================================================
    }

    private string? GetPreferredMediaPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentPreviewFilePath) && File.Exists(_currentPreviewFilePath))
        {
            return _currentPreviewFilePath;
        }

        if (!string.IsNullOrWhiteSpace(_lastDownloadedFilePath) && File.Exists(_lastDownloadedFilePath))
        {
            return _lastDownloadedFilePath;
        }

        return null;
    }

    private string NormalizeMediaPath(string mediaPath)
    {
        var trimmedPath = mediaPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(trimmedPath) || string.IsNullOrWhiteSpace(_currentOutputFolder))
        {
            return Path.GetFullPath(trimmedPath);
        }

        return Path.GetFullPath(Path.Combine(_currentOutputFolder, trimmedPath));
    }

    private HashSet<string> CaptureOutputFolderSnapshot(string outputFolder)
    {
        var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(outputFolder, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsPlayableMediaFile(filePath))
                {
                    continue;
                }

                snapshot.Add(Path.GetFullPath(filePath));
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Could not snapshot output folder: {ex.Message}");
        }

        return snapshot;
    }

    private string? ResolveCompletedMediaPath()
    {
        if (!string.IsNullOrWhiteSpace(_lastDownloadedFilePath))
        {
            var normalizedPath = NormalizeMediaPath(_lastDownloadedFilePath);
            if (File.Exists(normalizedPath) && IsPlayableMediaFile(normalizedPath))
            {
                return normalizedPath;
            }
        }

        if (string.IsNullOrWhiteSpace(_currentOutputFolder) || !Directory.Exists(_currentOutputFolder))
        {
            return null;
        }

        try
        {
            var candidates = Directory.EnumerateFiles(_currentOutputFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(IsPlayableMediaFile)
                .Select(filePath =>
                {
                    var fullPath = Path.GetFullPath(filePath);
                    var fileInfo = new FileInfo(fullPath);
                    return new
                    {
                        Path = fullPath,
                        fileInfo.LastWriteTimeUtc,
                        IsNew = !_outputFolderSnapshot.Contains(fullPath)
                    };
                })
                .OrderByDescending(candidate => candidate.IsNew)
                .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
                .ToList();

            var bestMatch = candidates.FirstOrDefault(candidate =>
                candidate.IsNew ||
                candidate.LastWriteTimeUtc >= _downloadStartedUtc.AddSeconds(-2));

            if (bestMatch is not null)
            {
                AppendLog($"Detected completed file: {bestMatch.Path}");
                return bestMatch.Path;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Could not detect completed media file: {ex.Message}");
        }

        return null;
    }

    private bool TryOpenMediaExternally(string mediaPath, bool showErrorDialog)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = mediaPath,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"Open externally failed: {ex.Message}");
            if (showErrorDialog)
            {
                MessageBox.Show(this, ex.Message, "Open File Error");
            }

            return false;
        }
    }

    private static bool IsPlayableMediaFile(string mediaPath)
    {
        return Path.GetExtension(mediaPath).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" or ".mov" or ".webm" or ".mkv" or
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" or
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff" => true,
            _ => false
        };
    }

    private static bool IsAudioOnlyMedia(string mediaPath)
    {
        return Path.GetExtension(mediaPath).ToLowerInvariant() switch
        {
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" => true,
            _ => false
        };
    }

    private static string BuildMediaPreviewHtml(
        string mediaUrl,
        string fileName,
        bool isAudioOnly,
        bool isStillImage)
    {
        var encodedFileName = WebUtility.HtmlEncode(fileName);
        var encodedMediaUrl = WebUtility.HtmlEncode(mediaUrl);
        var mediaTag = isAudioOnly
            ? $"""
               <audio id="player" controls autoplay preload="metadata">
                   <source src="{encodedMediaUrl}">
                   Your browser runtime could not load this audio file.
               </audio>
               """
            : isStillImage
                ? $"""
                   <img id="player" src="{encodedMediaUrl}" alt="{encodedFileName}">
                   """
                : $"""
               <video id="player" controls autoplay preload="metadata">
                   <source src="{encodedMediaUrl}">
                   Your browser runtime could not load this video file.
               </video>
               """;
        var cropOverlayMarkup = isAudioOnly || isStillImage
            ? string.Empty
            : """
              <div class="crop-overlay" id="cropOverlay">
                  <div class="crop-mask" id="cropMaskTop"></div>
                  <div class="crop-mask" id="cropMaskLeft"></div>
                  <div class="crop-mask" id="cropMaskRight"></div>
                  <div class="crop-mask" id="cropMaskBottom"></div>
                  <div class="crop-box" id="cropBox">
                      <div class="crop-grid crop-grid-v1"></div>
                      <div class="crop-grid crop-grid-v2"></div>
                      <div class="crop-grid crop-grid-h1"></div>
                      <div class="crop-grid crop-grid-h2"></div>
                      <div class="crop-handle crop-handle-tl" data-handle="tl"></div>
                      <div class="crop-handle crop-handle-tm" data-handle="tm"></div>
                      <div class="crop-handle crop-handle-tr" data-handle="tr"></div>
                      <div class="crop-handle crop-handle-ml" data-handle="ml"></div>
                      <div class="crop-handle crop-handle-mr" data-handle="mr"></div>
                      <div class="crop-handle crop-handle-bl" data-handle="bl"></div>
                      <div class="crop-handle crop-handle-bm" data-handle="bm"></div>
                      <div class="crop-handle crop-handle-br" data-handle="br"></div>
                      <div class="crop-badge" id="cropBadge">0 x 0</div>
                  </div>
              </div>
              """;

        return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>{{encodedFileName}}</title>
                    <style>
                        :root {
                            color-scheme: dark;
                            font-family: "Segoe UI", sans-serif;
                            scrollbar-color: #625b91 #111731;
                            scrollbar-width: thin;
                        }

                        *::-webkit-scrollbar {
                            width: 10px;
                            height: 10px;
                        }

                        *::-webkit-scrollbar-track {
                            background: #111731;
                            border-radius: 10px;
                            box-shadow: inset 2px 2px 4px rgba(5, 8, 20, 0.72);
                        }

                        *::-webkit-scrollbar-thumb {
                            background: #625b91;
                            border: 2px solid #111731;
                            border-radius: 10px;
                        }

                        *::-webkit-scrollbar-thumb:hover {
                            background: #7b6bb3;
                        }

                        body {
                            margin: 0;
                            min-height: 100vh;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            overflow: hidden;
                            background: #0d1228;
                            color: #f5f4ff;
                            position: relative;
                        }

                        .viewport {
                            width: 100vw;
                            height: 100vh;
                            box-sizing: border-box;
                            padding: 36px;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            position: relative;
                            z-index: 1;
                        }

                        .frame {
                            width: 100%;
                            height: 100%;
                            position: relative;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            overflow: hidden;
                            background: #171f43;
                            border: 1px solid #38436f;
                            border-radius: 28px;
                            box-shadow:
                                inset 1px 1px 0 rgba(119, 132, 184, 0.2),
                                0 14px 30px rgba(5, 8, 20, 0.42);
                        }

                        body.audio .frame {
                            background: #171f43;
                            box-sizing: border-box;
                        }

                        .stage {
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            width: 100%;
                            height: 100%;
                            position: relative;
                            padding: 34px;
                            box-sizing: border-box;
                        }

                        #player {
                            width: 100%;
                            height: 100%;
                            max-width: 100%;
                            max-height: 100%;
                            background: #000;
                            display: block;
                            object-fit: contain;
                            border-radius: 22px;
                            box-shadow: 0 10px 24px rgba(5, 8, 20, 0.42);
                        }

                        body.audio #player {
                            width: min(100%, 760px);
                            height: auto;
                            background: transparent;
                        }

                        .error {
                            position: absolute;
                            inset: 0;
                            display: none;
                            align-items: center;
                            justify-content: center;
                            padding: 32px;
                            text-align: center;
                            background: rgba(13, 18, 40, 0.94);
                            color: #f5f4ff;
                            font-size: 15px;
                            line-height: 1.5;
                        }

                        body.has-error .error {
                            display: flex;
                        }

                        body.has-error #player {
                            visibility: hidden;
                        }

                        .crop-overlay {
                            position: absolute;
                            inset: 0;
                            display: none;
                            pointer-events: auto;
                            touch-action: none;
                            user-select: none;
                            overflow: visible;
                        }

                        .crop-overlay.visible {
                            display: block;
                        }

                        .crop-mask {
                            position: absolute;
                            background: rgba(7, 10, 16, 0.54);
                            border: 1px solid rgba(255, 255, 255, 0.05);
                            box-sizing: border-box;
                            pointer-events: none;
                        }

                        .crop-box {
                            position: absolute;
                            border: 1.5px solid rgba(235, 241, 255, 0.9);
                            background: rgba(130, 88, 244, 0.14);
                            box-shadow: 0 0 0 1px rgba(191, 170, 255, 0.22);
                            box-sizing: border-box;
                            cursor: move;
                        }

                        .crop-grid {
                            position: absolute;
                            background: rgba(255, 255, 255, 0.32);
                        }

                        .crop-grid-v1,
                        .crop-grid-v2 {
                            top: 0;
                            bottom: 0;
                            width: 1px;
                        }

                        .crop-grid-v1 {
                            left: 33.333%;
                        }

                        .crop-grid-v2 {
                            left: 66.666%;
                        }

                        .crop-grid-h1,
                        .crop-grid-h2 {
                            left: 0;
                            right: 0;
                            height: 1px;
                        }

                        .crop-grid-h1 {
                            top: 33.333%;
                        }

                        .crop-grid-h2 {
                            top: 66.666%;
                        }

                        .crop-handle {
                            position: absolute;
                            width: 12px;
                            height: 12px;
                            background: #8258f4;
                            border: 2px solid rgba(241, 248, 255, 0.92);
                            border-radius: 4px;
                            box-sizing: border-box;
                            box-shadow: 0 6px 18px rgba(0, 0, 0, 0.38);
                        }

                        .crop-handle[data-handle="tl"],
                        .crop-handle[data-handle="br"] {
                            cursor: nwse-resize;
                        }

                        .crop-handle[data-handle="tr"],
                        .crop-handle[data-handle="bl"] {
                            cursor: nesw-resize;
                        }

                        .crop-handle[data-handle="tm"],
                        .crop-handle[data-handle="bm"] {
                            cursor: ns-resize;
                        }

                        .crop-handle[data-handle="ml"],
                        .crop-handle[data-handle="mr"] {
                            cursor: ew-resize;
                        }

                        .crop-handle-tl { left: -6px; top: -6px; }
                        .crop-handle-tm { left: calc(50% - 6px); top: -6px; }
                        .crop-handle-tr { right: -6px; top: -6px; }
                        .crop-handle-ml { left: -6px; top: calc(50% - 6px); }
                        .crop-handle-mr { right: -6px; top: calc(50% - 6px); }
                        .crop-handle-bl { left: -6px; bottom: -6px; }
                        .crop-handle-bm { left: calc(50% - 6px); bottom: -6px; }
                        .crop-handle-br { right: -6px; bottom: -6px; }

                        .crop-badge {
                            position: absolute;
                            left: 50%;
                            bottom: -36px;
                            transform: translateX(-50%);
                            padding: 6px 10px;
                            border-radius: 10px;
                            background: rgba(23, 31, 67, 0.96);
                            border: 1px solid rgba(56, 67, 111, 0.94);
                            color: #f5f4ff;
                            font: 600 12px "Segoe UI", sans-serif;
                            letter-spacing: 0.02em;
                            white-space: nowrap;
                            box-shadow: 0 10px 22px rgba(5, 8, 20, 0.34);
                        }
                    </style>
                </head>
                <body class="{{(isAudioOnly ? "audio" : isStillImage ? "image" : "video")}}">
                    <div class="viewport">
                        <div class="frame">
                            <div class="stage">
                                {{mediaTag}}
                                {{cropOverlayMarkup}}
                            </div>
                            <div class="error" id="previewError">
                                {{encodedFileName}} could not be played in the embedded preview.<br><br>
                                Use Open externally for formats or codecs the browser runtime does not support.
                            </div>
                        </div>
                    </div>
                    <script>
                        const player = document.getElementById('player');
                        const stage = document.querySelector('.stage');
                        const cropOverlay = document.getElementById('cropOverlay');
                        const cropBox = document.getElementById('cropBox');
                        const cropBadge = document.getElementById('cropBadge');
                        const cropMaskTop = document.getElementById('cropMaskTop');
                        const cropMaskLeft = document.getElementById('cropMaskLeft');
                        const cropMaskRight = document.getElementById('cropMaskRight');
                        const cropMaskBottom = document.getElementById('cropMaskBottom');
                        const showError = () => document.body.classList.add('has-error');

                        //== crop state ==========================================================
                        const cropState = {
                            enabled: false,
                            x: 0,
                            y: 0,
                            width: 0,
                            height: 0,
                            sourceWidth: 0,
                            sourceHeight: 0,
                            rotation: 0
                        };
                        const minimumCropSize = 24;
                        const dragState = {
                            active: false,
                            pointerId: -1,
                            mode: '',
                            handle: '',
                            originClientX: 0,
                            originClientY: 0,
                            originSelection: null,
                            overlayRect: null
                        };
                        const clamp = (value, minimum, maximum) => Math.min(Math.max(value, minimum), maximum);
                        //============================================================================

                        //== geometry helpers =====================================================
                        const getSourceBounds = () => ({
                            width: Math.max(Number(cropState.sourceWidth) || 0, 0),
                            height: Math.max(Number(cropState.sourceHeight) || 0, 0)
                        });

                        const copySelection = () => ({
                            x: Number(cropState.x) || 0,
                            y: Number(cropState.y) || 0,
                            width: Number(cropState.width) || 0,
                            height: Number(cropState.height) || 0
                        });

                        const normalizeSelection = selection => {
                            const sourceBounds = getSourceBounds();
                            if (sourceBounds.width <= 0 || sourceBounds.height <= 0) {
                                return { x: 0, y: 0, width: 0, height: 0 };
                            }

                            const minimumWidth = Math.min(minimumCropSize, sourceBounds.width);
                            const minimumHeight = Math.min(minimumCropSize, sourceBounds.height);
                            const width = clamp(Number(selection.width) || sourceBounds.width, minimumWidth, sourceBounds.width);
                            const height = clamp(Number(selection.height) || sourceBounds.height, minimumHeight, sourceBounds.height);
                            const x = clamp(Number(selection.x) || 0, 0, Math.max(sourceBounds.width - width, 0));
                            const y = clamp(Number(selection.y) || 0, 0, Math.max(sourceBounds.height - height, 0));
                            return { x, y, width, height };
                        };

                        const getDisplayedMediaRect = () => {
                            const playerRect = player.getBoundingClientRect();
                            const intrinsicWidth = player.videoWidth || player.clientWidth || 1;
                            const intrinsicHeight = player.videoHeight || player.clientHeight || 1;
                            const scale = Math.min(playerRect.width / intrinsicWidth, playerRect.height / intrinsicHeight);
                            const width = intrinsicWidth * scale;
                            const height = intrinsicHeight * scale;
                            const left = playerRect.left + ((playerRect.width - width) / 2);
                            const top = playerRect.top + ((playerRect.height - height) / 2);
                            return { left, top, width, height };
                        };
                        //============================================================================

                        //== overlay rendering ====================================================
                        const applyCropMaskStyles = (cropLeft, cropTop, cropWidth, cropHeight, overlayWidth, overlayHeight) => {
                            if (!cropMaskTop || !cropMaskLeft || !cropMaskRight || !cropMaskBottom) {
                                return;
                            }

                            cropMaskTop.style.left = '0px';
                            cropMaskTop.style.top = '0px';
                            cropMaskTop.style.width = `${overlayWidth}px`;
                            cropMaskTop.style.height = `${cropTop}px`;

                            cropMaskLeft.style.left = '0px';
                            cropMaskLeft.style.top = `${cropTop}px`;
                            cropMaskLeft.style.width = `${cropLeft}px`;
                            cropMaskLeft.style.height = `${cropHeight}px`;

                            cropMaskRight.style.left = `${cropLeft + cropWidth}px`;
                            cropMaskRight.style.top = `${cropTop}px`;
                            cropMaskRight.style.width = `${Math.max(overlayWidth - cropLeft - cropWidth, 0)}px`;
                            cropMaskRight.style.height = `${cropHeight}px`;

                            cropMaskBottom.style.left = '0px';
                            cropMaskBottom.style.top = `${cropTop + cropHeight}px`;
                            cropMaskBottom.style.width = `${overlayWidth}px`;
                            cropMaskBottom.style.height = `${Math.max(overlayHeight - cropTop - cropHeight, 0)}px`;
                        };

                        const renderCropOverlay = () => {
                            if (!cropOverlay || !cropBox || !stage) {
                                return true;
                            }

                            if (document.body.classList.contains('audio') ||
                                !cropState.enabled ||
                                cropState.sourceWidth <= 0 ||
                                cropState.sourceHeight <= 0) {
                                cropOverlay.classList.remove('visible');
                                return true;
                            }

                            const stageRect = stage.getBoundingClientRect();
                            const mediaRect = getDisplayedMediaRect();
                            const overlayLeft = mediaRect.left - stageRect.left;
                            const overlayTop = mediaRect.top - stageRect.top;
                            const overlayWidth = mediaRect.width;
                            const overlayHeight = mediaRect.height;
                            if (overlayWidth <= 0 || overlayHeight <= 0) {
                                cropOverlay.classList.remove('visible');
                                return true;
                            }

                            const cropLeft = clamp((cropState.x / cropState.sourceWidth) * overlayWidth, 0, overlayWidth);
                            const cropTop = clamp((cropState.y / cropState.sourceHeight) * overlayHeight, 0, overlayHeight);
                            const cropWidth = clamp((cropState.width / cropState.sourceWidth) * overlayWidth, 2, overlayWidth);
                            const cropHeight = clamp((cropState.height / cropState.sourceHeight) * overlayHeight, 2, overlayHeight);

                            cropOverlay.style.left = `${overlayLeft}px`;
                            cropOverlay.style.top = `${overlayTop}px`;
                            cropOverlay.style.width = `${overlayWidth}px`;
                            cropOverlay.style.height = `${overlayHeight}px`;

                            cropBox.style.left = `${cropLeft}px`;
                            cropBox.style.top = `${cropTop}px`;
                            cropBox.style.width = `${cropWidth}px`;
                            cropBox.style.height = `${cropHeight}px`;

                            applyCropMaskStyles(cropLeft, cropTop, cropWidth, cropHeight, overlayWidth, overlayHeight);

                            if (cropBadge) {
                                cropBadge.textContent = `${Math.round(cropState.width)} x ${Math.round(cropState.height)}`;
                            }

                            cropOverlay.classList.add('visible');
                            return true;
                        };

                        //== overlay drag interactions ============================================
                        const postCropSelectionToHost = () => {
                            if (!window.chrome || !window.chrome.webview) {
                                return;
                            }

                            window.chrome.webview.postMessage({
                                type: 'cropSelectionChanged',
                                x: cropState.x,
                                y: cropState.y,
                                width: cropState.width,
                                height: cropState.height
                            });
                        };

                        const beginCropDrag = (event, mode, handle) => {
                            if (!cropOverlay || !cropState.enabled) {
                                return;
                            }

                            dragState.active = true;
                            dragState.pointerId = event.pointerId;
                            dragState.mode = mode;
                            dragState.handle = handle;
                            dragState.originClientX = event.clientX;
                            dragState.originClientY = event.clientY;
                            dragState.originSelection = copySelection();
                            dragState.overlayRect = cropOverlay.getBoundingClientRect();
                            cropOverlay.setPointerCapture?.(event.pointerId);
                            event.preventDefault();
                        };

                        const resizeSelection = (selection, handle, deltaX, deltaY) => {
                            const sourceBounds = getSourceBounds();
                            const minimumWidth = Math.min(minimumCropSize, sourceBounds.width);
                            const minimumHeight = Math.min(minimumCropSize, sourceBounds.height);
                            let left = selection.x;
                            let top = selection.y;
                            let right = selection.x + selection.width;
                            let bottom = selection.y + selection.height;

                            if (handle.includes('l')) {
                                left = clamp(left + deltaX, 0, right - minimumWidth);
                            }

                            if (handle.includes('r')) {
                                right = clamp(right + deltaX, left + minimumWidth, sourceBounds.width);
                            }

                            if (handle.includes('t')) {
                                top = clamp(top + deltaY, 0, bottom - minimumHeight);
                            }

                            if (handle.includes('b')) {
                                bottom = clamp(bottom + deltaY, top + minimumHeight, sourceBounds.height);
                            }

                            return normalizeSelection({
                                x: left,
                                y: top,
                                width: right - left,
                                height: bottom - top
                            });
                        };

                        const moveSelection = (selection, deltaX, deltaY) => {
                            const sourceBounds = getSourceBounds();
                            return normalizeSelection({
                                x: clamp(selection.x + deltaX, 0, Math.max(sourceBounds.width - selection.width, 0)),
                                y: clamp(selection.y + deltaY, 0, Math.max(sourceBounds.height - selection.height, 0)),
                                width: selection.width,
                                height: selection.height
                            });
                        };

                        const updateCropDrag = event => {
                            if (!dragState.active || event.pointerId !== dragState.pointerId || !dragState.overlayRect || !dragState.originSelection) {
                                return;
                            }

                            const sourceBounds = getSourceBounds();
                            if (sourceBounds.width <= 0 || sourceBounds.height <= 0) {
                                return;
                            }

                            const deltaX = ((event.clientX - dragState.originClientX) / dragState.overlayRect.width) * sourceBounds.width;
                            const deltaY = ((event.clientY - dragState.originClientY) / dragState.overlayRect.height) * sourceBounds.height;
                            const nextSelection = dragState.mode === 'move'
                                ? moveSelection(dragState.originSelection, deltaX, deltaY)
                                : resizeSelection(dragState.originSelection, dragState.handle, deltaX, deltaY);

                            Object.assign(cropState, nextSelection);
                            renderCropOverlay();
                        };

                        const finishCropDrag = event => {
                            if (!dragState.active || event.pointerId !== dragState.pointerId) {
                                return;
                            }

                            cropOverlay?.releasePointerCapture?.(dragState.pointerId);
                            dragState.active = false;
                            dragState.pointerId = -1;
                            dragState.mode = '';
                            dragState.handle = '';
                            dragState.originSelection = null;
                            dragState.overlayRect = null;
                            postCropSelectionToHost();
                        };

                        cropOverlay?.addEventListener('pointerdown', event => {
                            if (event.button !== 0 || document.body.classList.contains('audio') || !cropState.enabled) {
                                return;
                            }

                            const target = event.target instanceof Element ? event.target : null;
                            if (!target) {
                                return;
                            }

                            const handleElement = target.closest('.crop-handle');
                            if (handleElement instanceof HTMLElement)
                            {
                                beginCropDrag(event, 'resize', handleElement.dataset.handle || '');
                                return;
                            }

                            if (cropBox && cropBox.contains(target)) {
                                beginCropDrag(event, 'move', '');
                            }
                        });

                        cropOverlay?.addEventListener('pointermove', updateCropDrag);
                        cropOverlay?.addEventListener('pointerup', finishCropDrag);
                        cropOverlay?.addEventListener('pointercancel', finishCropDrag);
                        //============================================================================

                        window.cropOverlayApi = {
                            setState(state) {
                                Object.assign(cropState, state || {});
                                const normalizedSelection = normalizeSelection(cropState);
                                cropState.x = normalizedSelection.x;
                                cropState.y = normalizedSelection.y;
                                cropState.width = normalizedSelection.width;
                                cropState.height = normalizedSelection.height;
                                return renderCropOverlay();
                            }
                        };

                        //== preview event wiring ==================================================
                        player.addEventListener('error', showError);
                        player.addEventListener('stalled', () => {
                            if (player.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) {
                                showError();
                            }
                        });
                        player.addEventListener('loadedmetadata', renderCropOverlay);
                        player.addEventListener('loadeddata', renderCropOverlay);
                        window.addEventListener('resize', renderCropOverlay);
                        if (window.ResizeObserver) {
                            const resizeObserver = new ResizeObserver(renderCropOverlay);
                            resizeObserver.observe(player);
                            resizeObserver.observe(stage);
                        }
                        renderCropOverlay();
                        //============================================================================
                    </script>
                </body>
                </html>
                """;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private static bool LooksLikeYouTubeUrl(string url)
    {
        return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryParsePercent(string? percentText)
    {
        if (string.IsNullOrWhiteSpace(percentText))
        {
            return null;
        }

        var cleaned = percentText.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return (int)Math.Round(value);
        }

        return null;
    }

    private static string? ResolveToolPath(
        string executableName,
        out IReadOnlyList<string> searchedPaths,
        params string[] relativeCandidates)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                {
                    candidates.Add(fullPath);
                }
            }
            catch
            {
                // Ignore malformed candidate paths.
            }
        }

        foreach (var baseDirectory in EnumerateSearchRoots())
        {
            foreach (var relativePath in relativeCandidates)
            {
                AddCandidate(Path.Combine(baseDirectory, relativePath));
            }
        }

        foreach (var absolutePath in candidates)
        {
            if (File.Exists(absolutePath))
            {
                searchedPaths = candidates;
                return absolutePath;
            }
        }

        foreach (var relativePath in relativeCandidates)
        {
            AddCandidate(relativePath);
        }

        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(pathEntry.Trim(), executableName);
                AddCandidate(candidate);
                if (File.Exists(candidate))
                {
                    searchedPaths = candidates;
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        searchedPaths = candidates;
        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                {
                    roots.Add(fullPath);
                }
            }
            catch
            {
                // Ignore malformed search roots.
            }
        }

        AddRoot(AppContext.BaseDirectory);
        AddRoot(Environment.CurrentDirectory);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && directory is not null; i++)
        {
            AddRoot(directory.FullName);
            directory = directory.Parent;
        }

        return roots;
    }
}
