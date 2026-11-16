using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using Microsoft.Win32;

namespace RemoteInput;

/// <summary>PCに接続されている中継ボード1台。SerialNumberで個体を識別する。</summary>
internal sealed record BoardInfo(string SerialNumber, string PortName)
{
    public const int SlotCount = 8;

    /// <summary>接続先から見えるBluetooth名。ファームウェアの命名規則と同じ。</summary>
    public string BluetoothName(int slot) => $"RemoteInput_{SerialNumber.Replace(":", "")[^4..]}_{slot}";

    public override string ToString() => $"{SerialNumber} ({PortName})";
}

/// <summary>STの応答。</summary>
/// <param name="Connected">接続先とのBluetooth接続が確立している(暗号化済み)。</param>
/// <param name="Slot">ボードが選んでいるスロット番号。不明なら0。</param>
/// <param name="Standby">どの接続先も受け付けていない待機状態。</param>
/// <param name="Pairing">新しい接続先とのペアリングを受付中。</param>
internal readonly record struct BoardStatus(bool Connected, int Slot, bool Standby, bool Pairing)
{
    public static BoardStatus Parse(string line)
    {
        int slot = 0;
        int at = line.IndexOf(" slot=", StringComparison.Ordinal);
        if (at >= 0)
        {
            ReadOnlySpan<char> rest = line.AsSpan(at + 6);
            int end = rest.IndexOf(' ');
            int.TryParse(end < 0 ? rest : rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out slot);
        }
        return new BoardStatus(
            line.StartsWith("OK conn=1", StringComparison.Ordinal),
            slot,
            line.Contains(" standby=1", StringComparison.Ordinal),
            line.Contains(" pairing=1", StringComparison.Ordinal));
    }
}

/// <summary>
/// 中継ボード(M5Stack AtomS3 Lite)とのUSBシリアル通信。入力イベントをキューに積み、送信スレッドが
/// テキストコマンド(KR / MR)として順に送る。
/// </summary>
internal sealed class BoardLink : IDisposable
{
    private const string UsbId = "VID_303A&PID_1001";

    // マウス移動は連続するレポートを1つにまとめ、この間隔より速くは送らない
    private const int MinMouseIntervalMs = 8;

    private readonly struct Report
    {
        public bool IsMouse { get; init; }
        public byte Modifiers { get; init; }
        public ulong Keys { get; init; }  // 6キー分を下位バイトから詰める
        public byte Buttons { get; init; }
        public int Dx { get; init; }
        public int Dy { get; init; }
        public int Wheel { get; init; }
        public int Pan { get; init; }
    }

    private readonly SerialPort _port;
    private readonly Thread _writer;
    private readonly Thread _reader;
    private readonly object _lock = new();
    private readonly LinkedList<Report> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _closing;
    private bool _statusRequested;
    private int _slotRequested;
    private readonly List<string> _commands = [];
    private long _lastReceiveTick = Environment.TickCount64;
    private int _lostRaised;

    /// <summary>STの応答を受けたとき。受信スレッドから呼ばれる。</summary>
    public event Action<BoardStatus>? StatusReceived;

    /// <summary>本体のボタンが押され、ボードが接続先を切断したとき。受信スレッドから呼ばれる。</summary>
    public event Action? ButtonPressed;

    /// <summary>PCからの通信が途絶えたため、ボードが自分で切断したとき。受信スレッドから呼ばれる。</summary>
    public event Action? IdleDisconnected;

    /// <summary>ペアリング中の接続先がパスキーを表示し、入力を待っているとき。受信スレッドから呼ばれる。</summary>
    public event Action? PasskeyRequested;

    /// <summary>新しい接続先とのペアリングが完了したとき。受信スレッドから呼ばれる。</summary>
    public event Action? Paired;

    /// <summary>ペアリングの受付が(完了以外の理由で)終わったとき。受信スレッドから呼ばれる。</summary>
    public event Action? PairingEnded;

    /// <summary>ペアリングが失敗したとき(パスキー違いなど)。受信スレッドから呼ばれる。</summary>
    public event Action? PairingFailed;

    /// <summary>接続先とのBLE接続が切れているとボードが報告したとき。受信スレッドから呼ばれる。</summary>
    public event Action? TargetDisconnected;

    /// <summary>USB通信が失われたとき(1回だけ)。任意のスレッドから呼ばれる。</summary>
    public event Action<string>? Lost;

    public BoardInfo Board { get; }

    public long MillisecondsSinceLastReceive => Environment.TickCount64 - Interlocked.Read(ref _lastReceiveTick);

    public BoardLink(BoardInfo board)
    {
        Board = board;
        _port = new SerialPort(board.PortName, 115200)
        {
            // 内蔵USB-Serial-JTAGではDTR/RTSがリセット線に効くため、有効のまま開くと
            // ボードが再起動してBLE接続が切れる。
            DtrEnable = false,
            RtsEnable = false,
            NewLine = "\n",
            WriteTimeout = 500,
        };
        _port.Open();
        _port.DiscardInBuffer();

        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "BoardLink writer" };
        _reader = new Thread(ReaderLoop) { IsBackground = true, Name = "BoardLink reader" };
        _writer.Start();
        _reader.Start();
    }

    /// <summary>接続中のボードを列挙する。COM番号ではなくUSB Serial Numberを基準にする。</summary>
    public static List<BoardInfo> Enumerate()
    {
        var boards = new List<BoardInfo>();
        var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);

        using var device = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\{UsbId}");
        using var cdc = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\{UsbId}&MI_00");
        if (device == null || cdc == null) return boards;

        foreach (string serial in device.GetSubKeyNames())
        {
            using var deviceKey = device.OpenSubKey(serial);
            if (deviceKey?.GetValue("ParentIdPrefix") is not string prefix) continue;

            foreach (string instance in cdc.GetSubKeyNames())
            {
                if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                using var parameters = cdc.OpenSubKey($@"{instance}\Device Parameters");
                if (parameters?.GetValue("PortName") is string portName && present.Contains(portName))
                {
                    boards.Add(new BoardInfo(serial.ToUpperInvariant(), portName));
                }
            }
        }
        return boards;
    }

    public void SendKeyboard(byte modifiers, ReadOnlySpan<byte> keys)
    {
        ulong packed = 0;
        for (int i = 0; i < keys.Length && i < 6; i++) packed |= (ulong)keys[i] << (8 * i);
        Enqueue(new Report { Modifiers = modifiers, Keys = packed });
    }

    public void SendMouse(byte buttons, int dx, int dy, int wheel, int pan)
    {
        lock (_lock)
        {
            // 直前がボタン状態の同じマウスレポートなら移動量を合算する。
            // ボタンが変わるレポートは別に積むので、短いクリックも失われない。
            if (_queue.Last is { } last && last.Value.IsMouse && last.Value.Buttons == buttons)
            {
                Report tail = last.Value;
                last.Value = new Report
                {
                    IsMouse = true,
                    Buttons = buttons,
                    Dx = tail.Dx + dx,
                    Dy = tail.Dy + dy,
                    Wheel = tail.Wheel + wheel,
                    Pan = tail.Pan + pan,
                };
            }
            else
            {
                _queue.AddLast(new Report { IsMouse = true, Buttons = buttons, Dx = dx, Dy = dy, Wheel = wheel, Pan = pan });
            }
        }
        _wake.Set();
    }

    /// <summary>未送信の入力を捨て、接続先で全キー・全ボタンを離した状態にする。</summary>
    public void ReleaseAll()
    {
        lock (_lock)
        {
            _queue.Clear();
            _queue.AddLast(new Report { Modifiers = 0, Keys = 0 });
            _queue.AddLast(new Report { IsMouse = true });
        }
        _wake.Set();
    }

    /// <summary>キューと指示が空になるまで待つ(終了前にReleaseAllやDISCONNECTを確実に送るため)。</summary>
    public void Flush(int timeoutMs)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < timeoutMs && !_closing && Volatile.Read(ref _lostRaised) == 0)
        {
            lock (_lock)
            {
                if (_queue.Count == 0 && _commands.Count == 0) break;
            }
            Thread.Sleep(2);
        }
        // 取り出し済みの最後の行が書き込まれるのを少し待つ
        Thread.Sleep(20);
    }

    /// <summary>接続先スロットを切り替える。ボードは再起動して新しいスロットで待ち受ける。</summary>
    public void SelectSlot(int slot)
    {
        lock (_lock)
        {
            _queue.Clear();
            _commands.Clear();  // 再起動するので、前のスロット向けの指示は捨てる
            _slotRequested = slot;
        }
        _wake.Set();
    }

    /// <summary>選択中のスロットにペアリング済みの接続先からの接続を受け付けさせる。</summary>
    public void Connect() => SendCommand("CONNECT");

    /// <summary>Bluetooth接続を切り、どの接続先も受け付けない待機状態にする。</summary>
    public void Disconnect()
    {
        lock (_lock) _queue.Clear();
        SendCommand("DISCONNECT");
    }

    /// <summary>選択中のスロットで、新しい接続先とのペアリングを一定時間受け付けさせる。</summary>
    public void Pair() => SendCommand("PAIR");

    /// <summary>接続先が表示した6桁のパスキーを渡す。</summary>
    public void SendPasskey(string digits) => SendCommand($"PASSKEY {digits}");

    private void SendCommand(string command)
    {
        lock (_lock) _commands.Add(command);
        _wake.Set();
    }

    public void RequestStatus()
    {
        lock (_lock) _statusRequested = true;
        _wake.Set();
    }

    private void Enqueue(Report report)
    {
        lock (_lock) _queue.AddLast(report);
        _wake.Set();
    }

    private void WriterLoop()
    {
        var sinceMouse = Stopwatch.StartNew();
        try
        {
            while (!_closing)
            {
                Report report = default;
                bool hasReport = false;
                bool status;
                int slot;
                string[] commands = [];
                lock (_lock)
                {
                    status = _statusRequested;
                    _statusRequested = false;
                    slot = _slotRequested;
                    _slotRequested = 0;
                    if (_commands.Count > 0)
                    {
                        commands = [.. _commands];
                        _commands.Clear();
                    }
                    if (_queue.First is { } first)
                    {
                        // 末尾のマウスレポートは合算中なので、間隔が空くまで送らない
                        bool mergeable = first.Value.IsMouse && first == _queue.Last;
                        if (!mergeable || sinceMouse.ElapsedMilliseconds >= MinMouseIntervalMs)
                        {
                            report = first.Value;
                            hasReport = true;
                            _queue.RemoveFirst();
                        }
                    }
                }

                if (slot != 0) _port.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SLOT {slot}"));
                foreach (string command in commands) _port.WriteLine(command);
                if (status) _port.WriteLine("ST");

                if (hasReport)
                {
                    _port.WriteLine(Format(report));
                    if (report.IsMouse) sinceMouse.Restart();
                    continue;
                }

                bool pending;
                lock (_lock) pending = _queue.Count > 0;
                if (pending)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    _wake.WaitOne(200);
                }
            }
        }
        catch (Exception ex) when (!_closing)
        {
            RaiseLost(ex.Message);
        }
        catch (Exception)
        {
            // Dispose中の例外は無視する
        }
    }

    private static string Format(Report r)
    {
        if (r.IsMouse)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"MR {r.Buttons:X2} {Math.Clamp(r.Dx, -32767, 32767)} {Math.Clamp(r.Dy, -32767, 32767)} {Math.Clamp(r.Wheel, -127, 127)} {Math.Clamp(r.Pan, -127, 127)}");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"KR {r.Modifiers:X2} {(byte)r.Keys:X2} {(byte)(r.Keys >> 8):X2} {(byte)(r.Keys >> 16):X2} {(byte)(r.Keys >> 24):X2} {(byte)(r.Keys >> 32):X2} {(byte)(r.Keys >> 40):X2}");
    }

    private void ReaderLoop()
    {
        try
        {
            while (!_closing)
            {
                string line = _port.ReadLine().Trim();
                Interlocked.Exchange(ref _lastReceiveTick, Environment.TickCount64);

                if (line.StartsWith("OK conn=", StringComparison.Ordinal))
                {
                    StatusReceived?.Invoke(BoardStatus.Parse(line));
                }
                else if (line.StartsWith("EV button", StringComparison.Ordinal))
                {
                    ButtonPressed?.Invoke();
                }
                else if (line.StartsWith("EV idle", StringComparison.Ordinal))
                {
                    IdleDisconnected?.Invoke();
                }
                else if (line.StartsWith("EV passkey", StringComparison.Ordinal))
                {
                    PasskeyRequested?.Invoke();
                }
                else if (line.StartsWith("EV paired", StringComparison.Ordinal))
                {
                    Paired?.Invoke();
                }
                else if (line.StartsWith("EV pair end", StringComparison.Ordinal))
                {
                    PairingEnded?.Invoke();
                }
                else if (line.StartsWith("EV auth bonded=0", StringComparison.Ordinal) ||
                         line.StartsWith("EV auth bonded=1 encrypted=0", StringComparison.Ordinal))
                {
                    PairingFailed?.Invoke();
                }
                else if (line.StartsWith("ERR not connected", StringComparison.Ordinal) ||
                         line.StartsWith("EV disconnect", StringComparison.Ordinal))
                {
                    TargetDisconnected?.Invoke();
                }
            }
        }
        catch (Exception ex) when (!_closing)
        {
            RaiseLost(ex.Message);
        }
        catch (Exception)
        {
            // Dispose中の例外は無視する
        }
    }

    private void RaiseLost(string reason)
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0) Lost?.Invoke(reason);
    }

    public void Dispose()
    {
        if (_closing) return;
        _closing = true;
        _wake.Set();
        try
        {
            _port.Close();
        }
        catch (Exception)
        {
            // 抜かれた後のCloseは失敗することがある
        }
        _port.Dispose();
    }
}
