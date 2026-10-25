using System.IO.Pipes;
using System.Reflection;
using KotorinToys.Integration;

namespace ChunapTool.Tests;

internal static class ManagementTests
{
    public static void Run(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new ChunapTool.MainForm(new ChunapTool.Native.NativeWindowService(), passive: true);
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                var reason = window.GetType().GetProperty("ManagementStopBlockReason", hidden)!;
                if (reason.GetValue(window) is not null) throw new Exception("待機中の停止が拒否されました。");
                foreach (var name in new[] { "busy" })
                {
                    var flag = window.GetType().GetField(name, hidden)!;
                    flag.SetValue(window, true);
                    if (reason.GetValue(window) is not string { Length: > 0 }) throw new Exception(name + "中の管理停止を拒否しませんでした。");
                    flag.SetValue(window, false);
                }
                if (window.Visible || reason.GetValue(window) is not null) throw new Exception("検証後の状態が不正です。");
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("管理停止のガード検証に失敗しました。", failure);
        Console.WriteLine("PASS 管理連携: 待機時の停止許可・1状態の停止拒否・画面非表示");
        RequestParsing();
        Console.WriteLine("PASS 管理連携: 整数でない期待PIDと長すぎる要求を例外なく拒否");
        EndpointSurvivesInvalidRequests();
        Console.WriteLine("PASS 管理連携: 不正な要求の後も受付を継続");
        ExistingInstance();
        Console.WriteLine("PASS 管理連携: 二重起動で既存画面を表示し、表示できない理由を返す");
    }

    private static void RequestParsing()
    {
        Equal(("status", "", (int?)null), ManagementEndpoint.ParseRequest("status"), "旧形式の文字列要求");
        Equal(("show", "r1", (int?)42), ManagementEndpoint.ParseRequest("{\"command\":\"show\",\"requestId\":\"r1\",\"expectedPid\":42}"), "JSON要求");
        foreach (var pid in new[] { "1.5", "1e12", "-9999999999" })
            Equal(("status", "", (int?)0), ManagementEndpoint.ParseRequest("{\"command\":\"status\",\"expectedPid\":" + pid + "}"), "整数でない期待PID " + pid);
        Equal("status", ManagementEndpoint.ReadRequestAsync(new StringReader("status\r\nnext"), CancellationToken.None).GetAwaiter().GetResult(), "改行までを1要求とする");
        Equal("", ManagementEndpoint.ReadRequestAsync(new StringReader(""), CancellationToken.None).GetAwaiter().GetResult(), "空の要求");
        var exact = new string('a', ManagementEndpoint.MaximumRequestLength);
        Equal(exact, ManagementEndpoint.ReadRequestAsync(new StringReader(exact + "\n"), CancellationToken.None).GetAwaiter().GetResult(), "上限ちょうどの要求");
        try
        {
            ManagementEndpoint.ReadRequestAsync(new StringReader(exact + "a\n"), CancellationToken.None).GetAwaiter().GetResult();
            throw new Exception("上限を超える要求を受け付けました。");
        }
        catch (InvalidDataException) { }
    }

    private static void EndpointSurvivesInvalidRequests()
    {
        // 製品の管理名と衝突しない検証専用IDで、この検証プロセス内の受付だけを使う。
        var id = "chunap-tool-test-" + Guid.NewGuid().ToString("N");
        using var started = new ManualResetEventSlim();
        int shows = 0;
        Control? dispatcher = null;
        ManagementEndpoint? endpoint = null;
        var thread = new Thread(() =>
        {
            using var context = new ApplicationContext();
            dispatcher = new Control();
            _ = dispatcher.Handle;
            endpoint = new ManagementEndpoint(id, dispatcher, () => shows++, () => { });
            dispatcher.HandleDestroyed += (_, _) => context.ExitThread();
            started.Set();
            Application.Run(context);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        started.Wait();
        try
        {
            Contains("\"result\":\"refused\"", Send(id, "{\"command\":\"status\",\"expectedPid\":1.5}"), "整数でない期待PIDは拒否");
            Contains("\"result\":\"completed\"", Send(id, "status"), "整数でない期待PIDの後も受付を継続");
            _ = TrySend(id, new string('x', ManagementEndpoint.MaximumRequestLength * 2));
            Contains("\"result\":\"completed\"", Send(id, "status"), "長すぎる要求の後も受付を継続");
            _ = TrySend(id, "{\"command\":");
            Contains("\"result\":\"completed\"", Send(id, "status"), "壊れたJSONの後も受付を継続");
            // 同じ実行ファイルの既存インスタンス（ここでは検証プロセス自身）は前面化し、理由を返さない。
            Equal<string?>(null, ManagementEndpoint.ShowExisting(id), "同じ実行ファイルの既存画面を表示");
            Equal(1, Volatile.Read(ref shows), "既存画面の表示要求を1回だけ送る");
        }
        finally
        {
            endpoint?.Dispose();
            dispatcher?.BeginInvoke(new Action(() => dispatcher.Dispose()));
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static void ExistingInstance()
    {
        var own = Environment.ProcessPath;
        Equal<string?>(null, ManagementEndpoint.ExistingInstanceProblem(true, own, own), "同じ場所の実行ファイル");
        Equal<string?>(null, ManagementEndpoint.ExistingInstanceProblem(true, own?.ToUpperInvariant(), own), "大文字小文字だけが違う場所");
        var other = ManagementEndpoint.ExistingInstanceProblem(true, @"C:\Portable\ChunapTool.exe", own) ?? "";
        Contains("別の場所", other, "別の場所の実行ファイルは理由を返す");
        Contains(@"C:\Portable\ChunapTool.exe", other, "起動中の場所を示す");
        Contains("不明", ManagementEndpoint.ExistingInstanceProblem(true, null, own) ?? "", "起動中の場所を取得できない場合");
        Contains("セッション", ManagementEndpoint.ExistingInstanceProblem(false, own, own) ?? "", "別セッション");
        var missing = ManagementEndpoint.ShowExisting("chunap-tool-test-missing-" + Guid.NewGuid().ToString("N")) ?? "";
        Contains("応答しません", missing, "受付のない既存インスタンスは理由を返す");
    }

    private static string Send(string id, string request) =>
        TrySend(id, request) ?? throw new Exception($"管理要求へ応答がありません: {request[..Math.Min(40, request.Length)]}");

    private static string? TrySend(string id, string request)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", ManagementEndpoint.PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(request);
            return reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (IOException) { return null; }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: 期待値 {expected} / 実際値 {actual}");
    }

    private static void Contains(string expected, string actual, string message)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal)) throw new Exception($"{message}: {actual}");
    }
}
