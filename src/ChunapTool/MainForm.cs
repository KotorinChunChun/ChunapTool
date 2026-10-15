using ChunapTool.Core;
using ChunapTool.Native;
using System.Diagnostics;

namespace ChunapTool;

public sealed class MainForm : BrandedForm
{
    private readonly IWindowService windows;
    private readonly bool passive;
    private readonly WindowHistory history = new();
    private readonly DwmPreview preview = new();
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
    internal string? ManagementStopBlockReason => busy ? "ウィンドウ操作中のため停止できません。" : null;
    private WindowEntry? selected;
    private WindowState? current;
    private bool previewFailed;
    private (WindowBounds Bounds, WindowBounds WorkArea)? lastConstraintAttempt;

    public MainForm() : this(new NativeWindowService()) { }

    internal MainForm(IWindowService windowService, bool passive = false)
    {
        windows = windowService;
        this.passive = passive;
        Text = WindowTitle.Create();
        Font = new Font("Yu Gothic UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 510);
        MinimumSize = new Size(780, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = !passive;
        if (passive) { Opacity = 0; ShowInTaskbar = false; }
        BuildLayout();
        search.TextChanged += (_, _) => { filterDelay.Stop(); filterDelay.Start(); };
        filterDelay.Tick += (_, _) => { filterDelay.Stop(); RefreshTargets(); };
        refresh.Click += (_, _) => RefreshTargets();
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
                if (reading || busy) return;
                dirty = true;
                if (autoApply.Checked) { editDelay.Stop(); editDelay.Start(); }
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

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        alwaysOnTop.Anchor = AnchorStyles.Left;
        searchRow.Controls.Add(Caption("検索"), 0, 0); searchRow.Controls.Add(search, 1, 0); searchRow.Controls.Add(alwaysOnTop, 2, 0);
        var targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        targetRow.Controls.Add(Caption("対象"), 0, 0); targetRow.Controls.Add(targets, 1, 0); targetRow.Controls.Add(refresh, 2, 0);
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Control[] actions = [maximize, autoRead, autoApply, apply, allowOutside, undo];
        for (var i = 0; i < actions.Length; i++)
        {
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions[i].Anchor = AnchorStyles.Left;
            options.Controls.Add(actions[i], i, 0);
        }
        var geometry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 1 };
        geometry.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            geometry.Controls.Add(Caption(new[] { "X", "Y", "W", "H" }[i]), i * 2, 0);
            numbers[i].Anchor = AnchorStyles.Left | AnchorStyles.Right;
            geometry.Controls.Add(numbers[i], i * 2 + 1, 0);
        }
        previewArea.Controls.Add(previewMessage);
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
            var previous = selected?.Identity;
            var entries = windows.Enumerate(search.Text.Trim());
            reading = true;
            try
            {
                targets.Items.Clear();
                targets.Items.AddRange(entries.Cast<object>().ToArray());
                targets.SelectedIndex = entries.ToList().FindIndex(x => x.Identity == previous);
            }
            finally { reading = false; }
            SelectTarget();
            if (selected is null) SetStatus(entries.Count == 0 ? "該当するウィンドウがありません。" : $"{entries.Count}件。対象を選択してください。");
        }
        catch (Exception ex) { SetStatus($"一覧を取得できません: {ex.Message}", true); }
    }

    private void SelectTarget()
    {
        try { SelectTargetCore(); }
        catch (Exception ex) { ClearTarget($"対象を読み込めません: {ex.Message}"); }
    }

    private void SelectTargetCore()
    {
        var next = targets.SelectedItem as WindowEntry;
        if (next?.Identity == selected?.Identity && next is not null) { selected = next; RefreshState(false); return; }
        editDelay.Stop();
        dirty = false;
        lastConstraintAttempt = null;
        selected = next;
        current = next is null ? null : windows.GetState(next.Identity);
        history.Select(current);
        preview.Clear();
        previewFailed = false;
        if (current is null) { ClearTarget("対象ウィンドウを選択してください"); return; }
        try { preview.Attach(Handle, current.Target.Handle); }
        catch (Exception ex) { previewFailed = true; SetStatus($"プレビューを開始できません: {ex.Message}", true); }
        WriteState(current);
        UpdateControls();
        UpdatePreview();
        if (!previewFailed) SetStatus("位置とサイズは見える枠のピクセル値です。");
    }

    private void RefreshState(bool force)
    {
        if (busy || selected is null) return;
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
        }, observeBefore: !autoConstrain, readGeometry: !autoConstrain || autoRead.Checked);
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
            return state => state.IsMaximized == target.IsMaximized && !state.IsMinimized
                && state.Placement.NormalBounds == target.Placement.NormalBounds;
        }, isUndo: true);
    }

    private async Task MutateAsync(string message, Func<WindowState, Func<WindowState, bool>> begin, bool isUndo = false, bool observeBefore = true, bool readGeometry = true)
    {
        if (busy || selected is null) return;
        editDelay.Stop();
        busy = true;
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
            dirty = false;
            if (readGeometry) WriteState(actual); else WriteMaximized(actual);
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
            if (!IsDisposed) { UpdateControls(); UpdatePreview(); }
        }
    }

    private void UpdateControls()
    {
        var exists = current is not null;
        var editable = exists && !busy && !current!.IsMaximized && !current.IsMinimized;
        foreach (var number in numbers) number.Enabled = editable;
        apply.Enabled = editable && !autoApply.Checked;
        undo.Enabled = exists && !busy && history.Count > 0;
        undo.Text = history.Count == 0 ? "戻す" : $"戻す ({history.Count})";
        maximize.Enabled = exists && !busy && !current!.IsMinimized && (current.CanMaximize || current.IsMaximized);
        autoRead.Enabled = exists && !busy;
        autoApply.Enabled = exists && !busy;
        allowOutside.Enabled = exists && !busy;
        targets.Enabled = !busy;
        refresh.Enabled = !busy;
        search.Enabled = !busy;
    }

    private void UpdatePreview()
    {
        if (!IsHandleCreated || IsDisposed) return;
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
        if (disposing) { poll.Dispose(); filterDelay.Dispose(); editDelay.Dispose(); preview.Dispose(); }
        base.Dispose(disposing);
    }

    // 自動回帰テストでは入力フォーカスや他の開発画面を奪わない。
    protected override bool ShowWithoutActivation => passive;
}
