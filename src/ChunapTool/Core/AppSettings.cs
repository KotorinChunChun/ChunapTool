using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChunapTool.Core;

/// <summary>サイズのプリセット。位置は変えず、基準（既定はクライアント領域）の幅・高さだけを指定する。</summary>
public sealed record Preset(string Name, int Width, int Height, BoundsBasis Basis = BoundsBasis.Client)
{
    public override string ToString() => $"{Name}（{Width}×{Height}・{(Basis == BoundsBasis.Client ? "中身" : "枠")}）";
}

/// <summary>GUIの各チェックの保存値。既定値は従来の初期状態と同じ。</summary>
public sealed record GuiSettings
{
    public bool AutoRead { get; init; } = true;
    public bool AutoApply { get; init; } = true;
    public bool AllowOutside { get; init; }
    public bool ToolTopMost { get; init; } = true;
    public BoundsBasis Basis { get; init; } = BoundsBasis.Frame;
    public bool KeepAspect { get; init; }
    public bool PreviewExpanded { get; init; }

    /// <summary>
    /// 各項目の名前（config get/setとsettings.jsonのguiで共通）と読み書き。項目を増やすときはここだけに加える。
    /// IsFlagの項目は真偽値（JSONではtrue／false、CLIではon／offも可）、それ以外は文字列。
    /// </summary>
    internal sealed record Item(string Key, bool IsFlag, Func<GuiSettings, string> Read, Func<GuiSettings, string, GuiSettings> Write);

    internal static readonly IReadOnlyList<Item> Items =
    [
        Flag("autoRead", settings => settings.AutoRead, (settings, value) => settings with { AutoRead = value }),
        Flag("autoApply", settings => settings.AutoApply, (settings, value) => settings with { AutoApply = value }),
        Flag("allowOutside", settings => settings.AllowOutside, (settings, value) => settings with { AllowOutside = value }),
        Flag("toolTopMost", settings => settings.ToolTopMost, (settings, value) => settings with { ToolTopMost = value }),
        new("basis", false, settings => BoundsBases.Name(settings.Basis), (settings, value) =>
            BoundsBases.TryParse(value, out var basis) ? settings with { Basis = basis }
                : throw new ArgumentException($"basis には frame か client を指定してください: {value}")),
        Flag("keepAspect", settings => settings.KeepAspect, (settings, value) => settings with { KeepAspect = value }),
        Flag("previewExpanded", settings => settings.PreviewExpanded, (settings, value) => settings with { PreviewExpanded = value }),
    ];

    // Itemsより後に宣言する（静的初期化は宣言順）。
    public static readonly IReadOnlyList<string> Keys = Items.Select(item => item.Key).ToArray();

    public string Get(string key) => Find(key).Read(this);

    public GuiSettings With(string key, string value) => Find(key).Write(this, value);

    private static Item Find(string key) => Items.FirstOrDefault(item => item.Key == key)
        ?? throw new ArgumentException($"不明な設定です: {key}（{string.Join("、", Keys)}）");

    private static Item Flag(string key, Func<GuiSettings, bool> read, Func<GuiSettings, bool, GuiSettings> write) =>
        new(key, true, settings => read(settings) ? "true" : "false", (settings, value) => write(settings, value switch
        {
            "true" or "on" => true,
            "false" or "off" => false,
            _ => throw new ArgumentException($"{key} には true／false（on／off）を指定してください: {value}"),
        }));
}

/// <summary>プリセット一覧の編集。名前は大文字小文字を区別せず一意とし、不正な指定はArgumentExceptionで拒否する。</summary>
public static class Presets
{
    public const int MaximumSize = 100000;
    public const int MaximumNameLength = 64;

    public static readonly IReadOnlyList<Preset> Defaults =
    [
        new("FHD", 1920, 1080), new("HD", 1280, 720), new("縦長", 720, 1280), new("1024×768", 1024, 768), new("800×600", 800, 600),
    ];

    public static Preset? Find(IReadOnlyList<Preset> presets, string name) =>
        presets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Preset> Add(IReadOnlyList<Preset> presets, Preset preset)
    {
        Validate(preset);
        if (Find(presets, preset.Name) is not null) throw new ArgumentException($"同じ名前のプリセットがあります: {preset.Name}");
        return [.. presets, preset];
    }

    public static IReadOnlyList<Preset> Remove(IReadOnlyList<Preset> presets, string name)
    {
        var found = Require(presets, name);
        return presets.Where(preset => preset != found).ToArray();
    }

    public static IReadOnlyList<Preset> Rename(IReadOnlyList<Preset> presets, string name, string newName) =>
        Replace(presets, name, Require(presets, name) with { Name = newName.Trim() });

    /// <summary>名前で選んだプリセットを、同じ順番のまま別の内容（名前を含む）へ置き換える。</summary>
    public static IReadOnlyList<Preset> Replace(IReadOnlyList<Preset> presets, string name, Preset replacement)
    {
        var found = Require(presets, name);
        Validate(replacement);
        if (Find(presets, replacement.Name) is { } other && other != found) throw new ArgumentException($"同じ名前のプリセットがあります: {replacement.Name}");
        return presets.Select(preset => preset == found ? replacement : preset).ToArray();
    }

    /// <summary>positionは1から始まる移動先の順番。</summary>
    public static IReadOnlyList<Preset> Move(IReadOnlyList<Preset> presets, string name, int position)
    {
        var found = Require(presets, name);
        if (position < 1 || position > presets.Count) throw new ArgumentException($"移動先は1〜{presets.Count}で指定してください: {position}");
        var list = presets.Where(preset => preset != found).ToList();
        list.Insert(position - 1, found);
        return list;
    }

    public static void Validate(Preset preset)
    {
        var name = preset.Name;
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
            throw new ArgumentException("プリセット名を指定してください（前後に空白を含めない）。");
        if (name.Length > MaximumNameLength || name.Any(char.IsControl))
            throw new ArgumentException($"プリセット名は制御文字を含まない{MaximumNameLength}文字以内で指定してください。");
        if (preset.Width is < WindowGeometry.MinimumSize or > MaximumSize || preset.Height is < WindowGeometry.MinimumSize or > MaximumSize)
            throw new ArgumentException($"プリセットの幅と高さは{WindowGeometry.MinimumSize}〜{MaximumSize}で指定してください。");
    }

    private static Preset Require(IReadOnlyList<Preset> presets, string name) =>
        Find(presets, name) ?? throw new ArgumentException($"プリセットがありません: {name}");
}

/// <summary>settings.jsonの内容。未対応の項目（後の版の項目など）は元のJSONのまま保持して書き戻す。</summary>
public sealed class SettingsDocument
{
    public const int SchemaVersion = 1;
    private readonly JsonObject root;

    public GuiSettings Gui { get; set; }
    public IReadOnlyList<Preset> Presets { get; set; }

    public SettingsDocument() : this(new JsonObject()) { }

    private SettingsDocument(JsonObject root)
    {
        this.root = root;
        Gui = ReadGui(root["gui"]);
        // presetsがない（初回）場合だけ既定値とする。空の配列は「すべて削除した」として尊重する。
        Presets = root["presets"] is { } presets ? ReadPresets(presets) : Core.Presets.Defaults;
    }

    internal static SettingsDocument Parse(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { throw new SettingsException("JSONとして読めません: " + ex.Message); }
        if (node is not JsonObject root) throw new SettingsException("最上位がオブジェクトではありません。");
        var version = root["schemaVersion"] is { } versionNode ? Read<int>(versionNode, "schemaVersion") : SchemaVersion;
        if (version > SchemaVersion)
            throw new SettingsException($"新しい版の設定です（schemaVersion {version}）。この版では読み書きしません。", newerSchema: true);
        if (version < 1) throw new SettingsException($"schemaVersionが不正です: {version}");
        return new SettingsDocument(root);
    }

    internal string Serialize()
    {
        root["schemaVersion"] = SchemaVersion;
        // gui内の未対応の項目も残すため、既存のオブジェクトへ上書きする。
        var gui = root["gui"] as JsonObject ?? new JsonObject();
        foreach (var item in GuiSettings.Items)
        {
            var value = item.Read(Gui);
            gui[item.Key] = item.IsFlag ? JsonValue.Create(value == "true") : JsonValue.Create(value);
        }
        root["gui"] = gui;
        root["presets"] = new JsonArray(Presets.Select(preset => (JsonNode)new JsonObject
        {
            ["name"] = preset.Name, ["width"] = preset.Width, ["height"] = preset.Height, ["basis"] = BoundsBases.Name(preset.Basis),
        }).ToArray());
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static GuiSettings ReadGui(JsonNode? node)
    {
        if (node is null) return new GuiSettings();
        if (node is not JsonObject gui) throw new SettingsException("guiがオブジェクトではありません。");
        // ない項目は既定値。真偽値の項目はJSONの真偽値だけを受け付ける（"true"などの文字列は不正とする）。
        var settings = new GuiSettings();
        foreach (var item in GuiSettings.Items)
        {
            if (gui[item.Key] is not { } value) continue;
            var text = item.IsFlag ? (Read<bool>(value, "gui." + item.Key) ? "true" : "false") : Read<string>(value, "gui." + item.Key);
            try { settings = item.Write(settings, text); }
            catch (ArgumentException ex) { throw new SettingsException($"gui.{item.Key}: {ex.Message}"); }
        }
        return settings;
    }

    private static IReadOnlyList<Preset> ReadPresets(JsonNode node)
    {
        if (node is not JsonArray array) throw new SettingsException("presetsが配列ではありません。");
        IReadOnlyList<Preset> presets = [];
        for (int i = 0; i < array.Count; i++)
        {
            var item = array[i] as JsonObject ?? throw new SettingsException($"presets[{i}]がオブジェクトではありません。");
            var basis = BoundsBasis.Client;
            if (item["basis"] is { } basisNode && !BoundsBases.TryParse(Read<string>(basisNode, $"presets[{i}].basis"), out basis))
                throw new SettingsException($"presets[{i}].basisは frame か client です。");
            var preset = new Preset(Read<string>(item["name"], $"presets[{i}].name"), Read<int>(item["width"], $"presets[{i}].width"),
                Read<int>(item["height"], $"presets[{i}].height"), basis);
            try { presets = Core.Presets.Add(presets, preset); }
            catch (ArgumentException ex) { throw new SettingsException($"presets[{i}]: {ex.Message}"); }
        }
        return presets;
    }

    private static T Read<T>(JsonNode? node, string name)
    {
        try { return (node ?? throw new SettingsException(name + "がありません。")).GetValue<T>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new SettingsException($"{name}の型が不正です。");
        }
    }
}

/// <summary>読み込めない設定。NewerSchemaは新しい版の設定で、退避も上書きもしない。</summary>
public sealed class SettingsException(string message, bool newerSchema = false) : Exception(message)
{
    public bool NewerSchema { get; } = newerSchema;
}

/// <summary>
/// settings.jsonの読み書き。書き込みは一時ファイルから置き換える。
/// GUIとCLIが同じファイルを変更するため、変更は毎回ファイルを読み直してから行う（Update）。
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KotorinToys", "ChunapTool");

    public string Directory { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>ファイルがなければ既定値。読めなければSettingsException。</summary>
    public SettingsDocument Load()
    {
        string text;
        try { text = File.ReadAllText(FilePath); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new SettingsDocument(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException($"設定ファイルを読めません: {FilePath} ({ex.Message})");
        }
        try { return SettingsDocument.Parse(text); }
        catch (SettingsException ex) { throw new SettingsException($"{FilePath}: {ex.Message}", ex.NewerSchema); }
    }

    /// <summary>
    /// 壊れたファイルはsettings.json.broken-日時へ退避して既定値を返し、warningに理由を入れる。
    /// 新しい版の設定は退避せず、writableをfalseにして既定値を返す（この版から上書きしない）。
    /// </summary>
    public SettingsDocument LoadOrRecover(out string? warning, out bool writable)
    {
        warning = null;
        writable = true;
        try { return Load(); }
        catch (SettingsException ex) when (ex.NewerSchema)
        {
            writable = false;
            warning = ex.Message + " 既定値で起動し、設定を保存しません。";
            return new SettingsDocument();
        }
        catch (SettingsException ex)
        {
            var backup = Backup();
            warning = backup is null ? ex.Message + " 既定値で起動しました。"
                : $"{ex.Message} 壊れた設定を {Path.GetFileName(backup)} へ退避し、既定値で起動しました。";
            return new SettingsDocument();
        }
    }

    /// <summary>壊れた設定を退避する。ファイルがなければnull。</summary>
    public string? Backup()
    {
        if (!File.Exists(FilePath)) return null;
        var backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        File.Move(FilePath, backup);
        return backup;
    }

    public void Save(SettingsDocument document)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temporary = Path.Combine(Directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, document.Serialize());
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>ファイルを読み直して変更し、保存する。読めない設定は上書きしない。</summary>
    public SettingsDocument Update(Action<SettingsDocument> change)
    {
        var document = Load();
        change(document);
        Save(document);
        return document;
    }
}
