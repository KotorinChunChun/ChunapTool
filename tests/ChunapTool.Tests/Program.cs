namespace ChunapTool.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--native" })) return NativeTests.Run();
        if (args.SequenceEqual(new[] { "--ui" }))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            return UiTests.Run();
        }
        if (args.Contains("--fixture", StringComparer.Ordinal))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FixtureForm());
            return 0;
        }

        if (args.Length > 0)
        {
            Console.Error.WriteLine("使用方法: 引数なしで単体検証、--uiで不可視画面回帰、--nativeで実機検証、--fixtureで動作確認用画面を起動します。");
            return 2;
        }

        ManagementTests.Run(Path.GetTempPath());
        return CoreTests.Run();
    }
}
