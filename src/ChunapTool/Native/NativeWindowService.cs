using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ChunapTool.Core;

namespace ChunapTool.Native;

/// <summary>見える外形を基準に、別プロセスのウィンドウを操作する。</summary>
public sealed class NativeWindowService : IWindowService
{
    public bool IsLeftButtonDown => (NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0;

    public IReadOnlyList<WindowEntry> Enumerate(string filter)
    {
        var entries = new List<WindowEntry>();
        var processNames = new Dictionary<uint, string>();
        uint ownProcess = (uint)Environment.ProcessId;
        string search = filter.Trim();
        NativeMethods.EnumWindowsCallback callback = (window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window))
                return true;
            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0 || processId == ownProcess)
                return true;
            if (NativeMethods.DwmGetWindowInt(window, NativeMethods.Cloaked, out int cloaked, sizeof(int)) >= 0
                && cloaked != 0)
                return true;

            int length = NativeMethods.GetWindowTextLengthW(window);
            if (length <= 0)
                return true;
            var titleBuffer = new StringBuilder(Math.Min(length + 1, 32768));
            NativeMethods.GetWindowTextW(window, titleBuffer, titleBuffer.Capacity);
            string title = titleBuffer.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;
            if (!processNames.TryGetValue(processId, out string? processName))
            {
                processName = GetProcessName(processId);
                processNames.Add(processId, processName);
            }
            var identity = new WindowIdentity(window, processId);
            if (Matches(identity) && (search.Length == 0
                || title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || processName.Contains(search, StringComparison.OrdinalIgnoreCase)))
                entries.Add(new WindowEntry(identity, title, processName));
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, 0))
            throw LastError("ウィンドウ一覧を取得できませんでした。");
        return entries.OrderBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Identity.ProcessId).ThenBy(entry => entry.Identity.Handle).ToArray();
    }

    public WindowState? GetState(WindowIdentity target)
    {
        if (!Matches(target))
            return null;
        var placement = NativeMethods.Placement.Create();
        if (!NativeMethods.GetWindowPlacement(target.Handle, ref placement))
        {
            int error = Marshal.GetLastPInvokeError();
            if (!Matches(target))
                return null;
            throw new Win32Exception(error, "ウィンドウの通常位置を取得できませんでした。");
        }
        if (!TryGetVisualRect(target.Handle, out var visual))
        {
            int error = Marshal.GetLastPInvokeError();
            if (!Matches(target))
                return null;
            throw new Win32Exception(error, "ウィンドウの位置を取得できませんでした。");
        }
        bool canMaximize = (NativeMethods.GetWindowLong(target.Handle, -16) & 0x00010000) != 0;
        WindowBounds? outerBounds = NativeMethods.GetWindowRect(target.Handle, out var outer) ? ToBounds(outer) : null;
        if (!Matches(target))
            return null;
        // 非同期指定は操作方法であり、Undoの比較対象となる位置状態には含めない。
        var savedPlacement = new WindowPlacement(placement.Flags & ~NativeMethods.AsyncWindowPlacement,
            placement.ShowCommand, new WindowPoint(placement.MinPosition.X, placement.MinPosition.Y),
            new WindowPoint(placement.MaxPosition.X, placement.MaxPosition.Y), ToBounds(placement.NormalPosition));
        return new WindowState(target, ToBounds(visual), savedPlacement, canMaximize) { OuterBounds = outerBounds };
    }

    public WindowBounds GetWorkArea(WindowIdentity target)
    {
        EnsureTarget(target);
        nint monitor = NativeMethods.MonitorFromWindow(target.Handle, 2);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfoW(monitor, ref info))
            throw LastError("対象モニターの作業領域を取得できませんでした。");
        EnsureTarget(target);
        return ToBounds(info.Work);
    }

    public void ApplyBounds(WindowIdentity target, WindowBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "幅と高さは正の値を指定してください。");
        var state = GetState(target) ?? throw MissingTarget();
        if (state.IsMaximized || state.IsMinimized)
            throw new InvalidOperationException("最大化・最小化中は位置やサイズを変更できません。元のサイズに戻してください。");
        EnsureResponsive(target);
        if (!NativeMethods.GetWindowRect(target.Handle, out var outer))
            throw LastError("ウィンドウの外枠を取得できませんでした。");
        var visual = NativeMethods.DwmGetWindowRect(target.Handle, NativeMethods.ExtendedFrameBounds,
            out var frame, Marshal.SizeOf<NativeMethods.Rect>()) >= 0 ? frame : outer;
        int x, y, width, height;
        checked
        {
            x = bounds.X - (visual.Left - outer.Left);
            y = bounds.Y - (visual.Top - outer.Top);
            width = bounds.Width + (outer.Right - outer.Left) - (visual.Right - visual.Left);
            height = bounds.Height + (outer.Bottom - outer.Top) - (visual.Bottom - visual.Top);
        }
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "補正後のウィンドウサイズが不正です。");
        EnsureTarget(target);
        // 対象アプリの処理をUIスレッドで待たず、前面化やZ順の変更もしない。
        const uint flags = 0x4000 | 0x0004 | 0x0010;
        if (!NativeMethods.SetWindowPos(target.Handle, 0, x, y, width, height, flags))
            throw LastError("位置・サイズ変更を要求できませんでした。");
    }

    public void SetMaximized(WindowIdentity target, bool maximized)
    {
        var state = GetState(target) ?? throw MissingTarget();
        if (maximized && !state.CanMaximize)
            throw new InvalidOperationException("このウィンドウは最大化できません。");
        if (state.IsMaximized == maximized && !state.IsMinimized)
            return;
        EnsureResponsive(target);
        EnsureTarget(target);
        // ShowWindowの戻り値は変更前の可視状態。ここでは要求の開始結果を返すAsync版を使う。
        if (!NativeMethods.ShowWindowAsync(target.Handle, maximized ? 3 : 9))
            throw LastError("最大化・復元を要求できませんでした。");
    }

    public void Restore(WindowState state)
    {
        EnsureResponsive(state.Target);
        var saved = state.Placement;
        var placement = NativeMethods.Placement.Create();
        placement.Flags = saved.Flags | NativeMethods.AsyncWindowPlacement;
        placement.ShowCommand = saved.ShowCommand;
        placement.MinPosition = new NativeMethods.Point { X = saved.MinPosition.X, Y = saved.MinPosition.Y };
        placement.MaxPosition = new NativeMethods.Point { X = saved.MaxPosition.X, Y = saved.MaxPosition.Y };
        placement.NormalPosition = ToRect(saved.NormalBounds);
        EnsureTarget(state.Target);
        // WINDOWPLACEMENTの作業領域座標を画面座標へ読み替えず、そのまま復元する。
        if (!NativeMethods.SetWindowPlacement(state.Target.Handle, in placement))
            throw LastError("保存したウィンドウ状態を復元できませんでした。");
        // スナップ中の通常復元位置はスナップ前の位置を指す。通常表示へ戻す場合は記録した外枠へ合わせる。
        // 非同期要求は対象スレッドで順に処理されるため、配置の復元後に位置が確定する。
        if (state is { IsMaximized: false, IsMinimized: false, OuterBounds: { } outer })
        {
            const uint flags = 0x4000 | 0x0004 | 0x0010;
            if (!NativeMethods.SetWindowPos(state.Target.Handle, 0, outer.X, outer.Y, outer.Width, outer.Height, flags))
                throw LastError("保存したウィンドウ位置を復元できませんでした。");
        }
    }

    internal static bool Matches(WindowIdentity target) => target.Handle != 0
        && NativeMethods.IsWindow(target.Handle)
        && NativeMethods.GetWindowThreadProcessId(target.Handle, out uint processId) != 0
        && processId == target.ProcessId;

    private static void EnsureTarget(WindowIdentity target)
    {
        if (!Matches(target))
            throw MissingTarget();
    }

    private static void EnsureResponsive(WindowIdentity target)
    {
        EnsureTarget(target);
        // WM_NULL、SMTO_BLOCK | ABORTIFHUNG | ERRORONEXIT。応答確認は最大200msで打ち切る。
        if (NativeMethods.SendMessageTimeoutW(target.Handle, 0, 0, 0, 0x0001 | 0x0002 | 0x0020,
            200, out _) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error == 0 ? NativeMethods.Timeout : error,
                "対象ウィンドウが応答していないか、操作が許可されていません。");
        }
    }

    // 一覧とドラッグ選択で同じ表示名にするため、実行ファイル名を共通の方法で取得する。
    internal static string GetProcessName(uint processId)
    {
        using var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process.IsInvalid)
            return "不明";
        var buffer = new StringBuilder(32768);
        uint capacity = (uint)buffer.Capacity;
        return NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref capacity)
            ? Path.GetFileName(buffer.ToString()) : "不明";
    }

    private static bool TryGetVisualRect(nint window, out NativeMethods.Rect rect)
    {
        if (NativeMethods.DwmGetWindowRect(window, NativeMethods.ExtendedFrameBounds,
            out rect, Marshal.SizeOf<NativeMethods.Rect>()) >= 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
            return true;
        return NativeMethods.GetWindowRect(window, out rect);
    }

    private static WindowBounds ToBounds(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static NativeMethods.Rect ToRect(WindowBounds bounds) => new()
    {
        Left = bounds.X, Top = bounds.Y,
        Right = checked(bounds.X + bounds.Width), Bottom = checked(bounds.Y + bounds.Height)
    };

    private static Win32Exception MissingTarget() =>
        new(NativeMethods.InvalidWindowHandle, "対象ウィンドウは終了したか、別のプロセスに置き換わりました。");

    private static Win32Exception LastError(string message) => new(Marshal.GetLastPInvokeError(), message);
}
