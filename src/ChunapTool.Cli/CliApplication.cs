using System.Text.Json.Nodes;
using ChunapTool.Core;
using ChunapTool.Native;
using ChunapTool.Operations;

namespace ChunapTool.Cli;

/// <summary>コマンドの実行と出力。結果は文章（既定）か、--jsonでJSONを1件出力する。</summary>
internal sealed class CliApplication(IWindowService windows, IWindowTargetPicker picker, TextWriter output, TextWriter error)
{
    private readonly WindowOperations operations = new(windows);
    private SettingsStore settings = new(SettingsStore.DefaultDirectory);

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
            // 共通オプション。設定を使わないコマンドで指定しても誤りにしない。
            if (arguments.Text("settings-dir") is { } directory)
            {
                try { settings = new SettingsStore(directory); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    throw CliException.Usage($"--settings-dir のフォルダー名が不正です: {directory}");
                }
            }
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
        // サブコマンドと名前を取るpreset・configは、それぞれで語数を検査する。
        if (command is not "preset" and not "config" && arguments.Words.Count > (command == "help" ? 2 : 1))
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
                    var basis = arguments.Basis();
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    var state = operations.GetState(entry.Identity);
                    if (basis == BoundsBasis.Client && state.ClientBounds is null)
                        throw new CliException(ExitCodes.Failure, "operation-failed", "対象のクライアント領域を取得できません（最小化中など）。");
                    return new(new JsonObject
                    {
                        ["target"] = CliJson.Target(entry), ["basis"] = BoundsBases.Name(basis),
                        ["bounds"] = CliJson.Bounds(BoundsBases.Read(state, basis)), ["state"] = CliJson.State(state),
                    }, $"{Describe(entry)}\n{DescribeState(state, basis)}");
                }
            case "set":
                {
                    var criteria = arguments.Criteria();
                    var request = new BoundsRequest(arguments.Integer("x"), arguments.Integer("y"),
                        arguments.Integer("width", 1), arguments.Integer("height", 1)) { Basis = arguments.Basis() };
                    if (request.IsEmpty) throw CliException.Usage("set には --x --y --width --height のいずれかを指定してください。");
                    var (keepAspect, aspectText) = (arguments.Flag("keep-aspect"), arguments.Text("aspect"));
                    if (keepAspect && aspectText is not null) throw CliException.Usage("--keep-aspect と --aspect は同時に指定できません。");
                    double? aspect = null;
                    if (aspectText is not null)
                        aspect = AspectRatio.TryParse(aspectText, out var ratio) ? ratio
                            : throw CliException.Usage($"--aspect は「16:9」の形式か正の数で指定してください: {aspectText}");
                    if ((keepAspect || aspect is not null) && request.Width is not null && request.Height is not null)
                        throw CliException.Usage("比率を固定する場合は、--width と --height のどちらか一方だけを指定してください。");
                    var (allowOutside, dryRun, timeout) = (arguments.Flag("allow-outside"), arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    var entry = Resolve(criteria);
                    if (keepAspect)
                    {
                        // 比率は対象の現在のサイズ（指定の基準）から求める。
                        var current = BoundsBases.Read(operations.GetState(entry.Identity), request.Basis);
                        aspect = AspectRatio.Of(current.Width, current.Height);
                    }
                    var result = await operations.SetBoundsAsync(entry.Identity, request with { Aspect = aspect }, allowOutside, dryRun, timeout);
                    return Changed(entry, result, "位置・サイズ");
                }
            case "preset":
                return await PresetAsync(arguments);
            case "config":
                return Config(arguments);
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

    private async Task<CommandResult> PresetAsync(CliArguments arguments)
    {
        var sub = arguments.Word(1) ?? throw CliException.Usage("preset のサブコマンドを指定してください（list add remove rename move apply）。");
        switch (sub)
        {
            case "list":
                {
                    Words(arguments, 2, "chunap preset list");
                    arguments.EnsureAllUsed();
                    var presets = settings.Load().Presets;
                    var text = presets.Count == 0 ? "プリセットはありません。"
                        : string.Join("\n", presets.Select((preset, i) => $"{i + 1}. {preset}"));
                    return new(new JsonObject { ["path"] = settings.FilePath, ["presets"] = CliJson.Presets(presets) }, text);
                }
            case "add":
                {
                    Words(arguments, 3, "chunap preset add <名前> --width N --height N [--basis client|frame]");
                    var width = arguments.Integer("width") ?? throw CliException.Usage("preset add には --width と --height を指定してください。");
                    var height = arguments.Integer("height") ?? throw CliException.Usage("preset add には --width と --height を指定してください。");
                    var preset = new Preset(arguments.Word(2)!, width, height, arguments.Basis(BoundsBasis.Client));
                    arguments.EnsureAllUsed();
                    return PresetChanged(UpdatePresets(presets => Presets.Add(presets, preset)), $"プリセットを追加しました: {preset}");
                }
            case "remove":
                {
                    Words(arguments, 3, "chunap preset remove <名前>");
                    arguments.EnsureAllUsed();
                    var name = arguments.Word(2)!;
                    return PresetChanged(UpdatePresets(presets => Presets.Remove(presets, name)), $"プリセットを削除しました: {name}");
                }
            case "rename":
                {
                    Words(arguments, 4, "chunap preset rename <名前> <新しい名前>");
                    arguments.EnsureAllUsed();
                    var (name, newName) = (arguments.Word(2)!, arguments.Word(3)!);
                    return PresetChanged(UpdatePresets(presets => Presets.Rename(presets, name, newName)), $"プリセットの名前を変更しました: {name} → {newName}");
                }
            case "move":
                {
                    Words(arguments, 3, "chunap preset move <名前> --to <順番（1から）>");
                    var position = arguments.Integer("to") ?? throw CliException.Usage("preset move には --to <順番（1から）> を指定してください。");
                    arguments.EnsureAllUsed();
                    var name = arguments.Word(2)!;
                    return PresetChanged(UpdatePresets(presets => Presets.Move(presets, name, position)), $"プリセットを{position}番目へ移動しました: {name}");
                }
            case "apply":
                {
                    Words(arguments, 3, "chunap preset apply <名前> <対象の指定>");
                    var criteria = arguments.Criteria();
                    var (allowOutside, dryRun, timeout) = (arguments.Flag("allow-outside"), arguments.Flag("dry-run"), arguments.Timeout);
                    arguments.EnsureAllUsed();
                    var name = arguments.Word(2)!;
                    var preset = Presets.Find(settings.Load().Presets, name)
                        ?? throw CliException.Usage($"プリセットがありません: {name}（chunap preset list で一覧を表示）");
                    var entry = Resolve(criteria);
                    var result = await operations.ApplyPresetAsync(entry.Identity, preset, allowOutside, dryRun, timeout);
                    var changed = Changed(entry, result, $"プリセット「{preset.Name}」の適用");
                    changed.Json["preset"] = CliJson.Preset(preset);
                    return changed;
                }
            default:
                throw CliException.Usage($"不明なサブコマンドです: preset {sub}（list add remove rename move apply）");
        }
    }

    // 一覧の編集は、名前の重複・存在しない名前などを引数の誤り（終了コード2）とする。
    private SettingsDocument UpdatePresets(Func<IReadOnlyList<Preset>, IReadOnlyList<Preset>> change)
    {
        try { return settings.Update(document => document.Presets = change(document.Presets)); }
        catch (ArgumentException ex) { throw CliException.Usage(ex.Message); }
    }

    private CommandResult PresetChanged(SettingsDocument document, string message) =>
        new(new JsonObject { ["path"] = settings.FilePath, ["presets"] = CliJson.Presets(document.Presets) }, message);

    private CommandResult Config(CliArguments arguments)
    {
        var sub = arguments.Word(1) ?? throw CliException.Usage("config のサブコマンドを指定してください（path get set reset）。");
        switch (sub)
        {
            case "path":
                Words(arguments, 2, "chunap config path");
                arguments.EnsureAllUsed();
                return new(new JsonObject { ["path"] = settings.FilePath, ["exists"] = File.Exists(settings.FilePath) }, settings.FilePath);
            case "get":
                {
                    if (arguments.Words.Count > 3) throw CliException.Usage("使い方: chunap config get [キー]");
                    var name = arguments.Word(2) is { } key ? GuiKey(key) : null;
                    arguments.EnsureAllUsed();
                    var gui = settings.Load().Gui;
                    if (name is not null)
                        return new(new JsonObject { ["key"] = "gui." + name, ["value"] = CliJson.SettingValue(gui.Get(name)) }, gui.Get(name));
                    var all = new JsonObject();
                    foreach (var item in GuiSettings.Keys) all["gui." + item] = CliJson.SettingValue(gui.Get(item));
                    return new(new JsonObject { ["path"] = settings.FilePath, ["values"] = all },
                        string.Join("\n", GuiSettings.Keys.Select(item => $"gui.{item} = {gui.Get(item)}")));
                }
            case "set":
                {
                    Words(arguments, 4, "chunap config set <キー> <値>");
                    arguments.EnsureAllUsed();
                    var (name, value) = (GuiKey(arguments.Word(2)!), arguments.Word(3)!);
                    // 値の誤りは、設定ファイルを読む前に終了コード2で拒否する。
                    try { new GuiSettings().With(name, value); }
                    catch (ArgumentException ex) { throw CliException.Usage(ex.Message); }
                    var document = settings.Update(document => document.Gui = document.Gui.With(name, value));
                    var saved = document.Gui.Get(name);
                    return new(new JsonObject { ["key"] = "gui." + name, ["value"] = CliJson.SettingValue(saved), ["path"] = settings.FilePath },
                        $"gui.{name} = {saved}（起動中のGUIには、次回の起動から反映されます）");
                }
            case "reset":
                {
                    Words(arguments, 2, "chunap config reset");
                    arguments.EnsureAllUsed();
                    // 読める設定はGUIの項目だけを既定値へ戻し、プリセットなどは残す。読めない設定は退避してから既定値で作り直す。
                    string? backup = null;
                    try { settings.Update(document => document.Gui = new GuiSettings()); }
                    catch (SettingsException ex) when (!ex.NewerSchema)
                    {
                        backup = settings.Backup();
                        settings.Save(new SettingsDocument());
                    }
                    return new(new JsonObject { ["path"] = settings.FilePath, ["backup"] = backup },
                        backup is null ? "GUIの設定を既定値へ戻しました（プリセットは残しています）。"
                            : $"読めない設定を {backup} へ退避し、既定値で作り直しました。");
                }
            default:
                throw CliException.Usage($"不明なサブコマンドです: config {sub}（path get set reset）");
        }
    }

    private static string GuiKey(string key) =>
        key.StartsWith("gui.", StringComparison.Ordinal) && GuiSettings.Keys.Contains(key[4..]) ? key[4..]
            : throw CliException.Usage($"不明な設定です: {key}（{string.Join("、", GuiSettings.Keys.Select(name => "gui." + name))}）");

    private static void Words(CliArguments arguments, int count, string usage)
    {
        if (arguments.Words.Count != count) throw CliException.Usage("使い方: " + usage);
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
            ["requested"] = result.Requested is { } requested ? CliJson.Requested(requested, result.Basis, result.RequestedInBasis) : null,
            ["after"] = result.After is { } after ? CliJson.State(after) : null,
            ["warnings"] = new JsonArray(result.Warnings.Select(warning => (JsonNode)warning).ToArray()),
        };
        var lines = new List<string> { Describe(entry) };
        var basis = result.Basis;
        var label = basis == BoundsBasis.Client ? "中身" : "見える枠";
        lines.Add(result.After is null
            ? $"{action}の試行（変更なし）: 要求（{label}） {DescribeBounds(result.RequestedInBasis ?? result.Requested)}"
            : $"{action}（{label}）: {DescribeBounds(BoundsBases.Read(result.Before, basis))} → {DescribeBounds(BoundsBases.Read(result.After, basis))}");
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

    private static string DescribeState(WindowState state, BoundsBasis basis) =>
        (basis == BoundsBasis.Client ? $"中身: {DescribeBounds(BoundsBases.Read(state, basis))}\n" : "")
        + $"見える枠: {DescribeBounds(state.Bounds)}"
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
