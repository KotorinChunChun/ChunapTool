namespace ChunapTool.Core;

internal static class WindowCandidates
{
    internal static bool Matches(WindowEntry entry, string filter) =>
        entry.Title.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)
        || entry.ProcessName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal interface IWindowTargetPicker
{
    WindowEntry? Resolve(Point screenPoint);
}
