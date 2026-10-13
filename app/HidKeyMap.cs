namespace RemoteInput;

/// <summary>
/// Windowsのスキャンコード(Set 1)をHIDキーボードUsageへ変換する。
/// 物理キー位置をそのまま転送するので、Source/Targetが同じレイアウトなら文字も一致する。
/// </summary>
internal static class HidKeyMap
{
    public const byte LeftControl = 0xE0;
    public const byte LeftAlt = 0xE2;
    public const byte RightControl = 0xE4;
    public const byte RightAlt = 0xE6;

    private static readonly byte[] Normal = new byte[0x80];
    private static readonly byte[] Extended = new byte[0x80];

    static HidKeyMap()
    {
        void Set(byte[] table, int scanCode, params byte[] usages)
        {
            for (int i = 0; i < usages.Length; i++) table[scanCode + i] = usages[i];
        }

        Set(Normal, 0x01, 0x29);                                                       // Esc
        Set(Normal, 0x02, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27); // 1-0
        Set(Normal, 0x0C, 0x2D, 0x2E, 0x2A, 0x2B);                                     // - = BS Tab
        Set(Normal, 0x10, 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13); // QWERTYUIOP
        Set(Normal, 0x1A, 0x2F, 0x30, 0x28, 0xE0);                                     // [ ] Enter LCtrl
        Set(Normal, 0x1E, 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F);       // ASDFGHJKL
        Set(Normal, 0x27, 0x33, 0x34, 0x35, 0xE1, 0x31);                               // ; ' ` LShift \
        Set(Normal, 0x2C, 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10);                   // ZXCVBNM
        Set(Normal, 0x33, 0x36, 0x37, 0x38, 0xE5, 0x55, 0xE2, 0x2C, 0x39);             // , . / RShift KP* LAlt Space Caps
        Set(Normal, 0x3B, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43); // F1-F10
        Set(Normal, 0x45, 0x53, 0x47);                                                 // NumLock ScrollLock
        Set(Normal, 0x47, 0x5F, 0x60, 0x61, 0x56);                                     // KP7 KP8 KP9 KP-
        Set(Normal, 0x4B, 0x5C, 0x5D, 0x5E, 0x57);                                     // KP4 KP5 KP6 KP+
        Set(Normal, 0x4F, 0x59, 0x5A, 0x5B, 0x62, 0x63);                               // KP1 KP2 KP3 KP0 KP.
        Set(Normal, 0x56, 0x64);                                                       // ISO \|
        Set(Normal, 0x57, 0x44, 0x45);                                                 // F11 F12

        // JP固有キー
        Set(Normal, 0x70, 0x88); // カタカナひらがな (International2)
        Set(Normal, 0x73, 0x87); // ろ (International1)
        Set(Normal, 0x79, 0x8A); // 変換 (International4)
        Set(Normal, 0x7B, 0x8B); // 無変換 (International5)
        Set(Normal, 0x7D, 0x89); // ¥ (International3)

        Set(Extended, 0x1C, 0x58); // KP Enter
        Set(Extended, 0x1D, 0xE4); // RCtrl
        Set(Extended, 0x35, 0x54); // KP /
        Set(Extended, 0x37, 0x46); // PrintScreen
        Set(Extended, 0x38, 0xE6); // RAlt
        Set(Extended, 0x47, 0x4A, 0x52, 0x4B); // Home Up PgUp
        Set(Extended, 0x4B, 0x50);             // Left
        Set(Extended, 0x4D, 0x4F);             // Right
        Set(Extended, 0x4F, 0x4D, 0x51, 0x4E, 0x49, 0x4C); // End Down PgDn Ins Del
        Set(Extended, 0x5B, 0xE3, 0xE7, 0x65); // LWin RWin Apps
    }

    /// <summary>対応するUsageがなければ0を返す。</summary>
    public static byte ToUsage(uint vkCode, uint scanCode, bool extended)
    {
        // 低レベルフックではスキャンコードが実キーと食い違うキーをVKで補正する
        switch (vkCode)
        {
            case NativeMethods.VK_PAUSE: return 0x48;
            case NativeMethods.VK_NUMLOCK: return 0x53;
            case NativeMethods.VK_SNAPSHOT: return 0x46;
        }

        if (scanCode >= 0x80) return 0;
        if (scanCode == 0x36) return 0xE5; // 右Shiftは拡張フラグ付きで届くことがある
        return extended ? Extended[scanCode] : Normal[scanCode];
    }

    public static bool IsModifier(byte usage) => usage >= 0xE0 && usage <= 0xE7;

    /// <summary>
    /// 日本語キーボードの 半角/全角・英数・カタカナひらがな 等は、押下しか通知されない
    /// (離上が来ない)ことがあるため、押下時に押して離す扱いにする。
    /// </summary>
    public static bool IsMakeOnlyKey(uint vkCode) => vkCode >= 0xF0 && vkCode <= 0xF6;
}
