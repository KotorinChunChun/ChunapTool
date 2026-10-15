namespace ChunapTool.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--capture-smoke") return WgcSmokeTests.Run(args[1]);
        if (args.SequenceEqual(new[] { "--native" })) return NativeTests.Run();
        if (args.SequenceEqual(new[] { "--ui" }))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 実アプリと同じUIメッセージループでawait後のタイマー再開も検証する。
            // メインフォームを設けず、各テストは不可視・非アクティブなままとする。
            using var context = new ApplicationContext();
            int result = 1;
            EventHandler? runTests = null;
            runTests = (_, _) =>
            {
                Application.Idle -= runTests;
                try
                {
                    int ui = UiTests.Run();
                    int thumbnails = ThumbnailTests.Run();
                    result = ui == 0 && thumbnails == 0 ? 0 : 1;
                }
                finally { context.ExitThread(); }
            };
            Application.Idle += runTests;
            Application.Run(context);
            return result;
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
