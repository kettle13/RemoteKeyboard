# RemoteKeyboard

メインのWindows PCのキーボードとマウスで、別の端末（Mac / Windows / iPad / iPhone など）を一時的に操作するためのWindowsアプリ「Remote Input」です。

PCと操作先の端末をネットワークでつなぐ必要はありません。PCにUSBで挿した M5Stack AtomS3 Lite が、操作先からは普通のBluetoothキーボード兼マウスとして見えます。操作先に専用ソフトを入れる必要もありません。

![Remote Input のしくみ: Windows PCのアプリが取り込んだキーボードとマウスの入力を、USB接続した M5Stack AtomS3 Lite がBluetoothのキーボードとマウスとして操作先の端末へ送る](https://raw.githubusercontent.com/kettle13/RemoteKeyboard/assets/overview.png)

## 必要なもの

- Windows 10 / 11（64ビット）
- [.NET 10 デスクトップ ランタイム](https://dotnet.microsoft.com/download/dotnet/10.0)
- 対応ファームウェアを書き込んだ M5Stack AtomS3 Lite

ファームウェアはこのリポジトリには含まれておらず、現在は公開していません。

## インストール

[Releases](https://github.com/kettle13/RemoteKeyboard/releases) からzipをダウンロードし、任意のフォルダに展開して `RemoteInput.exe` を起動します。インストーラーはありません。

## 使い方

1. AtomS3 Lite をPCにUSBで接続し、アプリを起動します。
2. 操作先の端末のBluetooth設定で `RemoteInput_xxxx_1` を選んでペアリングします。`xxxx` はボードごとに異なる4桁のIDです。
3. 状態欄が「接続先: Bluetooth接続中」になったら「開始」を押します。
4. キーボードとマウスの入力が操作先へ送られます。この間、入力はWindows側には届きません。
5. 解除キー（初期値は F12）を押すと、Windowsの操作に戻ります。

### 制御から抜ける方法

- 解除キー（F8～F12 から選択、初期値 F12）
- AtomS3 Lite 本体のボタン
- Ctrl + Alt + Del
- AtomS3 Lite のUSBケーブルを抜く

ボードとの通信が途切れた場合、操作先とのBluetooth接続が切れた場合、画面をロックした場合も自動で解除されます。

### 接続先の切り替え

1台のボードに最大8台の端末を登録できます。ボードは接続先の番号（1～8）ごとに別のBluetoothデバイス（`RemoteInput_xxxx_1` ～ `_8`）として見えるので、端末ごとに違う番号へペアリングし、アプリの「接続先」で切り替えます。「名称」ボタンで、それぞれに分かりやすい名前を付けられます。

本体のボタンを押すと、どの端末とも接続していない状態になります。アプリの「接続」ボタンで再び接続します。

### 設定

| 設定 | 内容 |
|---|---|
| 解除キー | 制御を解除するキー（F8～F12） |
| ポインタ速度 | マウスの移動量の倍率（50～400%） |
| スクロール倍率 | ホイール1目盛りあたりのスクロール量（1～20） |
| スクロールを逆にする | ホイールの向きを反転 |
| キーマッピング | キーの入れ替え・変換・無効化。接続先ごとに保存 |

キーマッピングには、Windows から macOS / iPadOS を操作するためのプリセット（Ctrl ⇔ Command、Alt ⇔ Win、変換・無変換 → かな・英数）があります。

設定は `%APPDATA%\RemoteInput\settings.json` に保存されます。ペアリングの情報はボード側に保存されます。

## 制限事項

- キーは物理的な位置のまま送ります。PCと操作先でキーボード配列の設定（日本語 / 英語）が異なると、一部の記号が一致しません。
- 管理者権限で動いているウィンドウなど、Windowsの保護された画面では入力を横取りできません。
- 複数の端末へ同時に入力を送ることはできません。

## ビルド

.NET 10 SDK が必要です。

```
dotnet build app/RemoteInput.csproj -c Release -p:Platform=x64
```

`v` で始まるタグ（例: `v0.1.0`）をプッシュすると、GitHub Actions がビルドしてReleaseを作成します。

## ライセンス

[MIT License](LICENSE)

配布物に含まれる第三者のコンポーネントについては [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
