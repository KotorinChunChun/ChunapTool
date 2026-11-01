using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using KotorinToys.Integration;

namespace ChunapTool.Tests;

/// <summary>
/// chunap.exeを別プロセスで実行し、終了コードとJSONで判定する。各ケースの実行コマンドを表示するので、そのまま手で再現できる。
/// --cliはウィンドウを変更しない検証、--cli-nativeはこの検証が起動した確認用ウィンドウと、--instanceで分離したGUIだけを操作する。
/// </summary>
internal static class CliTests
{
    private static readonly string ProductVersion = WindowTitle.Create().Split(" - ver ")[^1];

    internal static int RunLogic() => RunCases("CLI論理検証", [
        ("help は使い方を表示", () =>
        {
            var run = Cli("help");
            Equal(0, run.ExitCode, "終了コード");
            True(run.Output.Contains("chunap", StringComparison.Ordinal), "使い方の表示");
            Equal(0, Cli("help", "set").ExitCode, "コマンド別の使い方");
            Equal(2, Cli("help", "no-such-command").ExitCode, "不明なコマンドの使い方");
        }),
        ("version は製品と同じ版", () => Equal(ProductVersion, Json("version", "--json")["version"]!.GetValue<string>(), "版")),
        ("不明なコマンド・重複・値なし・余分な引数は終了コード2", () =>
        {
            Usage("no-such-command", "--json");
            Usage("get", "--pid", "1", "--pid", "2", "--json");
            Usage("get", "--pid", "--json");
            Usage("get", "extra", "--pid", "1", "--json");
        }),
        ("綴り誤りのオプションは対象を探す前に拒否", () => Usage("set", "--hwnd", "0x1", "--x", "1", "--wdith", "5", "--json")),
        ("対象の指定がない・形式が不正なら終了コード2", () =>
        {
            Usage("get", "--json");
            Usage("get", "--hwnd", "zz", "--json");
            Usage("get", "--hwnd", "-5", "--json");
            Usage("pick", "--at", "10", "--json");
            Usage("set", "--pid", "1", "--x", "abc", "--json");
            Usage("set", "--pid", "1", "--width", "0", "--json");
            Usage("set", "--pid", "1", "--x", "1", "--timeout", "5", "--json");
            Usage("set", "--pid", "1", "--json");
        }),
        ("restore は読めないファイルを対象の解決前に拒否", () =>
        {
            var run = Cli("restore", "--pid", "1", "--from", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), "--json");
            Equal(2, run.ExitCode, "終了コード");
            Equal("invalid-state", run.Json!["error"]!["code"]!.GetValue<string>(), "エラーの種類");
        }),
        ("存在しない対象は終了コード3", () =>
        {
            var run = Cli("get", "--pid", int.MaxValue.ToString(CultureInfo.InvariantCulture), "--json");
            Equal(3, run.ExitCode, "終了コード");
            Equal("target-not-found", run.Json!["error"]!["code"]!.GetValue<string>(), "エラーの種類");
        }),
        ("list はChunapTool自身を含まずJSONで一覧", () =>
        {
            var json = Json("list", "--json");
            var windows = json["windows"]!.AsArray();
            Equal(windows.Count, json["count"]!.GetValue<int>(), "件数");
            True(windows.All(w => w!["process"]!.GetValue<string>() is not "ChunapTool.exe" and not "chunap.exe"), "自身を除外");
        }),
        ("基準・比率固定の指定の誤りは対象を探す前に終了コード2", () =>
        {
            Usage("set", "--pid", "1", "--width", "10", "--basis", "outer", "--json");
            Usage("set", "--pid", "1", "--width", "100", "--height", "100", "--keep-aspect", "--json");
            Usage("set", "--pid", "1", "--width", "100", "--height", "100", "--aspect", "16:9", "--json");
            Usage("set", "--pid", "1", "--width", "100", "--keep-aspect", "--aspect", "16:9", "--json");
            Usage("set", "--pid", "1", "--width", "100", "--aspect", "16:0", "--json");
            Usage("get", "--pid", "1", "--basis", "outer", "--json");
        }),
        ("preset は一時フォルダーの設定で追加・名前変更・移動・削除", () => WithSettings(dir =>
        {
            var list = Json("preset", "list", "--settings-dir", dir, "--json");
            Equal(5, list["presets"]!.AsArray().Count, "既定のプリセット");
            True(!File.Exists(Path.Combine(dir, "settings.json")), "一覧だけでは保存しない");
            var added = Json("preset", "add", "配信", "--width", "1280", "--height", "720", "--settings-dir", dir, "--json");
            Equal("client", added["presets"]!.AsArray()[5]!["basis"]!.GetValue<string>(), "既定の基準はclient");
            Json("preset", "add", "枠", "--width", "800", "--height", "600", "--basis", "frame", "--settings-dir", dir, "--json");
            Json("preset", "rename", "配信", "配信HD", "--settings-dir", dir, "--json");
            var moved = Json("preset", "move", "配信HD", "--to", "1", "--settings-dir", dir, "--json")["presets"]!.AsArray();
            Equal("配信HD", moved[0]!["name"]!.GetValue<string>(), "先頭へ移動");
            var removed = Json("preset", "remove", "fhd", "--settings-dir", dir, "--json")["presets"]!.AsArray();
            True(removed.All(p => p!["name"]!.GetValue<string>() != "FHD"), "大文字小文字を区別せず削除");
            var text = Cli("preset", "list", "--settings-dir", dir);
            True(text.Output.Contains("配信HD", StringComparison.Ordinal), "文章の一覧に日本語の名前");
        })),
        ("preset の重複・存在しない名前・語数の誤りは終了コード2", () => WithSettings(dir =>
        {
            Usage("preset", "add", "HD", "--width", "1280", "--height", "720", "--settings-dir", dir, "--json");
            Usage("preset", "add", "x", "--width", "10", "--height", "720", "--settings-dir", dir, "--json");
            Usage("preset", "add", "x", "--width", "100", "--settings-dir", dir, "--json");
            Usage("preset", "remove", "none", "--settings-dir", dir, "--json");
            Usage("preset", "move", "HD", "--to", "9", "--settings-dir", dir, "--json");
            Usage("preset", "rename", "HD", "--settings-dir", dir, "--json");
            Usage("preset", "list", "extra", "--settings-dir", dir, "--json");
            Usage("preset", "unknown", "--settings-dir", dir, "--json");
            Usage("preset", "apply", "none", "--pid", "1", "--settings-dir", dir, "--json");
            True(!File.Exists(Path.Combine(dir, "settings.json")), "失敗した操作では保存しない");
        })),
        ("config で取得・変更・既定値への復帰", () => WithSettings(dir =>
        {
            var path = Json("config", "path", "--settings-dir", dir, "--json");
            Equal(Path.Combine(dir, "settings.json"), path["path"]!.GetValue<string>(), "設定ファイルの場所");
            True(!path["exists"]!.GetValue<bool>(), "まだ作られていない");
            True(Json("config", "get", "gui.autoRead", "--settings-dir", dir, "--json")["value"]!.GetValue<bool>(), "既定値");
            Json("config", "set", "gui.autoRead", "off", "--settings-dir", dir, "--json");
            Json("config", "set", "gui.basis", "client", "--settings-dir", dir, "--json");
            Json("preset", "add", "残す", "--width", "100", "--height", "100", "--settings-dir", dir, "--json");
            var all = Json("config", "get", "--settings-dir", dir, "--json")["values"]!;
            True(!all["gui.autoRead"]!.GetValue<bool>() && all["gui.basis"]!.GetValue<string>() == "client", "変更を保存");
            Json("config", "reset", "--settings-dir", dir, "--json");
            all = Json("config", "get", "--settings-dir", dir, "--json")["values"]!;
            True(all["gui.autoRead"]!.GetValue<bool>() && all["gui.basis"]!.GetValue<string>() == "frame", "既定値へ戻す");
            True(Json("preset", "list", "--settings-dir", dir, "--json")["presets"]!.AsArray().Any(p => p!["name"]!.GetValue<string>() == "残す"), "プリセットは残す");
            Usage("config", "get", "autoRead", "--settings-dir", dir, "--json");
            Usage("config", "set", "gui.autoRead", "yes", "--settings-dir", dir, "--json");
            Usage("config", "set", "gui.noSuch", "true", "--settings-dir", dir, "--json");
            Usage("config", "set", "gui.autoRead", "--settings-dir", dir, "--json");
        })),
        ("読めない設定は終了コード8で変更せず、reset で退避して作り直す", () => WithSettings(dir =>
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, "{ broken");
            foreach (var args in new[] { new[] { "config", "get" }, ["preset", "list"], ["config", "set", "gui.autoRead", "true"], ["preset", "apply", "HD", "--pid", "1"] })
            {
                var run = Cli([.. args, "--settings-dir", dir, "--json"]);
                Equal(8, run.ExitCode, "終了コード: " + string.Join(" ", args));
                Equal("settings-invalid", run.Json!["error"]!["code"]!.GetValue<string>(), "エラーの種類");
            }
            Equal("{ broken", File.ReadAllText(file), "壊れた設定を上書きしない");
            var reset = Json("config", "reset", "--settings-dir", dir, "--json");
            var backup = reset["backup"]!.GetValue<string>();
            Equal("{ broken", File.ReadAllText(backup), "退避したファイル");
            Equal(5, Json("preset", "list", "--settings-dir", dir, "--json")["presets"]!.AsArray().Count, "既定値で作り直す");
        })),
        ("新しい版の設定は終了コード8で、reset でも上書きしない", () => WithSettings(dir =>
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "settings.json");
            const string newer = """{ "schemaVersion": 99 }""";
            File.WriteAllText(file, newer);
            Equal(8, Cli("config", "reset", "--settings-dir", dir, "--json").ExitCode, "reset");
            Equal(8, Cli("config", "get", "--settings-dir", dir, "--json").ExitCode, "get");
            Equal(newer, File.ReadAllText(file), "そのまま残す");
            Equal(1, Directory.GetFiles(dir).Length, "退避もしない");
        })),
    ]);

    // 利用者の設定（%LOCALAPPDATA%）に触れないよう、検証ごとに一時フォルダーを使う。
    private static void WithSettings(Action<string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "chunap-cli-" + Guid.NewGuid().ToString("N"));
        try { test(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    internal static int RunNative()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var fixtures = new List<Process>();
        try
        {
            var a = StartFixture(fixtures, $"CLI-A-{tag}", "--client", "640x360", "--position", "80,80");
            var b = StartFixture(fixtures, $"CLI-B-{tag}", "--position", "200,160", "--topmost");
            return RunCases("CLI実機検証", [
                ("get の見える枠はWin32の実測と一致", () => Equal(Win32.Frame(a.Handle), Frame(Json("get", "--hwnd", a.Hex, "--json")["state"]!), "見える枠")),
                ("set の位置・サイズが実測と一致", () =>
                {
                    var after = Json("set", "--hwnd", a.Hex, "--x", "120", "--y", "100", "--width", "700", "--height", "420", "--json")["after"]!;
                    Equal(new Rectangle(120, 100, 700, 420), Frame(after), "結果");
                    Equal(new Rectangle(120, 100, 700, 420), Win32.Frame(a.Handle), "実測");
                }),
                ("--dry-run は計算だけで変更しない", () =>
                {
                    var before = Win32.Frame(a.Handle);
                    var json = Json("set", "--hwnd", a.Hex, "--x", "300", "--dry-run", "--json");
                    True(json["dryRun"]!.GetValue<bool>() && json["after"] is null, "試行の表示");
                    Equal(300, json["requested"]!["x"]!.GetValue<int>(), "要求の計算");
                    Equal(before, Win32.Frame(a.Handle), "実測は不変");
                }),
                ("指定しない値は現在の値を保つ", () =>
                {
                    var before = Win32.Frame(a.Handle);
                    Json("set", "--hwnd", a.Hex, "--width", "500", "--json");
                    Equal(new Rectangle(before.X, before.Y, 500, before.Height), Win32.Frame(a.Handle), "幅だけ変更");
                }),
                ("作業領域の外は補正し、--allow-outside では要求どおり", () =>
                {
                    var area = Screen.FromHandle(a.Handle).WorkingArea;
                    var json = Json("set", "--hwnd", a.Hex, "--x", (area.Right - 100).ToString(CultureInfo.InvariantCulture), "--json");
                    True(json["warnings"]!.AsArray().Count > 0, "補正の注意");
                    True(area.Contains(Win32.Frame(a.Handle)), "作業領域内");
                    Json("set", "--hwnd", a.Hex, "--x", (area.Right - 100).ToString(CultureInfo.InvariantCulture), "--allow-outside", "--json");
                    Equal(area.Right - 100, Win32.Frame(a.Handle).X, "画面外を許可");
                }),
                ("constrain で作業領域へ収める", () =>
                {
                    Json("constrain", "--hwnd", a.Hex, "--json");
                    True(Screen.FromHandle(a.Handle).WorkingArea.Contains(Win32.Frame(a.Handle)), "作業領域内");
                }),
                ("maximize・unmaximize と、最大化中の set の拒否", () =>
                {
                    Json("maximize", "--hwnd", a.Hex, "--json");
                    True(Win32.IsZoomed(a.Handle), "最大化");
                    var refused = Cli("set", "--hwnd", a.Hex, "--x", "10", "--json");
                    Equal(1, refused.ExitCode, "最大化中のset");
                    Json("unmaximize", "--hwnd", a.Hex, "--json");
                    True(!Win32.IsZoomed(a.Handle), "通常表示");
                }),
                ("get の出力から restore で戻す", () =>
                {
                    var file = Path.Combine(Path.GetTempPath(), $"chunap-{tag}.json");
                    var saved = Cli("get", "--hwnd", a.Hex, "--json");
                    File.WriteAllText(file, saved.Output);
                    try
                    {
                        Json("set", "--hwnd", a.Hex, "--x", "40", "--y", "40", "--width", "400", "--height", "300", "--json");
                        Json("restore", "--hwnd", a.Hex, "--from", file, "--json");
                        Equal(Frame(saved.Json!["state"]!), Win32.Frame(a.Handle), "保存時の見える枠");
                    }
                    finally { File.Delete(file); }
                }),
                ("条件の組合せで一意に決まる", () =>
                {
                    var json = Json("get", "--process", "ChunapTool.Tests.exe", "--title", $"CLI-A-{tag}", "--json");
                    Equal(a.Hex, json["target"]!["hwnd"]!.GetValue<string>(), "対象");
                }),
                ("一致が複数なら終了コード4で何も変更しない", () =>
                {
                    var twin = StartFixture(fixtures, $"CLI-A-{tag}", "--position", "400,200");
                    var before = (Win32.Frame(a.Handle), Win32.Frame(twin.Handle));
                    var run = Cli("set", "--title", $"CLI-A-{tag}", "--x", "10", "--json");
                    Equal(4, run.ExitCode, "終了コード");
                    Equal(2, run.Json!["error"]!["details"]!["candidates"]!.AsArray().Count, "候補");
                    Equal(before, (Win32.Frame(a.Handle), Win32.Frame(twin.Handle)), "どちらも不変");
                    Stop(twin.Process);
                }),
                ("一致がなければ終了コード3", () => Equal(3, Cli("get", "--title", $"CLI-none-{tag}", "--json").ExitCode, "終了コード")),
                ("pick と --at は座標のウィンドウを選ぶ", () =>
                {
                    var frame = Win32.Frame(b.Handle);
                    var at = $"{frame.X + frame.Width / 2},{frame.Y + frame.Height / 2}";
                    Equal(b.Hex, Json("pick", "--at", at, "--json")["target"]!["hwnd"]!.GetValue<string>(), "pick");
                    Json("set", "--at", at, "--width", "450", "--json");
                    Equal(450, Win32.Frame(b.Handle).Width, "--at で変更");
                }),
                ("応答しない対象は終了コード5で要求を送らない", () =>
                {
                    var hung = StartFixture(fixtures, $"CLI-hung-{tag}", "--hung", "--position", "500,300");
                    Thread.Sleep(300);
                    var before = Win32.Frame(hung.Handle);
                    Equal(5, Cli("set", "--hwnd", hung.Hex, "--x", "20", "--json").ExitCode, "終了コード");
                    Equal(before, Win32.Frame(hung.Handle), "不変");
                    hung.Process.Kill();
                }),
                ("変更を受け付けない対象は終了コード7", () =>
                {
                    var locked = StartFixture(fixtures, $"CLI-locked-{tag}", "--locked", "--position", "600,320");
                    var run = Cli("set", "--hwnd", locked.Hex, "--x", "20", "--timeout", "600", "--json");
                    Equal(7, run.ExitCode, "終了コード");
                    Equal("not-confirmed", run.Json!["error"]!["code"]!.GetValue<string>(), "エラーの種類");
                }),
                ("get --basis client の中身はWin32のクライアント領域と一致", () =>
                {
                    var json = Json("get", "--hwnd", a.Hex, "--basis", "client", "--json");
                    Equal(Win32.Client(a.Handle), Rect(json["bounds"]!), "bounds");
                    Equal(Win32.Client(a.Handle), Rect(json["state"]!["client"]!), "state.client");
                }),
                ("set --basis client で中身の大きさを指定どおりにする", () =>
                {
                    var after = Json("set", "--hwnd", a.Hex, "--basis", "client", "--width", "640", "--height", "360", "--json");
                    Equal(new Size(640, 360), Win32.Client(a.Handle).Size, "実測の中身");
                    Equal("client", after["requested"]!["basis"]!.GetValue<string>(), "要求の基準");
                    Equal(new Size(640, 360), Rect(after["after"]!["client"]!).Size, "結果の中身");
                }),
                ("同じ要求をframeとclientで出すと、差は枠の太さと一致", () =>
                {
                    Json("set", "--hwnd", a.Hex, "--x", "100", "--y", "100", "--width", "700", "--height", "400", "--json");
                    var frame = Win32.Frame(a.Handle);
                    var client = Win32.Client(a.Handle);
                    Json("set", "--hwnd", a.Hex, "--basis", "client", "--x", "100", "--y", "100", "--width", "700", "--height", "400", "--json");
                    var byClient = Win32.Frame(a.Handle);
                    Equal(new Rectangle(100, 100, 700, 400), Win32.Client(a.Handle), "中身が要求どおり");
                    Equal(new Size(frame.Width - client.Width, frame.Height - client.Height), new Size(byClient.Width - 700, byClient.Height - 400), "枠の太さの差");
                    Equal(new Point(100 - (client.X - frame.X), 100 - (client.Y - frame.Y)), byClient.Location, "左上の差");
                }),
                ("--keep-aspect と --aspect で縦横比を保つ", () =>
                {
                    Json("set", "--hwnd", a.Hex, "--basis", "client", "--x", "60", "--y", "60", "--width", "640", "--height", "360", "--json");
                    Json("set", "--hwnd", a.Hex, "--basis", "client", "--width", "800", "--keep-aspect", "--json");
                    Equal(new Size(800, 450), Win32.Client(a.Handle).Size, "現在の16:9を保つ");
                    Json("set", "--hwnd", a.Hex, "--basis", "client", "--height", "480", "--aspect", "4:3", "--json");
                    Equal(new Size(640, 480), Win32.Client(a.Handle).Size, "指定の4:3");
                }),
                ("preset add と apply で中身を指定の大きさにし、位置は変えない", () => WithSettings(dir =>
                {
                    Json("preset", "add", "検証", "--width", "720", "--height", "405", "--settings-dir", dir, "--json");
                    var before = Win32.Frame(a.Handle);
                    var json = Json("preset", "apply", "検証", "--hwnd", a.Hex, "--settings-dir", dir, "--json");
                    Equal(new Size(720, 405), Win32.Client(a.Handle).Size, "実測の中身");
                    Equal(before.Location, Win32.Frame(a.Handle).Location, "位置は不変");
                    Equal("検証", json["preset"]!["name"]!.GetValue<string>(), "適用したプリセット");
                    var dry = Json("preset", "apply", "HD", "--hwnd", a.Hex, "--dry-run", "--settings-dir", dir, "--json");
                    True(dry["after"] is null && Win32.Client(a.Handle).Size == new Size(720, 405), "--dry-run は不変");
                })),
                ("--instance のGUIは利用者のGUIと分離され、一覧から除外される", () => IsolatedGui(tag)),
            ]);
        }
        finally
        {
            foreach (var process in fixtures) Stop(process);
        }
    }

    // --passive --instanceで起動したGUIは、専用のpipe名で応答し、CLIの一覧に現れない。
    private static void IsolatedGui(string tag)
    {
        var instance = "test-" + tag;
        var settings = Path.Combine(Path.GetTempPath(), "chunap-gui-" + tag);
        using var gui = Process.Start(new ProcessStartInfo(ProductPath("ChunapTool.exe"), ["--passive", "--instance", instance, "--settings-dir", settings]))!;
        try
        {
            var response = WaitFor(() => Management("chunap-tool." + instance, "status"), "検証用GUIの応答");
            Equal(gui.Id, response["pid"]!.GetValue<int>(), "検証用GUIが応答");
            True(Json("list", "--json")["windows"]!.AsArray().All(w => w!["pid"]!.GetValue<int>() != gui.Id), "一覧から除外");
            Equal("accepted", Management("chunap-tool." + instance, "stop")!["result"]!.GetValue<string>(), "停止要求");
            True(gui.WaitForExit(5000), "停止");
        }
        finally
        {
            if (!gui.HasExited) gui.Kill();
            if (Directory.Exists(settings)) Directory.Delete(settings, true);
        }
    }

    private static JsonNode? Management(string id, string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ManagementEndpoint.PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(500);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(command);
            return JsonNode.Parse(reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() ?? "");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { return null; }
    }

    private static FixtureWindow StartFixture(List<Process> fixtures, string title, params string[] options)
    {
        var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--fixture", "--title", title, .. options]))!;
        fixtures.Add(process);
        var json = WaitFor(() =>
        {
            var run = Cli("list", "--filter", title, "--json");
            return run.Json?["windows"]?.AsArray().FirstOrDefault(w => w!["pid"]!.GetValue<int>() == process.Id);
        }, "確認用ウィンドウの表示");
        var hex = json["hwnd"]!.GetValue<string>();
        return new(process, (nint)long.Parse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture), hex);
    }

    private static void Stop(Process process)
    {
        if (process.HasExited) return;
        if (!process.CloseMainWindow() || !process.WaitForExit(3000)) process.Kill();
    }

    private static T WaitFor<T>(Func<T?> probe, string what) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10000)
        {
            if (probe() is { } value) return value;
            Thread.Sleep(150);
        }
        throw new TimeoutException(what + "を10秒待っても確認できません。");
    }

    private static int RunCases(string name, (string Name, Action Execute)[] cases)
    {
        Console.WriteLine($"{name}: chunap = {ProductPath("chunap.exe")}");
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Execute(); Console.WriteLine($"成功: {test.Name}"); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"失敗: {test.Name}\n{ex}"); }
        }
        Console.WriteLine($"{name}: {cases.Length - failures}/{cases.Length} 成功");
        return failures == 0 ? 0 : 1;
    }

    private static CliRun Cli(params string[] args)
    {
        // JSONはASCIIのみ。文章の出力はコンソールのOEMコードページで受け取る。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var start = new ProcessStartInfo(ProductPath("chunap.exe"), args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("chunapが30秒で終了しません: " + string.Join(" ", args)); }
        var run = new CliRun(process.ExitCode, output.Result, error.Result);
        Console.WriteLine($"  実行: chunap {string.Join(" ", args.Select(Quote))} → 終了コード {run.ExitCode}");
        return run;
    }

    private static JsonNode Json(params string[] args)
    {
        var run = Cli(args);
        if (run.ExitCode != 0) throw new InvalidOperationException($"終了コード {run.ExitCode}: {run.Output}{run.Error}");
        return run.Json ?? throw new InvalidOperationException("JSONを出力しません: " + run.Output);
    }

    private static void Usage(params string[] args)
    {
        var run = Cli(args);
        Equal(2, run.ExitCode, "引数の誤りの終了コード: " + string.Join(" ", args));
        Equal("usage", run.Json!["error"]!["code"]!.GetValue<string>(), "エラーの種類");
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    // 製品の出力先（Debug）を、この検証の実行ファイルから辿って探す。
    private static string ProductPath(string file)
    {
        var project = file == "chunap.exe" ? "ChunapTool.Cli" : "ChunapTool";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", project, "bin", "Debug", "net10.0-windows10.0.19041.0", file);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("製品の実行ファイルが見つかりません。先にdev/scripts/build.ps1を実行してください。", file);
    }

    private static Rectangle Frame(JsonNode state) => Rect(state["frame"]!);

    private static Rectangle Rect(JsonNode bounds) =>
        new(bounds["x"]!.GetValue<int>(), bounds["y"]!.GetValue<int>(), bounds["width"]!.GetValue<int>(), bounds["height"]!.GetValue<int>());

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: 期待値 {expected} / 実際値 {actual}");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error)
    {
        public JsonNode? Json { get; } = TryParse(Output);
        private static JsonNode? TryParse(string text) { try { return JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { return null; } }
    }

    private sealed record FixtureWindow(Process Process, nint Handle, string Hex);

    /// <summary>製品のコードを通さずに、DWMの見える枠と最大化を読む（自己確認にしないため）。</summary>
    private static class Win32
    {
        internal static Rectangle Frame(nint window)
        {
            if (DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<Rect>()) < 0) throw new InvalidOperationException("DWMの枠を取得できません。");
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        internal static bool IsZoomed(nint window) => IsZoomedNative(window);

        internal static Rectangle Client(nint window)
        {
            if (!GetClientRect(window, out var rect)) throw new InvalidOperationException("クライアント領域を取得できません。");
            var origin = new Point();
            if (!ClientToScreen(window, ref origin)) throw new InvalidOperationException("クライアント座標を変換できません。");
            return new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(nint window, ref Point point);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);

        [DllImport("user32.dll", EntryPoint = "IsZoomed")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomedNative(nint window);
    }
}
