using System.Globalization;
using ChunapTool.Core;

namespace ChunapTool.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--management-tests")
        {
            var directory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(directory);
            ManagementTests.Run(directory);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--capture-smoke") return WgcSmokeTests.Run(args[1]);
        if (args.SequenceEqual(new[] { "--native" })) return NativeTests.Run();
        if (args is ["--native", "--fixture", var x, var y, var w, var h]
            && TryInteger(x, out int left) && TryInteger(y, out int top)
            && TryInteger(w, out int width) && TryInteger(h, out int height) && width > 0 && height > 0)
            return NativeTests.Run(new WindowBounds(left, top, width, height));
        if (args.SequenceEqual(new[] { "--cli" })) return CliTests.RunLogic();
        if (args.SequenceEqual(new[] { "--cli-native" })) return CliTests.RunNative();
        if (args.SequenceEqual(new[] { "--cli-foreground" })) return CliTests.RunForeground();
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
        if (args.FirstOrDefault() == "--fixture")
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var options = FixtureOptions.Parse(args);
            using var fixture = new FixtureForm(options);
            if (options.OwnedPassive) fixture.Shown += async (_, _) =>
            {
                try { await Task.Run(() => Console.In.ReadLine()); }
                catch (IOException) { }
                fixture.Close();
            };
            Application.Run(fixture);
            return 0;
        }

        if (args.Length > 0)
        {
            Console.Error.WriteLine("使用方法: 引数なしで単体検証、--uiで不可視画面回帰、--nativeで実機検証、--cliでCLI論理検証、--cli-nativeでCLI実機検証（背面）、--cli-foregroundでCLI前面検証、--fixtureで動作確認用画面を起動します。");
            Console.Error.WriteLine("専用ウィンドウの指定矩形を検証: --native --fixture X Y W H（整数、幅・高さは正数）");
            return 2;
        }

        ManagementTests.Run(Path.GetTempPath());
        int core = CoreTests.Run();
        int settings = SettingsTests.Run();
        return core == 0 && settings == 0 ? 0 : 1;
    }

    private static bool TryInteger(string value, out int number) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
}
