using Microsoft.Win32;

namespace RemoteInput;

/// <summary>
/// 操作ウィンドウ。待機中は「開始」ボタンを出し、制御中はTopMostにしてカーソルを
/// ボタン中央へ固定する。
/// </summary>
/// <remarks>
/// ボードは電源投入時・スロット切替時とも切断状態で起動し、「接続」(または「毎回BT切断する」が
/// オンのときの「開始」)を押したときだけ接続先からの接続を受け付ける。新しい接続先との
/// ペアリングは「ペアリング」を押してから一定時間だけ受け付け、接続先に表示されたパスキーを
/// このアプリで入力して完了する。
/// </remarks>
internal sealed class MainForm : Form
{
    // この時間ボードから何も返らなければ通信異常とみなす
    private const int LinkTimeoutMs = 2500;

    // 自己テストでは、解除キーが効かなくてもこの時間で必ず解除する
    private const int SelfTestTimeoutMs = 4000;

    // スロット切替でボードが再起動して新しいスロットを報告するまでの猶予
    private const int SlotSwitchTimeoutMs = 3000;

    // 「接続」を押してから接続先がつないでくるまで待つ時間
    private const int ConnectTimeoutMs = 15000;

    // 接続を指示した直後は、指示前の(待機中の)状態報告が届くことがあるので無視する
    private const int ConnectGraceMs = 1500;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ComboBox _boardCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _refreshButton = new() { Text = "更新" };
    private readonly ComboBox _slotCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _renameButton = new() { Text = "名称" };
    private readonly ComboBox _releaseKeyCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _scrollScale = new() { Minimum = 1, Maximum = 20 };
    private readonly NumericUpDown _pointerSpeed = new()
    {
        Minimum = AppSettings.MinPointerSpeed,
        Maximum = AppSettings.MaxPointerSpeed,
        Increment = 10,
    };
    private readonly CheckBox _invertScroll = new() { Text = "スクロールを逆にする" };
    private readonly Button _keyMapButton = new() { Text = "キーマッピング..." };
    private readonly Label _keyMapLabel = new() { AutoEllipsis = true };
    private readonly CheckBox _disconnectAfterUse = new() { Text = "毎回BT切断する" };
    private readonly Button _pairButton = new() { Text = "ペアリング" };
    private readonly Button _disconnectButton = new() { Text = "切断" };
    private readonly Button _startButton = new() { Text = "開始", Enabled = false };
    private readonly Label _statusLabel = new() { AutoEllipsis = true };
    private readonly Label _messageLabel = new() { AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    private readonly ToolTip _toolTip = new();

    private BoardLink? _link;
    private InputCapture? _capture;
    private bool _targetConnected;
    private bool _standby = true;  // どの接続先も受け付けていない状態
    private bool _pairing;         // 新しい接続先とのペアリングを受付中
    private bool _connectPending;  // 「接続」を押し、接続先がつないでくるのを待っている
    private bool _startWhenConnected;
    private long _connectRequestedAt;
    private PasskeyPrompt? _passkeyDialog;
    private bool _passkeyDialogClosedByBoard;
    private bool _updatingBoards;
    private bool _updatingSlot;
    private bool _updatingOptions;
    private long _slotSwitchDeadline;
    private readonly string? _selfTestLog;
    private bool _selfTestStarted;

    private bool IsControlling => _capture != null;

    private int SelectedSlot => Math.Max(_slotCombo.SelectedIndex, 0) + 1;

    private TargetProfile? SelectedProfile =>
        _boardCombo.SelectedItem is BoardInfo board ? _settings.GetProfile(board.SerialNumber, SelectedSlot) : null;

    /// <param name="selfTestLog">
    /// 指定すると自己テストとして動く: 接続先へ接続して制御を開始し、注入された入力も
    /// 実入力として扱い、解除後に経過をこのファイルへ書いて終了する。
    /// </param>
    public MainForm(string? selfTestLog = null)
    {
        _selfTestLog = selfTestLog;
        Text = "Remote Input";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 404);

        var boardLabel = new Label { Text = "ボード", Location = new Point(12, 16), Size = new Size(84, 20) };
        _boardCombo.Location = new Point(100, 12);
        _boardCombo.Size = new Size(200, 24);
        _refreshButton.Location = new Point(308, 11);
        _refreshButton.Size = new Size(60, 26);

        var slotLabel = new Label { Text = "接続先", Location = new Point(12, 50), Size = new Size(84, 20) };
        _slotCombo.Location = new Point(100, 46);
        _slotCombo.Size = new Size(200, 24);
        _slotCombo.DropDownWidth = 268;
        _renameButton.Location = new Point(308, 45);
        _renameButton.Size = new Size(60, 26);

        var releaseLabel = new Label { Text = "解除キー", Location = new Point(12, 84), Size = new Size(84, 20) };
        _releaseKeyCombo.Location = new Point(100, 80);
        _releaseKeyCombo.Size = new Size(70, 24);
        for (int n = AppSettings.MinReleaseKey; n <= AppSettings.MaxReleaseKey; n++) _releaseKeyCombo.Items.Add($"F{n}");
        _releaseKeyCombo.SelectedIndex = _settings.ReleaseKey - AppSettings.MinReleaseKey;

        var scrollLabel = new Label { Text = "スクロール倍率", Location = new Point(190, 84), Size = new Size(110, 20) };
        _scrollScale.Location = new Point(308, 80);
        _scrollScale.Size = new Size(60, 24);
        _scrollScale.Value = _settings.ScrollScale;

        var speedLabel = new Label { Text = "ポインタ速度(%)", Location = new Point(12, 118), Size = new Size(88, 20) };
        _pointerSpeed.Location = new Point(100, 114);
        _pointerSpeed.Size = new Size(70, 24);
        _pointerSpeed.Value = _settings.PointerSpeed;
        _invertScroll.Location = new Point(190, 115);
        _invertScroll.Size = new Size(178, 24);
        _invertScroll.Checked = _settings.InvertScroll;

        _keyMapButton.Location = new Point(12, 146);
        _keyMapButton.Size = new Size(158, 26);
        _keyMapLabel.Location = new Point(190, 151);
        _keyMapLabel.Size = new Size(178, 20);

        _disconnectAfterUse.Location = new Point(12, 182);
        _disconnectAfterUse.Size = new Size(170, 24);
        _pairButton.Location = new Point(190, 180);
        _pairButton.Size = new Size(86, 26);
        _disconnectButton.Location = new Point(282, 180);
        _disconnectButton.Size = new Size(86, 26);
        _toolTip.SetToolTip(_disconnectAfterUse,
            "制御を終えるたびにBluetooth接続を切ります。\niPad / iPhone で画面キーボードを使いたい場合にオンにします。\n接続先ごとに保存されます。");
        _toolTip.SetToolTip(_pairButton, "新しい接続先とのペアリングを60秒間受け付けます。");
        _toolTip.SetToolTip(_disconnectButton, "接続先とのBluetooth接続を切ります。");

        _statusLabel.Location = new Point(12, 218);
        _statusLabel.Size = new Size(356, 20);

        _startButton.Location = new Point(12, 246);
        _startButton.Size = new Size(356, 110);
        _startButton.Font = new Font(Font.FontFamily, 22F, FontStyle.Bold);

        _messageLabel.Location = new Point(12, 368);
        _messageLabel.Size = new Size(356, 20);

        Controls.AddRange([boardLabel, _boardCombo, _refreshButton, slotLabel, _slotCombo, _renameButton, releaseLabel, _releaseKeyCombo, scrollLabel,
            _scrollScale, speedLabel, _pointerSpeed, _invertScroll, _keyMapButton, _keyMapLabel,
            _disconnectAfterUse, _pairButton, _disconnectButton, _statusLabel, _startButton, _messageLabel]);

        _refreshButton.Click += (_, _) => RefreshBoards();
        _boardCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingBoards) OpenSelectedBoard();
        };
        _slotCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingSlot) SwitchSlot();
        };
        _renameButton.Click += (_, _) => RenameSlot();
        _disconnectAfterUse.CheckedChanged += (_, _) =>
        {
            if (_updatingOptions || SelectedProfile is not { } profile) return;
            profile.DisconnectAfterUse = _disconnectAfterUse.Checked;
            SaveSettings();
            UpdateStatus();
        };
        _pairButton.Click += (_, _) => StartPairing();
        _disconnectButton.Click += (_, _) => DisconnectTarget("切断しました");
        _startButton.Click += (_, _) => OnStartButton();
        _keyMapButton.Click += (_, _) => EditKeyMap();
        _statusTimer.Tick += (_, _) => PollLink();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshBoards();
        _statusTimer.Start();
    }

    private void RefreshBoards()
    {
        List<BoardInfo> boards = BoardLink.Enumerate();
        string? wanted = (_boardCombo.SelectedItem as BoardInfo)?.SerialNumber ?? _settings.BoardSerial;

        _updatingBoards = true;
        _boardCombo.Items.Clear();
        foreach (BoardInfo board in boards) _boardCombo.Items.Add(board);
        int index = boards.FindIndex(b => b.SerialNumber == wanted);
        _boardCombo.SelectedIndex = boards.Count == 0 ? -1 : Math.Max(index, 0);
        _updatingBoards = false;

        OpenSelectedBoard();
    }

    private void OpenSelectedBoard()
    {
        if (_boardCombo.SelectedItem is not BoardInfo board)
        {
            CloseLink();
            ShowSlots(null);
            UpdateStatus();
            return;
        }

        // 同じボード・同じポートを開いたままなら何もしない
        if (_link != null && _link.Board == board) return;

        CloseLink();
        ShowSlots(board);
        try
        {
            var link = new BoardLink(board);
            link.StatusReceived += status => Post(link, () => OnTargetStatus(status));
            link.TargetDisconnected += () => Post(link, OnTargetDisconnected);
            link.ButtonPressed += () => Post(link, () => OnBoardDisconnected("本体のボタンで切断しました"));
            link.IdleDisconnected += () => Post(link, () => OnBoardDisconnected("アプリからの通信が途切れたため、ボードが切断しました"));
            link.PasskeyRequested += () => Post(link, AskPasskey);
            link.Paired += () => Post(link, OnPaired);
            link.PairingEnded += () => Post(link, OnPairingEnded);
            link.PairingFailed += () => Post(link, OnPairingFailed);
            link.Lost += reason => Post(link, () => OnLinkLost($"ボードとの通信が切れました: {reason}"));
            _link = link;
            _settings.BoardSerial = board.SerialNumber;
            link.RequestStatus();
        }
        catch (Exception ex)
        {
            _messageLabel.Text = $"{board.PortName} を開けません: {ex.Message}";
        }
        UpdateStatus();
    }

    // 接続先の一覧には、付けた名称と、接続先のBluetooth設定に出る名前を表示する
    private void ShowSlots(BoardInfo? board)
    {
        int selected = Math.Max(_slotCombo.SelectedIndex, 0);

        _updatingSlot = true;
        _slotCombo.Items.Clear();
        if (board != null)
        {
            for (int slot = 1; slot <= BoardInfo.SlotCount; slot++)
            {
                string? name = _settings.GetProfile(board.SerialNumber, slot).Name;
                _slotCombo.Items.Add(string.IsNullOrEmpty(name)
                    ? $"{slot}: {board.BluetoothName(slot)}"
                    : $"{slot}: {name} ({board.BluetoothName(slot)})");
            }
            _slotCombo.SelectedIndex = selected;
        }
        _updatingSlot = false;
        ShowTargetOptions();
    }

    // 接続先ごとの設定(毎回BT切断する)を画面に反映する
    private void ShowTargetOptions()
    {
        _updatingOptions = true;
        _disconnectAfterUse.Checked = SelectedProfile?.DisconnectAfterUse ?? false;
        _updatingOptions = false;
    }

    private void RenameSlot()
    {
        if (_boardCombo.SelectedItem is not BoardInfo board || _slotCombo.SelectedIndex < 0) return;

        TargetProfile profile = _settings.GetProfile(board.SerialNumber, SelectedSlot);
        using var dialog = new NamePrompt(board.BluetoothName(SelectedSlot), profile.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        profile.Name = dialog.Value.Length == 0 ? null : dialog.Value;
        SaveSettings();
        ShowSlots(board);
    }

    private void SwitchSlot()
    {
        if (_link == null || _slotCombo.SelectedIndex < 0) return;

        // ボードは再起動し、新しいスロットでも切断状態で起動する
        CancelConnect();
        ClosePasskeyDialog();
        _link.SelectSlot(SelectedSlot);
        _slotSwitchDeadline = Environment.TickCount64 + SlotSwitchTimeoutMs;
        _targetConnected = false;
        _standby = true;
        _pairing = false;
        ShowTargetOptions();
        _messageLabel.Text = $"接続先 {SelectedSlot} に切り替えました";
        UpdateStatus();
    }

    // 通信スレッドからUIスレッドへ。閉じた後や、別のボードへ切り替えた後の通知は捨てる。
    private void Post(BoardLink source, Action action)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (ReferenceEquals(_link, source)) action();
            });
        }
        catch (InvalidOperationException)
        {
            // ウィンドウ破棄と競合した
        }
    }

    private void CloseLink()
    {
        StopControl(null);
        CancelConnect();
        ClosePasskeyDialog();
        _link?.Disconnect();
        _link?.Flush(300);
        _link?.Dispose();
        _link = null;
        _targetConnected = false;
        _standby = true;
        _pairing = false;
    }

    private void PollLink()
    {
        if (_link == null)
        {
            // ボードが後から挿された場合に自動で見つける
            if (!IsControlling && BoardLink.Enumerate().Count > 0) RefreshBoards();
            return;
        }

        if (_link.MillisecondsSinceLastReceive > LinkTimeoutMs)
        {
            OnLinkLost("ボードから応答がありません");
            return;
        }

        if (_connectPending && Environment.TickCount64 - _connectRequestedAt > ConnectTimeoutMs)
        {
            DisconnectTarget("接続先がつないできませんでした。接続先のBluetoothがオンか確認してください");
            return;
        }

        // ボードはこの定期的な問い合わせが途絶えると自分で切断する
        _link.RequestStatus();
    }

    private void OnTargetStatus(BoardStatus status)
    {
        if (status.Slot != 0 && status.Slot != SelectedSlot)
        {
            // 切替の直後は、再起動前の古いスロットの状態が届くので捨てる
            if (Environment.TickCount64 < _slotSwitchDeadline) return;

            // それ以外はボード側が正なので表示を合わせる(起動時や、他で切り替えられた場合)
            _updatingSlot = true;
            _slotCombo.SelectedIndex = status.Slot - 1;
            _updatingSlot = false;
            ShowTargetOptions();
        }

        bool justRequested = Environment.TickCount64 - _connectRequestedAt < ConnectGraceMs;
        if (justRequested && status.Standby) return;  // 接続指示より前の状態

        _targetConnected = status.Connected;
        _standby = status.Standby;
        _pairing = status.Pairing;
        if (!status.Connected) StopControl("接続先とのBluetooth接続が切れたため解除しました");
        if (_standby && _connectPending) CancelConnect();

        if (status.Connected && _connectPending)
        {
            _connectPending = false;
            if (_startWhenConnected)
            {
                UpdateStatus();
                StartControl();
                return;
            }
            _messageLabel.Text = "接続しました";
        }
        UpdateStatus();

        // 自己テスト: 起動したら接続して制御を開始する
        if (_selfTestLog != null && !_selfTestStarted && !_connectPending && !IsControlling)
        {
            if (status.Connected)
            {
                StartControl();
            }
            else if (!_pairing)
            {
                BeginConnect(startWhenConnected: true);
            }
        }
    }

    private void OnTargetDisconnected()
    {
        _targetConnected = false;
        StopControl("接続先とのBluetooth接続が切れたため解除しました");
        UpdateStatus();
    }

    // 本体ボタン、または通信途絶でボードが切断した。入力をWindows側へ戻す。
    private void OnBoardDisconnected(string message)
    {
        _targetConnected = false;
        _standby = true;
        _pairing = false;
        CancelConnect();
        ClosePasskeyDialog();
        StopControl(null);
        _messageLabel.Text = message;
        UpdateStatus();
    }

    private void OnStartButton()
    {
        if (IsControlling || _link == null) return;

        if (_connectPending)
        {
            DisconnectTarget("接続を中止しました");
        }
        else if (_targetConnected)
        {
            StartControl();
        }
        else
        {
            // 「毎回BT切断する」がオンなら、つながり次第そのまま制御を始める
            BeginConnect(startWhenConnected: SelectedProfile?.DisconnectAfterUse == true);
        }
    }

    private void BeginConnect(bool startWhenConnected)
    {
        if (_link == null) return;

        _link.Connect();
        _connectPending = true;
        _startWhenConnected = startWhenConnected;
        _connectRequestedAt = Environment.TickCount64;
        _standby = false;
        _messageLabel.Text = "接続先がつないでくるのを待っています";
        UpdateStatus();
    }

    private void CancelConnect()
    {
        _connectPending = false;
        _startWhenConnected = false;
    }

    private void DisconnectTarget(string? message)
    {
        if (_link == null) return;

        StopControl(null);
        CancelConnect();
        ClosePasskeyDialog();
        _link.Disconnect();
        _targetConnected = false;
        _standby = true;
        _pairing = false;
        if (message != null) _messageLabel.Text = message;
        UpdateStatus();
    }

    private void StartPairing()
    {
        if (_link == null || IsControlling || _boardCombo.SelectedItem is not BoardInfo board) return;

        // 接続中の接続先があれば切ってから受け付ける(同時に接続できるのは1台だけ)
        CancelConnect();
        if (_targetConnected) _link.Disconnect();
        _link.Pair();
        _targetConnected = false;
        _standby = false;
        _pairing = true;
        _connectRequestedAt = Environment.TickCount64;
        _messageLabel.Text = $"接続先のBluetooth設定で {board.BluetoothName(SelectedSlot)} を選んでください";
        UpdateStatus();
    }

    private void AskPasskey()
    {
        if (_link == null || _passkeyDialog != null || _boardCombo.SelectedItem is not BoardInfo board) return;

        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        using var dialog = new PasskeyPrompt(board.BluetoothName(SelectedSlot));
        _passkeyDialog = dialog;
        _passkeyDialogClosedByBoard = false;
        DialogResult result = dialog.ShowDialog(this);
        _passkeyDialog = null;
        if (_passkeyDialogClosedByBoard || _link == null) return;

        if (result == DialogResult.OK)
        {
            _link.SendPasskey(dialog.Value);
            _messageLabel.Text = "パスキーを送りました";
        }
        else
        {
            DisconnectTarget("ペアリングを中止しました");
        }
    }

    private void ClosePasskeyDialog()
    {
        if (_passkeyDialog == null) return;
        _passkeyDialogClosedByBoard = true;
        _passkeyDialog.Close();
    }

    private void OnPaired()
    {
        ClosePasskeyDialog();
        _pairing = false;
        _messageLabel.Text = "ペアリングしました";
        UpdateStatus();
    }

    private void OnPairingEnded()
    {
        ClosePasskeyDialog();
        _pairing = false;
        if (!_targetConnected) _messageLabel.Text = "ペアリングの受付を終了しました";
        UpdateStatus();
    }

    private void OnPairingFailed()
    {
        ClosePasskeyDialog();
        if (_pairing) _messageLabel.Text = "ペアリングに失敗しました。数字を確かめて、接続先からやり直してください";
        UpdateStatus();
    }

    private void OnLinkLost(string message)
    {
        CloseLink();
        _messageLabel.Text = message;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_link == null)
        {
            _statusLabel.Text = _boardCombo.Items.Count == 0 ? "ボードが見つかりません" : "ボードに接続していません";
        }
        else
        {
            _statusLabel.Text = _pairing ? "ボード: 接続済み / 接続先: ペアリング受付中"
                : _targetConnected ? "ボード: 接続済み / 接続先: Bluetooth接続中"
                : _connectPending || !_standby ? "ボード: 接続済み / 接続先: 接続待ち"
                : "ボード: 接続済み / 接続先: 切断中";
        }

        bool idle = !IsControlling && _link != null;
        if (!IsControlling)
        {
            bool autoStart = SelectedProfile?.DisconnectAfterUse == true;
            _startButton.Text = _connectPending ? "接続中… (押すと中止)"
                : _targetConnected || autoStart ? "開始"
                : "接続";
            _startButton.Enabled = idle && !_pairing;
        }

        var board = _boardCombo.SelectedItem as BoardInfo;
        _keyMapButton.Enabled = !IsControlling && board != null;
        _slotCombo.Enabled = idle;
        _renameButton.Enabled = !IsControlling && board != null;
        _disconnectAfterUse.Enabled = !IsControlling && board != null;
        _pairButton.Enabled = idle && !_pairing;
        _disconnectButton.Enabled = idle && (_targetConnected || _connectPending || _pairing || !_standby);
        int count = board == null ? 0 : _settings.GetProfile(board.SerialNumber, SelectedSlot).KeyMap.Count;
        _keyMapLabel.Text = board == null ? "" : count == 0 ? "定義なし (そのまま転送)" : $"{count} 件の定義";
    }

    private void EditKeyMap()
    {
        if (_boardCombo.SelectedItem is not BoardInfo board) return;

        TargetProfile profile = _settings.GetProfile(board.SerialNumber, SelectedSlot);
        string target = string.IsNullOrEmpty(profile.Name) ? board.BluetoothName(SelectedSlot) : profile.Name;
        using var dialog = new KeyMapForm(target, profile.KeyMap);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        profile.KeyMap = dialog.Entries;
        SaveSettings();
        UpdateStatus();
    }

    private void StartControl()
    {
        if (IsControlling || _link == null || !_targetConnected) return;

        int releaseKey = AppSettings.MinReleaseKey + _releaseKeyCombo.SelectedIndex;
        uint releaseVk = NativeMethods.VK_F1 + (uint)(releaseKey - 1);
        int scrollScale = _invertScroll.Checked ? -(int)_scrollScale.Value : (int)_scrollScale.Value;
        byte[] keyMap = KeyMapping.BuildTable(_settings.GetProfile(_link.Board.SerialNumber, SelectedSlot).KeyMap);
        var capture = new InputCapture(_link, Handle, releaseVk, scrollScale, (int)_pointerSpeed.Value, keyMap, RequestStop,
            acceptInjected: _selfTestLog != null);
        try
        {
            capture.Start();
        }
        catch (Exception ex)
        {
            _messageLabel.Text = ex.Message;
            return;
        }
        _capture = capture;

        SaveInputSettings();

        _startButton.Text = "制御中";
        _startButton.Enabled = false;
        _boardCombo.Enabled = false;
        _refreshButton.Enabled = false;
        _releaseKeyCombo.Enabled = false;
        _scrollScale.Enabled = false;
        _pointerSpeed.Enabled = false;
        _invertScroll.Enabled = false;
        _keyMapButton.Enabled = false;
        _slotCombo.Enabled = false;
        _renameButton.Enabled = false;
        _disconnectAfterUse.Enabled = false;
        _pairButton.Enabled = false;
        _disconnectButton.Enabled = false;
        _messageLabel.Text = $"F{releaseKey} で解除します";
        TopMost = true;

        Point center = _startButton.PointToScreen(new Point(_startButton.Width / 2, _startButton.Height / 2));
        var clip = new RECT { left = center.X, top = center.Y, right = center.X + 1, bottom = center.Y + 1 };
        NativeMethods.SetCursorPos(center.X, center.Y);
        NativeMethods.ClipCursor(ref clip);
        NativeMethods.timeBeginPeriod(1);
        SelfTestLog("start");

        if (_selfTestLog != null && !_selfTestStarted)
        {
            _selfTestStarted = true;
            var timeout = new System.Windows.Forms.Timer { Interval = SelfTestTimeoutMs };
            timeout.Tick += (_, _) =>
            {
                timeout.Dispose();
                StopControl("selftest timeout");
            };
            timeout.Start();
        }
    }

    // フックの中から呼ばれるので、フック解除はメッセージループへ戻ってから行う
    private void RequestStop(string reason)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => StopControl(reason));
    }

    /// <summary>制御中でなければ何もしない。</summary>
    private void StopControl(string? reason)
    {
        if (_capture == null) return;

        _capture.Stop();
        _capture = null;
        _link?.ReleaseAll();

        NativeMethods.ClipCursor(IntPtr.Zero);
        NativeMethods.timeEndPeriod(1);
        TopMost = false;

        // 「毎回BT切断する」: 制御の終わり方によらず切断する
        if (_link != null && _targetConnected && SelectedProfile?.DisconnectAfterUse == true)
        {
            _link.Flush(200);  // 全キーを離したレポートを先に届ける
            _link.Disconnect();
            _targetConnected = false;
            _standby = true;
        }

        _startButton.Text = "開始";
        _boardCombo.Enabled = true;
        _refreshButton.Enabled = true;
        _releaseKeyCombo.Enabled = true;
        _scrollScale.Enabled = true;
        _pointerSpeed.Enabled = true;
        _invertScroll.Enabled = true;
        if (reason != null) _messageLabel.Text = reason;
        UpdateStatus();

        if (_selfTestLog != null)
        {
            SelfTestLog($"stop: {reason}");
            BeginInvoke(Close);
        }
    }

    private void SelfTestLog(string line)
    {
        if (_selfTestLog != null) File.AppendAllText(_selfTestLog, line + Environment.NewLine);
    }

    private void SaveInputSettings()
    {
        _settings.ReleaseKey = AppSettings.MinReleaseKey + _releaseKeyCombo.SelectedIndex;
        _settings.ScrollScale = (int)_scrollScale.Value;
        _settings.InvertScroll = _invertScroll.Checked;
        _settings.PointerSpeed = (int)_pointerSpeed.Value;
        SaveSettings();
    }

    private void SaveSettings()
    {
        string? error = _settings.Save();
        if (error != null) _messageLabel.Text = $"設定を保存できませんでした: {error}";
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // ロック画面などではフックが届かず、離上を取りこぼすので解除する
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => StopControl("セッションが切り替わったため解除しました"));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT) _capture?.HandleRawInput(m.LParam);
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statusTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        CloseLink();  // 接続先とのBluetooth接続も切る

        SaveInputSettings();
        base.OnFormClosing(e);
    }
}
