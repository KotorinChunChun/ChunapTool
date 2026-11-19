namespace ChunapTool.Core;

/// <summary>CLI・管理pipeの対象指定。指定した条件はすべて満たす必要がある（AND）。</summary>
internal sealed record TargetCriteria
{
    public nint? Handle { get; init; }
    public uint? ProcessId { get; init; }
    public string? Title { get; init; }
    public string? TitleExact { get; init; }
    public string? Process { get; init; }
    public string? ClassName { get; init; }
    public Point? At { get; init; }
    public bool Foreground { get; init; }

    public bool IsEmpty => Handle is null && ProcessId is null && Title is null && TitleExact is null
        && Process is null && ClassName is null && At is null && !Foreground;
}

internal static class TargetSelector
{
    // ChunapTool自身（GUI・CLI）の画面は操作対象にしない。
    internal static readonly string[] ExcludedProcesses = ["ChunapTool.exe", "chunap.exe"];

    internal static bool IsExcluded(WindowEntry entry) =>
        ExcludedProcesses.Contains(entry.ProcessName, StringComparer.OrdinalIgnoreCase);

    /// <summary>anchorは座標指定・前面指定で先に決まったウィンドウ。指定がなければnull。</summary>
    internal static IReadOnlyList<WindowEntry> Filter(IEnumerable<WindowEntry> entries, TargetCriteria criteria, WindowIdentity? anchor) =>
        entries.Where(entry => !IsExcluded(entry)
            && (anchor is null || entry.Identity == anchor)
            && (criteria.Handle is null || entry.Identity.Handle == criteria.Handle)
            && (criteria.ProcessId is null || entry.Identity.ProcessId == criteria.ProcessId)
            && (criteria.Title is null || entry.Title.Contains(criteria.Title, StringComparison.OrdinalIgnoreCase))
            && (criteria.TitleExact is null || string.Equals(entry.Title, criteria.TitleExact, StringComparison.Ordinal))
            && (criteria.Process is null || string.Equals(entry.ProcessName, criteria.Process, StringComparison.OrdinalIgnoreCase))
            && (criteria.ClassName is null || string.Equals(entry.ClassName, criteria.ClassName, StringComparison.Ordinal)))
        .ToArray();
}
