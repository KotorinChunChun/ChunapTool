using ChunapTool.Core;
using ChunapTool.Native;
using ChunapTool.Thumbnails;
using System.Diagnostics;

namespace ChunapTool;

public sealed class MainForm : BrandedForm
{
    private readonly IWindowService windows;
    private readonly bool passive;
    private readonly IWindowTargetPicker targetPicker;
    private IReadOnlyList<WindowEntry> candidates = [];
    private bool picking;
    private WindowEntry? pickedCandidate;
    private bool previewExpanded;
    private int expandedPreviewHeight = 294;
    private TableLayoutPanel layout = null!;
    private readonly Button targetPickerButton = new() { Text = "⌖", AutoSize = true, AccessibleName = "対象をドラッグして選択", Width = 38 };
    private readonly Button previewToggle = new() { Text = "▼", AutoSize = true, AccessibleName = "プレビューを表示", Width = 32 };
    private readonly Button thumbnailSelect = new() { Text = "サムネイル選択", AutoSize = true, Anchor = AnchorStyles.Left };
    private ThumbnailPickerForm? thumbnailPicker;
    private readonly WindowHistory history = new();
    private readonly DwmPreview preview = new();
    private readonly ToolTip tooltip = new() { ShowAlways = true, InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 10000 };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "タイトルまたは実行ファイル名", AccessibleName = "検索" };
    private readonly ComboBox targets = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "対象ウィンドウ", DropDownWidth = 800 };
    private readonly Button refresh = new() { Text = "更新", AutoSize = true };
    private readonly CheckBox autoRead = new() { Text = "自動読み取り", Checked = true, AutoSize = true };
    private readonly CheckBox autoApply = new() { Text = "自動反映", Checked = true, AutoSize = true };
    private readonly CheckBox allowOutside = new() { Text = "画面外可", AutoSize = true };
    private readonly CheckBox maximize = new() { Text = "最大化", Appearance = Appearance.Button, AutoSize = true };
    private readonly CheckBox alwaysOnTop = new() { Text = "最前面", Checked = true, AutoSize = true };
    private readonly Button apply = new() { Text = "適用", AutoSize = true };
    private readonly Button undo = new() { Text = "戻す", AutoSize = true };
    private readonly NumericUpDown[] numbers = Enumerable.Range(0, 4).Select(i => new NumericUpDown
    {
        Minimum = i < 2 ? -1000000 : 32, Maximum = 1000000, Width = 110,
        ThousandsSeparator = false, AccessibleName = new[] { "X座標", "Y座標", "幅", "高さ" }[i]
    }).ToArray();
    private readonly Panel previewArea = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 27, 32), Margin = new Padding(0, 10, 0, 0) };
    private readonly Label previewMessage = new() { Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, TextAlign = ContentAlignment.MiddleCenter, Text = "対象ウィンドウを選択してください" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer filterDelay = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer editDelay = new() { Interval = 250 };
    private bool reading, busy, dirty;
    private bool geometryEditableWhileBusy;
    private long editRevision;
    internal string? ManagementStopBlockReason => busy || picking || thumbnailPicker is { IsDisposed: false }
        ? "ウィンドウ操作中のため停止できません。" : null;
    private WindowEntry? selected;
    private WindowState? current;
    private bool previewFailed;
    private (WindowBounds Bounds, WindowBounds WorkArea)? lastConstraintAttempt;

    public MainForm() : this(new NativeWindowService()) { }

    internal MainForm(IWindowService windowService, bool passive = false, IWindowTargetPicker? targetPicker = null)
    {
        windows = windowService;
        this.passive = passive;
        this.targetPicker = targetPicker ?? new WindowTargetPicker();
        Text = WindowTitle.Create();
        Font = new Font("Yu Gothic UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 216);
        MinimumSize = new Size(780, 255);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = !passive;
        if (passive) { Opacity = 0; ShowInTaskbar = false; }
        BuildLayout();
        ConfigureTooltips();
        search.TextChanged += (_, _) => ApplyTargetFilter();
        filterDelay.Tick += (_, _) => { filterDelay.Stop(); RefreshTargets(); };
        refresh.Click += (_, _) => RefreshTargets();
        previewToggle.Click += (_, _) => TogglePreview();
        thumbnailSelect.Click += (_, _) => OpenThumbnailPicker();
        targetPickerButton.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginTargetPick(); };
        targetPickerButton.MouseMove += (_, _) => { if (picking) UpdateTargetPick(Cursor.Position); };
        targetPickerButton.MouseUp += (_, e) =>
        {
            if (!picking || e.Button != MouseButtons.Left) return;
            UpdateTargetPick(Cursor.Position);
            EndTargetPick(true);
        };
        targetPickerButton.MouseCaptureChanged += (_, _) =>
        {
            if (picking && !targetPickerButton.Capture) EndTargetPick(false);
        };
        targets.SelectedIndexChanged += (_, _) => { if (!reading) SelectTarget(); };
        alwaysOnTop.CheckedChanged += (_, _) => TopMost = alwaysOnTop.Checked;
        autoRead.CheckedChanged += (_, _) => RefreshState(false);
        autoApply.CheckedChanged += (_, _) =>
        {
            editDelay.Stop();
            if (autoApply.Checked && dirty) editDelay.Start();
            UpdateControls();
        };
        allowOutside.CheckedChanged += async (_, _) =>
        {
            if (reading) return;
            lastConstraintAttempt = null;
            if (autoApply.Checked) await ApplyAsync();
        };
        maximize.CheckedChanged += async (_, _) => { if (!reading) await MaximizeAsync(); };
        apply.Click += async (_, _) => await ApplyAsync();
        undo.Click += async (_, _) => await UndoAsync();
        foreach (var number in numbers)
        {
            number.ValueChanged += (_, _) =>
            {
                if (reading || (busy && !geometryEditableWhileBusy)) return;
                dirty = true;
                editRevision++;
                if (autoApply.Checked && !busy) { editDelay.Stop(); editDelay.Start(); }
            };
            number.TextChanged += (_, _) =>
            {
                // 安定確認中に次の欄へ入力した未確定の文字列も上書きしない。
                if (reading || !busy || !geometryEditableWhileBusy) return;
                dirty = true;
                editRevision++;
            };
            number.KeyDown += async (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ValidateChildren();
                if (autoApply.Checked) await ApplyAsync();
            };
        }
        editDelay.Tick += async (_, _) => { editDelay.Stop(); if (autoApply.Checked) await ApplyAsync(); };
        poll.Tick += (_, _) => RefreshState(false);
        previewArea.Resize += (_, _) => UpdatePreview();
        Resize += (_, _) => UpdatePreview();
        Shown += (_, _) => { RefreshTargets(); poll.Start(); };
        FormClosed += (_, _) => { poll.Stop(); filterDelay.Stop(); editDelay.Stop(); preview.Dispose(); };
        UpdateControls();
        SetStatus("対象を選ぶと、位置・サイズを調整できます。");
    }

    private void ConfigureTooltips()
    {
        tooltip.SetToolTip(refresh, "対象ウィンドウの候補一覧を再取得します。");
        tooltip.SetToolTip(targetPickerButton, "左ボタンを押したまま対象ウィンドウへドラッグし、離して選択します。\nEscで取り消します。");
        tooltip.SetToolTip(thumbnailSelect, "ウィンドウの撮影画像から操作対象を選びます。\n選択画面を閉じると撮影を停止します。");
        tooltip.SetToolTip(autoRead, "対象の位置・サイズの変化を入力欄へ読み取ります。\n編集中・未適用の値は保持します。");
        tooltip.SetToolTip(autoApply, "位置・サイズの編集を自動で反映します。\nOFFでは「適用」を押すまで反映しません。");
        tooltip.SetToolTip(allowOutside, "モニターの作業領域外への移動・サイズ指定を許可します。\nOFFでは反映時に作業領域（タスクバーを除く）へ収めます。");
        tooltip.SetToolTip(alwaysOnTop, "ChunapToolを他のウィンドウより手前に表示します。");
        tooltip.SetToolTip(apply, "入力した位置・サイズを対象へ反映します。\n「自動反映」がOFFのときに使用できます。");
        tooltip.SetToolTip(undo, "対象の位置・サイズ・最大化状態を一つ前へ戻します。\n最大100手。対象を切り替えると履歴をリセットします。");
        UpdateMaximizeTooltip(false);
        UpdatePreviewTooltip();
    }

    private void UpdateMaximizeTooltip(bool isMaximized)
    {
        var text = isMaximized
            ? "対象ウィンドウを通常表示に戻します。"
            : "対象ウィンドウを最大化します。";
        if (tooltip.GetToolTip(maximize) != text) tooltip.SetToolTip(maximize, text);
    }

    private void UpdatePreviewTooltip() => tooltip.SetToolTip(previewToggle, previewExpanded
        ? "プレビューを折りたたみ、プレビュー処理を停止します。"
        : "対象ウィンドウのプレビューを展開します。");

    private void BuildLayout()
    {
        layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        alwaysOnTop.Anchor = AnchorStyles.Left;
        searchRow.Controls.Add(Caption("検索"), 0, 0); searchRow.Controls.Add(search, 1, 0);
        searchRow.Controls.Add(thumbnailSelect, 2, 0); searchRow.Controls.Add(alwaysOnTop, 3, 0);
        var targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        targetPickerButton.Anchor = AnchorStyles.Left;
        targetRow.Controls.Add(Caption("対象"), 0, 0); targetRow.Controls.Add(targets, 1, 0);
        targetRow.Controls.Add(targetPickerButton, 2, 0); targetRow.Controls.Add(refresh, 3, 0);
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Control[] actions = [maximize, autoRead, autoApply, apply, allowOutside, undo];
        for (var i = 0; i < actions.Length; i++)
        {
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions[i].Anchor = AnchorStyles.Left;
            options.Controls.Add(actions[i], i, 0);
        }
        var geometry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 9, RowCount = 1 };
        geometry.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        previewToggle.Anchor = AnchorStyles.Left;
        geometry.Controls.Add(previewToggle, 0, 0);
        for (var i = 0; i < 4; i++)
        {
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            geometry.Controls.Add(Caption(new[] { "X", "Y", "W", "H" }[i]), i * 2 + 1, 0);
            numbers[i].Anchor = AnchorStyles.Left | AnchorStyles.Right;
            geometry.Controls.Add(numbers[i], i * 2 + 2, 0);
        }
        previewArea.Controls.Add(previewMessage);
        previewArea.Visible = false;
        layout.Controls.Add(searchRow, 0, 0); layout.Controls.Add(targetRow, 0, 1);
        layout.Controls.Add(options, 0, 2); layout.Controls.Add(geometry, 0, 3);
        layout.Controls.Add(previewArea, 0, 4); layout.Controls.Add(status, 0, 5);
        Controls.Add(layout);
    }

    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private void RefreshTargets()
    {
        if (busy) { filterDelay.Start(); return; }
        try
        {
            candidates = windows.Enumerate("");
            if (selected is not null)
                selected = candidates.FirstOrDefault(entry => entry.Identity == selected.Identity) ?? selected;
            ApplyTargetFilter();
            RefreshState(false);
        }
        catch (Exception ex) { SetStatus($"一覧を取得できません: {ex.Message}", true); }
    }

    private void ApplyTargetFilter()
    {
        var entries = candidates.Where(entry => WindowCandidates.Matches(entry, search.Text)).ToList();
        var count = entries.Count;
        // 絞込外でも現在対象はコンボに保持し、検索だけで別ウィンドウを操作しない。
        if (selected is not null && !entries.Any(entry => entry.Identity == selected.Identity)) entries.Insert(0, selected);
        reading = true;
        try
        {
            targets.BeginUpdate();
            targets.Items.Clear();
            targets.Items.AddRange(entries.Cast<object>().ToArray());
            targets.SelectedIndex = entries.FindIndex(entry => entry.Identity == selected?.Identity);
        }
        finally { targets.EndUpdate(); reading = false; }
        if (!picking) SetStatus(selected is null ? $"{count}件。対象を選択してください。" : $"候補 {count}件 / 対象: {selected.Title}");
    }

    private void SelectTarget()
    {
        try { SelectTargetCore(); }
        catch (Exception ex) { ClearTarget($"対象を読み込めません: {ex.Message}"); }
    }

    private void SelectTargetCore()
    {
        var next = targets.SelectedItem as WindowEntry;
        if (next is null) return;
        ChooseTarget(next);
    }

    private void ChooseTarget(WindowEntry next)
    {
        var state = windows.GetState(next.Identity);
        if (state is null) { ApplyTargetFilter(); SetStatus("対象が終了しました。一覧を更新してください。", true); return; }
        if (next?.Identity == selected?.Identity && next is not null) { selected = next; RefreshState(false); return; }
        editDelay.Stop();
        dirty = false;
        lastConstraintAttempt = null;
        selected = next;
        current = state;
        history.Select(current);
        preview.Clear();
        previewFailed = false;
        if (current is null) { ClearTarget("対象ウィンドウを選択してください"); return; }
        ApplyTargetFilter();
        WriteState(current);
        UpdateControls();
        UpdatePreview();
    }

    private void RefreshState(bool force)
    {
        if (busy || picking || thumbnailPicker is { IsDisposed: false } || selected is null) return;
        try
        {
            var state = windows.GetState(selected.Identity);
            if (state is null) { ClearTarget("対象が終了しました。一覧を更新してください。"); return; }
            var dragging = windows.IsLeftButtonDown;
            current = state;
            if (autoApply.Checked && !state.IsMaximized && !state.IsMinimized && !allowOutside.Checked && !dragging && !dirty)
            {
                var workArea = windows.GetWorkArea(state.Target);
                if (lastConstraintAttempt is { } attempted && attempted != (state.Bounds, workArea))
                    lastConstraintAttempt = null;
                var bounded = WindowGeometry.Constrain(state.Bounds, workArea, false);
                if (bounded != state.Bounds && lastConstraintAttempt != (state.Bounds, workArea))
                {
                    lastConstraintAttempt = (state.Bounds, workArea);
                    _ = ApplyBoundsAsync(bounded, autoConstrain: true);
                    return;
                }
            }
            history.Observe(state, dragging);
            if (force || (autoRead.Checked && !dirty && !numbers.Any(x => x.ContainsFocus))) WriteState(state);
            else WriteMaximized(state);
            UpdateControls();
            UpdatePreview();
        }
        catch (Exception ex) { SetStatus($"状態を取得できません: {ex.Message}", true); }
    }

    private void WriteState(WindowState state)
    {
        reading = true;
        try
        {
            var values = new[] { state.Bounds.X, state.Bounds.Y, state.Bounds.Width, state.Bounds.Height };
            for (var i = 0; i < 4; i++) numbers[i].Value = Math.Clamp(values[i], numbers[i].Minimum, numbers[i].Maximum);
            maximize.Checked = state.IsMaximized;
            maximize.Text = state.IsMaximized ? "通常化" : "最大化";
            UpdateMaximizeTooltip(state.IsMaximized);
        }
        finally { reading = false; }
    }

    private void WriteMaximized(WindowState state)
    {
        reading = true;
        try
        {
            maximize.Checked = state.IsMaximized;
            maximize.Text = state.IsMaximized ? "通常化" : "最大化";
            UpdateMaximizeTooltip(state.IsMaximized);
        }
        finally { reading = false; }
    }

    private async Task ApplyAsync()
    {
        editDelay.Stop();
        if (busy || selected is null || current is null || current.IsMaximized || current.IsMinimized) return;
        ValidateChildren();
        var bounds = new WindowBounds((int)numbers[0].Value, (int)numbers[1].Value, (int)numbers[2].Value, (int)numbers[3].Value);
        await ApplyBoundsAsync(bounds);
    }

    private async Task ApplyBoundsAsync(WindowBounds requested, bool autoConstrain = false)
    {
        await MutateAsync("位置・サイズを反映しました。", before =>
        {
            var desired = WindowGeometry.Constrain(requested, windows.GetWorkArea(before.Target), allowOutside.Checked);
            windows.ApplyBounds(before.Target, desired);
            return state => state.Bounds == desired || state.Bounds != before.Bounds;
        }, observeBefore: !autoConstrain, readGeometry: !autoConstrain || autoRead.Checked, keepGeometryEditable: true);
    }

    private async Task MaximizeAsync()
    {
        var desired = maximize.Checked;
        await MutateAsync(desired ? "最大化しました。" : "通常の位置・サイズに戻しました。", before =>
        {
            windows.SetMaximized(before.Target, desired);
            return state => state.IsMaximized == desired && !state.IsMinimized;
        });
    }

    private async Task UndoAsync()
    {
        await MutateAsync("直前の状態に戻しました。", before =>
        {
            history.Observe(before);
            if (!history.TryPeekUndo(out var target) || target is null)
                throw new InvalidOperationException("戻せる履歴がありません。");
            windows.Restore(target);
            return state => UndoCompleted(state, target);
        }, isUndo: true);
    }

    // 最大化は通常復元位置で、通常表示は見える矩形で比べる。スナップ中は両者が一致しない。
    private static bool UndoCompleted(WindowState actual, WindowState target) =>
        actual.IsMaximized == target.IsMaximized && !actual.IsMinimized
        && (target.IsMaximized ? actual.Placement.NormalBounds == target.Placement.NormalBounds : actual.Bounds == target.Bounds);

    private async Task MutateAsync(string message, Func<WindowState, Func<WindowState, bool>> begin, bool isUndo = false, bool observeBefore = true, bool readGeometry = true, bool keepGeometryEditable = false)
    {
        if (busy || selected is null) return;
        editDelay.Stop();
        busy = true;
        geometryEditableWhileBusy = keepGeometryEditable;
        var revision = editRevision;
        UpdateControls();
        try
        {
            var before = windows.GetState(selected.Identity) ?? throw new InvalidOperationException("対象が終了しました。");
            if (!isUndo && observeBefore) history.Observe(before);
            var completed = begin(before);
            var watch = Stopwatch.StartNew();
            WindowState? actual = null, previous = null;
            var stable = 0;
            while (watch.ElapsedMilliseconds < 1800)
            {
                await Task.Delay(70);
                if (IsDisposed) return;
                actual = windows.GetState(before.Target) ?? throw new InvalidOperationException("対象が終了しました。");
                stable = actual == previous ? stable + 1 : 0;
                previous = actual;
                if (completed(actual) && stable >= 2) break;
            }
            if (actual is null || !completed(actual)) throw new InvalidOperationException("対象が変更に応答しませんでした。権限または対象アプリの制限を確認してください。");
            if (isUndo) history.AcceptUndo(actual); else history.Observe(actual);
            current = actual;
            var hasPendingEdits = editRevision != revision;
            if (!hasPendingEdits) dirty = false;
            if (readGeometry && !hasPendingEdits) WriteState(actual); else WriteMaximized(actual);
            SetStatus(message);
        }
        catch (Exception ex)
        {
            SetStatus($"変更できません: {ex.Message}", true);
            if (current is not null) WriteMaximized(current);
        }
        finally
        {
            busy = false;
            geometryEditableWhileBusy = false;
            if (!IsDisposed)
            {
                UpdateControls();
                UpdatePreview();
                if (editRevision != revision && dirty && autoApply.Checked) editDelay.Start();
            }
        }
    }

    private void UpdateControls()
    {
        var exists = current is not null;
        var available = !busy && !picking && thumbnailPicker is null;
        var editable = exists && available && !current!.IsMaximized && !current.IsMinimized;
        // 座標反映だけでは入力欄を無効化しない。Tab移動とEnter後のフォーカスを維持する。
        var geometryEditable = exists && (!busy || geometryEditableWhileBusy) && !picking && thumbnailPicker is null
            && !current!.IsMaximized && !current.IsMinimized;
        foreach (var number in numbers) number.Enabled = geometryEditable;
        apply.Enabled = editable && !autoApply.Checked;
        undo.Enabled = exists && available && history.Count > 0;
        undo.Text = history.Count == 0 ? "戻す" : $"戻す ({history.Count})";
        maximize.Enabled = exists && available && !current!.IsMinimized && (current.CanMaximize || current.IsMaximized);
        autoRead.Enabled = exists && available;
        autoApply.Enabled = exists && available;
        allowOutside.Enabled = exists && available;
        targets.Enabled = available;
        refresh.Enabled = available;
        search.Enabled = available;
        targetPickerButton.Enabled = !busy && thumbnailPicker is null;
        thumbnailSelect.Enabled = available;
    }

    private void UpdatePreview()
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (!previewExpanded) { preview.Clear(); return; }
        if (current is not null && !preview.IsAttached && !previewFailed)
        {
            try { preview.Attach(Handle, current.Target.Handle); }
            catch (Exception ex) { previewFailed = true; SetStatus($"プレビューを開始できません: {ex.Message}", true); }
        }
        var visible = current is not null && !current.IsMinimized && WindowState != FormWindowState.Minimized && !previewFailed;
        previewMessage.Text = current is null ? "対象ウィンドウを選択してください"
            : current.IsMinimized ? "対象は最小化されています。復元するとプレビューを再開します。"
            : previewFailed ? "このウィンドウのプレビューを取得できません。" : "";
        if (!preview.IsAttached) return;
        try
        {
            var area = RectangleToClient(previewArea.RectangleToScreen(previewArea.ClientRectangle));
            preview.Update(area, visible);
        }
        catch (Exception ex) { preview.Clear(); previewFailed = true; previewMessage.Text = "プレビューを表示できません。"; SetStatus(ex.Message, true); }
    }

    private void ClearTarget(string message)
    {
        selected = null; current = null; dirty = false;
        history.Select(null);
        preview.Clear();
        reading = true;
        try { targets.SelectedIndex = -1; maximize.Checked = false; maximize.Text = "最大化"; }
        finally { reading = false; }
        UpdateControls(); UpdatePreview(); SetStatus(message);
    }

    private void SetStatus(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { EndTargetPick(false); thumbnailPicker?.Dispose(); poll.Dispose(); filterDelay.Dispose(); editDelay.Dispose(); preview.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }

    // 自動回帰テストでは入力フォーカスや他の開発画面を奪わない。
    protected override bool ShowWithoutActivation => passive;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (picking && keyData == Keys.Escape) { EndTargetPick(false); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BeginTargetPick()
    {
        if (busy || picking) return;
        picking = true;
        pickedCandidate = null;
        editDelay.Stop();
        UpdateControls();
        if (!passive) { targetPickerButton.Capture = true; Cursor = Cursors.Cross; }
        SetStatus("対象ウィンドウへドラッグして離してください。Escで取り消します。");
    }

    private void UpdateTargetPick(Point screenPoint)
    {
        if (!picking) return;
        try { pickedCandidate = targetPicker.Resolve(screenPoint); }
        catch (Exception ex) { pickedCandidate = null; SetStatus(ex.Message, true); return; }
        SetStatus(pickedCandidate is null ? "選択できるウィンドウへドラッグしてください。Escで取消。" : $"選択候補: {pickedCandidate.Title}");
    }

    private void EndTargetPick(bool accept)
    {
        if (!picking) return;
        var entry = pickedCandidate;
        picking = false;
        pickedCandidate = null;
        if (!passive) { targetPickerButton.Capture = false; Cursor = Cursors.Default; }
        if (accept && entry is not null)
        {
            try { ChooseTarget(entry); }
            catch (Exception ex) { SetStatus($"対象を選択できません: {ex.Message}", true); }
        }
        else SetStatus("対象選択を取り消しました。");
        UpdateControls();
        if (dirty && autoApply.Checked && !IsDisposed) editDelay.Start();
    }

    private void TogglePreview()
    {
        var previousHeight = Height;
        previewExpanded = !previewExpanded;
        SuspendLayout();
        try
        {
            if (!previewExpanded)
            {
                expandedPreviewHeight = Math.Max(150, layout.GetRowHeights()[4]);
                preview.Clear();
                previewArea.Visible = false;
                layout.RowStyles[4].SizeType = SizeType.Absolute;
                layout.RowStyles[4].Height = 0;
                MinimumSize = new Size(MinimumSize.Width, 255 * DeviceDpi / 96);
                if (WindowState == FormWindowState.Normal) ClientSize = new Size(ClientSize.Width, Math.Max(216 * DeviceDpi / 96, ClientSize.Height - expandedPreviewHeight));
            }
            else
            {
                MinimumSize = new Size(MinimumSize.Width, 420 * DeviceDpi / 96);
                if (WindowState == FormWindowState.Normal)
                {
                    var area = Screen.FromControl(this).WorkingArea;
                    Height = Math.Min(area.Height, Math.Max(MinimumSize.Height, previousHeight + expandedPreviewHeight));
                    if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
                }
                layout.RowStyles[4].SizeType = SizeType.Percent;
                layout.RowStyles[4].Height = 100;
                previewArea.Visible = true;
                previewFailed = false;
            }
            previewToggle.Text = previewExpanded ? "▲" : "▼";
            previewToggle.AccessibleName = previewExpanded ? "プレビューを非表示" : "プレビューを表示";
            UpdatePreviewTooltip();
        }
        finally { ResumeLayout(true); }
        UpdatePreview();
    }

    private void OpenThumbnailPicker()
    {
        if (busy || picking || thumbnailPicker is not null) return;
        editDelay.Stop();
        try
        {
            using var picker = new ThumbnailPickerForm(windows, new WgcThumbnailCapture(), selected?.Identity, search.Text, passive);
            thumbnailPicker = picker;
            UpdateControls();
            picker.Bounds = Screen.FromControl(this).WorkingArea;
            picker.WindowState = FormWindowState.Maximized;
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedEntry is { } entry) ChooseTarget(entry);
        }
        catch (Exception ex) { SetStatus($"サムネイル一覧を開けません: {ex.Message}", true); }
        finally
        {
            thumbnailPicker = null;
            if (!IsDisposed)
            {
                UpdateControls();
                RefreshState(false);
                if (dirty && autoApply.Checked) editDelay.Start();
            }
        }
    }
}
