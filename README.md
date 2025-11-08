# DMX_CTRL

Unityを使用したArt-Net/DMX制御システム。KineticLightなどのDMX対応ライトを制御し、音声入力やオブジェクトの位置に応じてリアルタイムにライトを操作できます。

## 概要

このプロジェクトは、UnityエンジンからArt-Netプロトコル経由でDMXデバイス（特にKineticLight）を制御するシステムです。複数NIC環境に対応し、音声入力や3Dオブジェクトの位置に基づいてライトの色、明るさ、高さなどをリアルタイムに制御できます。

## 主な機能

### Art-Net送信機能
- **ArtNetSender.cs**: Art-Net v4プロトコルによるDMXデータ送信
  - 複数NIC環境での自動IP選択
  - ユニキャスト/ブロードキャスト送信対応
  - 自動送信モード（30-44fps推奨）と手動送信モード
  - Universe/SubUniverse設定（Net: 0-127, SubUni: 0-255）

### KineticLight制御
- **KineticLightController.cs**: KineticLight専用制御クラス
  - RGB色制御
  - ディマー（明るさ）制御
  - ストロボ制御
  - 高さ制御（0-100）
  - 複数フィクスチャの一括制御

- **ArtNetKineticController.cs**: Art-Net送信とKineticLight制御を統合
  - UnityオブジェクトのY座標に応じた高さ制御
  - Y=-5 → LightHeight=100, Y=5 → LightHeight=0 のマッピング

### 入力連携
- **MicVolumeMover.cs** (Voice_CTRL.cs): マイク音量に応じたオブジェクト移動
  - マイク入力のRMS（音量）検出
  - 音量に応じたY軸移動
  - スムーズな補間処理

- **SmoothHeightFollow2D.cs** (Square_move.cs): 2Dオブジェクトの高さ追従
  - ターゲットオブジェクトのY座標をスムーズに追従
  - イージング機能付き

## システム要件

- Unity 6000.2.12f1 (Unity 6 LTS)
- Art-Net対応のDMXデバイス（KineticLight推奨）
- ネットワーク接続（有線/無線LAN）

## 使用方法

### 基本的なセットアップ

1. **ArtNetSender**をシーンに追加
   - 送信先IPアドレスを設定（例: 192.168.1.200）
   - Universe/SubUniverseを設定
   - 自動送信を有効化

2. **KineticLightController**を追加
   - ArtNetSenderへの参照を設定（自動検出も可能）
   - 各フィクスチャの開始チャンネルを設定（デフォルト: 1, 17, 33, 49, 65, 81）

3. **音声連携**（オプション）
   - MicVolumeMoverをオブジェクトに追加
   - マイクデバイス名を指定（空欄で自動選択）
   - 感度と移動範囲を調整

4. **高さ追従**（オプション）
   - ArtNetKineticControllerのheightTargetに追従対象を設定
   - Y座標の範囲（minY, maxY）を調整

## DMXチャンネルマッピング（KineticLight）

各フィクスチャは6チャンネルを使用：
- Ch1: Red (0-255)
- Ch2: Green (0-255)
- Ch3: Blue (0-255)
- Ch4: Dimmer (0-255)
- Ch5: Strobe (0-255)
- Ch6: Height (0-100)

## ネットワーク設定

### 複数NIC環境での動作

このシステムは複数のネットワークインターフェースがある環境でも動作します：

1. **明示的IP指定**: `bindLocalIp`に送信元IPを直接指定
2. **インターフェース名指定**: `bindInterfaceName`にNIC名を指定（例: "USB", "Ethernet"）
3. **自動選択**: 宛先IPと同一サブネットのNICを自動検出

### トラブルシューティング

- "No route to host"エラーが発生する場合
  - `bindLocalIp`を明示的に設定
  - `autoSelectLocalIp`を有効化
  - `logSelection`を有効化してログを確認

## プロジェクト構成

```
Assets/Scenes/
├── ArtNetSender.cs              # Art-Net送信コア
├── ArtNetKineticController.cs   # Art-Net + KineticLight制御統合版
├── KineticLightController.cs    # KineticLight制御（ArtNetSender使用）
├── Voice_CTRL.cs                # マイク音量検出（MicVolumeMover）
└── Square_move.cs               # 高さ追従（SmoothHeightFollow2D）
```

## ライセンス

このプロジェクトのライセンス情報は記載されていません。

## 注意事項

- Art-NetはUDPプロトコルを使用するため、ファイアウォール設定を確認してください
- DMXチャンネル番号は1ベース（1-512）です
- 高さ制御は0-100の範囲に制限されます
- マイク入力はリアルタイム処理のため、低レイテンシのデバイスを推奨します
