namespace RemoteInput;

/// <summary>ペアリング時に接続先が表示する6桁のパスキーを入力させるダイアログ。</summary>
internal sealed class PasskeyPrompt : Form
{
    private readonly TextBox _textBox = new() { MaxLength = 6 };
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, Enabled = false };

    /// <summary>入力された6桁の数字。</summary>
    public string Value => _textBox.Text;

    public PasskeyPrompt(string bluetoothName)
    {
        Text = "ペアリング";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true;
        ClientSize = new Size(340, 136);

        var label = new Label
        {
            Text = $"接続先の画面に表示された6桁の数字を入力してください。\n({bluetoothName})",
            Location = new Point(12, 12),
            Size = new Size(316, 40),
        };
        _textBox.Location = new Point(12, 60);
        _textBox.Size = new Size(120, 28);
        _textBox.Font = new Font(Font.FontFamily, 14F);
        _textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true;
        };
        _textBox.TextChanged += (_, _) => _ok.Enabled = _textBox.Text.Length == 6 && _textBox.Text.All(char.IsAsciiDigit);

        _ok.Location = new Point(142, 98);
        _ok.Size = new Size(90, 26);
        var cancel = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(238, 98),
            Size = new Size(90, 26),
        };
        AcceptButton = _ok;
        CancelButton = cancel;
        Controls.AddRange([label, _textBox, _ok, cancel]);
    }
}
