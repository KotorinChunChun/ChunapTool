using System.Reflection;
using System.Diagnostics;
using ChunapTool.Core;

namespace ChunapTool.Tests;

/// <summary>不可視・非アクティブな専用フォームと偽サービスだけで画面状態の回帰を検証する。</summary>
public static class UiTests
{
    public static int Run()
    {
        Console.WriteLine("検証方式: 不可視・非アクティブな自フォームと偽サービスのみを使用し、他の画面へ入力しません。");
        (string Name, Action Execute)[] tests =
        [
            ("初回表示では対象を自動選択せず操作しない", InitialSelection),
            ("同期OFFの未適用値を一覧更新と検索後も保持", PreservePendingEdits),
            ("対象切替で編集値とUndoを新しい対象へリセット", SwitchTarget),
            ("最大化中は位置とサイズを編集できない", MaximizedControls),
            ("最小化中は位置・サイズと最大化を操作できない", MinimizedControls),
            ("対象終了後は編集・適用・Undoを無効化", TargetClosed),
            ("最小ウィンドウサイズでも全操作を画面内に配置", MinimumLayout),
            ("ポーリング前の外部変更もUndo開始時に取り込む", UndoLatestExternalChange),
            ("画面外への移動と自動補正を1操作として戻す", AutoConstrainAsOneStep),
            ("拒否された自動補正を同じ状態で繰り返さない", AutoConstrainRetry),
            ("変更の待機中に閉じても破棄した画面へ更新しない", DisposeDuringMutation),
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

        Console.WriteLine($"UI回帰検証: {tests.Length - failures}/{tests.Length} 成功");
        Console.WriteLine("検証範囲: 状態・イベント処理・配置座標。実描画、フォーカス、実マウス/キー操作、実対象のDWM表示は未検証です。");
        return failures == 0 ? 0 : 1;
    }

    private static void InitialSelection()
    {
        using var screen = new TestScreen();
        Equal(2, screen.Targets.Items.Count, "候補数");
        Equal(-1, screen.Targets.SelectedIndex, "起動時の選択");
        True(screen.Numbers.All(control => !control.Enabled), "未選択の座標欄は無効");
        True(!screen.Control<Button>("apply").Enabled, "未選択の適用は無効");
        True(!screen.Control<CheckBox>("maximize").Enabled, "未選択の最大化は無効");
        Equal(0, screen.Service.Mutations, "起動時に他のウィンドウを変更しない");
    }

    private static void PreservePendingEdits()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("sync").Checked = false;
        decimal[] pending = [333, 222, 777, 444];
        for (int index = 0; index < pending.Length; index++)
        {
            screen.Numbers[index].Value = pending[index];
        }

        screen.Service.UpdateFirst(new WindowBounds(140, 90, 510, 350));
        screen.Invoke("RefreshState", false);
        EqualValues(pending, screen.Numbers, "定期更新後");

        screen.Control<Button>("refresh").PerformClick();
        EqualValues(pending, screen.Numbers, "一覧更新後");

        screen.Control<TextBox>("search").Text = "Alpha";
        screen.Invoke("RefreshTargets");
        screen.StopTimers();
        Equal(1, screen.Targets.Items.Count, "検索結果数");
        Equal(FakeWindows.First, ((WindowEntry)screen.Targets.SelectedItem!).Identity, "検索後も同じ対象");
        EqualValues(pending, screen.Numbers, "検索後");
        True(screen.Control<Button>("apply").Enabled, "未適用値を適用可能");
        Equal(0, screen.Service.Mutations, "同期OFFの編集でサービスを変更しない");
    }

    private static void SwitchTarget()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("sync").Checked = false;
        screen.Service.UpdateFirst(new WindowBounds(150, 90, 510, 350));
        screen.Invoke("RefreshState", false);
        True(screen.Control<Button>("undo").Enabled, "最初の対象には変更履歴がある");
        screen.Numbers[0].Value = 999;

        screen.Select(1);
        EqualValues([320, 180, 620, 410], screen.Numbers, "別対象を選んだ後");
        True(!screen.Control<Button>("undo").Enabled, "前の対象の履歴を持ち越さない");

        screen.Service.UpdateSecond(new WindowBounds(330, 190, 630, 420));
        screen.Invoke("RefreshState", false);
        EqualValues([330, 190, 630, 420], screen.Numbers, "新しい対象には未適用値が残らない");
        Equal(0, screen.Service.Mutations, "選択切替だけではサービスを変更しない");
    }

    private static void MaximizedControls()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("sync").Checked = false;
        screen.Service.SetShowCommand(FakeWindows.First, 3);
        screen.Invoke("RefreshState", false);
        True(screen.Numbers.All(control => !control.Enabled), "最大化中の座標欄は無効");
        True(!screen.Control<Button>("apply").Enabled, "最大化中の適用は無効");
        True(screen.Control<CheckBox>("maximize").Checked, "外部での最大化を表示");
        True(screen.Control<CheckBox>("maximize").Enabled, "最大化から復帰できる");

        screen.Service.SetShowCommand(FakeWindows.First, 1);
        screen.Invoke("RefreshState", false);
        True(screen.Numbers.All(control => control.Enabled), "通常状態に戻ると編集可能");
        True(screen.Control<Button>("apply").Enabled, "通常状態の手動適用は有効");
        Equal(0, screen.Service.Mutations, "表示の更新を最大化操作として送らない");
    }

    private static void MinimizedControls()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Service.SetShowCommand(FakeWindows.First, 2);
        screen.Invoke("RefreshState", false);
        True(screen.Numbers.All(control => !control.Enabled), "最小化中の座標欄は無効");
        True(!screen.Control<Button>("apply").Enabled, "最小化中の適用は無効");
        True(!screen.Control<CheckBox>("maximize").Enabled, "最小化中の最大化は無効");
        True(screen.Control<Label>("previewMessage").Text.Contains("最小化", StringComparison.Ordinal), "最小化中の案内を表示");
        Equal(0, screen.Service.Mutations, "最小化を観測してもサービスを変更しない");
    }

    private static void TargetClosed()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("sync").Checked = false;
        screen.Service.UpdateFirst(new WindowBounds(150, 90, 510, 350));
        screen.Invoke("RefreshState", false);
        True(screen.Control<Button>("undo").Enabled, "終了前には履歴がある");

        screen.Service.RemoveFirst();
        screen.Invoke("RefreshState", false);
        Equal(-1, screen.Targets.SelectedIndex, "終了した対象の選択解除");
        True(screen.Numbers.All(control => !control.Enabled), "対象終了後の座標欄は無効");
        foreach (string field in new[] { "apply", "undo", "maximize", "sync", "allowOutside" })
        {
            True(!screen.Control<Control>(field).Enabled, $"対象終了後の{field}は無効");
        }

        True(screen.Control<Label>("status").Text.Contains("終了", StringComparison.Ordinal), "対象終了を案内");
        Equal(0, screen.Service.Mutations, "終了した対象へ変更を送らない");
    }

    private static void MinimumLayout()
    {
        using var screen = new TestScreen();
        screen.Form.Size = screen.Form.MinimumSize;
        screen.Form.PerformLayout();
        Application.DoEvents();

        var controls = new List<Control>(screen.Numbers);
        controls.AddRange(new[] { "search", "targets", "refresh", "alwaysOnTop", "maximize", "sync", "allowOutside", "apply", "undo" }
            .Select(screen.Control<Control>));
        var rectangles = new List<(Control Control, Rectangle Bounds)>();
        foreach (var control in controls)
        {
            True(control.Visible && control.Width > 0 && control.Height > 0, $"{control.Text}/{control.AccessibleName}の表示領域");
            var parent = control.Parent ?? throw new InvalidOperationException("親コントロールがありません。");
            True(parent.ClientRectangle.Contains(control.Bounds), $"{control.Text}/{control.AccessibleName}が親領域内");
            var bounds = screen.Form.RectangleToClient(parent.RectangleToScreen(control.Bounds));
            True(screen.Form.ClientRectangle.Contains(bounds), $"{control.Text}/{control.AccessibleName}が画面内");
            foreach (var previous in rectangles)
            {
                True(!bounds.IntersectsWith(previous.Bounds), $"{control.Text}/{control.AccessibleName}と{previous.Control.Text}/{previous.Control.AccessibleName}が重ならない");
            }

            rectangles.Add((control, bounds));
        }

        True(screen.Control<Panel>("previewArea").Height > 0, "プレビューにも表示領域が残る");
    }

    private static void UndoLatestExternalChange()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        var observed = new WindowBounds(200, 100, 500, 340);
        screen.Service.UpdateFirst(observed);
        screen.Invoke("RefreshState", false);
        screen.Service.UpdateFirst(new WindowBounds(300, 150, 500, 340));

        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal(observed, screen.Service.GetState(FakeWindows.First)!.Bounds, "未観測の変更の直前へUndo");
        Equal(1, screen.Control<WindowHistory>("history").Count, "さらに前の履歴を保持");
        Equal(1, screen.Service.Mutations, "Undoを一度だけ送信");
    }

    private static void AutoConstrainAsOneStep()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        var initial = screen.Service.GetState(FakeWindows.First)!.Bounds;
        screen.Service.UpdateFirst(new WindowBounds(-150, 80, 500, 340));
        screen.Invoke("RefreshState", false);
        screen.AwaitIdle();
        Equal(new WindowBounds(0, 80, 500, 340), screen.Service.GetState(FakeWindows.First)!.Bounds, "外部移動を作業領域へ補正");
        Equal(1, screen.Control<WindowHistory>("history").Count, "外部移動と補正を一手として保持");

        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal(initial, screen.Service.GetState(FakeWindows.First)!.Bounds, "補正前の画面外位置を経ずに元へ戻る");
        Equal(0, screen.Control<WindowHistory>("history").Count, "1回のUndoで履歴を消費");
    }

    private static void AutoConstrainRetry()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Service.RejectApplyBounds = true;
        var outside = new WindowBounds(-150, 80, 500, 340);
        screen.Service.UpdateFirst(outside);
        screen.Invoke("RefreshState", false);
        screen.AwaitIdle();
        Equal(1, screen.Service.Mutations, "最初の自動補正を試す");
        for (int index = 0; index < 10; index++)
        {
            screen.Invoke("RefreshState", false);
        }

        Equal(1, screen.Service.Mutations, "応答しない同一状態へ補正を連続送信しない");
        screen.Service.UpdateFirst(new WindowBounds(120, 80, 500, 340));
        screen.Invoke("RefreshState", false);
        screen.Service.UpdateFirst(outside);
        screen.Invoke("RefreshState", false);
        screen.AwaitIdle();
        Equal(2, screen.Service.Mutations, "状態が変わった後の新しい画面外移動は補正を再試行");
    }

    private static void DisposeDuringMutation()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("sync").Checked = false;
        screen.Numbers[0].Value = 250;
        var mutation = screen.InvokeTask("ApplyAsync");
        True(!mutation.IsCompleted, "変更の安定を非同期に待っている");
        int callsBeforeClose = screen.Service.StateReads;
        screen.Form.Close();
        screen.Await(mutation);
        True(screen.Form.IsDisposed, "画面を閉じた");
        Equal(callsBeforeClose, screen.Service.StateReads, "閉じた後に対象を再取得しない");
    }

    private static void EqualValues(decimal[] expected, NumericUpDown[] actual, string context)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            Equal(expected[index], actual[index].Value, $"{context}の{actual[index].AccessibleName}");
        }
    }

    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{context}: 期待値={expected}、実際値={actual}");
        }
    }

    private static void True(bool condition, string context)
    {
        if (!condition)
        {
            throw new InvalidOperationException(context);
        }
    }

    private sealed class TestScreen : IDisposable
    {
        public FakeWindows Service { get; } = new();
        public MainForm Form { get; }
        public ComboBox Targets => Control<ComboBox>("targets");
        public NumericUpDown[] Numbers => Control<NumericUpDown[]>("numbers");

        public TestScreen()
        {
            Form = new MainForm(Service, passive: true);
            // 表示前に受動モードを確認し、他の開発画面へ重ねたりタスクバーへ追加したりしない。
            True(!Form.TopMost, "テストフォームは最前面ではない");
            Equal(0d, Form.Opacity, "テストフォームは完全に透明");
            True(!Form.ShowInTaskbar, "テストフォームをタスクバーへ追加しない");
            Form.Show();
            Application.DoEvents();
            StopTimers();
        }

        public T Control<T>(string name) =>
            (T)(typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Form)
                ?? throw new InvalidOperationException($"画面フィールドが見つかりません: {name}"));

        public void Invoke(string name, params object[] arguments)
        {
            var method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"画面処理が見つかりません: {name}");
            method.Invoke(Form, arguments);
        }

        public Task InvokeTask(string name)
        {
            var method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"画面処理が見つかりません: {name}");
            return (Task)(method.Invoke(Form, []) ?? throw new InvalidOperationException($"Taskが返されません: {name}"));
        }

        public void Await(Task task)
        {
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        public void AwaitIdle() => PumpUntil(() => !Control<bool>("busy"));

        private static void PumpUntil(Func<bool> completed)
        {
            var watch = Stopwatch.StartNew();
            while (!completed())
            {
                if (watch.ElapsedMilliseconds > 4000)
                {
                    throw new TimeoutException("画面処理の完了を4秒待っても応答がありません。");
                }

                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        public void Select(int index)
        {
            Targets.SelectedIndex = index;
            Application.DoEvents();
            StopTimers();
        }

        public void StopTimers()
        {
            foreach (string name in new[] { "poll", "filterDelay", "editDelay" })
            {
                Control<System.Windows.Forms.Timer>(name).Stop();
            }
        }

        public void Dispose()
        {
            Form.Close();
            Form.Dispose();
            Application.DoEvents();
        }
    }

    private sealed class FakeWindows : IWindowService
    {
        // HWND=0は必ず無効。実在する外部ウィンドウへDWMを接続しない。
        public static WindowIdentity First { get; } = new(0, 1);
        public static WindowIdentity Second { get; } = new(0, 2);
        private readonly Dictionary<WindowIdentity, WindowState> states = new()
        {
            [First] = MakeState(First, new WindowBounds(120, 80, 500, 340)),
            [Second] = MakeState(Second, new WindowBounds(320, 180, 620, 410)),
        };

        public int Mutations { get; private set; }
        public int StateReads { get; private set; }
        public bool RejectApplyBounds { get; set; }
        public bool IsLeftButtonDown => false;

        public IReadOnlyList<WindowEntry> Enumerate(string filter) =>
            new[] { new WindowEntry(First, "Alpha 検証対象", "TestAlpha"), new WindowEntry(Second, "Beta 検証対象", "TestBeta") }
                .Where(entry => states.ContainsKey(entry.Identity)
                    && (entry.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        public WindowState? GetState(WindowIdentity target)
        {
            StateReads++;
            return states.GetValueOrDefault(target);
        }
        public WindowBounds GetWorkArea(WindowIdentity target) => new(0, 0, 1920, 1080);

        public void ApplyBounds(WindowIdentity target, WindowBounds bounds)
        {
            Mutations++;
            if (RejectApplyBounds)
            {
                throw new InvalidOperationException("検証用サービスが位置変更を拒否しました。");
            }

            states[target] = MakeState(target, bounds);
        }

        public void SetMaximized(WindowIdentity target, bool maximized)
        {
            Mutations++;
            SetShowCommand(target, maximized ? 3u : 1u);
        }

        public void Restore(WindowState state)
        {
            Mutations++;
            states[state.Target] = state;
        }

        public void UpdateFirst(WindowBounds bounds) => states[First] = MakeState(First, bounds);
        public void UpdateSecond(WindowBounds bounds) => states[Second] = MakeState(Second, bounds);
        public void RemoveFirst() => states.Remove(First);
        public void SetShowCommand(WindowIdentity target, uint command) => states[target] = states[target] with
        {
            Placement = states[target].Placement with { ShowCommand = command },
        };

        private static WindowState MakeState(WindowIdentity target, WindowBounds bounds) =>
            new(target, bounds, new WindowPlacement(0, 1, new WindowPoint(-1, -1), new WindowPoint(-1, -1), bounds), true);
    }
}
