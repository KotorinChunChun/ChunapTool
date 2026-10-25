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
            ("プレビューは初期停止し開閉で領域と高さを復元", CollapsedPreview),
            ("検索は即時反映し0件でも現在対象と編集値を維持", ImmediateSearch),
            ("ターゲットドラッグの確定と取消は対象を安全に選択", DragTargetSelection),
            ("ドラッグのマウスイベントと終了した候補の選択を処理", DragEventsAndClosedCandidate),
            ("自動読み取りと自動反映の4組合せを独立制御", IndependentAutomation),
            ("自動反映OFFではEnterと画面外設定で適用しない", ManualApplyOnly),
            ("位置サイズ反映中もEnterとTabの入力先を維持", GeometryInputFocus),
            ("反映待機中の次の入力を保持して順次反映", PreserveEditsDuringApply),
            ("自動反映OFFで待機中の反映を取消しONで再開", ToggleAutomaticApply),
            ("自動読み取りOFFでも対象終了と最大化を検出", ObserveSafetyWithoutAutomaticRead),
            ("自動読み取りOFFでは自動画面内補正後も入力欄を保持", ConstrainWithoutAutomaticRead),
            ("最大化ボタンの操作とUndoで表示を切替", MaximizeCaptionAndUndo),
            ("自動反映OFFの未適用値を一覧更新と検索後も保持", PreservePendingEdits),
            ("対象切替で編集値とUndoを新しい対象へリセット", SwitchTarget),
            ("最大化中は位置とサイズを編集できない", MaximizedControls),
            ("最小化中は位置・サイズと最大化を操作できない", MinimizedControls),
            ("対象終了後は編集・適用・Undoを無効化", TargetClosed),
            ("最小ウィンドウサイズでも全操作を画面内に配置", MinimumLayout),
            ("ポーリング前の外部変更もUndo開始時に取り込む", UndoLatestExternalChange),
            ("スナップ中の位置へのUndoは見える位置の一致で完了とする", UndoSnappedState),
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
        True(screen.Control<CheckBox>("autoRead").Checked, "自動読み取りの初期値はON");
        True(screen.Control<CheckBox>("autoApply").Checked, "自動反映の初期値はON");
        Equal(WindowTitle.Create(), screen.Form.Text, "完成版のタイトル");
    }

    private static void CollapsedPreview()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        int collapsedHeight = screen.Form.Height;
        var panel = screen.Control<Panel>("previewArea");
        var toggle = screen.Control<Button>("previewToggle");
        True(!panel.Visible, "起動時はプレビュー非表示");
        True(!screen.Control<ChunapTool.Native.DwmPreview>("preview").IsAttached, "非表示時はDWM未登録");
        Equal("▼", toggle.Text, "折りたたみ表示");
        True(!screen.Control<Label>("status").Text.Contains("見える枠のピクセル値", StringComparison.Ordinal), "固定説明は表示しない");
        toggle.PerformClick();
        Application.DoEvents();
        True(panel.Visible && panel.Height > 0, "展開すると領域を表示");
        True(screen.Form.Height > collapsedHeight, "展開時に高さを増やす");
        int expandedHeight = screen.Form.Height;
        Equal("▲", toggle.Text, "展開表示");
        toggle.PerformClick();
        Application.DoEvents();
        True(!panel.Visible, "折りたたむと領域を除去");
        Equal(collapsedHeight, screen.Form.Height, "折りたたみ時の高さを復元");
        toggle.PerformClick();
        Application.DoEvents();
        Equal(expandedHeight, screen.Form.Height, "再展開時の高さを復元");
    }

    private static void ImmediateSearch()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Numbers[0].Value = 444;
        var search = screen.Control<TextBox>("search");
        int enumerations = screen.Service.Enumerations;
        search.Text = "一致しない検索";
        Equal(FakeWindows.First, screen.Control<WindowEntry>("selected").Identity, "検索0件でも現在対象を維持");
        Equal(444m, screen.Numbers[0].Value, "未適用値を保持");
        Equal(1, screen.Targets.Items.Count, "絞込外の現在対象だけをコンボへ保持");
        True(screen.Control<Label>("status").Text.Contains("候補 0件", StringComparison.Ordinal), "検索件数は現在対象と分離");
        Equal(enumerations, screen.Service.Enumerations, "入力時にOS列挙を行わない");
        search.Text = "Beta";
        Equal(2, screen.Targets.Items.Count, "入力の直後にBetaと現在対象を表示");
        search.Clear();
        Equal(2, screen.Targets.Items.Count, "検索解除直後に全候補を表示");
        Equal(0, screen.Service.Mutations, "検索では対象を変更しない");
    }

    private static void DragTargetSelection()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Picker.Next = screen.Service.Enumerate("")[1];
        screen.Invoke("BeginTargetPick");
        screen.Invoke("UpdateTargetPick", new Point(30, 40));
        Equal(FakeWindows.First, screen.Control<WindowEntry>("selected").Identity, "ドラッグ中は未確定");
        True(screen.Form.ManagementStopBlockReason is not null, "ドラッグ中は管理停止を拒否");
        True(!screen.Targets.Enabled && screen.Numbers.All(number => !number.Enabled), "ドラッグ中は別の編集を停止");
        screen.Invoke("EndTargetPick", true);
        Equal(FakeWindows.Second, screen.Control<WindowEntry>("selected").Identity, "離した時点で対象を確定");
        EqualValues([320, 180, 620, 410], screen.Numbers, "確定した対象の座標を表示");

        screen.Picker.Next = screen.Service.Enumerate("")[0];
        screen.Invoke("BeginTargetPick");
        screen.Invoke("UpdateTargetPick", Point.Empty);
        var message = Message.Create(screen.Form.Handle, 0x100, (nint)Keys.Escape, 0);
        var method = typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Equal(true, (bool)method.Invoke(screen.Form, [message, Keys.Escape])!, "Escはドラッグを取り消す");
        Equal(FakeWindows.Second, screen.Control<WindowEntry>("selected").Identity, "Escで元の対象を維持");
        True(!screen.Control<bool>("picking"), "Esc後にドラッグ状態を解除");

        screen.Picker.Next = null;
        screen.Invoke("BeginTargetPick");
        screen.Invoke("UpdateTargetPick", Point.Empty);
        screen.Invoke("EndTargetPick", true);
        Equal(FakeWindows.Second, screen.Control<WindowEntry>("selected").Identity, "無効な対象へのドロップでは変更しない");
        screen.Invoke("BeginTargetPick");
        typeof(Control).GetMethod("OnMouseCaptureChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(screen.Control<Button>("targetPickerButton"), [EventArgs.Empty]);
        True(!screen.Control<bool>("picking"), "キャプチャ喪失で解除");
        Equal(0, screen.Service.Mutations, "対象指定だけでは位置・サイズを変更しない");
    }

    private static void DragEventsAndClosedCandidate()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Picker.Next = screen.Service.Enumerate("")[1];
        var button = screen.Control<Button>("targetPickerButton");
        static void RaiseMouse(Control control, string name) => typeof(Control)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)]);
        RaiseMouse(button, "OnMouseDown");
        True(screen.Control<bool>("picking"), "マウス押下で開始");
        RaiseMouse(button, "OnMouseMove");
        RaiseMouse(button, "OnMouseUp");
        Equal(FakeWindows.Second, screen.Control<WindowEntry>("selected").Identity, "マウス解放イベントで確定");
        True(screen.Targets.Enabled && !screen.Control<bool>("picking"), "確定後に操作を復元");

        var dead = screen.Service.Enumerate("")[0];
        screen.Service.RemoveFirst();
        screen.Targets.SelectedItem = dead;
        Equal(FakeWindows.Second, screen.Control<WindowEntry>("selected").Identity, "終了した候補は現在対象を変えない");
        Equal(FakeWindows.Second, ((WindowEntry)screen.Targets.SelectedItem!).Identity, "コンボも元の現在対象へ戻す");
        Equal(0, screen.Service.Mutations, "選択では位置を変更しない");
    }

    private static void IndependentAutomation()
    {
        foreach (bool read in new[] { false, true })
        foreach (bool apply in new[] { false, true })
        {
            using var screen = new TestScreen();
            screen.Select(0);
            screen.Control<CheckBox>("autoRead").Checked = read;
            screen.Control<CheckBox>("autoApply").Checked = apply;
            var external = new WindowBounds(200, 160, 540, 380);
            screen.Service.UpdateFirst(external);
            screen.Invoke("RefreshState", false);
            Equal(read ? 200m : 120m, screen.Numbers[0].Value, $"読取={read}/反映={apply}: 外部移動の表示");
            Equal(0, screen.Service.Mutations, "読み取りだけで反映を送信しない");
            Equal(!apply, screen.Control<Button>("apply").Enabled, "適用の有効状態は自動反映だけで決まる");

            screen.Numbers[0].Value = 350;
            screen.Service.UpdateFirst(external with { Y = 170 });
            screen.Invoke("RefreshState", false);
            Equal(350m, screen.Numbers[0].Value, "読み取りONでも未適用値は保護する");
            if (apply)
            {
                screen.AwaitMutations(1);
                Equal(350, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "読み取り設定によらず自動反映する");
            }
            else
            {
                screen.PumpFor(350);
                Equal(0, screen.Service.Mutations, "自動反映OFFでは待機しても反映しない");
                screen.Control<Button>("apply").PerformClick();
                screen.AwaitIdle();
                Equal(350, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "適用ボタンで反映する");
            }
        }
    }

    private static void ManualApplyOnly()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Numbers[0].Value = 320;
        screen.RaiseEnter(screen.Numbers[0]);
        screen.Control<CheckBox>("allowOutside").Checked = true;
        screen.Control<CheckBox>("allowOutside").Checked = false;
        screen.PumpFor(350);
        Equal(0, screen.Service.Mutations, "Enterと設定切替で未適用値を送信しない");
        screen.Control<Button>("apply").PerformClick();
        screen.AwaitIdle();
        Equal(320, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "適用ボタンで編集値を反映");

        screen.Service.UpdateFirst(new WindowBounds(-150, 80, 500, 340));
        screen.Invoke("RefreshState", false);
        screen.PumpFor(100);
        Equal(1, screen.Service.Mutations, "自動反映OFFでは定期処理の画面内補正もしない");
        screen.Control<Button>("apply").PerformClick();
        screen.AwaitIdle();
        Equal(0, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "手動適用時に画面内へ制限");
    }

    private static void GeometryInputFocus()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        for (int index = 0; index < screen.Numbers.Length; index++)
        {
            var number = screen.Numbers[index];
            // 非アクティブフォームの論理的な入力先のみを設定する。実キーは送信しない。
            True(!screen.Form.ContainsFocus, "他の開発画面からフォーカスを奪わない");
            screen.Form.ActiveControl = number;
            number.Value += 10;
            screen.RaiseEnter(number);
            True(screen.Control<bool>("busy"), "Enterから反映処理を開始");
            True(screen.Numbers.All(control => control.Enabled), "安定確認中も座標欄を有効に保つ");
            Equal<Control>(number, screen.Form.ActiveControl!, "Enter後も同じ入力先");
            var next = index < 3 ? screen.Numbers[index + 1] : screen.Numbers[index - 1];
            True(screen.Form.SelectNextControl(number, index < 3, true, true, false), "確認中にTabまたはShift+Tab移動できる");
            Equal<Control>(next, screen.Form.ActiveControl!, "X/Y/W/Hの順に移動");
            screen.AwaitIdle();
            Equal<Control>(next, screen.Form.ActiveControl!, "反映後も移動先を維持");
            True(!screen.Form.ContainsFocus, "論理入力先の確認後もフォームは非アクティブ");
        }

        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Form.ActiveControl = screen.Numbers[0];
        screen.Numbers[0].Value += 10;
        screen.Await(screen.InvokeTask("ApplyAsync"));
        Equal<Control>(screen.Numbers[0], screen.Form.ActiveControl!, "手動適用でも入力先を維持");
        screen.Service.RejectApplyBounds = true;
        screen.Numbers[0].Value += 10;
        screen.Await(screen.InvokeTask("ApplyAsync"));
        Equal<Control>(screen.Numbers[0], screen.Form.ActiveControl!, "適用失敗時も入力先を維持");
        True(screen.Numbers.All(control => control.Enabled), "失敗後も入力可能");
    }

    private static void PreserveEditsDuringApply()
    {
        foreach (bool automatic in new[] { false, true })
        {
            using var screen = new TestScreen();
            screen.Select(0);
            screen.Control<CheckBox>("autoApply").Checked = automatic;
            screen.Numbers[0].Value = 250;
            True(SynchronizationContext.Current is WindowsFormsSynchronizationContext, "反映継続はUI同期コンテキストで実行");
            var firstApply = screen.InvokeTask("ApplyAsync");
            True(screen.Control<bool>("busy"), "最初の反映は安定確認中");
            screen.Numbers[1].Value = 150;
            screen.Numbers[2].Text = "650";
            screen.Await(firstApply);
            Equal(150m, screen.Numbers[1].Value, "確認中に変更した次の値を保持");
            Equal("650", screen.Numbers[2].Text, "確認中の未確定文字列を保持");
            Equal(1, screen.Service.Mutations, "確認中に別の位置変更を並列送信しない");
            if (automatic) screen.AwaitMutations(2);
            else
            {
                screen.PumpFor(350);
                Equal(1, screen.Service.Mutations, "手動設定では確認中の編集も自動反映しない");
                screen.Await(screen.InvokeTask("ApplyAsync"));
            }
            Equal(new WindowBounds(250, 150, 650, 340), screen.Service.GetState(FakeWindows.First)!.Bounds, "続けて入力した値を欠落させず反映");
        }
    }

    private static void ToggleAutomaticApply()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Numbers[0].Value = 260;
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.PumpFor(350);
        Equal(0, screen.Service.Mutations, "OFF切替で待機中の自動反映を取消す");
        Equal(260m, screen.Numbers[0].Value, "OFFへ切り替えても編集値は保持");
        screen.Control<CheckBox>("autoApply").Checked = true;
        screen.AwaitMutations(1);
        Equal(260, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "ON切替で保留した編集を反映");
        True(!screen.Control<Button>("apply").Enabled, "ONでは適用ボタンを無効化");
    }

    private static void ObserveSafetyWithoutAutomaticRead()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoRead").Checked = false;
        screen.Service.UpdateFirst(new WindowBounds(250, 160, 540, 380));
        screen.Invoke("RefreshState", false);
        Equal(120m, screen.Numbers[0].Value, "自動読み取りOFFで座標欄を変更しない");
        screen.Service.SetShowCommand(FakeWindows.First, 3);
        screen.Invoke("RefreshState", false);
        Equal("通常化", screen.Control<CheckBox>("maximize").Text, "自動読み取りOFFでも最大化状態を表示");
        True(screen.Numbers.All(control => !control.Enabled), "最大化中の座標入力は停止");
        screen.Service.RemoveFirst();
        screen.Invoke("RefreshState", false);
        Equal(-1, screen.Targets.SelectedIndex, "自動読み取りOFFでも対象終了を検出");
    }

    private static void MaximizeCaptionAndUndo()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        var maximize = screen.Control<CheckBox>("maximize");
        Equal("最大化", maximize.Text, "通常状態の表示");
        maximize.Checked = true;
        screen.AwaitIdle();
        Equal("通常化", maximize.Text, "最大化操作後の表示");
        maximize.Checked = false;
        screen.AwaitIdle();
        Equal("最大化", maximize.Text, "通常化操作後の表示");
        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal("通常化", maximize.Text, "Undoで最大化した際の表示");
        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal("最大化", maximize.Text, "Undoで通常表示へ戻した際の表示");
    }

    private static void ConstrainWithoutAutomaticRead()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoRead").Checked = false;
        screen.Service.UpdateFirst(new WindowBounds(-150, 90, 550, 350));
        screen.Invoke("RefreshState", false);
        screen.AwaitIdle();
        Equal(0, screen.Service.GetState(FakeWindows.First)!.Bounds.X, "自動反映ONの画面内補正は継続する");
        EqualValues([120, 80, 500, 340], screen.Numbers, "自動読み取りOFFでは補正後の座標を自動表示しない");
        screen.Control<CheckBox>("autoRead").Checked = true;
        EqualValues([0, 90, 550, 350], screen.Numbers, "自動読み取りONで現在値を表示");
    }

    private static void PreservePendingEdits()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
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
        Equal(0, screen.Service.Mutations, "自動反映OFFの編集でサービスを変更しない");
    }

    private static void SwitchTarget()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<CheckBox>("autoApply").Checked = false;
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
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Service.SetShowCommand(FakeWindows.First, 3);
        screen.Invoke("RefreshState", false);
        True(screen.Numbers.All(control => !control.Enabled), "最大化中の座標欄は無効");
        True(!screen.Control<Button>("apply").Enabled, "最大化中の適用は無効");
        True(screen.Control<CheckBox>("maximize").Checked, "外部での最大化を表示");
        True(screen.Control<CheckBox>("maximize").Enabled, "最大化から復帰できる");
        Equal("通常化", screen.Control<CheckBox>("maximize").Text, "外部最大化の表示");

        screen.Service.SetShowCommand(FakeWindows.First, 1);
        screen.Invoke("RefreshState", false);
        True(screen.Numbers.All(control => control.Enabled), "通常状態に戻ると編集可能");
        True(screen.Control<Button>("apply").Enabled, "通常状態の手動適用は有効");
        Equal("最大化", screen.Control<CheckBox>("maximize").Text, "外部通常化の表示");
        Equal(0, screen.Service.Mutations, "表示の更新を最大化操作として送らない");
    }

    private static void MinimizedControls()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        screen.Control<Button>("previewToggle").PerformClick();
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
        screen.Control<CheckBox>("autoApply").Checked = false;
        screen.Service.UpdateFirst(new WindowBounds(150, 90, 510, 350));
        screen.Invoke("RefreshState", false);
        True(screen.Control<Button>("undo").Enabled, "終了前には履歴がある");

        screen.Service.RemoveFirst();
        screen.Invoke("RefreshState", false);
        Equal(-1, screen.Targets.SelectedIndex, "終了した対象の選択解除");
        True(screen.Numbers.All(control => !control.Enabled), "対象終了後の座標欄は無効");
        foreach (string field in new[] { "apply", "undo", "maximize", "autoRead", "autoApply", "allowOutside" })
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

        // 行の高さなどは96DPI基準で指定している。表示倍率に比例して拡大されなければ文字だけが大きくなり部品がはみ出す。
        int dpi = screen.Form.DeviceDpi;
        var rowHeights = screen.Control<TableLayoutPanel>("layout").GetRowHeights();
        True(Math.Abs(rowHeights[0] - 38 * dpi / 96) <= 1 && Math.Abs(rowHeights[5] - 34 * dpi / 96) <= 1, $"行の高さを表示倍率に合わせて拡大 DPI={dpi}: {string.Join(",", rowHeights)}");
        var controls = new List<Control>(screen.Numbers);
        controls.AddRange(new[] { "search", "targets", "refresh", "alwaysOnTop", "maximize", "autoRead", "autoApply", "allowOutside", "apply", "undo" }
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

        True(!screen.Control<Panel>("previewArea").Visible, "初期状態ではプレビューを折りたたむ");
        var apply = screen.Control<Button>("apply");
        var outside = screen.Control<CheckBox>("allowOutside");
        True(apply.Right < outside.Left, "画面外可は適用ボタンの右に配置");
        var actionNames = new[] { "maximize", "autoRead", "autoApply", "apply", "allowOutside", "undo" };
        var actionControls = actionNames.Select(screen.Control<Control>).ToArray();
        var centers = actionControls.Select(control => control.Top * 2 + control.Height).ToArray();
        True(centers.Max() - centers.Min() <= 2, "操作行のボタンとチェックボックスの縦中心を1px以内に揃える");
        Console.WriteLine($"配置実測 DPI={screen.Form.DeviceDpi}: " + string.Join(", ", actionControls.Select(control => $"{control.Text} Y={control.Top} H={control.Height}")));
        foreach (var number in screen.Numbers)
        {
            var parent = (TableLayoutPanel)number.Parent!;
            int column = parent.GetColumn(number);
            var label = parent.GetControlFromPosition(column - 1, 0)!;
            True(Math.Abs((number.Top * 2 + number.Height) - (label.Top * 2 + label.Height)) <= 2,
                $"{number.AccessibleName}と左ラベルの縦中心を1px以内に揃える");
            Console.WriteLine($"座標欄実測 {number.AccessibleName}: Y={number.Top} H={number.Height}, ラベルY={label.Top} H={label.Height}");
        }
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

    private static void UndoSnappedState()
    {
        using var screen = new TestScreen();
        screen.Select(0);
        // スナップ中は見える位置が左半分、通常復元位置はスナップ前の位置となる。
        var snapped = new WindowBounds(0, 0, 960, 1040);
        var beforeSnap = screen.Service.GetState(FakeWindows.First)!.Bounds;
        screen.Service.SetFirst(screen.Service.GetState(FakeWindows.First)! with { Bounds = snapped });
        screen.Invoke("RefreshState", false);
        screen.Service.UpdateFirst(new WindowBounds(400, 300, 500, 340));
        screen.Invoke("RefreshState", false);
        Equal(2, screen.Control<WindowHistory>("history").Count, "スナップと移動を履歴へ記録");

        screen.Service.RestorePlacementOnly = true;
        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal(beforeSnap, screen.Service.GetState(FakeWindows.First)!.Bounds, "通常復元位置だけを戻す実装を再現");
        True(screen.Control<Label>("status").Text.StartsWith("変更できません", StringComparison.Ordinal), "見える位置が違えば成功と表示しない");
        Equal(2, screen.Control<WindowHistory>("history").Count, "失敗したUndoは履歴を消費しない");

        // 失敗したUndoで動いた位置を履歴へ入れないよう、移動後の状態へ戻してから再実行する。
        screen.Service.UpdateFirst(new WindowBounds(400, 300, 500, 340));
        screen.Service.RestorePlacementOnly = false;
        screen.Await(screen.InvokeTask("UndoAsync"));
        Equal(snapped, screen.Service.GetState(FakeWindows.First)!.Bounds, "スナップ中の見える位置へ戻す");
        Equal("直前の状態に戻しました。", screen.Control<Label>("status").Text, "成功を表示");
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
        screen.Control<CheckBox>("autoApply").Checked = false;
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
        public FakePicker Picker { get; } = new();
        public MainForm Form { get; }
        public ComboBox Targets => Control<ComboBox>("targets");
        public NumericUpDown[] Numbers => Control<NumericUpDown[]>("numbers");

        public TestScreen()
        {
            Form = new MainForm(Service, passive: true, targetPicker: Picker);
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

        public void AwaitMutations(int count) => PumpUntil(() => Service.Mutations >= count && !Control<bool>("busy"));

        public void PumpFor(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        public void RaiseEnter(NumericUpDown control)
        {
            var method = typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(control, [new KeyEventArgs(Keys.Enter)]);
        }

        private void PumpUntil(Func<bool> completed)
        {
            var watch = Stopwatch.StartNew();
            while (!completed())
            {
                if (watch.ElapsedMilliseconds > 4000)
                {
                    throw new TimeoutException($"画面処理の完了を4秒待っても応答がありません。反映={Service.Mutations}, busy={Control<bool>("busy")}, dirty={Control<bool>("dirty")}, タイマー={Control<System.Windows.Forms.Timer>("editDelay").Enabled}, 状態={Control<Label>("status").Text}");
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
        public int Enumerations { get; private set; }
        public bool RejectApplyBounds { get; set; }
        public bool IsLeftButtonDown => false;

        public IReadOnlyList<WindowEntry> Enumerate(string filter)
        {
            Enumerations++;
            return new[] { new WindowEntry(First, "Alpha 検証対象", "TestAlpha"), new WindowEntry(Second, "Beta 検証対象", "TestBeta") }
                .Where(entry => states.ContainsKey(entry.Identity)
                    && (entry.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

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

        // trueでは修正前の実装と同様に、通常復元位置だけを戻して見える位置を合わせない。
        public bool RestorePlacementOnly { get; set; }

        public void Restore(WindowState state)
        {
            Mutations++;
            states[state.Target] = RestorePlacementOnly && !state.IsMaximized
                ? state with { Bounds = state.Placement.NormalBounds }
                : state;
        }

        public void SetFirst(WindowState state) => states[First] = state;
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

    private sealed class FakePicker : IWindowTargetPicker
    {
        public WindowEntry? Next { get; set; }
        public WindowEntry? Resolve(Point point) => Next;
    }
}
