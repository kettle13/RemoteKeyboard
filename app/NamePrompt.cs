namespace RemoteInput;

/// <summary>接続先の名称を1行入力させる小さなダイアログ。</summary>
internal sealed class NamePrompt : Form
{
    private readonly TextBox _textBox = new() { MaxLength = 40 };

    /// <summary>入力された名称。空なら名称なし。</summary>
    public string Value => _textBox.Text.Trim();

    public NamePrompt(string bluetoothName, string? current)
    {
        Text = "接続先の名称";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(340, 112);

        var label = new Label
        {
            Text = $"{bluetoothName} の名称 (空欄で名称なし)",
            Location = new Point(12, 14),
            Size = new Size(316, 20),
        };
        _textBox.Location = new Point(12, 38);
        _textBox.Size = new Size(316, 24);
        _textBox.Text = current ?? "";

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(142, 74), Size = new Size(90, 26) };
        var cancel = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(238, 74),
            Size = new Size(90, 26),
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([label, _textBox, ok, cancel]);
    }
}
