using System.Runtime.InteropServices;

namespace ChunapTool.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        // GUIと同じ物理ピクセルの座標で読み書きするため、最初にPer-Monitor V2にする。
        _ = SetProcessDpiAwarenessContext(-4);
        return CliApplication.Run(args);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);
}
