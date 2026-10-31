using System.Text.Json.Nodes;
using ChunapTool.Core;
using ChunapTool.Native;
using ChunapTool.Operations;

namespace ChunapTool.Cli;

/// <summary>コマンドの実行と出力。結果は文章（既定）か、--jsonでJSONを1件出力する。</summary>
internal sealed class CliApplication(IWindowService windows, IWindowTargetPicker picker, TextWriter output, TextWriter error)
{
    private readonly WindowOperations operations = new(windows);

    internal static int Run(string[] args) =>
        new CliApplication(new NativeWindowService(), new WindowTargetPicker(), Console.Out, Console.Error)
            .RunAsync(args).GetAwaiter().GetResult();

    internal async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        bool json = args.Contains("--json");
        string command = "";
        try
        {
            var arguments = CliArguments.Parse(args);
            json = arguments.Flag("json");
            command = arguments.Word(0) ?? "help";
            var result = await ExecuteAsync(command, arguments);
            result.Json["ok"] = true;
            result.Json["command"] = command;
            output.WriteLine(json ? CliJson.Serialize(Ordered(result.Json)) : result.Text);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            var failure = CliException.From(ex);
            if (json)
            {
                var errorNode = new JsonObject { ["code"] = failure.Code, ["message"] = failure.Message };
                if (failure.Details is JsonNode details) errorNode["details"] = details;
                output.WriteLine(CliJson.Serialize(new JsonObject { ["ok"] = false, ["command"] = command, ["error"] = errorNode }));
            }
            else
            {
                error.WriteLine($"エラー: {failure.Message}");
                if (failure.Details is JsonObject { } detail && detail["candidates"] is JsonArray candidates)
                    foreach (var candidate in candidates) error.WriteLine("  候補: " + Describe(candidate!));
            }
            return failure.ExitCode;
        }
    }

    // 引数はすべて読み取って検査してから（EnsureAllUsed）、対象の解決と変更を行う。綴り誤りのオプションで対象を動かさない。
    private async Task<CommandResult> ExecuteAsync(string command, CliArguments arguments)
    {
        if (arguments.Words.Count > (command == "help" ? 2 : 1))
            throw CliException.Usage("余分な引数があります: " + string.Join(" ", arguments.Words.Skip(command == "help" ? 2 : 1)));
        switch (command)
        {
            case "help":
                arguments.EnsureAllUsed();
                return Help(arguments.Word(1));
            case "version":
                arguments.EnsureAllUsed();
                return new(new JsonObject { ["version"] = Version }, $"chunap {Version}");
            case "list":
                {
                    var filter = arguments.Text("filter") ?? "";
                    arguments.EnsureAllUsed();
                    return List(filter);
                }
            case "pick":
                {
                    var at = arguments.Coordinate("at") ?? throw CliException.Usage("pick には --at x,y を指定してください。");
                    arguments.EnsureAllUsed();
                    var entry = Resolve(new TargetCriteria { At = at });
                    return new(new JsonObject { ["target"] = CliJson.Target(entry) }, $"{at.X},{at.Y} の対象: {Describe(entry)}");
                }
            case "get":
                {
                    var criteria = arguments.Criteria();
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    var state = operations.GetState(entry.Identity);
                    return new(new JsonObject { ["target"] = CliJson.Target(entry), ["state"] = CliJson.State(state) },
                        $"{Describe(entry)}\n{DescribeState(state)}");
                }
            case "set":
                {
                    var criteria = arguments.Criteria();
                    var request = new BoundsRequest(arguments.Integer("x"), arguments.Integer("y"),
                        arguments.Integer("width", 1), arguments.Integer("height", 1));
                    if (request.IsEmpty) throw CliException.Usage("set には --x --y --width --height のいずれかを指定してください。");
                    var (allowOutside, dryRun, timeout) = (arguments.Flag("allow-outside"), arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    return Changed(entry, await operations.SetBoundsAsync(entry.Identity, request, allowOutside, dryRun, timeout), "位置・サイズ");
                }
            case "maximize":
            case "unmaximize":
                {
                    var criteria = arguments.Criteria();
                    var (dryRun, timeout) = (arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    var result = await operations.SetMaximizedAsync(entry.Identity, command == "maximize", dryRun, timeout);
                    return Changed(entry, result, command == "maximize" ? "最大化" : "通常表示");
                }
            case "constrain":
                {
                    var criteria = arguments.Criteria();
                    var (dryRun, timeout) = (arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    // 現在の位置・サイズを、画面外可OFFの規則で作業領域へ収める。
                    var result = await operations.SetBoundsAsync(entry.Identity, new BoundsRequest(null, null, null, null), false, dryRun, timeout);
                    return Changed(entry, result, "作業領域への補正");
                }
            case "restore":
                {
                    var criteria = arguments.Criteria();
                    var path = arguments.Text("from") ?? throw CliException.Usage("restore には --from <JSONファイル> を指定してください。");
                    var (dryRun, timeout) = (arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    string text;
                    try { text = File.ReadAllText(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw new CliException(ExitCodes.Usage, "invalid-state", $"ファイルを読めません: {path} ({ex.Message})");
                    }
                    var entry = Resolve(criteria);
                    var saved = CliJson.ReadState(text, entry.Identity);
                    return Changed(entry, await operations.RestoreAsync(entry.Identity, saved, dryRun, timeout), "保存した状態の復元");
                }
            default:
                throw CliException.Usage($"不明なコマンドです: {command}（chunap help で一覧を表示）");
        }
    }

    private CommandResult List(string filter)
    {
        var entries = windows.Enumerate("").Where(entry => !TargetSelector.IsExcluded(entry)
            && (filter.Length == 0 || WindowCandidates.Matches(entry, filter))).ToArray();
        var array = new JsonArray(entries.Select(entry => (JsonNode)CliJson.Target(entry)).ToArray());
        var text = entries.Length == 0 ? "該当するウィンドウはありません。" : string.Join("\n", entries.Select(Describe));
        return new(new JsonObject { ["windows"] = array, ["count"] = entries.Length }, text);
    }

    // 一意に決まらない場合は操作せず、候補を返す。
    private WindowEntry Resolve(TargetCriteria criteria)
    {
        if (criteria.IsEmpty)
            throw CliException.Usage("対象を指定してください（--hwnd --pid --title --title-exact --process --class --at --foreground）。");
        WindowIdentity? anchor = null;
        if (criteria.At is { } point)
            anchor = picker.Resolve(point)?.Identity ?? throw NotFound($"{point.X},{point.Y} に操作できるウィンドウがありません。");
        if (criteria.Foreground)
        {
            var foreground = windows.GetForeground() ?? throw NotFound("前面ウィンドウを取得できません。");
            if (anchor is not null && anchor != foreground) throw NotFound("座標の対象と前面ウィンドウが一致しません。");
            anchor = foreground;
        }
        var matches = TargetSelector.Filter(windows.Enumerate(""), criteria, anchor);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw NotFound("指定に一致するウィンドウがありません。"),
            _ => throw new CliException(ExitCodes.Ambiguous, "ambiguous-target",
                $"指定に一致するウィンドウが {matches.Count} 件あるため操作しません。条件を追加してください。",
                new JsonObject { ["candidates"] = new JsonArray(matches.Select(entry => (JsonNode)CliJson.Target(entry)).ToArray()) }),
        };
    }

    private static CliException NotFound(string message) => new(ExitCodes.NotFound, "target-not-found", message);

    private static CommandResult Changed(WindowEntry entry, OperationResult result, string action)
    {
        var json = new JsonObject
        {
            ["dryRun"] = result.After is null,
            ["target"] = CliJson.Target(entry),
            ["before"] = CliJson.State(result.Before),
            ["requested"] = result.Requested is { } requested ? CliJson.Bounds(requested) : null,
            ["after"] = result.After is { } after ? CliJson.State(after) : null,
            ["warnings"] = new JsonArray(result.Warnings.Select(warning => (JsonNode)warning).ToArray()),
        };
        var lines = new List<string> { Describe(entry) };
        lines.Add(result.After is null
            ? $"{action}の試行（変更なし）: 要求 {DescribeBounds(result.Requested)}"
            : $"{action}: {DescribeBounds(result.Before.Bounds)} → {DescribeBounds(result.After.Bounds)}");
        lines.AddRange(result.Warnings.Select(warning => "注意: " + warning));
        return new(json, string.Join("\n", lines));
    }

    private static JsonObject Ordered(JsonObject json)
    {
        // ok・commandを先頭に置いて読みやすくする。
        var ordered = new JsonObject { ["ok"] = json["ok"]?.DeepClone(), ["command"] = json["command"]?.DeepClone() };
        foreach (var (key, value) in json)
            if (key is not "ok" and not "command") ordered[key] = value?.DeepClone();
        return ordered;
    }

    private static string Describe(WindowEntry entry) =>
        $"{CliJson.Handle(entry.Identity.Handle)} [{entry.ProcessName}] {entry.Title} (PID {entry.Identity.ProcessId})";

    private static string Describe(JsonNode target) =>
        $"{target["hwnd"]} [{target["process"]}] {target["title"]} (PID {target["pid"]})";

    private static string DescribeBounds(WindowBounds? bounds) =>
        bounds is { } b ? $"X={b.X} Y={b.Y} W={b.Width} H={b.Height}" : "なし";

    private static string DescribeState(WindowState state) =>
        $"見える枠: {DescribeBounds(state.Bounds)}"
        + (state.IsMaximized ? "（最大化中）" : state.IsMinimized ? "（最小化中）" : "");

    private static string Version
    {
        get
        {
            var version = typeof(CliApplication).Assembly.GetName().Version!;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private static CommandResult Help(string? topic)
    {
        var text = topic is null ? CliHelp.Overview : CliHelp.For(topic)
            ?? throw CliException.Usage($"不明なコマンドです: {topic}（chunap help で一覧を表示）");
        return new(new JsonObject { ["help"] = text }, text);
    }

    private sealed record CommandResult(JsonObject Json, string Text);
}
