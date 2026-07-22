using System.Drawing.Drawing2D;

namespace VeditorWindow.UI;

internal static class StudioTheme
{
    //== palette ==============================================================
    internal static readonly Color WindowBackground = Color.FromArgb(13, 18, 38);
    internal static readonly Color WindowBackgroundDeep = Color.FromArgb(7, 11, 27);
    internal static readonly Color Surface = Color.FromArgb(23, 31, 67);
    internal static readonly Color SurfaceRaised = Color.FromArgb(32, 41, 79);
    internal static readonly Color SurfaceElevated = Color.FromArgb(40, 51, 94);
    internal static readonly Color SurfaceMuted = Color.FromArgb(20, 27, 58);
    internal static readonly Color SurfaceInput = Color.FromArgb(17, 24, 53);
    internal static readonly Color StageSurface = Color.FromArgb(18, 25, 56);
    internal static readonly Color StageBackground = Color.FromArgb(11, 16, 36);
    internal static readonly Color StatusSurface = Color.FromArgb(25, 34, 70);
    internal static readonly Color Border = Color.FromArgb(62, 185, 174, 255);
    internal static readonly Color BorderSoft = Color.FromArgb(38, 185, 174, 255);
    internal static readonly Color ClayShadow = Color.FromArgb(166, 3, 6, 20);
    internal static readonly Color ClayShadowStrong = Color.FromArgb(205, 3, 6, 20);
    internal static readonly Color ClayHighlight = Color.FromArgb(117, 129, 210);
    internal static readonly Color ClayInnerHighlight = Color.FromArgb(38, 185, 174, 255);
    internal static readonly Color TextPrimary = Color.FromArgb(245, 244, 255);
    internal static readonly Color TextSecondary = Color.FromArgb(182, 190, 220);
    internal static readonly Color TextMuted = Color.FromArgb(135, 145, 181);
    internal static readonly Color TextDisabled = Color.FromArgb(151, 160, 195);
    internal static readonly Color Accent = Color.FromArgb(130, 88, 244);
    internal static readonly Color AccentHover = Color.FromArgb(148, 111, 255);
    internal static readonly Color AccentBright = Color.FromArgb(191, 170, 255);
    internal static readonly Color AccentDeep = Color.FromArgb(101, 67, 214);
    internal static readonly Color AccentPressed = Color.FromArgb(91, 60, 190);
    internal static readonly Color AccentSoft = Color.FromArgb(55, 47, 112);
    internal static readonly Color Focus = Color.FromArgb(191, 170, 255);
    internal static readonly Color FocusSeparation = Color.FromArgb(176, 6, 9, 27);
    internal static readonly Color Success = Color.FromArgb(76, 224, 139);
    internal static readonly Color Warning = Color.FromArgb(255, 185, 121);
    internal static readonly Color Error = Color.FromArgb(255, 111, 145);
    //=======================================================================

    //== layout tokens =======================================================
    internal const int TitleBarHeight = 70;
    internal const int NavigationWideWidth = 176;
    internal const int NavigationCompactWidth = 92;
    internal const int InspectorWidth = 360;
    internal const int CompactBreakpoint = 1380;
    internal const int SurfaceRadius = 22;
    internal const int ControlRadius = 12;
    //=======================================================================

    internal static LinearGradientBrush CreateWindowBrush(Rectangle bounds)
    {
        var safeBounds = bounds.Width > 0 && bounds.Height > 0
            ? bounds
            : new Rectangle(0, 0, 1, 1);

        return new LinearGradientBrush(
            safeBounds,
            Color.FromArgb(20, 27, 58),
            WindowBackgroundDeep,
            128F);
    }

    internal static LinearGradientBrush CreateAccentBrush(Rectangle bounds)
    {
        var safeBounds = bounds.Width > 0 && bounds.Height > 0
            ? bounds
            : new Rectangle(0, 0, 1, 1);

        return new LinearGradientBrush(
            safeBounds,
            AccentHover,
            AccentDeep,
            LinearGradientMode.ForwardDiagonal);
    }
}

internal static class StudioDefaults
{
    internal const string VideoExtension = "mp4";
    internal const string VideoCodec = "h264";
    internal const int QualityTrackValue = 3;
    internal const int QualityPercent = 78;
}
