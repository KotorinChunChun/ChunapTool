namespace ChunapTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, @"Local\ChunapTool", out var firstInstance);
        if (!firstInstance) { KotorinToys.Integration.ManagementEndpoint.ShowExisting("chunap-tool"); return; }
        try
        {
            var window = new MainForm(new Native.NativeWindowService(), args.Contains("--passive"));
            using var context = new KotorinToys.Integration.ManagedToolContext("chunap-tool", window,
                args.Contains("--startup"), () => window.ManagementStopBlockReason);
            Application.Run(context);
        }
        finally { singleInstance.ReleaseMutex(); }
    }
}
