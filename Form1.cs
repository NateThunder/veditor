using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace VeditorWindow;

public partial class Form1 : Form
{
    private const string PreviewHostName = "preview.media";
    private static readonly Color AppBackgroundColor = Color.FromArgb(243, 246, 251);
    private static readonly Color CardBackgroundColor = Color.White;
    private static readonly Color CardBorderColor = Color.FromArgb(225, 231, 240);
    private static readonly Color PrimaryTextColor = Color.FromArgb(25, 34, 52);
    private static readonly Color SecondaryTextColor = Color.FromArgb(110, 122, 145);
    private static readonly Color AccentColor = Color.FromArgb(70, 104, 255);
    private static readonly Color AccentSoftColor = Color.FromArgb(232, 238, 255);
    private static readonly Color StageBackgroundColor = Color.FromArgb(18, 23, 34);
    private static readonly Color StageSurfaceColor = Color.FromArgb(238, 242, 248);
    private static readonly Color LogBackgroundColor = Color.FromArgb(247, 249, 252);

    private enum AppOperation
    {
        None,
        Download,
        Convert
    }

    private enum WorkspacePage
    {
        Source,
        Audio,
        Video
    }

    //== compression profiles ==================================================
    private readonly record struct VideoQualityPreset(
        string Name,
        int Crf,
        string EncoderPreset,
        string AudioBitrate,
        int? MaxHeight,
        string HintText,
        bool WarnOfNoticeableLoss);

    private static readonly VideoQualityPreset[] VideoQualityPresets =
    [
        new("Best", 20, "slow", "160k", null, "Full resolution with the lightest compression.", false),
        new("High", 23, "slow", "160k", null, "Full resolution with a smaller H.264 encode.", false),
        new("Balanced", 26, "slow", "128k", null, "Smaller output using lower video and audio bitrates.", false),
        new("Smaller", 28, "slow", "128k", 1080, "Caps video at 1080p for more reliable size savings.", true),
        new("Smallest", 30, "slow", "96k", 720, "Caps video at 720p and lowers audio bitrate the most.", true)
    ];
    //=========================================================================

    private CancellationTokenSource? _downloadCts;
    private Process? _activeProcess;
    private AppOperation _activeOperation;
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
    private Panel? _workspacePageHost;
    private Panel? _workspacePageViewport;
    private readonly Dictionary<WorkspacePage, Panel> _workspacePagePanels = [];
    private readonly Dictionary<WorkspacePage, Button> _workspacePageButtons = [];

    public Form1()
    {
        InitializeComponent();
        ConfigureStudioLayout();

        txtOutputFolder.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "VeditorDownloads");
        lblStatus.Text = "Idle";
        Shown += Form1_Shown;
        webPreview.NavigationCompleted += webPreview_NavigationCompleted;
        SetUiBusy(AppOperation.None);
        UpdateVideoQualityUi();
    }

    private void ConfigureStudioLayout()
    {
        SuspendLayout();

        _workspacePageHost = null;
        _workspacePageViewport = null;
        _workspacePagePanels.Clear();
        _workspacePageButtons.Clear();
        _currentWorkspacePage = WorkspacePage.Source;

        BackColor = AppBackgroundColor;
        DoubleBuffered = true;
        ClientSize = new Size(1360, 820);
        MinimumSize = new Size(1180, 740);
        Padding = Padding.Empty;
        Text = "VeditorWindow Studio";

        lblStatusCaption.Text = "Status";
        lblUrl.Text = "Video URL";
        lblOutputFolder.Text = "Output folder";
        lblVideoQualityCaption.Text = "Video quality";
        lblVideoQualityScaleLeft.Text = "Higher quality";
        lblVideoQualityScaleRight.Text = "Smaller file";
        chkExtractAudio.Text = "Extract audio after download (MP3)";
        btnDownload.Text = "Download media";
        btnOpenMediaFile.Text = "Open media";
        btnPreviewLast.Text = "Open latest";
        btnOpenExternal.Text = "Open outside";
        btnConvertMp3.Text = "MP3";
        btnConvertWav.Text = "WAV";
        btnConvertM4a.Text = "M4A";
        btnConvertMp4.Text = "MP4";
        btnConvertMkv.Text = "MKV";
        btnConvertMov.Text = "MOV";

        StyleTextInput(txtUrl, "Paste a YouTube or direct video URL");
        StyleTextInput(txtOutputFolder, "Choose where downloaded files should go");
        StyleActionButton(btnDownload, primary: true);
        StyleActionButton(btnBrowseOutput, primary: false);
        StyleActionButton(btnOpenMediaFile, primary: false);
        StyleActionButton(btnPreviewLast, primary: false);
        StyleActionButton(btnOpenExternal, primary: false);
        StyleActionButton(btnConvertMp3, primary: false);
        StyleActionButton(btnConvertWav, primary: false);
        StyleActionButton(btnConvertM4a, primary: false);
        StyleActionButton(btnConvertMp4, primary: false);
        StyleActionButton(btnConvertMkv, primary: false);
        StyleActionButton(btnConvertMov, primary: false);

        lblUrl.AutoSize = true;
        lblOutputFolder.AutoSize = true;
        lblUrl.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblOutputFolder.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblUrl.ForeColor = SecondaryTextColor;
        lblOutputFolder.ForeColor = SecondaryTextColor;

        chkExtractAudio.AutoSize = true;
        chkExtractAudio.ForeColor = PrimaryTextColor;
        chkExtractAudio.BackColor = Color.Transparent;

        txtLog.BorderStyle = BorderStyle.None;
        txtLog.BackColor = LogBackgroundColor;
        txtLog.ForeColor = PrimaryTextColor;
        txtLog.Font = new Font("Consolas", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
        txtLog.WordWrap = false;

        lblPreviewState.BackColor = StageBackgroundColor;
        lblPreviewState.ForeColor = Color.WhiteSmoke;
        lblPreviewState.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewState.Padding = new Padding(32);
        lblPreviewState.TextAlign = ContentAlignment.MiddleCenter;

        webPreview.DefaultBackgroundColor = StageBackgroundColor;

        lblStatusCaption.ForeColor = SecondaryTextColor;
        lblStatus.ForeColor = PrimaryTextColor;
        lblStatus.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblStatusCaption.AutoSize = true;
        lblStatusCaption.Margin = Padding.Empty;
        lblStatus.AutoEllipsis = true;
        lblStatus.Margin = new Padding(10, 0, 0, 0);
        lblFileInfo.ForeColor = SecondaryTextColor;
        progressDownload.Height = 10;
        progressDownload.Minimum = 0;
        progressDownload.Maximum = 100;
        progressDownload.Style = ProgressBarStyle.Marquee;
        progressDownload.MarqueeAnimationSpeed = 30;
        progressDownload.Visible = false;

        tableFooter.BackColor = Color.Transparent;
        tableFooter.ColumnStyles[0].Width = 52F;
        tableFooter.ColumnStyles[2].Width = 260F;

        Controls.Clear();

        var shellLayout = new TableLayoutPanel
        {
            BackColor = AppBackgroundColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(14),
            RowCount = 2
        };
        shellLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shellLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var workspaceLayout = new TableLayoutPanel
        {
            BackColor = AppBackgroundColor,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var topBar = BuildTopBar();
        var sidebar = BuildSidebar();
        var editorArea = BuildEditorArea();
        var navRail = BuildNavigationRail(
            (WorkspacePage.Source, "Source"),
            (WorkspacePage.Audio, "Audio"),
            (WorkspacePage.Video, "Video"));

        workspaceLayout.Controls.Add(navRail, 0, 0);
        workspaceLayout.Controls.Add(sidebar, 1, 0);
        workspaceLayout.Controls.Add(editorArea, 2, 0);

        shellLayout.Controls.Add(topBar, 0, 0);
        shellLayout.Controls.Add(workspaceLayout, 0, 1);

        Controls.Add(shellLayout);
        ShowWorkspacePage(WorkspacePage.Source);

        ResumeLayout(performLayout: true);
    }

    private Control BuildTopBar()
    {
        var topBar = CreateCard();
        topBar.AutoSize = true;
        topBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        topBar.Dock = DockStyle.Top;
        topBar.Margin = new Padding(0, 0, 0, 14);
        topBar.MinimumSize = new Size(0, 86);
        topBar.Padding = new Padding(18, 12, 18, 12);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleStack = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 2
        };
        titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "VeditorWindow"
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 2, 0, 0),
            MaximumSize = new Size(720, 0),
            Text = "Light workspace for download, preview, and conversion"
        };

        titleStack.Controls.Add(titleLabel, 0, 0);
        titleStack.Controls.Add(subtitleLabel, 0, 1);

        var actionBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2),
            WrapContents = false
        };

        var modeChip = new Label
        {
            AutoSize = true,
            BackColor = AccentSoftColor,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 8, 10, 8),
            Text = "Light studio"
        };

        btnOpenMediaFile.Margin = new Padding(0, 0, 8, 0);
        btnPreviewLast.Margin = new Padding(0, 0, 8, 0);
        btnOpenExternal.Margin = Padding.Empty;

        actionBar.Controls.Add(modeChip);
        actionBar.Controls.Add(btnOpenMediaFile);
        actionBar.Controls.Add(btnPreviewLast);
        actionBar.Controls.Add(btnOpenExternal);

        layout.Controls.Add(titleStack, 0, 0);
        layout.Controls.Add(actionBar, 1, 0);
        topBar.Controls.Add(layout);
        return topBar;
    }

    private Control BuildNavigationRail(params (WorkspacePage Page, string Label)[] items)
    {
        var rail = CreateCard();
        rail.Dock = DockStyle.Fill;
        rail.Margin = Padding.Empty;
        rail.Padding = new Padding(10, 12, 10, 12);

        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            WrapContents = false
        };

        var brand = new Label
        {
            AutoSize = false,
            BackColor = AccentSoftColor,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = AccentColor,
            Margin = new Padding(0, 0, 0, 18),
            Size = new Size(72, 56),
            Text = "V",
            TextAlign = ContentAlignment.MiddleCenter
        };

        stack.Controls.Add(brand);

        foreach (var item in items)
        {
            var button = CreateRailButton(item.Label, () => ShowWorkspacePage(item.Page));
            _workspacePageButtons[item.Page] = button;
            ApplyRailButtonState(button, item.Page == _currentWorkspacePage);
            stack.Controls.Add(button);
        }

        rail.Controls.Add(stack);
        return rail;
    }

    private Control BuildSidebar()
    {
        var sidebar = CreateCard();
        sidebar.Dock = DockStyle.Fill;
        sidebar.Margin = new Padding(14, 0, 14, 0);
        sidebar.Padding = Padding.Empty;

        var headerPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(18, 18, 18, 14),
            RowCount = 2
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = PrimaryTextColor,
            Margin = Padding.Empty,
            Text = "Workspace tools"
        };

        var headerSubtitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 4, 0, 0),
            MaximumSize = new Size(300, 0),
            Text = "Choose a tool page while keeping preview on the main stage."
        };

        headerPanel.Controls.Add(headerTitle, 0, 0);
        headerPanel.Controls.Add(headerSubtitle, 0, 1);

        var viewport = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 10, 18)
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
        var audioPage = BuildWorkspacePage(BuildAudioConvertCard());
        var videoPage = BuildWorkspacePage(BuildVideoConvertCard());

        _workspacePagePanels[WorkspacePage.Source] = sourcePage;
        _workspacePagePanels[WorkspacePage.Audio] = audioPage;
        _workspacePagePanels[WorkspacePage.Video] = videoPage;

        pageHost.Controls.Add(videoPage);
        pageHost.Controls.Add(audioPage);
        pageHost.Controls.Add(sourcePage);

        viewport.Controls.Add(pageHost);

        sidebar.Controls.Add(viewport);
        sidebar.Controls.Add(headerPanel);
        return sidebar;
    }

    private static Panel BuildWorkspacePage(params Control[] cards)
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

        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            card.Dock = DockStyle.Top;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(card, 0, index);
        }

        page.Controls.Add(stack);
        return page;
    }

    private Panel BuildSourceCard()
    {
        var card = CreateCard();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(16);

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

        layout.Controls.Add(CreateSectionTitle("Source video"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Paste a link and prepare the workspace before downloading."), 0, 1);
        layout.Controls.Add(lblUrl, 0, 2);
        layout.Controls.Add(txtUrl, 0, 3);

        txtUrl.Dock = DockStyle.Top;
        txtUrl.Margin = new Padding(0, 6, 0, 0);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildOutputCard()
    {
        var card = CreateCard();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(16);

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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            RowCount = 1
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        folderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        txtOutputFolder.Dock = DockStyle.Fill;
        txtOutputFolder.Margin = Padding.Empty;
        btnBrowseOutput.Dock = DockStyle.Fill;
        btnBrowseOutput.Margin = new Padding(10, 0, 0, 0);

        folderRow.Controls.Add(txtOutputFolder, 0, 0);
        folderRow.Controls.Add(btnBrowseOutput, 1, 0);

        layout.Controls.Add(CreateSectionTitle("Storage"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Choose where new media files and conversions are written."), 0, 1);
        layout.Controls.Add(lblOutputFolder, 0, 2);
        layout.Controls.Add(folderRow, 0, 3);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildCaptureCard()
    {
        var card = CreateCard();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(16);

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

        btnDownload.Dock = DockStyle.Top;
        btnDownload.Margin = new Padding(0, 10, 0, 0);

        layout.Controls.Add(CreateSectionTitle("Capture"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Download the selected media into the current workspace folder."), 0, 1);
        layout.Controls.Add(chkExtractAudio, 0, 2);
        layout.Controls.Add(btnDownload, 0, 3);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildAudioConvertCard()
    {
        var card = CreateCard();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Margin = new Padding(0, 0, 0, 12);
        card.Padding = new Padding(16);

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

        layout.Controls.Add(CreateSectionTitle("Audio exports"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Create lighter audio versions from the loaded media file."), 0, 1);
        layout.Controls.Add(CreateButtonGrid(btnConvertMp3, btnConvertWav, btnConvertM4a), 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildVideoConvertCard()
    {
        var card = CreateCard();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Margin = new Padding(0);
        card.Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var scaleRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            RowCount = 1
        };
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        scaleRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lblVideoQualityCaption.ForeColor = SecondaryTextColor;
        lblVideoQualityCaption.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblVideoQualityValue.ForeColor = PrimaryTextColor;
        lblVideoQualityHint.ForeColor = SecondaryTextColor;
        lblVideoQualityScaleLeft.ForeColor = SecondaryTextColor;
        lblVideoQualityScaleRight.ForeColor = SecondaryTextColor;

        trkVideoQuality.Dock = DockStyle.Top;
        trkVideoQuality.Margin = new Padding(0, 6, 0, 0);
        trkVideoQuality.BackColor = CardBackgroundColor;
        trkVideoQuality.AutoSize = false;
        trkVideoQuality.Height = 30;

        lblVideoQualityValue.Dock = DockStyle.Right;
        lblVideoQualityHint.Dock = DockStyle.Top;
        lblVideoQualityHint.Margin = new Padding(0, 6, 0, 0);
        lblVideoQualityHint.MaximumSize = new Size(0, 0);

        lblVideoQualityScaleLeft.Dock = DockStyle.Left;
        lblVideoQualityScaleRight.Dock = DockStyle.Right;

        scaleRow.Controls.Add(lblVideoQualityScaleLeft, 0, 0);
        scaleRow.Controls.Add(lblVideoQualityScaleRight, 1, 0);

        var qualityRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1
        };
        qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        qualityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        qualityRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        qualityRow.Controls.Add(lblVideoQualityCaption, 0, 0);
        qualityRow.Controls.Add(lblVideoQualityValue, 1, 0);

        layout.Controls.Add(CreateSectionTitle("Video exports"), 0, 0);
        layout.Controls.Add(CreateSectionSubtitle("Convert the current media into standard video formats."), 0, 1);
        layout.Controls.Add(CreateButtonGrid(btnConvertMp4, btnConvertMkv, btnConvertMov), 0, 2);
        layout.Controls.Add(qualityRow, 0, 3);
        layout.Controls.Add(trkVideoQuality, 0, 4);
        layout.Controls.Add(scaleRow, 0, 5);
        layout.Controls.Add(lblVideoQualityHint, 0, 6);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildEditorArea()
    {
        var editorLayout = new TableLayoutPanel
        {
            BackColor = AppBackgroundColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var stageCard = CreateCard();
        stageCard.Dock = DockStyle.Fill;
        stageCard.Margin = Padding.Empty;
        stageCard.Padding = Padding.Empty;

        var stageLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        stageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        ConfigurePreviewToolbar();

        var stageShell = new Panel
        {
            BackColor = StageSurfaceColor,
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 0, 22, 22)
        };

        var previewStage = new Panel
        {
            BackColor = StageBackgroundColor,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        webPreview.Dock = DockStyle.Fill;
        lblPreviewState.Dock = DockStyle.Fill;

        previewStage.Controls.Add(webPreview);
        previewStage.Controls.Add(lblPreviewState);
        stageShell.Controls.Add(previewStage);

        stageLayout.Controls.Add(panelPreviewToolbar, 0, 0);
        stageLayout.Controls.Add(stageShell, 0, 1);
        stageCard.Controls.Add(stageLayout);

        var activityCard = BuildActivityCard();
        editorLayout.Controls.Add(stageCard, 0, 0);
        editorLayout.Controls.Add(activityCard, 0, 1);

        return editorLayout;
    }

    private void ConfigurePreviewToolbar()
    {
        panelPreviewToolbar.Controls.Clear();
        panelPreviewToolbar.AutoSize = true;
        panelPreviewToolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        panelPreviewToolbar.Dock = DockStyle.Fill;
        panelPreviewToolbar.MinimumSize = new Size(0, 84);
        panelPreviewToolbar.Padding = new Padding(22, 16, 22, 14);
        panelPreviewToolbar.BackColor = CardBackgroundColor;

        lblPreviewCaption.AutoSize = true;
        lblPreviewCaption.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewCaption.ForeColor = SecondaryTextColor;
        lblPreviewCaption.Margin = Padding.Empty;
        lblPreviewCaption.Text = "Current media source";

        lblPreviewPath.AutoEllipsis = false;
        lblPreviewPath.AutoSize = true;
        lblPreviewPath.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Regular, GraphicsUnit.Point);
        lblPreviewPath.ForeColor = PrimaryTextColor;
        lblPreviewPath.Margin = new Padding(0, 4, 0, 0);
        lblPreviewPath.MaximumSize = new Size(0, 0);
        lblPreviewPath.TextAlign = ContentAlignment.MiddleLeft;

        lblFileInfo.AutoSize = true;
        lblFileInfo.AutoEllipsis = false;
        lblFileInfo.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblFileInfo.ForeColor = SecondaryTextColor;
        lblFileInfo.Margin = Padding.Empty;
        lblFileInfo.TextAlign = ContentAlignment.MiddleRight;

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
        headerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerRow.Controls.Add(lblPreviewCaption, 0, 0);
        headerRow.Controls.Add(lblFileInfo, 1, 0);

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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(headerRow, 0, 0);
        layout.Controls.Add(lblPreviewPath, 0, 1);

        void UpdatePreviewPathWidth()
        {
            var availableWidth = Math.Max(0, layout.ClientSize.Width);
            lblPreviewPath.MaximumSize = new Size(availableWidth, 0);
        }

        layout.SizeChanged += (_, _) => UpdatePreviewPathWidth();
        panelPreviewToolbar.SizeChanged += (_, _) => UpdatePreviewPathWidth();

        panelPreviewToolbar.Controls.Add(layout);
        UpdatePreviewPathWidth();
    }

    private Control BuildActivityCard()
    {
        var card = CreateCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 14, 0, 0);
        card.Padding = new Padding(18, 14, 18, 16);
        card.MinimumSize = new Size(0, 82);

        lblStatusCaption.Dock = DockStyle.None;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        progressDownload.Dock = DockStyle.Top;
        progressDownload.Margin = new Padding(0, 10, 0, 0);

        var statusRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusRow.Controls.Add(lblStatusCaption, 0, 0);
        statusRow.Controls.Add(lblStatus, 1, 0);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(statusRow, 0, 0);
        layout.Controls.Add(progressDownload, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private static Panel CreateCard()
    {
        var panel = new Panel
        {
            BackColor = CardBackgroundColor
        };

        panel.Paint += (_, e) =>
        {
            var bounds = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using var pen = new Pen(CardBorderColor);
            e.Graphics.DrawRectangle(pen, bounds);
        };

        return panel;
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point),
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
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 4, 0, 10),
            MaximumSize = new Size(288, 0),
            Text = text
        };
    }

    private void ShowWorkspacePage(WorkspacePage page)
    {
        _currentWorkspacePage = page;

        _workspacePageHost?.SuspendLayout();

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

        if (_workspacePageViewport is not null)
        {
            _workspacePageViewport.AutoScrollPosition = new Point(0, 0);
        }

        _workspacePageHost?.ResumeLayout(performLayout: true);
    }

    private Button CreateRailButton(string text, Action onClick)
    {
        var button = new Button
        {
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 0, 0, 10),
            Size = new Size(72, 42),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false
        };

        button.FlatAppearance.BorderColor = CardBorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = AccentSoftColor;
        button.FlatAppearance.MouseOverBackColor = AccentSoftColor;
        button.Click += (_, _) => onClick();

        return button;
    }

    private static void ApplyRailButtonState(Button button, bool selected)
    {
        button.BackColor = selected ? AccentSoftColor : CardBackgroundColor;
        button.ForeColor = selected ? AccentColor : SecondaryTextColor;
        button.FlatAppearance.BorderColor = selected ? AccentColor : CardBorderColor;
    }

    private static TableLayoutPanel CreateButtonGrid(params Button[] buttons)
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = buttons.Length,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 0),
            RowCount = 1
        };

        for (var index = 0; index < buttons.Length; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / buttons.Length));
            buttons[index].Dock = DockStyle.Fill;
            buttons[index].Margin = index == buttons.Length - 1
                ? Padding.Empty
                : new Padding(0, 0, 8, 0);
            layout.Controls.Add(buttons[index], index, 0);
        }

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static void StyleActionButton(Button button, bool primary)
    {
        button.AutoSize = false;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
        button.Height = 38;
        button.UseVisualStyleBackColor = false;

        void ApplyPalette()
        {
            if (primary)
            {
                button.BackColor = button.Enabled ? AccentColor : CardBorderColor;
                button.ForeColor = button.Enabled ? Color.White : SecondaryTextColor;
                button.FlatAppearance.BorderColor = button.Enabled ? AccentColor : CardBorderColor;
            }
            else
            {
                button.BackColor = button.Enabled ? CardBackgroundColor : LogBackgroundColor;
                button.ForeColor = button.Enabled ? PrimaryTextColor : SecondaryTextColor;
                button.FlatAppearance.BorderColor = CardBorderColor;
            }
        }

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(59, 92, 238) : AccentSoftColor;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(54, 85, 224) : AccentSoftColor;
        button.EnabledChanged += (_, _) => ApplyPalette();
        ApplyPalette();
    }

    private static void StyleTextInput(TextBox textBox, string placeholder)
    {
        textBox.BackColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        textBox.ForeColor = PrimaryTextColor;
        textBox.PlaceholderText = placeholder;
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

    private async void btnConvertMp3_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("MP3", "mp3", requiresVideo: false);
    }

    private async void btnConvertWav_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("WAV", "wav", requiresVideo: false);
    }

    private async void btnConvertM4a_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("M4A", "m4a", requiresVideo: false);
    }

    private async void btnConvertMp4_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("MP4", "mp4", requiresVideo: true);
    }

    private async void btnConvertMkv_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("MKV", "mkv", requiresVideo: true);
    }

    private async void btnConvertMov_Click(object sender, EventArgs e)
    {
        await StartConversionAsync("MOV", "mov", requiresVideo: true);
    }

    private async void Form1_Shown(object? sender, EventArgs e)
    {
        await EnsurePreviewReadyAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_downloadCts is not null)
        {
            _downloadCts.Cancel();
        }

        try
        {
            if (_activeOperation == AppOperation.Convert &&
                _activeProcess is not null &&
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

    private async Task StartDownloadAsync()
    {
        var url = txtUrl.Text.Trim();
        var outputFolder = txtOutputFolder.Text.Trim();

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

        if (chkExtractAudio.Checked && ffmpegPath is null)
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

        SetUiBusy(AppOperation.Download);
        UpdateStatus("Starting download...");

        _downloadCts = new CancellationTokenSource();

        try
        {
            var startInfo = BuildStartInfo(
                ytDlpPath,
                ffmpegPath,
                denoPath,
                url,
                outputFolder,
                chkExtractAudio.Checked);

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
            }
            else if (process.ExitCode == 0)
            {
                SetProgressValue(100);
                var completedMediaPath = ResolveCompletedMediaPath();
                if (!string.IsNullOrWhiteSpace(completedMediaPath))
                {
                    completedMediaPath = NormalizeMediaPath(completedMediaPath);
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
            }
            else
            {
                UpdateStatus($"Failed (exit code {process.ExitCode})");
                AppendLog($"yt-dlp exited with code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Error");
            AppendLog(ex.Message);
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
        txtLog.AppendText(Environment.NewLine);
        AppendLog($"ffmpeg: {ffmpegPath}");
        AppendLog($"Converting to {formatLabel}: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)}");
        if (requiresVideo)
        {
            var selectedVideoQuality = GetSelectedVideoQualityPreset();
            AppendLog($"Compression profile: {DescribeVideoQualityPreset(selectedVideoQuality)}");
        }
        //=========================================================================

        SetUiBusy(AppOperation.Convert);
        UpdateStatus($"Converting to {formatLabel}...");

        try
        {
            //== external process =================================================
            var startInfo = BuildConversionStartInfo(ffmpegPath, sourcePath, outputPath, targetExtension);
            using var process = new Process { StartInfo = startInfo };
            _activeProcess = process;

            process.Start();

            var outputTask = ReadLinesAsync(process.StandardOutput, AppendLog);
            var errorTask = ReadLinesAsync(process.StandardError, AppendLog);

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            //=========================================================================

            //== output handling ===================================================
            if (process.ExitCode == 0)
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
            }
            else
            {
                UpdateStatus($"Conversion failed (exit code {process.ExitCode})");
                AppendLog($"ffmpeg exited with code {process.ExitCode}.");
            }
            //=========================================================================
        }
        catch (Exception ex)
        {
            //== error handling =====================================================
            UpdateStatus("Conversion error");
            AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "Conversion Error");
            //=========================================================================
        }
        finally
        {
            //== cleanup ============================================================
            _activeProcess = null;
            SetUiBusy(AppOperation.None);
            //=========================================================================
        }
    }

    private ProcessStartInfo BuildStartInfo(
        string ytDlpPath,
        string? ffmpegPath,
        string? denoPath,
        string url,
        string outputFolder,
        bool extractAudio)
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
            startInfo.ArgumentList.Add("mp3");
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
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);

        foreach (var argument in GetConversionArguments(targetExtension))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private IReadOnlyList<string> GetConversionArguments(string targetExtension)
    {
        //== output format selection ============================================
        return targetExtension.ToLowerInvariant() switch
        {
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "mp4" => BuildVideoConversionArguments(includeFastStart: true),
            "mkv" => BuildVideoConversionArguments(includeFastStart: false),
            "mov" => BuildVideoConversionArguments(includeFastStart: true),
            _ => throw new InvalidOperationException($"Unsupported conversion target: {targetExtension}")
        };
        //=========================================================================
    }

    private IReadOnlyList<string> BuildVideoConversionArguments(bool includeFastStart)
    {
        //== compression profile =================================================
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        var arguments = new List<string>
        {
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-c:v", "libx264",
            "-preset", selectedVideoQuality.EncoderPreset,
            "-crf", selectedVideoQuality.Crf.ToString(CultureInfo.InvariantCulture),
            "-c:a", "aac",
            "-b:a", selectedVideoQuality.AudioBitrate
        };
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
        if (includeFastStart)
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }
        //=========================================================================

        return arguments;
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
            btnDownload.Enabled = operation is AppOperation.None or AppOperation.Download;
            btnDownload.Text = operation == AppOperation.Download ? "Cancel download" : "Download media";
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
            Filter = "Media files|*.mp4;*.m4v;*.mov;*.webm;*.mp3;*.m4a;*.wav;*.aac;*.ogg|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetCurrentMediaSource(dialog.FileName);

        if (await EnsurePreviewReadyAsync())
        {
            await LoadPreviewAsync(dialog.FileName, switchToPreview: true);
        }
        else
        {
            FocusPreviewStage();
        }
    }

    private void btnOpenExternal_Click(object sender, EventArgs e)
    {
        var mediaPath = GetPreferredMediaPath();
        if (mediaPath is null)
        {
            MessageBox.Show(this, "There is no media file available to open.", "Open File");
            return;
        }

        TryOpenMediaExternally(mediaPath, showErrorDialog: true);
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
            ShowPreviewState("Download a file or open one to preview it here.");
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
            _currentPreviewFilePath = fullPath;
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
        _currentPreviewFilePath = NormalizeMediaPath(mediaFilePath);
        RefreshPreviewSummary();
        UpdatePreviewButtons();
    }

    private void webPreview_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            HidePreviewState();
            return;
        }

        AppendLog($"Preview navigation failed: {e.WebErrorStatus}");
        ShowPreviewState("This file could not be rendered in the embedded player.\r\n\r\nTry Open externally for formats the browser runtime does not support.");
        UpdatePreviewButtons();
    }

    private void ShowPreviewState(string message, bool keepPreviewVisible = false)
    {
        lblPreviewState.Text = message;
        lblPreviewState.Visible = true;
        webPreview.Visible = keepPreviewVisible;
        lblPreviewState.BringToFront();
    }

    private void HidePreviewState()
    {
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

        webPreview.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PreviewHostName,
            mediaDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        webPreview.CoreWebView2.NavigateToString(BuildMediaPreviewHtml(mediaUrl, fileName, isAudioOnly));
    }

    private void RefreshPreviewSummary()
    {
        var mediaPath = _currentPreviewFilePath ?? _lastDownloadedFilePath;
        lblPreviewPath.Text = string.IsNullOrWhiteSpace(mediaPath)
            ? "Nothing loaded yet."
            : mediaPath;
        lblFileInfo.Text = BuildFileInfoText(mediaPath);
    }

    private string BuildFileInfoText(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return "No file selected";
        }

        try
        {
            var normalizedPath = NormalizeMediaPath(mediaPath);
            var format = GetFileFormatLabel(normalizedPath);

            if (!File.Exists(normalizedPath))
            {
                return $"Format: {format} | Size: unavailable";
            }

            var fileInfo = new FileInfo(normalizedPath);
            return $"Format: {format} | Size: {FormatFileSize(fileInfo.Length)}";
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

    private void UpdatePreviewButtons()
    {
        var canInteract = _activeOperation == AppOperation.None;
        var hasLastDownload = !string.IsNullOrWhiteSpace(_lastDownloadedFilePath) &&
                              File.Exists(_lastDownloadedFilePath);

        btnOpenMediaFile.Enabled = canInteract;
        btnPreviewLast.Enabled = _previewReady && hasLastDownload && canInteract;
        btnOpenExternal.Enabled = GetPreferredMediaPath() is not null && canInteract;
        UpdateConversionButtons();
    }

    private void UpdateConversionButtons()
    {
        var canInteract = _activeOperation == AppOperation.None;
        var mediaPath = GetPreferredMediaPath();
        var hasMedia = mediaPath is not null;
        var hasVideo = hasMedia && !IsAudioOnlyMedia(mediaPath!);

        btnConvertMp3.Enabled = canInteract && hasMedia;
        btnConvertWav.Enabled = canInteract && hasMedia;
        btnConvertM4a.Enabled = canInteract && hasMedia;
        btnConvertMp4.Enabled = canInteract && hasVideo;
        btnConvertMkv.Enabled = canInteract && hasVideo;
        btnConvertMov.Enabled = canInteract && hasVideo;
    }

    private VideoQualityPreset GetSelectedVideoQualityPreset()
    {
        var presetIndex = Math.Clamp(trkVideoQuality.Value, 0, VideoQualityPresets.Length - 1);
        return VideoQualityPresets[presetIndex];
    }

    private void UpdateVideoQualityUi()
    {
        //== output shaping ======================================================
        var selectedVideoQuality = GetSelectedVideoQualityPreset();
        lblVideoQualityValue.Text = $"{selectedVideoQuality.Name} (CRF {selectedVideoQuality.Crf})";
        lblVideoQualityValue.ForeColor = selectedVideoQuality.WarnOfNoticeableLoss
            ? Color.FromArgb(176, 112, 0)
            : PrimaryTextColor;
        lblVideoQualityHint.ForeColor = selectedVideoQuality.WarnOfNoticeableLoss
            ? Color.FromArgb(176, 112, 0)
            : SecondaryTextColor;
        lblVideoQualityHint.Text = selectedVideoQuality.HintText;
        trkVideoQuality.Enabled = _activeOperation == AppOperation.None;
        //=========================================================================
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
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" => true,
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

    private static string BuildMediaPreviewHtml(string mediaUrl, string fileName, bool isAudioOnly)
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
            : $"""
               <video id="player" controls autoplay preload="metadata">
                   <source src="{encodedMediaUrl}">
                   Your browser runtime could not load this video file.
               </video>
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
                            color-scheme: light;
                            font-family: "Segoe UI", sans-serif;
                        }

                        body {
                            margin: 0;
                            min-height: 100vh;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            background:
                                radial-gradient(circle at top, #ffffff 0%, #eef2f8 52%, #e3eaf4 100%);
                            color: #1e293b;
                        }

                        .viewport {
                            width: 100vw;
                            height: 100vh;
                            box-sizing: border-box;
                            padding: 22px;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                        }

                        .frame {
                            width: 100%;
                            height: 100%;
                            position: relative;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            overflow: hidden;
                            background: #0f172a;
                            border-radius: 24px;
                            box-shadow: 0 24px 60px rgba(15, 23, 42, 0.18);
                        }

                        body.audio .frame {
                            background:
                                linear-gradient(180deg, rgba(255, 255, 255, 0.05), rgba(148, 163, 184, 0.08)),
                                #0f172a;
                            padding: 28px;
                            box-sizing: border-box;
                        }

                        .stage {
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            width: 100%;
                            height: 100%;
                            position: relative;
                        }

                        #player {
                            width: 100%;
                            height: 100%;
                            max-width: 100%;
                            max-height: 100%;
                            background: #000;
                            display: block;
                            object-fit: contain;
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
                            padding: 24px;
                            text-align: center;
                            background: rgba(15, 23, 42, 0.88);
                            color: #f8fafc;
                            font-size: 15px;
                            line-height: 1.5;
                        }

                        body.has-error .error {
                            display: flex;
                        }

                        body.has-error #player {
                            visibility: hidden;
                        }
                    </style>
                </head>
                <body class="{{(isAudioOnly ? "audio" : "video")}}">
                    <div class="viewport">
                        <div class="frame">
                            <div class="stage">
                                {{mediaTag}}
                            </div>
                            <div class="error" id="previewError">
                                {{encodedFileName}} could not be played in the embedded preview.<br><br>
                                Use Open externally for formats or codecs the browser runtime does not support.
                            </div>
                        </div>
                    </div>
                    <script>
                        const player = document.getElementById('player');
                        const showError = () => document.body.classList.add('has-error');

                        player.addEventListener('error', showError);
                        player.addEventListener('stalled', () => {
                            if (player.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) {
                                showError();
                            }
                        });
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
