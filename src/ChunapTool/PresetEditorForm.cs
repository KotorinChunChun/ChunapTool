using ChunapTool.Core;

namespace ChunapTool;

/// <summary>プリセットの読み書き先。設定ファイルがない場合（不可視UI回帰など）はメモリ内だけで保持する。</summary>
internal sealed class PresetSource(SettingsStore? store)
{
    private IReadOnlyList<Preset> memory = Presets.Defaults;

    internal IReadOnlyList<Preset> Load() => store?.Load().Presets ?? memory;

    // 設定ファイルは毎回読み直してから変更し、CLIなど別の経路の変更を上書きしない。
    internal IReadOnlyList<Preset> Update(Func<IReadOnlyList<Preset>, IReadOnlyList<Preset>> change) =>
        store is null ? memory = change(memory) : store.Update(document => document.Presets = change(document.Presets)).Presets;
}

/// <summary>プリセットの追加・変更・削除・並べ替え。操作のたびに保存する。</summary>
internal sealed class PresetEditorForm : BrandedForm
{
    private readonly PresetSource source;
    private readonly bool passive;
    internal readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "プリセット一覧" };
    internal readonly TextBox name = new() { Dock = DockStyle.Fill, AccessibleName = "プリセット名", MaxLength = Presets.MaximumNameLength };
    internal readonly NumericUpDown width = new() { Minimum = WindowGeometry.MinimumSize, Maximum = Presets.MaximumSize, Width = 100, AccessibleName = "プリセットの幅" };
    internal readonly NumericUpDown height = new() { Minimum = WindowGeometry.MinimumSize, Maximum = Presets.MaximumSize, Width = 100, AccessibleName = "プリセットの高さ" };
    internal readonly ComboBox basis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, AccessibleName = "プリセットの基準" };
    internal readonly Button add = Action("追加");
    internal readonly Button replace = Action("変更");
    internal readonly Button remove = Action("削除");
    internal readonly Button up = Action("上へ");
    internal readonly Button down = Action("下へ");
    internal readonly Label message = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

    internal PresetEditorForm(PresetSource source, Size initialSize, BoundsBasis initialBasis, bool passive = false)
    {
        this.source = source;
        this.passive = passive;
        SuspendLayout();
        Text = WindowTitle.Create("プリセットの管理");
        Font = new Font("Yu Gothic UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(560, 340);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        if (passive) { Opacity = 0; StartPosition = FormStartPosition.Manual; }
        basis.Items.AddRange(["中身", "枠"]);
        basis.SelectedIndex = initialBasis == BoundsBasis.Client ? 0 : 1;
        width.Value = Math.Clamp(initialSize.Width, width.Minimum, width.Maximum);
        height.Value = Math.Clamp(initialSize.Height, height.Minimum, height.Maximum);
        BuildLayout();
        ResumeLayout(false);
        PerformLayout();
        list.SelectedIndexChanged += (_, _) => ShowSelected();
        add.Click += (_, _) => AddPreset();
        replace.Click += (_, _) => ReplaceSelected();
        remove.Click += (_, _) => RemoveSelected();
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        Reload(null);
    }

    private static Button Action(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(75, 0) };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(10, 0, 0, 0) };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Field(string caption, Control control)
        {
            var row = editor.RowCount++;
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            editor.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            editor.Controls.Add(control, 1, row);
        }
        editor.RowCount = 0;
        Field("名前", name);
        Field("幅", width);
        Field("高さ", height);
        Field("基準", basis);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(220, 0) };
        buttons.Controls.AddRange([add, replace, remove, up, down]);
        var row = editor.RowCount++;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.Controls.Add(buttons, 0, row);
        editor.SetColumnSpan(buttons, 2);
        root.Controls.Add(list, 0, 0);
        root.Controls.Add(editor, 1, 0);
        root.Controls.Add(message, 0, 1);
        root.SetColumnSpan(message, 2);
        Controls.Add(root);
    }

    internal BoundsBasis SelectedBasis => basis.SelectedIndex == 0 ? BoundsBasis.Client : BoundsBasis.Frame;

    private Preset InputPreset() => new(name.Text.Trim(), (int)width.Value, (int)height.Value, SelectedBasis);

    private void ShowSelected()
    {
        if (list.SelectedItem is not Preset preset) { UpdateButtons(); return; }
        name.Text = preset.Name;
        width.Value = Math.Clamp(preset.Width, width.Minimum, width.Maximum);
        height.Value = Math.Clamp(preset.Height, height.Minimum, height.Maximum);
        basis.SelectedIndex = preset.Basis == BoundsBasis.Client ? 0 : 1;
        UpdateButtons();
    }

    internal void AddPreset()
    {
        var preset = InputPreset();
        Change(presets => Presets.Add(presets, preset), preset.Name, $"追加しました: {preset}");
    }

    internal void ReplaceSelected()
    {
        if (list.SelectedItem is not Preset selected) return;
        var preset = InputPreset();
        Change(presets => Presets.Replace(presets, selected.Name, preset), preset.Name, $"変更しました: {preset}");
    }

    internal void RemoveSelected()
    {
        if (list.SelectedItem is not Preset selected) return;
        Change(presets => Presets.Remove(presets, selected.Name), null, $"削除しました: {selected.Name}");
    }

    internal void MoveSelected(int offset)
    {
        if (list.SelectedItem is not Preset selected) return;
        int position = list.SelectedIndex + 1 + offset;
        if (position < 1 || position > list.Items.Count) return;
        Change(presets => Presets.Move(presets, selected.Name, position), selected.Name, $"{position}番目へ移動しました: {selected.Name}");
    }

    private void Change(Func<IReadOnlyList<Preset>, IReadOnlyList<Preset>> change, string? select, string done)
    {
        try
        {
            Reload(source.Update(change), select);
            SetMessage(done, false);
        }
        catch (Exception ex) when (ex is ArgumentException or SettingsException or IOException or UnauthorizedAccessException)
        {
            SetMessage(ex.Message, true);
        }
    }

    private void Reload(string? select)
    {
        try { Reload(source.Load(), select); }
        catch (Exception ex) when (ex is SettingsException or IOException or UnauthorizedAccessException)
        {
            Reload([], null);
            SetMessage("プリセットを読めません: " + ex.Message, true);
        }
    }

    private void Reload(IReadOnlyList<Preset> presets, string? select)
    {
        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(presets.Cast<object>().ToArray());
        list.EndUpdate();
        list.SelectedIndex = select is null ? -1 : presets.ToList().FindIndex(preset => string.Equals(preset.Name, select, StringComparison.OrdinalIgnoreCase));
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = list.SelectedIndex >= 0;
        replace.Enabled = remove.Enabled = selected;
        up.Enabled = selected && list.SelectedIndex > 0;
        down.Enabled = selected && list.SelectedIndex < list.Items.Count - 1;
    }

    private void SetMessage(string text, bool error)
    {
        message.Text = text;
        message.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    protected override bool ShowWithoutActivation => passive;
}
