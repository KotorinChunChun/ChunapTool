using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace KotorinToys.Integration;

// 共有契約。ユーザー専用、UIスレッド応答、PID指定で別インスタンスへの操作を防ぐ。
internal sealed class ManagementEndpoint : IDisposable
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(int processId);
    readonly CancellationTokenSource lifetime = new();
    public static string PipeName(string id) => "KotorinToys." + WindowsIdentity.GetCurrent().User!.Value + "." + id;
    public static string LaunchOrigin
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--launch-origin");
            return index >= 0 && index + 1 < args.Length && args[index + 1] is "via_kotorintoys" or "direct_task" or "host_manual"
                ? args[index + 1] : "standalone";
        }
    }

    public ManagementEndpoint(string id, Control dispatcher, Action show, Action stop,
        Func<string?>? stopBlockReason = null, Func<object?>? shortcuts = null)
    {
        _ = dispatcher.Handle;
        _ = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(lifetime.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var (command, requestId, expectedPid) = ParseRequest(await ReadRequestAsync(reader, deadline.Token));
                    var result = "completed";
                    var detail = "";
                    object? keyState = null;
                    bool stopAccepted = false;
                    var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        // タイムアウト後に遅れて届いた操作を実行しない。
                        if (deadline.IsCancellationRequested || lifetime.IsCancellationRequested) return;
                        try
                        {
                            if (expectedPid.HasValue && expectedPid != Environment.ProcessId) { result = "refused"; detail = "対象PIDが変わりました。"; }
                            else if (command == "show") show();
                            else if (command == "stop")
                            {
                                detail = stopBlockReason?.Invoke() ?? "";
                                result = detail.Length == 0 ? "accepted" : "refused";
                                stopAccepted = result == "accepted";
                            }
                            else if (command == "shortcuts")
                            {
                                if (shortcuts == null) { result = "unsupported"; detail = "実効キー情報は未対応です。"; }
                                else keyState = shortcuts();
                            }
                            else if (command != "status") { result = "unsupported"; detail = "未対応の操作です。"; }
                            applied.TrySetResult(true);
                        }
                        catch (Exception) { result = "failed"; detail = "UI操作を完了できませんでした。"; applied.TrySetResult(false); }
                    }));
                    var uiReady = await applied.Task.WaitAsync(deadline.Token);
                    var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                    var capabilities = shortcuts == null ? new[] { "status", "show", "stop" } : new[] { "status", "show", "stop", "shortcuts" };
                    // 応答を読まないクライアントが単一の受付枠を占有し続けないよう、書込みにも期限を設ける。
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new {
                        protocolVersion = 2, requestId, pid = Environment.ProcessId, ready = uiReady,
                        version = version == null ? null : $"{version.Major}.{version.Minor}.{version.Build}",
                        launchOrigin = LaunchOrigin, capabilities, result, detail, shortcuts = keyState
                    }).AsMemory(), deadline.Token);
                    // 終了要求の受理を返してから、既存の正常終了へ渡す。完了はクライアントがプロセスで確認する。
                    if (stopAccepted && !dispatcher.IsDisposed) dispatcher.BeginInvoke(stop);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    // 1件の不正な要求や想定外の失敗で受付ループを終了させない。連続失敗時は少し待って再試行する。
                    Debug.WriteLine($"管理要求を処理できませんでした: {ex.GetType().Name}");
                    try { await Task.Delay(RetryDelay, lifetime.Token); }
                    catch (OperationCanceledException) { }
                }
            }
        });
    }

    internal const int MaximumRequestLength = 4096;
    static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    // 改行までの1要求を読む。上限を超える要求は全体を読まずに拒否する。
    internal static async Task<string> ReadRequestAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var line = new System.Text.StringBuilder();
        var buffer = new char[256];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return line.ToString();
            int end = Array.IndexOf(buffer, '\n', 0, count);
            line.Append(buffer, 0, end >= 0 ? end : count);
            if (line.Length > MaximumRequestLength) throw new InvalidDataException("管理要求が長すぎます。");
            if (end >= 0) return line.ToString().TrimEnd('\r');
        }
    }

    // 文字列のみの旧形式と、JSONの新形式を解釈する。期待PIDが整数でなければ一致しない値として扱う。
    internal static (string Command, string RequestId, int? ExpectedPid) ParseRequest(string line)
    {
        if (!line.StartsWith('{')) return (line, "", null);
        using var request = JsonDocument.Parse(line);
        var command = request.RootElement.GetProperty("command").GetString() ?? "";
        var requestId = request.RootElement.TryGetProperty("requestId", out var req) ? req.GetString() ?? "" : "";
        int? expectedPid = null;
        if (request.RootElement.TryGetProperty("expectedPid", out var pid) && pid.ValueKind == JsonValueKind.Number)
            expectedPid = pid.TryGetInt32(out var value) ? value : 0;
        return (command, requestId, expectedPid);
    }

    public static void ShowExisting(string id)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1200);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine("status");
            var line = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            using var response = JsonDocument.Parse(line ?? "");
            var pid = response.RootElement.GetProperty("pid").GetInt32();
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId ||
                !string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            AllowSetForegroundWindow(pid);
            using var showPipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            showPipe.Connect(1200);
            using var showWriter = new StreamWriter(showPipe) { AutoFlush = true };
            showWriter.WriteLine(JsonSerializer.Serialize(new { command = "show", expectedPid = pid }));
            using var showReader = new StreamReader(showPipe);
            _ = showReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() => lifetime.Cancel();
}
