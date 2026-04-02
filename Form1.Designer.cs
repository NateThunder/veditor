namespace VeditorWindow;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblUrl = new Label();
        txtUrl = new TextBox();
        lblOutputFolder = new Label();
        txtOutputFolder = new TextBox();
        btnBrowseOutput = new Button();
        chkExtractAudio = new CheckBox();
        btnDownload = new Button();
        grpAudioConvert = new GroupBox();
        btnConvertM4a = new Button();
        btnConvertWav = new Button();
        btnConvertMp3 = new Button();
        grpVideoConvert = new GroupBox();
        lblVideoQualityHint = new Label();
        lblVideoQualityScaleRight = new Label();
        lblVideoQualityScaleLeft = new Label();
        trkVideoQuality = new TrackBar();
        lblVideoQualityValue = new Label();
        lblVideoQualityCaption = new Label();
        btnConvertMov = new Button();
        btnConvertMkv = new Button();
        btnConvertMp4 = new Button();
        progressDownload = new ProgressBar();
        lblStatusCaption = new Label();
        lblStatus = new Label();
        tableFooter = new TableLayoutPanel();
        lblFileInfo = new Label();
        tabOutput = new TabControl();
        tabPreview = new TabPage();
        lblPreviewState = new Label();
        webPreview = new Microsoft.Web.WebView2.WinForms.WebView2();
        panelPreviewToolbar = new Panel();
        lblPreviewPath = new Label();
        lblPreviewCaption = new Label();
        btnOpenExternal = new Button();
        btnOpenMediaFile = new Button();
        btnPreviewLast = new Button();
        tabLog = new TabPage();
        txtLog = new TextBox();
        grpAudioConvert.SuspendLayout();
        grpVideoConvert.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)trkVideoQuality).BeginInit();
        tableFooter.SuspendLayout();
        tabOutput.SuspendLayout();
        tabPreview.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)webPreview).BeginInit();
        panelPreviewToolbar.SuspendLayout();
        tabLog.SuspendLayout();
        SuspendLayout();
        // 
        // lblUrl
        // 
        lblUrl.AutoSize = true;
        lblUrl.Location = new Point(12, 18);
        lblUrl.Name = "lblUrl";
        lblUrl.Size = new Size(28, 15);
        lblUrl.TabIndex = 0;
        lblUrl.Text = "URL";
        // 
        // txtUrl
        // 
        txtUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtUrl.Location = new Point(12, 36);
        txtUrl.Name = "txtUrl";
        txtUrl.PlaceholderText = "Paste a video URL";
        txtUrl.Size = new Size(757, 23);
        txtUrl.TabIndex = 1;
        // 
        // lblOutputFolder
        // 
        lblOutputFolder.AutoSize = true;
        lblOutputFolder.Location = new Point(12, 72);
        lblOutputFolder.Name = "lblOutputFolder";
        lblOutputFolder.Size = new Size(76, 15);
        lblOutputFolder.TabIndex = 2;
        lblOutputFolder.Text = "Output folder";
        // 
        // txtOutputFolder
        // 
        txtOutputFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtOutputFolder.Location = new Point(12, 90);
        txtOutputFolder.Name = "txtOutputFolder";
        txtOutputFolder.Size = new Size(666, 23);
        txtOutputFolder.TabIndex = 3;
        // 
        // btnBrowseOutput
        // 
        btnBrowseOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseOutput.Location = new Point(684, 89);
        btnBrowseOutput.Name = "btnBrowseOutput";
        btnBrowseOutput.Size = new Size(85, 25);
        btnBrowseOutput.TabIndex = 4;
        btnBrowseOutput.Text = "Browse";
        btnBrowseOutput.UseVisualStyleBackColor = true;
        btnBrowseOutput.Click += btnBrowseOutput_Click;
        // 
        // chkExtractAudio
        // 
        chkExtractAudio.AutoSize = true;
        chkExtractAudio.Location = new Point(12, 129);
        chkExtractAudio.Name = "chkExtractAudio";
        chkExtractAudio.Size = new Size(120, 19);
        chkExtractAudio.TabIndex = 5;
        chkExtractAudio.Text = "Extract audio (mp3)";
        chkExtractAudio.UseVisualStyleBackColor = true;
        // 
        // btnDownload
        // 
        btnDownload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnDownload.Location = new Point(642, 124);
        btnDownload.Name = "btnDownload";
        btnDownload.Size = new Size(127, 29);
        btnDownload.TabIndex = 6;
        btnDownload.Text = "Download";
        btnDownload.UseVisualStyleBackColor = true;
        btnDownload.Click += btnDownload_Click;
        // 
        // grpAudioConvert
        // 
        grpAudioConvert.Controls.Add(btnConvertM4a);
        grpAudioConvert.Controls.Add(btnConvertWav);
        grpAudioConvert.Controls.Add(btnConvertMp3);
        grpAudioConvert.Location = new Point(12, 161);
        grpAudioConvert.Name = "grpAudioConvert";
        grpAudioConvert.Size = new Size(365, 67);
        grpAudioConvert.TabIndex = 7;
        grpAudioConvert.TabStop = false;
        grpAudioConvert.Text = "Audio convert";
        // 
        // btnConvertM4a
        // 
        btnConvertM4a.Enabled = false;
        btnConvertM4a.Location = new Point(244, 26);
        btnConvertM4a.Name = "btnConvertM4a";
        btnConvertM4a.Size = new Size(104, 29);
        btnConvertM4a.TabIndex = 2;
        btnConvertM4a.Text = "To M4A";
        btnConvertM4a.UseVisualStyleBackColor = true;
        btnConvertM4a.Click += btnConvertM4a_Click;
        // 
        // btnConvertWav
        // 
        btnConvertWav.Enabled = false;
        btnConvertWav.Location = new Point(128, 26);
        btnConvertWav.Name = "btnConvertWav";
        btnConvertWav.Size = new Size(104, 29);
        btnConvertWav.TabIndex = 1;
        btnConvertWav.Text = "To WAV";
        btnConvertWav.UseVisualStyleBackColor = true;
        btnConvertWav.Click += btnConvertWav_Click;
        // 
        // btnConvertMp3
        // 
        btnConvertMp3.Enabled = false;
        btnConvertMp3.Location = new Point(12, 26);
        btnConvertMp3.Name = "btnConvertMp3";
        btnConvertMp3.Size = new Size(104, 29);
        btnConvertMp3.TabIndex = 0;
        btnConvertMp3.Text = "To MP3";
        btnConvertMp3.UseVisualStyleBackColor = true;
        btnConvertMp3.Click += btnConvertMp3_Click;
        // 
        // grpVideoConvert
        // 
        grpVideoConvert.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        grpVideoConvert.Controls.Add(lblVideoQualityHint);
        grpVideoConvert.Controls.Add(lblVideoQualityScaleRight);
        grpVideoConvert.Controls.Add(lblVideoQualityScaleLeft);
        grpVideoConvert.Controls.Add(trkVideoQuality);
        grpVideoConvert.Controls.Add(lblVideoQualityValue);
        grpVideoConvert.Controls.Add(lblVideoQualityCaption);
        grpVideoConvert.Controls.Add(btnConvertMov);
        grpVideoConvert.Controls.Add(btnConvertMkv);
        grpVideoConvert.Controls.Add(btnConvertMp4);
        grpVideoConvert.Location = new Point(404, 161);
        grpVideoConvert.Name = "grpVideoConvert";
        grpVideoConvert.Size = new Size(365, 140);
        grpVideoConvert.TabIndex = 8;
        grpVideoConvert.TabStop = false;
        grpVideoConvert.Text = "Video convert";
        // 
        // lblVideoQualityHint
        // 
        lblVideoQualityHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblVideoQualityHint.AutoEllipsis = true;
        lblVideoQualityHint.ForeColor = SystemColors.GrayText;
        lblVideoQualityHint.Location = new Point(12, 120);
        lblVideoQualityHint.Name = "lblVideoQualityHint";
        lblVideoQualityHint.Size = new Size(336, 15);
        lblVideoQualityHint.TabIndex = 8;
        lblVideoQualityHint.Text = "Smaller output using lower video and audio bitrates.";
        // 
        // lblVideoQualityScaleRight
        // 
        lblVideoQualityScaleRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblVideoQualityScaleRight.Location = new Point(253, 104);
        lblVideoQualityScaleRight.Name = "lblVideoQualityScaleRight";
        lblVideoQualityScaleRight.Size = new Size(95, 15);
        lblVideoQualityScaleRight.TabIndex = 7;
        lblVideoQualityScaleRight.Text = "Smaller file";
        lblVideoQualityScaleRight.TextAlign = ContentAlignment.MiddleRight;
        // 
        // lblVideoQualityScaleLeft
        // 
        lblVideoQualityScaleLeft.AutoSize = true;
        lblVideoQualityScaleLeft.Location = new Point(12, 104);
        lblVideoQualityScaleLeft.Name = "lblVideoQualityScaleLeft";
        lblVideoQualityScaleLeft.Size = new Size(81, 15);
        lblVideoQualityScaleLeft.TabIndex = 6;
        lblVideoQualityScaleLeft.Text = "Higher quality";
        // 
        // trkVideoQuality
        // 
        trkVideoQuality.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        trkVideoQuality.AutoSize = false;
        trkVideoQuality.LargeChange = 1;
        trkVideoQuality.Location = new Point(12, 76);
        trkVideoQuality.Maximum = 4;
        trkVideoQuality.Name = "trkVideoQuality";
        trkVideoQuality.Size = new Size(336, 28);
        trkVideoQuality.TabIndex = 5;
        trkVideoQuality.TickFrequency = 1;
        trkVideoQuality.Value = 2;
        trkVideoQuality.ValueChanged += trkVideoQuality_ValueChanged;
        // 
        // lblVideoQualityValue
        // 
        lblVideoQualityValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblVideoQualityValue.Location = new Point(224, 58);
        lblVideoQualityValue.Name = "lblVideoQualityValue";
        lblVideoQualityValue.Size = new Size(124, 15);
        lblVideoQualityValue.TabIndex = 4;
        lblVideoQualityValue.Text = "Balanced (CRF 26)";
        lblVideoQualityValue.TextAlign = ContentAlignment.MiddleRight;
        // 
        // lblVideoQualityCaption
        // 
        lblVideoQualityCaption.AutoSize = true;
        lblVideoQualityCaption.Location = new Point(12, 58);
        lblVideoQualityCaption.Name = "lblVideoQualityCaption";
        lblVideoQualityCaption.Size = new Size(75, 15);
        lblVideoQualityCaption.TabIndex = 3;
        lblVideoQualityCaption.Text = "Video quality";
        // 
        // btnConvertMov
        // 
        btnConvertMov.Enabled = false;
        btnConvertMov.Location = new Point(244, 26);
        btnConvertMov.Name = "btnConvertMov";
        btnConvertMov.Size = new Size(104, 29);
        btnConvertMov.TabIndex = 2;
        btnConvertMov.Text = "To MOV";
        btnConvertMov.UseVisualStyleBackColor = true;
        btnConvertMov.Click += btnConvertMov_Click;
        // 
        // btnConvertMkv
        // 
        btnConvertMkv.Enabled = false;
        btnConvertMkv.Location = new Point(128, 26);
        btnConvertMkv.Name = "btnConvertMkv";
        btnConvertMkv.Size = new Size(104, 29);
        btnConvertMkv.TabIndex = 1;
        btnConvertMkv.Text = "To MKV";
        btnConvertMkv.UseVisualStyleBackColor = true;
        btnConvertMkv.Click += btnConvertMkv_Click;
        // 
        // btnConvertMp4
        // 
        btnConvertMp4.Enabled = false;
        btnConvertMp4.Location = new Point(12, 26);
        btnConvertMp4.Name = "btnConvertMp4";
        btnConvertMp4.Size = new Size(104, 29);
        btnConvertMp4.TabIndex = 0;
        btnConvertMp4.Text = "To MP4";
        btnConvertMp4.UseVisualStyleBackColor = true;
        btnConvertMp4.Click += btnConvertMp4_Click;
        // 
        // progressDownload
        // 
        progressDownload.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        progressDownload.Location = new Point(12, 579);
        progressDownload.MarqueeAnimationSpeed = 30;
        progressDownload.Name = "progressDownload";
        progressDownload.Size = new Size(757, 11);
        progressDownload.Style = ProgressBarStyle.Marquee;
        progressDownload.TabIndex = 7;
        progressDownload.Visible = false;
        // 
        // lblStatusCaption
        // 
        lblStatusCaption.Dock = DockStyle.Fill;
        lblStatusCaption.Location = new Point(0, 0);
        lblStatusCaption.Name = "lblStatusCaption";
        lblStatusCaption.Size = new Size(45, 19);
        lblStatusCaption.TabIndex = 8;
        lblStatusCaption.TextAlign = ContentAlignment.MiddleLeft;
        lblStatusCaption.Text = "Status";
        // 
        // lblStatus
        // 
        lblStatus.AutoEllipsis = true;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Location = new Point(45, 0);
        lblStatus.Margin = new Padding(0);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(472, 19);
        lblStatus.TabIndex = 9;
        lblStatus.Text = "Idle";
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // tableFooter
        // 
        tableFooter.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        tableFooter.ColumnCount = 3;
        tableFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
        tableFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
        tableFooter.Controls.Add(lblStatusCaption, 0, 0);
        tableFooter.Controls.Add(lblStatus, 1, 0);
        tableFooter.Controls.Add(lblFileInfo, 2, 0);
        tableFooter.Location = new Point(12, 555);
        tableFooter.Margin = new Padding(0);
        tableFooter.Name = "tableFooter";
        tableFooter.RowCount = 1;
        tableFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableFooter.Size = new Size(757, 19);
        tableFooter.TabIndex = 10;
        // 
        // lblFileInfo
        // 
        lblFileInfo.AutoEllipsis = true;
        lblFileInfo.Dock = DockStyle.Fill;
        lblFileInfo.Location = new Point(517, 0);
        lblFileInfo.Margin = new Padding(0);
        lblFileInfo.Name = "lblFileInfo";
        lblFileInfo.Size = new Size(240, 19);
        lblFileInfo.TabIndex = 10;
        lblFileInfo.Text = "No file selected";
        lblFileInfo.TextAlign = ContentAlignment.MiddleRight;
        // 
        // tabOutput
        // 
        tabOutput.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tabOutput.Controls.Add(tabPreview);
        tabOutput.Controls.Add(tabLog);
        tabOutput.Location = new Point(12, 313);
        tabOutput.Name = "tabOutput";
        tabOutput.SelectedIndex = 0;
        tabOutput.Size = new Size(757, 234);
        tabOutput.TabIndex = 9;
        // 
        // tabPreview
        // 
        tabPreview.Controls.Add(lblPreviewState);
        tabPreview.Controls.Add(webPreview);
        tabPreview.Controls.Add(panelPreviewToolbar);
        tabPreview.Location = new Point(4, 24);
        tabPreview.Name = "tabPreview";
        tabPreview.Padding = new Padding(3);
        tabPreview.Size = new Size(749, 279);
        tabPreview.TabIndex = 0;
        tabPreview.Text = "Preview";
        tabPreview.UseVisualStyleBackColor = true;
        // 
        // lblPreviewState
        // 
        lblPreviewState.Dock = DockStyle.Fill;
        lblPreviewState.Location = new Point(3, 59);
        lblPreviewState.Name = "lblPreviewState";
        lblPreviewState.Padding = new Padding(24);
        lblPreviewState.Size = new Size(743, 217);
        lblPreviewState.TabIndex = 2;
        lblPreviewState.Text = "Preview is loading...";
        lblPreviewState.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // webPreview
        // 
        webPreview.AllowExternalDrop = true;
        webPreview.CreationProperties = null;
        webPreview.DefaultBackgroundColor = Color.Black;
        webPreview.Dock = DockStyle.Fill;
        webPreview.Location = new Point(3, 59);
        webPreview.Name = "webPreview";
        webPreview.Size = new Size(743, 217);
        webPreview.TabIndex = 1;
        webPreview.Visible = false;
        webPreview.ZoomFactor = 1D;
        // 
        // panelPreviewToolbar
        // 
        panelPreviewToolbar.Controls.Add(lblPreviewPath);
        panelPreviewToolbar.Controls.Add(lblPreviewCaption);
        panelPreviewToolbar.Controls.Add(btnOpenExternal);
        panelPreviewToolbar.Controls.Add(btnOpenMediaFile);
        panelPreviewToolbar.Controls.Add(btnPreviewLast);
        panelPreviewToolbar.Dock = DockStyle.Top;
        panelPreviewToolbar.Location = new Point(3, 3);
        panelPreviewToolbar.Name = "panelPreviewToolbar";
        panelPreviewToolbar.Size = new Size(743, 56);
        panelPreviewToolbar.TabIndex = 0;
        // 
        // lblPreviewPath
        // 
        lblPreviewPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblPreviewPath.AutoEllipsis = true;
        lblPreviewPath.Location = new Point(363, 28);
        lblPreviewPath.Name = "lblPreviewPath";
        lblPreviewPath.Size = new Size(368, 15);
        lblPreviewPath.TabIndex = 4;
        lblPreviewPath.Text = "Nothing loaded yet.";
        // 
        // lblPreviewCaption
        // 
        lblPreviewCaption.AutoSize = true;
        lblPreviewCaption.Location = new Point(363, 10);
        lblPreviewCaption.Name = "lblPreviewCaption";
        lblPreviewCaption.Size = new Size(120, 15);
        lblPreviewCaption.TabIndex = 3;
        lblPreviewCaption.Text = "Current media source";
        // 
        // btnOpenExternal
        // 
        btnOpenExternal.Enabled = false;
        btnOpenExternal.Location = new Point(236, 14);
        btnOpenExternal.Name = "btnOpenExternal";
        btnOpenExternal.Size = new Size(109, 27);
        btnOpenExternal.TabIndex = 2;
        btnOpenExternal.Text = "Open externally";
        btnOpenExternal.UseVisualStyleBackColor = true;
        btnOpenExternal.Click += btnOpenExternal_Click;
        // 
        // btnOpenMediaFile
        // 
        btnOpenMediaFile.Enabled = false;
        btnOpenMediaFile.Location = new Point(132, 14);
        btnOpenMediaFile.Name = "btnOpenMediaFile";
        btnOpenMediaFile.Size = new Size(96, 27);
        btnOpenMediaFile.TabIndex = 1;
        btnOpenMediaFile.Text = "Open file";
        btnOpenMediaFile.UseVisualStyleBackColor = true;
        btnOpenMediaFile.Click += btnOpenMediaFile_Click;
        // 
        // btnPreviewLast
        // 
        btnPreviewLast.Enabled = false;
        btnPreviewLast.Location = new Point(8, 14);
        btnPreviewLast.Name = "btnPreviewLast";
        btnPreviewLast.Size = new Size(116, 27);
        btnPreviewLast.TabIndex = 0;
        btnPreviewLast.Text = "Open last file";
        btnPreviewLast.UseVisualStyleBackColor = true;
        btnPreviewLast.Click += btnPreviewLast_Click;
        // 
        // tabLog
        // 
        tabLog.Controls.Add(txtLog);
        tabLog.Location = new Point(4, 24);
        tabLog.Name = "tabLog";
        tabLog.Padding = new Padding(3);
        tabLog.Size = new Size(749, 279);
        tabLog.TabIndex = 1;
        tabLog.Text = "Log";
        tabLog.UseVisualStyleBackColor = true;
        // 
        // txtLog
        // 
        txtLog.Dock = DockStyle.Fill;
        txtLog.Location = new Point(3, 3);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(743, 273);
        txtLog.TabIndex = 0;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(784, 602);
        Controls.Add(tabOutput);
        Controls.Add(grpVideoConvert);
        Controls.Add(grpAudioConvert);
        Controls.Add(tableFooter);
        Controls.Add(progressDownload);
        Controls.Add(btnDownload);
        Controls.Add(chkExtractAudio);
        Controls.Add(btnBrowseOutput);
        Controls.Add(txtOutputFolder);
        Controls.Add(lblOutputFolder);
        Controls.Add(txtUrl);
        Controls.Add(lblUrl);
        MinimumSize = new Size(800, 641);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "VeditorWindow - yt-dlp Wrapper";
        grpAudioConvert.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)trkVideoQuality).EndInit();
        grpVideoConvert.ResumeLayout(false);
        grpVideoConvert.PerformLayout();
        tableFooter.ResumeLayout(false);
        tabOutput.ResumeLayout(false);
        tabPreview.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)webPreview).EndInit();
        panelPreviewToolbar.ResumeLayout(false);
        panelPreviewToolbar.PerformLayout();
        tabLog.ResumeLayout(false);
        tabLog.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblUrl;
    private TextBox txtUrl;
    private Label lblOutputFolder;
    private TextBox txtOutputFolder;
    private Button btnBrowseOutput;
    private CheckBox chkExtractAudio;
    private Button btnDownload;
    private GroupBox grpAudioConvert;
    private Button btnConvertM4a;
    private Button btnConvertWav;
    private Button btnConvertMp3;
    private GroupBox grpVideoConvert;
    private Button btnConvertMov;
    private Button btnConvertMkv;
    private Button btnConvertMp4;
    private Label lblVideoQualityHint;
    private Label lblVideoQualityScaleRight;
    private Label lblVideoQualityScaleLeft;
    private TrackBar trkVideoQuality;
    private Label lblVideoQualityValue;
    private Label lblVideoQualityCaption;
    private ProgressBar progressDownload;
    private Label lblStatusCaption;
    private Label lblStatus;
    private TableLayoutPanel tableFooter;
    private Label lblFileInfo;
    private TabControl tabOutput;
    private TabPage tabPreview;
    private Label lblPreviewState;
    private Microsoft.Web.WebView2.WinForms.WebView2 webPreview;
    private Panel panelPreviewToolbar;
    private Label lblPreviewPath;
    private Label lblPreviewCaption;
    private Button btnOpenExternal;
    private Button btnOpenMediaFile;
    private Button btnPreviewLast;
    private TabPage tabLog;
    private TextBox txtLog;
}
