using ChunapTool.Core;
using System.Diagnostics;
using System.Text;

namespace ChunapTool.Native;

internal sealed class WindowTargetPicker : IWindowTargetPicker
{
    public WindowEntry? Resolve(Point screenPoint)
    {
        var child = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = screenPoint.X, Y = screenPoint.Y });
        var window = NativeMethods.GetAncestor(child, 2);
        if (window == 0 || !NativeMethods.IsWindowVisible(window)) return null;
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;
        if (NativeMethods.DwmGetWindowInt(window, NativeMethods.Cloaked, out var cloaked, sizeof(int)) >= 0 && cloaked != 0)
            return null;
        var title = new StringBuilder(32768);
        NativeMethods.GetWindowTextW(window, title, title.Capacity);
        if (string.IsNullOrWhiteSpace(title.ToString())) return null;
        var identity = new WindowIdentity(window, processId);
        if (!NativeWindowService.Matches(identity)) return null;
        string name;
        try { using var process = Process.GetProcessById((int)processId); name = process.ProcessName + ".exe"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { name = "不明"; }
        return new WindowEntry(identity, title.ToString(), name);
    }
}
