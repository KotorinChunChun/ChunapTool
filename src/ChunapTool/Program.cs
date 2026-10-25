namespace ChunapTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, @"Local\ChunapTool", out var firstInstance);
        if (!firstInstance)
        {
            var problem = KotorinToys.Integration.ManagementEndpoint.ShowExisting("chunap-tool");
            // 常駐起動と検証では対話表示しない。手動起動で既存画面を出せないときだけ理由を伝える。
            if (problem is not null && !args.Contains("--startup") && !args.Contains("--passive"))
                MessageBox.Show(problem, WindowTitle.Create(), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
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
