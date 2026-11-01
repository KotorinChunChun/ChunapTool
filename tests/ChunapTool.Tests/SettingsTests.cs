using ChunapTool.Core;
using ChunapTool.Operations;

namespace ChunapTool.Tests;

/// <summary>P2（設定、プリセット、クライアント基準、比率固定）の純粋な計算と設定ファイルの読み書き。設定は一時フォルダーだけを使う。</summary>
internal static class SettingsTests
{
    private static readonly WindowIdentity Target = new((nint)123, 456);

    public static int Run()
    {
        (string Name, Action Execute)[] tests =
        [
            ("中身の基準と見える枠を4辺の差で往復換算", BasisRoundTrip),
            ("クライアント領域がない状態は見える枠として扱う", BasisWithoutClient),
            ("比率の指定を解釈し不正な値を拒否", AspectParse),
            ("比率固定は片方の辺から四捨五入で他方を求める", AspectFit),
            ("比率固定で最小サイズを満たさない辺は他方から逆算", AspectMinimum),
            ("比率を保ったまま作業領域に収まるよう縮める", AspectShrink),
            ("プリセットの追加・名前変更・置換・移動・削除", PresetEditing),
            ("プリセットの重複・不正な名前とサイズを拒否", PresetValidation),
            ("設定ファイルがなければ既定値と既定のプリセット", SettingsDefaults),
            ("設定を保存して読み直し、未対応の項目を保持", SettingsRoundTrip),
            ("空のプリセット一覧は削除済みとして保つ", SettingsEmptyPresets),
            ("読めない設定は例外とし、復旧時は退避して既定値", SettingsBroken),
            ("新しい版の設定は退避も上書きもしない", SettingsNewerSchema),
            ("GUI設定の名前と値を検査して変更", GuiSettingKeys),
            ("中身の基準の要求を見える枠へ換算して作業領域へ収める", PlanClientBasis),
            ("比率固定の要求は収めるときも比率を保つ", PlanAspectShrink),
            ("クライアント領域がなければ中身の基準を拒否", PlanClientUnavailable),
        ];

        int failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Execute();
                Console.WriteLine($"成功: {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失敗: {test.Name}\n{exception}");
            }
        }

        Console.WriteLine($"設定・サイズ合わせの単体検証: {tests.Length - failures}/{tests.Length} 成功");
        return failures == 0 ? 0 : 1;
    }

    // 左右下8px、上31px（タイトルバー）の枠を持つウィンドウ。
    private static WindowState State(WindowBounds frame, bool withClient = true) =>
        new(Target, frame, new WindowPlacement(0, 1, new WindowPoint(-1, -1), new WindowPoint(-1, -1), frame), true)
        {
            ClientBounds = withClient ? new WindowBounds(frame.X + 8, frame.Y + 31, frame.Width - 16, frame.Height - 39) : null,
        };

    private static void BasisRoundTrip()
    {
        var state = State(new WindowBounds(100, 50, 816, 489));
        Equal(new WindowBounds(108, 81, 800, 450), BoundsBases.Read(state, BoundsBasis.Client));
        var frame = BoundsBases.ToFrame(new WindowBounds(0, 0, 1280, 720), state, BoundsBasis.Client);
        Equal(new WindowBounds(-8, -31, 1296, 759), frame);
        Equal(new WindowBounds(0, 0, 1280, 720), BoundsBases.FromFrame(frame, state, BoundsBasis.Client));
        Equal(frame, BoundsBases.ToFrame(frame, state, BoundsBasis.Frame));
    }

    private static void BasisWithoutClient()
    {
        var state = State(new WindowBounds(100, 50, 816, 489), withClient: false);
        Equal(state.Bounds, BoundsBases.Read(state, BoundsBasis.Client));
        Equal(new WindowBounds(1, 2, 3, 4), BoundsBases.ToFrame(new WindowBounds(1, 2, 3, 4), state, BoundsBasis.Client));
    }

    private static void AspectParse()
    {
        True(AspectRatio.TryParse("16:9", out var ratio) && Math.Abs(ratio - 16.0 / 9) < 1e-12, "16:9");
        True(AspectRatio.TryParse("1.5", out ratio) && ratio == 1.5, "小数");
        foreach (var text in new[] { "", "16:0", "0:9", "-4:3", "a:b", "16:9:1", "NaN", "Infinity" })
            True(!AspectRatio.TryParse(text, out _), "不正な比率: " + text);
    }

    private static void AspectFit()
    {
        Equal((1600, 900), AspectRatio.Fit(1600, null, 16.0 / 9, 1280));
        Equal((1280, 720), AspectRatio.Fit(null, 720, 16.0 / 9, 1));
        Equal((1000, 563), AspectRatio.Fit(1000, null, 16.0 / 9, 1)); // 562.5は切り上げ
        Equal((640, 360), AspectRatio.Fit(null, null, 16.0 / 9, 640));
    }

    private static void AspectMinimum()
    {
        Equal((512, 32), AspectRatio.Fit(100, null, 16, 1));
        Equal((32, 512), AspectRatio.Fit(null, 100, 1.0 / 16, 1));
        Equal((32, 32), AspectRatio.Fit(10, null, 1, 1));
    }

    private static void AspectShrink()
    {
        Equal((1600, 900), AspectRatio.Shrink(1600, 900, 16.0 / 9, 1920, 1040));
        Equal((1849, 1040), AspectRatio.Shrink(2560, 1440, 16.0 / 9, 1920, 1040));
        Equal((1000, 563), AspectRatio.Shrink(1920, 1080, 16.0 / 9, 1000, 2000));
    }

    private static void PresetEditing()
    {
        IReadOnlyList<Preset> presets = [new("A", 100, 100), new("B", 200, 200), new("C", 300, 300)];
        presets = Presets.Add(presets, new Preset("D", 400, 300, BoundsBasis.Frame));
        Equal("A,B,C,D", Names(presets));
        presets = Presets.Rename(presets, "b", "Beta");
        Equal("A,Beta,C,D", Names(presets));
        presets = Presets.Replace(presets, "C", new Preset("Gamma", 640, 480));
        Equal(new Preset("Gamma", 640, 480), presets[2]);
        presets = Presets.Move(presets, "D", 1);
        Equal("D,A,Beta,Gamma", Names(presets));
        presets = Presets.Move(presets, "D", 4);
        Equal("A,Beta,Gamma,D", Names(presets));
        presets = Presets.Remove(presets, "a");
        Equal("Beta,Gamma,D", Names(presets));
        Equal(BoundsBasis.Frame, Presets.Find(presets, "d")!.Basis);
        Equal(5, Presets.Defaults.Count);
    }

    private static void PresetValidation()
    {
        IReadOnlyList<Preset> presets = [new("FHD", 1920, 1080), new("HD", 1280, 720)];
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset("fhd", 1, 1)), "名前の重複（大文字小文字を区別しない）");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset("x", 31, 100)), "最小サイズ未満");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset("x", 100, Presets.MaximumSize + 1)), "最大サイズ超過");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset(" ", 100, 100)), "空白の名前");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset(" a", 100, 100)), "前後の空白");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset("a\tb", 100, 100)), "制御文字");
        Throws<ArgumentException>(() => Presets.Add(presets, new Preset(new string('a', 65), 100, 100)), "長すぎる名前");
        Throws<ArgumentException>(() => Presets.Rename(presets, "HD", "FHD"), "既存の名前への変更");
        Throws<ArgumentException>(() => Presets.Remove(presets, "none"), "存在しない名前");
        Throws<ArgumentException>(() => Presets.Move(presets, "HD", 3), "範囲外の移動先");
        Equal("FHD,hd", Names(Presets.Rename(presets, "HD", "hd")));
    }

    private static void SettingsDefaults()
    {
        using var folder = new TempFolder();
        var document = folder.Store.Load();
        Equal(new GuiSettings(), document.Gui);
        Equal(Names(Presets.Defaults), Names(document.Presets));
        True(!File.Exists(folder.Store.FilePath), "読むだけではファイルを作らない");
    }

    private static void SettingsRoundTrip()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Store.Directory);
        // 後の版で追加する項目（layouts、gui.monitorRelative）は、読み書きしても消さない。
        File.WriteAllText(folder.Store.FilePath, """
            { "schemaVersion": 1, "gui": { "autoRead": false, "monitorRelative": true },
              "presets": [ { "name": "縦長", "width": 720, "height": 1280 } ], "layouts": [ { "name": "録画" } ] }
            """);
        var document = folder.Store.Update(document =>
        {
            document.Gui = document.Gui with { Basis = BoundsBasis.Client, KeepAspect = true };
            document.Presets = Presets.Add(document.Presets, new Preset("HD", 1280, 720, BoundsBasis.Frame));
        });
        var loaded = folder.Store.Load();
        Equal(document.Gui, loaded.Gui);
        True(!loaded.Gui.AutoRead && loaded.Gui.Basis == BoundsBasis.Client && loaded.Gui.KeepAspect, "変更した値");
        Equal("縦長,HD", Names(loaded.Presets));
        Equal(BoundsBasis.Client, loaded.Presets[0].Basis);
        Equal(BoundsBasis.Frame, loaded.Presets[1].Basis);
        var text = File.ReadAllText(folder.Store.FilePath);
        True(text.Contains("\"layouts\"", StringComparison.Ordinal) && text.Contains("\"monitorRelative\": true", StringComparison.Ordinal), "未対応の項目を保持");
        True(text.Contains("縦長", StringComparison.Ordinal), "日本語をそのまま保存");
        True(Directory.GetFiles(folder.Store.Directory).Length == 1, "一時ファイルを残さない");
    }

    private static void SettingsEmptyPresets()
    {
        using var folder = new TempFolder();
        folder.Store.Update(document => document.Presets = []);
        Equal(0, folder.Store.Load().Presets.Count);
    }

    private static void SettingsBroken()
    {
        foreach (var json in new[] { "{ broken", "[]", """{ "gui": { "autoRead": "yes" } }""", """{ "presets": [ { "name": "a", "width": 10, "height": 10 } ] }""",
                     """{ "presets": [ { "name": "a", "width": 100, "height": 100 }, { "name": "A", "width": 100, "height": 100 } ] }""",
                     """{ "gui": { "basis": "outer" } }""", """{ "schemaVersion": 0 }""" })
        {
            using var folder = new TempFolder();
            Directory.CreateDirectory(folder.Store.Directory);
            File.WriteAllText(folder.Store.FilePath, json);
            var error = Throws<SettingsException>(() => folder.Store.Load(), "読めない設定: " + json);
            True(!error.NewerSchema && error.Message.Contains(folder.Store.FilePath, StringComparison.Ordinal), "場所を示す: " + json);
            Throws<SettingsException>(() => folder.Store.Update(_ => { }), "読めない設定は上書きしない");
            Equal(json, File.ReadAllText(folder.Store.FilePath));
            var recovered = folder.Store.LoadOrRecover(out var warning, out var writable);
            True(writable && warning is not null && warning.Contains("退避", StringComparison.Ordinal), "退避の案内");
            Equal(new GuiSettings(), recovered.Gui);
            True(!File.Exists(folder.Store.FilePath), "壊れた設定を退避");
            var backup = Directory.GetFiles(folder.Store.Directory, SettingsStore.FileName + ".broken-*").Single();
            Equal(json, File.ReadAllText(backup));
        }
    }

    private static void SettingsNewerSchema()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Store.Directory);
        const string json = """{ "schemaVersion": 2, "gui": { "autoRead": false } }""";
        File.WriteAllText(folder.Store.FilePath, json);
        True(Throws<SettingsException>(() => folder.Store.Load(), "新しい版").NewerSchema, "新しい版の判定");
        var document = folder.Store.LoadOrRecover(out var warning, out var writable);
        True(!writable && warning is not null, "保存しない");
        Equal(new GuiSettings(), document.Gui);
        Equal(json, File.ReadAllText(folder.Store.FilePath));
        Equal(1, Directory.GetFiles(folder.Store.Directory).Length);
    }

    private static void GuiSettingKeys()
    {
        var gui = new GuiSettings();
        foreach (var key in GuiSettings.Keys) Equal(gui.Get(key), gui.With(key, gui.Get(key)).Get(key));
        Equal("client", gui.With("basis", "client").Get("basis"));
        True(!gui.With("autoRead", "off").AutoRead && gui.With("allowOutside", "on").AllowOutside, "on／off");
        Throws<ArgumentException>(() => gui.With("autoRead", "yes"), "真偽値以外");
        Throws<ArgumentException>(() => gui.With("basis", "outer"), "不明な基準");
        Throws<ArgumentException>(() => gui.With("noSuchKey", "true"), "不明な名前");
        Throws<ArgumentException>(() => gui.Get("noSuchKey"), "不明な名前の取得");
    }

    private static void PlanClientBasis()
    {
        var state = State(new WindowBounds(100, 50, 816, 489));
        var plan = WindowOperations.Plan(state, new BoundsRequest(null, null, 1280, 720) { Basis = BoundsBasis.Client },
            new WindowBounds(0, 0, 1920, 1040), false);
        Equal(new WindowBounds(100, 50, 1296, 759), plan.Frame);
        Equal(new WindowBounds(108, 81, 1280, 720), plan.InBasis);
        Equal(0, plan.Warnings.Count);
        // 右下へはみ出す位置は、見える枠で作業領域へ収める。
        plan = WindowOperations.Plan(state, new BoundsRequest(1800, null, null, null) { Basis = BoundsBasis.Client },
            new WindowBounds(0, 0, 1920, 1040), false);
        Equal(1920 - 816, plan.Frame.X);
        Equal(1, plan.Warnings.Count);
    }

    private static void PlanAspectShrink()
    {
        var state = State(new WindowBounds(0, 0, 816, 489));
        var plan = WindowOperations.Plan(state, new BoundsRequest(null, null, 2560, null) { Basis = BoundsBasis.Client, Aspect = 16.0 / 9 },
            new WindowBounds(0, 0, 1920, 1040), false);
        // 作業領域から枠の分（幅16、高さ39）を除いた1904×1001に、16:9で収める。
        Equal(new WindowBounds(8, 31, 1780, 1001), plan.InBasis);
        True(plan.Frame.Width <= 1920 && plan.Frame.Height <= 1040, "見える枠が作業領域内");
        True(plan.Warnings.Count > 0, "縮めたことを知らせる");
        var outside = WindowOperations.Plan(state, new BoundsRequest(null, null, 2560, null) { Basis = BoundsBasis.Client, Aspect = 16.0 / 9 },
            new WindowBounds(0, 0, 1920, 1040), true);
        Equal(new WindowBounds(8, 31, 2560, 1440), outside.InBasis);
    }

    private static void PlanClientUnavailable() =>
        Throws<InvalidOperationException>(() => WindowOperations.Plan(State(new WindowBounds(0, 0, 500, 300), withClient: false),
            new BoundsRequest(null, null, 400, null) { Basis = BoundsBasis.Client }, new WindowBounds(0, 0, 1920, 1040), false), "中身の基準");

    private static string Names(IReadOnlyList<Preset> presets) => string.Join(",", presets.Select(preset => preset.Name));

    private static T Throws<T>(Action action, string context) where T : Exception
    {
        try { action(); }
        catch (T expected) { return expected; }
        throw new InvalidOperationException($"{typeof(T).Name}が発生しません: {context}");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"期待値: {expected}\n実際値: {actual}");
    }

    private sealed class TempFolder : IDisposable
    {
        public SettingsStore Store { get; } = new(Path.Combine(Path.GetTempPath(), "chunap-settings-" + Guid.NewGuid().ToString("N")));
        public void Dispose()
        {
            if (Directory.Exists(Store.Directory)) Directory.Delete(Store.Directory, true);
        }
    }
}
