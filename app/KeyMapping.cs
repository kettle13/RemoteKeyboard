namespace RemoteInput;

/// <summary>1キー分の変換定義。From / To はHIDキーボードUsage。To が 0 ならそのキーを無効にする。</summary>
internal sealed class KeyMapEntry
{
    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>接続先ごとに保持する設定。</summary>
internal sealed class TargetProfile
{
    /// <summary>画面上で接続先を見分けるための名称。接続先には送られない。</summary>
    public string? Name { get; set; }

    public List<KeyMapEntry> KeyMap { get; set; } = [];
}

internal sealed record KeyPreset(string Name, (byte From, byte To)[] Entries)
{
    public override string ToString() => Name;
}

internal sealed record KeyName(byte Usage, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// キーマッピング。Source / Target Layout とは独立で、物理キー(HID Usage)同士を対応付ける。
/// 定義のないキーはそのまま転送する。
/// </summary>
internal static class KeyMapping
{
    public const byte Disabled = 0;

    private const byte LeftControl = 0xE0, LeftAlt = 0xE2, LeftGui = 0xE3;
    private const byte RightControl = 0xE4, RightAlt = 0xE6, RightGui = 0xE7;
    private const byte Henkan = 0x8A, Muhenkan = 0x8B, Lang1 = 0x90, Lang2 = 0x91;

    // macOS / iPadOS は 変換・無変換 を解釈しないので、JISのMacキーボードが送る かな・英数 に置き換える
    private static readonly (byte, byte)[] JapaneseImeKeysForApple = [(Henkan, Lang1), (Muhenkan, Lang2)];

    public static readonly KeyPreset[] Presets =
    [
        new("Windows → macOS / iPadOS (Ctrl ⇔ Command)",
        [
            (LeftControl, LeftGui), (LeftGui, LeftControl),
            (RightControl, RightGui), (RightGui, RightControl),
            .. JapaneseImeKeysForApple,
        ]),
        new("Windows → macOS / iPadOS (Macの配列に合わせる: Alt ⇔ Win)",
        [
            (LeftAlt, LeftGui), (LeftGui, LeftAlt),
            (RightAlt, RightGui), (RightGui, RightAlt),
            .. JapaneseImeKeysForApple,
        ]),
        new("Windows → macOS / iPadOS (変換・無変換 → かな・英数 のみ)", JapaneseImeKeysForApple),
    ];

    /// <summary>設定画面に出すキーの一覧。</summary>
    public static readonly KeyName[] Keys = BuildKeys();

    private static KeyName[] BuildKeys()
    {
        var keys = new List<KeyName>
        {
            new(0xE0, "左Ctrl"), new(0xE1, "左Shift"), new(0xE2, "左Alt"), new(0xE3, "左Win / Command"),
            new(0xE4, "右Ctrl"), new(0xE5, "右Shift"), new(0xE6, "右Alt"), new(0xE7, "右Win / Command"),
            new(0x39, "CapsLock (JP: 英数)"), new(0x35, "` (JP: 半角/全角)"),
            new(0x8B, "無変換"), new(0x8A, "変換"), new(0x88, "カタカナひらがな"),
            new(0x91, "英数 (Mac)"), new(0x90, "かな (Mac)"),
            new(0x89, "¥"), new(0x87, "ろ"),
            new(0x29, "Esc"), new(0x2B, "Tab"), new(0x28, "Enter"), new(0x2C, "Space"), new(0x2A, "Backspace"),
            new(0x65, "アプリケーション (メニュー)"),
        };

        for (int i = 0; i < 26; i++) keys.Add(new((byte)(0x04 + i), ((char)('A' + i)).ToString()));
        for (int i = 0; i < 10; i++) keys.Add(new((byte)(0x1E + i), ((i + 1) % 10).ToString()));

        keys.AddRange(
        [
            new(0x2D, "-"), new(0x2E, "= (JP: ^)"), new(0x2F, "[ (JP: @)"), new(0x30, "] (JP: [)"),
            new(0x31, "\\ (JP: ])"), new(0x33, ";"), new(0x34, "' (JP: :)"),
            new(0x36, ","), new(0x37, "."), new(0x38, "/"), new(0x64, "ISO \\"),
        ]);

        for (int i = 0; i < 12; i++) keys.Add(new((byte)(0x3A + i), $"F{i + 1}"));
        for (int i = 0; i < 12; i++) keys.Add(new((byte)(0x68 + i), $"F{i + 13}"));

        keys.AddRange(
        [
            new(0x52, "↑"), new(0x51, "↓"), new(0x50, "←"), new(0x4F, "→"),
            new(0x4A, "Home"), new(0x4D, "End"), new(0x4B, "PageUp"), new(0x4E, "PageDown"),
            new(0x49, "Insert"), new(0x4C, "Delete"),
            new(0x46, "PrintScreen"), new(0x47, "ScrollLock"), new(0x48, "Pause"),
            new(0x53, "NumLock"), new(0x54, "テンキー /"), new(0x55, "テンキー *"), new(0x56, "テンキー -"),
            new(0x57, "テンキー +"), new(0x58, "テンキー Enter"), new(0x63, "テンキー ."),
        ]);
        for (int i = 0; i < 10; i++) keys.Add(new((byte)(i == 0 ? 0x62 : 0x58 + i), $"テンキー {i}"));

        return [.. keys];
    }

    public static string NameOf(int usage)
    {
        if (usage == Disabled) return "(無効)";
        return Array.Find(Keys, k => k.Usage == usage)?.Name ?? $"Usage 0x{usage:X2}";
    }

    /// <summary>Usageで引く256要素の変換表を作る。定義のないキーは自分自身に対応する。</summary>
    public static byte[] BuildTable(IEnumerable<KeyMapEntry> entries)
    {
        var table = new byte[256];
        for (int i = 0; i < table.Length; i++) table[i] = (byte)i;
        foreach (KeyMapEntry entry in entries)
        {
            if (entry.From is > 0 and < 256 && entry.To is >= 0 and < 256) table[entry.From] = (byte)entry.To;
        }
        return table;
    }
}
