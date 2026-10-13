using System.Runtime.InteropServices;

namespace RemoteInput;

/// <summary>
/// Remote Input中の入力取得。低レベルフックでキーボード・マウスをWindows側へ流さず、
/// キーはフックから、マウスはRaw Input(加速前の相対移動量)から読み取って転送する。
/// すべてUIスレッドで動く。
/// </summary>
internal sealed class InputCapture
{
    private const int WheelDelta = 120;

    private readonly BoardLink _link;
    private readonly IntPtr _windowHandle;
    private readonly uint _releaseVk;
    private readonly int _scrollScale;
    private readonly int _pointerSpeed;
    private readonly Action<string> _requestStop;
    private readonly bool _acceptInjected;
    private readonly byte[] _keyMap;

    // GCに回収されないようフィールドとして保持する
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _rawInputRegistered;

    private byte _modifiers;
    private byte _physicalModifiers;  // キーマッピング適用前の修飾キー状態
    private readonly List<byte> _keys = new(6);
    private byte _buttons;
    private int _wheelRemainder;
    private int _panRemainder;
    private int _dxRemainder;
    private int _dyRemainder;

    /// <param name="requestStop">解除を要求する。フック内から呼ばれるので、呼ばれた側で後回しに実行すること。</param>
    /// <param name="scrollScale">ホイール1ノッチあたりのスクロール量。負の値で向きが逆になる。</param>
    /// <param name="pointerSpeed">マウス移動量の倍率(%)。</param>
    /// <param name="keyMap">KeyMapping.BuildTable で作ったキー変換表。</param>
    /// <param name="acceptInjected">自己テスト用。注入された入力も実入力として扱う。</param>
    public InputCapture(BoardLink link, IntPtr windowHandle, uint releaseVk, int scrollScale, int pointerSpeed,
        byte[] keyMap, Action<string> requestStop, bool acceptInjected = false)
    {
        _link = link;
        _windowHandle = windowHandle;
        _releaseVk = releaseVk;
        _scrollScale = scrollScale;
        _pointerSpeed = pointerSpeed;
        _keyMap = keyMap;
        _requestStop = requestStop;
        _acceptInjected = acceptInjected;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public void Start()
    {
        IntPtr module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new InvalidOperationException($"キーボードフックの登録に失敗しました: 0x{Marshal.GetLastWin32Error():X8}");
        }

        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"マウスフックの登録に失敗しました: 0x{error:X8}");
        }

        var device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = _windowHandle,
        };
        if (!NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"Raw Inputの登録に失敗しました: 0x{error:X8}");
        }
        _rawInputRegistered = true;
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_rawInputRegistered)
        {
            var device = new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = NativeMethods.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero,
            };
            NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _rawInputRegistered = false;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // 他のソフトが注入した入力は転送も抑止もしない
            if (_acceptInjected || (info.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                bool up = (info.flags & NativeMethods.LLKHF_UP) != 0;

                if (info.vkCode == _releaseVk)
                {
                    if (!up) _requestStop("解除キーで解除しました");
                    return 1;
                }

                HandleKey(info, up);
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleKey(KBDLLHOOKSTRUCT info, bool up)
    {
        bool extended = (info.flags & NativeMethods.LLKHF_EXTENDED) != 0;
        byte physical = HidKeyMap.ToUsage(info.vkCode, info.scanCode, extended);
        if (physical == 0) return;

        if (HidKeyMap.IsModifier(physical))
        {
            byte bit = (byte)(1 << (physical - 0xE0));
            _physicalModifiers = up ? (byte)(_physicalModifiers & ~bit) : (byte)(_physicalModifiers | bit);
        }

        // Ctrl+Alt+Del はWindows側でも必ず処理され、以降の離上を取りこぼすので解除する
        if (!up && info.vkCode == NativeMethods.VK_DELETE && IsControlDown && IsAltDown)
        {
            _requestStop("Ctrl+Alt+Del のため解除しました");
            return;
        }

        byte usage = _keyMap[physical];
        if (usage == KeyMapping.Disabled) return;

        if (HidKeyMap.IsMakeOnlyKey(info.vkCode))
        {
            if (up) return;
            Press(usage);
            SendKeyboard();
            Release(usage);
            SendKeyboard();
            return;
        }

        bool changed = up ? Release(usage) : Press(usage);
        if (changed) SendKeyboard();
    }

    // Windows側が実際に見ている組み合わせで判定するので、マッピング前の状態を使う
    private bool IsControlDown => (_physicalModifiers & 0x11) != 0;
    private bool IsAltDown => (_physicalModifiers & 0x44) != 0;

    private bool Press(byte usage)
    {
        if (HidKeyMap.IsModifier(usage))
        {
            byte bit = (byte)(1 << (usage - 0xE0));
            if ((_modifiers & bit) != 0) return false;
            _modifiers |= bit;
            return true;
        }

        // オートリピートや7キー目以降は状態が変わらないので送らない
        if (_keys.Contains(usage) || _keys.Count >= 6) return false;
        _keys.Add(usage);
        return true;
    }

    private bool Release(byte usage)
    {
        if (HidKeyMap.IsModifier(usage))
        {
            byte bit = (byte)(1 << (usage - 0xE0));
            if ((_modifiers & bit) == 0) return false;
            _modifiers &= (byte)~bit;
            return true;
        }

        return _keys.Remove(usage);
    }

    private void SendKeyboard() => _link.SendKeyboard(_modifiers, CollectionsMarshal.AsSpan(_keys));

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            // マウスの内容はRaw Input側で読むので、ここではWindows側へ流さないだけ
            if (_acceptInjected || (info.flags & NativeMethods.LLMHF_INJECTED) == 0) return 1;
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>フォームのWndProcから WM_INPUT を渡す。</summary>
    public void HandleRawInput(IntPtr lParam)
    {
        uint size = (uint)Marshal.SizeOf<RAWINPUT>();
        uint read = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, out RAWINPUT input, ref size,
            (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (read == uint.MaxValue || input.header.dwType != NativeMethods.RIM_TYPEMOUSE) return;

        RAWMOUSE mouse = input.mouse;
        int dx = 0;
        int dy = 0;

        // 絶対座標のデバイス(ペンやリモート操作)は相対移動に直せないので移動は無視する
        if ((mouse.usFlags & NativeMethods.MOUSE_MOVE_ABSOLUTE) == 0)
        {
            dx = Scale(ref _dxRemainder, mouse.lLastX);
            dy = Scale(ref _dyRemainder, mouse.lLastY);
        }

        // RI_MOUSE_BUTTON_n_DOWN / UP は 2ビットずつ並んでいる(左, 右, 中, X1, X2)
        byte before = _buttons;
        for (int i = 0; i < 5; i++)
        {
            if ((mouse.usButtonFlags & (1 << (i * 2))) != 0) _buttons |= (byte)(1 << i);
            if ((mouse.usButtonFlags & (2 << (i * 2))) != 0) _buttons &= (byte)~(1 << i);
        }

        int wheel = 0;
        int pan = 0;
        if ((mouse.usButtonFlags & NativeMethods.RI_MOUSE_WHEEL) != 0)
        {
            wheel = TakeNotches(ref _wheelRemainder, mouse.usButtonData);
        }
        if ((mouse.usButtonFlags & NativeMethods.RI_MOUSE_HWHEEL) != 0)
        {
            pan = TakeNotches(ref _panRemainder, mouse.usButtonData);
        }

        if (dx == 0 && dy == 0 && wheel == 0 && pan == 0 && _buttons == before) return;
        _link.SendMouse(_buttons, dx, dy, wheel, pan);
    }

    // 倍率をかけた端数を持ち越すので、ゆっくり動かしても移動量が失われない
    private int Scale(ref int remainder, int delta)
    {
        remainder += delta * _pointerSpeed;
        int counts = remainder / 100;
        remainder -= counts * 100;
        return counts;
    }

    // 高分解能ホイールは1ノッチ未満の端数で届くので、端数を持ち越して換算する
    private int TakeNotches(ref int remainder, short delta)
    {
        remainder += delta * _scrollScale;
        int units = remainder / WheelDelta;
        remainder -= units * WheelDelta;
        return units;
    }
}
