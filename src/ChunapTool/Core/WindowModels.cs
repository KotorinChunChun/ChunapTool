namespace ChunapTool.Core;

public readonly record struct WindowIdentity(nint Handle, uint ProcessId);
public readonly record struct WindowBounds(int X, int Y, int Width, int Height);
public readonly record struct WindowPoint(int X, int Y);
// NormalBoundsはWINDOWPLACEMENTの作業領域座標。画面座標として流用しない。
public sealed record WindowPlacement(uint Flags, uint ShowCommand, WindowPoint MinPosition,
    WindowPoint MaxPosition, WindowBounds NormalBounds);
public sealed record WindowState(WindowIdentity Target, WindowBounds Bounds, WindowPlacement Placement, bool CanMaximize)
{
    // 透明なリサイズ枠を含むGetWindowRectの外枠。スナップ中など通常復元位置と実位置が異なる状態の復元に使う。
    public WindowBounds? OuterBounds { get; init; }
    public bool IsMaximized => Placement.ShowCommand == 3;
    public bool IsMinimized => Placement.ShowCommand is 2 or 6 or 7 or 11;
}
public sealed record WindowEntry(WindowIdentity Identity, string Title, string ProcessName)
{
    public override string ToString() => $"[{ProcessName}] {Title} ({Identity.ProcessId})";
}
public interface IWindowService
{
    IReadOnlyList<WindowEntry> Enumerate(string filter);
    WindowState? GetState(WindowIdentity target);
    WindowBounds GetWorkArea(WindowIdentity target);
    void ApplyBounds(WindowIdentity target, WindowBounds bounds);
    void SetMaximized(WindowIdentity target, bool maximized);
    void Restore(WindowState state);
    bool IsLeftButtonDown { get; }
}
