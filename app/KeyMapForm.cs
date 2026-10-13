namespace RemoteInput;

/// <summary>
/// キーマッピングの編集画面。プリセットを適用したあと、個別の定義を追加・削除して上書きできる。
/// </summary>
internal sealed class KeyMapForm : Form
{
    private static readonly KeyName DisabledKey = new(KeyMapping.Disabled, "(無効)");

    private readonly ComboBox _presetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _applyPresetButton = new() { Text = "適用" };
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly ComboBox _fromCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _toCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _addButton = new() { Text = "追加" };
    private readonly Button _swapButton = new() { Text = "入れ替えで追加" };
    private readonly Button _removeButton = new() { Text = "選択を削除" };
    private readonly Button _clearButton = new() { Text = "すべて削除" };
    private readonly Button _okButton = new() { Text = "OK", DialogResult = DialogResult.OK };
    private readonly Button _cancelButton = new() { Text = "キャンセル", DialogResult = DialogResult.Cancel };

    // 変換元Usage → 変換先Usage。変換元ごとに1定義。
    private readonly SortedDictionary<int, int> _map = [];

    public List<KeyMapEntry> Entries => [.. _map.Select(pair => new KeyMapEntry { From = pair.Key, To = pair.Value })];

    public KeyMapForm(string targetName, IEnumerable<KeyMapEntry> entries)
    {
        Text = $"キーマッピング - {targetName}";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 430);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        var presetLabel = new Label { Text = "プリセット", Location = new Point(12, 16), Size = new Size(70, 20) };
        _presetCombo.Location = new Point(84, 12);
        _presetCombo.Size = new Size(352, 24);
        _presetCombo.Items.AddRange(KeyMapping.Presets);
        _presetCombo.SelectedIndex = 0;
        _applyPresetButton.Location = new Point(444, 11);
        _applyPresetButton.Size = new Size(64, 26);

        _list.Location = new Point(12, 46);
        _list.Size = new Size(496, 250);
        _list.Columns.Add("変換元 (Windows側で押すキー)", 236);
        _list.Columns.Add("変換先 (接続先に送るキー)", 236);

        _removeButton.Location = new Point(12, 302);
        _removeButton.Size = new Size(110, 26);
        _clearButton.Location = new Point(128, 302);
        _clearButton.Size = new Size(110, 26);

        var fromLabel = new Label { Text = "変換元", Location = new Point(12, 344), Size = new Size(50, 20) };
        _fromCombo.Location = new Point(64, 340);
        _fromCombo.Size = new Size(186, 24);
        var toLabel = new Label { Text = "→ 変換先", Location = new Point(258, 344), Size = new Size(62, 20) };
        _toCombo.Location = new Point(322, 340);
        _toCombo.Size = new Size(186, 24);
        _fromCombo.Items.AddRange(KeyMapping.Keys);
        _toCombo.Items.Add(DisabledKey);
        _toCombo.Items.AddRange(KeyMapping.Keys);
        _fromCombo.SelectedIndex = 0;
        _toCombo.SelectedIndex = 1;

        _addButton.Location = new Point(282, 370);
        _addButton.Size = new Size(90, 26);
        _swapButton.Location = new Point(378, 370);
        _swapButton.Size = new Size(130, 26);

        _okButton.Location = new Point(322, 398);
        _okButton.Size = new Size(90, 26);
        _cancelButton.Location = new Point(418, 398);
        _cancelButton.Size = new Size(90, 26);

        Controls.AddRange([presetLabel, _presetCombo, _applyPresetButton, _list, _removeButton, _clearButton,
            fromLabel, _fromCombo, toLabel, _toCombo, _addButton, _swapButton, _okButton, _cancelButton]);

        _applyPresetButton.Click += (_, _) => ApplyPreset();
        _addButton.Click += (_, _) => AddSelected(swap: false);
        _swapButton.Click += (_, _) => AddSelected(swap: true);
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton.Click += (_, _) =>
        {
            _map.Clear();
            RefreshList();
        };

        foreach (KeyMapEntry entry in entries) _map[entry.From] = entry.To;
        RefreshList();
    }

    private void ApplyPreset()
    {
        if (_presetCombo.SelectedItem is not KeyPreset preset) return;

        if (_map.Count > 0)
        {
            DialogResult answer = MessageBox.Show(this, "現在の定義をプリセットの内容で置き換えます。", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
        }

        _map.Clear();
        foreach ((byte from, byte to) in preset.Entries) _map[from] = to;
        RefreshList();
    }

    private void AddSelected(bool swap)
    {
        if (_fromCombo.SelectedItem is not KeyName from || _toCombo.SelectedItem is not KeyName to) return;

        if (swap && to.Usage == KeyMapping.Disabled)
        {
            MessageBox.Show(this, "入れ替えには変換先のキーを選んでください。", Text, MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // 同じキーへの変換は定義なしと同じなので、既存の定義を消すだけにする
        Set(from.Usage, to.Usage);
        if (swap) Set(to.Usage, from.Usage);
        RefreshList();
    }

    private void Set(int from, int to)
    {
        if (from == to)
        {
            _map.Remove(from);
        }
        else
        {
            _map[from] = to;
        }
    }

    private void RemoveSelected()
    {
        foreach (ListViewItem item in _list.SelectedItems) _map.Remove((int)item.Tag!);
        RefreshList();
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach ((int from, int to) in _map)
        {
            _list.Items.Add(new ListViewItem([KeyMapping.NameOf(from), KeyMapping.NameOf(to)]) { Tag = from });
        }
        _list.EndUpdate();
    }
}
