using System.Text.RegularExpressions;

namespace ChunapTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // --instanceを指定すると、ミューテックスと管理pipeの名前を分ける。検証用の起動が利用者の画面に触れないようにする。
        var instance = Option(args, "--instance");
        if (instance is not null && !Regex.IsMatch(instance, "^[A-Za-z0-9-]{1,64}$"))
        {
            if (!args.Contains("--passive"))
                MessageBox.Show("--instance には英数字とハイフンを64文字以内で指定してください。", WindowTitle.Create(),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // 設定の場所は--settings-dirで変えられる（検証用の起動が利用者の設定に触れないようにする）。
        Core.SettingsStore settings;
        try { settings = new Core.SettingsStore(Option(args, "--settings-dir") ?? Core.SettingsStore.DefaultDirectory); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            if (!args.Contains("--passive"))
                MessageBox.Show("--settings-dir のフォルダー名が不正です: " + ex.Message, WindowTitle.Create(),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var managementId = instance is null ? "chunap-tool" : "chunap-tool." + instance;
        using var singleInstance = new Mutex(true, instance is null ? @"Local\ChunapTool" : @"Local\ChunapTool." + instance, out var firstInstance);
        if (!firstInstance)
        {
            var problem = KotorinToys.Integration.ManagementEndpoint.ShowExisting(managementId);
            // 常駐起動と検証では対話表示しない。手動起動で既存画面を出せないときだけ理由を伝える。
            if (problem is not null && !args.Contains("--startup") && !args.Contains("--passive"))
            {
                // 既存画面は既定で最前面のため、通常のメッセージはその裏に隠れる。最前面の透明な所有者を介して表示する。
                using var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0,
                    FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterScreen, Size = new Size(1, 1) };
                owner.Show();
                MessageBox.Show(owner, problem, WindowTitle.Create(), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }
        try
        {
            var window = new MainForm(new Native.NativeWindowService(), args.Contains("--passive"), settings: settings);
            using var context = new KotorinToys.Integration.ManagedToolContext(managementId, window,
                args.Contains("--startup"), () => window.ManagementStopBlockReason);
            Application.Run(context);
        }
        finally { singleInstance.ReleaseMutex(); }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
