namespace VeditorWindow.UI;

internal enum StudioLayoutMode
{
    Wide,
    Compact
}

internal static class StudioLayoutPolicy
{
    internal static StudioLayoutMode Resolve(int logicalClientWidth)
    {
        return logicalClientWidth < StudioTheme.CompactBreakpoint
            ? StudioLayoutMode.Compact
            : StudioLayoutMode.Wide;
    }
}
