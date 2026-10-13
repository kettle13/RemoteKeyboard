namespace RemoteInput;

/// <summary>
/// メインWindowsのキーボード・マウス入力を、USB接続した M5Stack AtomS3 Lite 経由で
/// Bluetooth HIDとして別端末へ転送する。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\RemoteInput_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 既に起動中なので、このプロセスは即終了する。
            return;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            // --selftest <ログファイル>: 解除キー等の動作確認用。数秒で必ず自動解除して終了する。
            int index = Array.IndexOf(args, "--selftest");
            string? selfTestLog = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            Application.Run(new MainForm(selfTestLog));
        }
        finally
        {
            // 異常終了してもカーソル固定が残らないようにする
            NativeMethods.ClipCursor(IntPtr.Zero);
        }
    }
}
