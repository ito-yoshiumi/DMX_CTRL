# DMX_CTRL

Unityを使用したArt-Net/DMX制御システム。KineticLightなどのDMX対応ライトを制御し、音声入力やシナリオに応じてリアルタイムにライトを操作できます。

## 概要

このプロジェクトは、UnityエンジンからArt-Netプロトコル経由でDMXデバイス（特にKineticLight）を制御するシステムです。複数NIC環境に対応し、音声入力やシナリオに基づいてライトの色、明るさ、高さなどをリアルタイムに制御できます。

## 主な機能

### Art-Net送信機能
- **ArtNetClient.cs**: Art-Net v4プロトコルによるDMXデータ送信
  - 複数NIC環境での自動IP選択
  - ユニキャスト/ブロードキャスト送信対応
  - ローカルIPの明示的指定（`bindLocalIp`）
  - Universe/SubUniverse設定（Net: 0-127, Subnet: 0-15, Universe: 0-15）

### KineticLight制御
- **KineticLightController.cs**: KineticLight専用制御クラス
  - RGB色制御
  - 高さ制御（0-100）
  - 複数フィクスチャの一括制御
  - `ArtNetClient`と連携してDMX送信

### シナリオ実行機能
- **ScenarioRunner.cs**: JSONシナリオファイルの読み込みと実行
  - WAVファイルの再生
  - TTS（Text-to-Speech）対応（VOICEVOX/COEIROINK）
  - DMXキューとの同期
  - 録音機能の統合

- **CuePlayer.cs**: DMXキュー（キーフレーム）の再生
  - JSONキーフレームファイルの読み込み
  - 線形補間によるスムーズなアニメーション
  - `KineticLightController`への制御値適用

### 音声入力マッピング
- **AudioInputManager.cs**: マイク入力の管理
  - RMS（音量）検出
  - ピッチ（周波数）推定（ACF方式）
  - イベントベースの通知

- **PitchToHeightMapper.cs**: ピッチ→高さマッピング
  - 周波数（Hz）をDMX値（0-100）に変換
  - スムージング処理
  - 速度・加速度リミット
  - 範囲外ピッチの無視

- **VolumeToColorMapper.cs**: 音量→色マッピング
  - RMS値をHSV色空間に変換
  - 小音量=青、大音量=赤のグラデーション

### 安全機能
- **SafetyManager.cs**: 非常停止（E-Stop）機能
  - DMX出力の即座にゼロ化
  - 緊急時の安全確保

## システム要件

- Unity 6000.2.12f1 (Unity 6 LTS)
- Art-Net対応のDMXデバイス（KineticLight推奨）
- ネットワーク接続（有線/無線LAN）
- マイク（音声入力機能を使用する場合）

## 使用方法

### 基本的なセットアップ

1. **ArtNetClient**をシーンに追加
   - 送信先IPアドレスを設定（例: 192.168.1.200）
   - Universe設定（Net: 0, Subnet: 1, Universe: 0）
   - ローカルIPのバインド設定（`bindLocalIp`、`autoSelectLocalIp`）

2. **KineticLightController**を追加
   - `ArtNetClient`への参照を設定
   - 各フィクスチャの開始アドレスとチャンネル構成を設定
   - デフォルト: 開始アドレス 17, 33, 49, 65, 81（各6チャンネル）

3. **シナリオ実行**（オプション）
   - `ScenarioRunner`を追加
   - `CuePlayer`への参照を設定
   - `AudioSource`を設定
   - シナリオJSONファイルを`Resources/Scenario/`に配置

4. **音声入力連携**（オプション）
   - `AudioInputManager`を追加
   - `PitchToHeightMapper`と`VolumeToColorMapper`を設定
   - マイクデバイス名を指定（空欄で自動選択）

## DMXチャンネルマッピング（KineticLight）

各フィクスチャは6チャンネルを使用：
- Ch1: Red (0-255)
- Ch2: Green (0-255)
- Ch3: Blue (0-255)
- Ch4: Dimmer (0-255)
- Ch5: Strobe (0-255)
- Ch6: Height (0-100)

**現在の設定:**
- 開始アドレス: 17, 33, 49, 65, 81
- Universe: Net=0, Subnet=1, Universe=0
- 使用チャンネル範囲: Ch17〜Ch86

## ネットワーク設定

### 複数NIC環境での動作

このシステムは複数のネットワークインターフェースがある環境でも動作します：

1. **明示的IP指定**: `bindLocalIp`に送信元IPを直接指定（例: "192.168.1.199"）
2. **インターフェース名指定**: `bindInterfaceName`にNIC名を指定（例: "USB", "Ethernet"）
3. **自動選択**: `autoSelectLocalIp`を有効化すると、宛先IPと同一サブネットのNICを自動検出

### トラブルシューティング

- "No route to host"エラーが発生する場合
  - `bindLocalIp`を明示的に設定
  - `autoSelectLocalIp`を有効化
  - `logSelection`を有効化してログを確認

## プロジェクト構成

```
Assets/
├── Scripts/
│   ├── DMX/
│   │   ├── ArtNetClient.cs              # Art-Net送信コア
│   │   └── KineticLightController.cs    # KineticLight制御
│   ├── Scenario/
│   │   ├── ScenarioRunner.cs            # シナリオ実行
│   │   ├── CuePlayer.cs                 # DMXキュー再生
│   │   └── ScenarioTypes.cs             # データ型定義
│   ├── Audio/
│   │   ├── AudioInputManager.cs         # マイク入力管理
│   │   ├── PitchEstimator.cs            # ピッチ推定
│   │   └── RMSMeter.cs                  # RMS計算
│   ├── Mapping/
│   │   ├── PitchToHeightMapper.cs       # ピッチ→高さマッピング
│   │   └── VolumeToColorMapper.cs       # 音量→色マッピング
│   ├── Safety/
│   │   └── SafetyManager.cs             # 非常停止
│   └── Testing/
│       ├── PitchStaffVisualizer.cs      # テスト用可視化
│       ├── TestScenarioController.cs    # テスト用コントローラー
│       └── DMXDebugLogger.cs            # DMXデバッグログ
├── Resources/
│   ├── Scenario/
│   │   └── scenario_ja.json             # シナリオファイル
│   └── Cues/
│       ├── intro_light.json             # イントロ用キュー
│       └── 5note_light.json             # 5音用キュー
└── Scenes/
    └── SampleScene.unity                 # メインシーン

docs/
├── README.md                             # プロジェクト設計書
├── TESTING.md                            # テスト手順
└── Tasks/
    └── todo.md                           # 実装タスク一覧
```

## テスト状況

現在、テスト1〜4まで完了しています：

- ✅ **テスト1**: マイク入力の可視化
- ✅ **テスト2**: 5つのSquareの五線譜動作
- ✅ **テスト3**: シナリオ実行
- ✅ **テスト4**: CuePlayerの動作

詳細は`docs/TESTING.md`を参照してください。

## 実装状況

### P0（MVP）✅ 完了
- [x] ArtNetClient: UDP で Art-Net パケットを送出
- [x] KineticLightController: 5台分の高さ/色を DMX に反映
- [x] CuePlayer: Cues/*.json を読み、線形補間でキーフレーム適用
- [x] ScenarioRunner: scenario_ja.json を読み、WAV 再生＋Cue 起動
- [x] AudioInputManager: RMS と簡易ピッチをイベントで発火
- [x] PitchToHeightMapper/VolumeToColorMapper: マッピング＆スムージング
- [x] SafetyManager: 非常停止（出力ゼロ/Apply停止）

### P1（展示向け）
- [ ] VOICEVOX/COEIROINK TTS: 起動時キャッシュ→AudioClip 化
- [ ] 参加者5音の録音＆ミックス再生
- [ ] OperatorPanel: 進行/スキップ/巻き戻し/音量/UI
- [ ] ログ保存（JSON/CSV）

### P2（拡張）
- [ ] YIN ピッチ推定の最適化（低レイテンシ）
- [ ] マルチ言語台本（scenario_en.json 他）
- [ ] 安全フェンス/人感のセンサー連携

## ライセンス

このプロジェクトのライセンス情報は記載されていません。

## 注意事項

- Art-NetはUDPプロトコルを使用するため、ファイアウォール設定を確認してください
- DMXチャンネル番号は1ベース（1-512）です
- 高さ制御は0-100の範囲に制限されます（DMX値0=最上段、100=最下段/床）
- マイク入力はリアルタイム処理のため、低レイテンシのデバイスを推奨します
- ピッチ検出範囲は80-350Hz（大人の男性〜子供の声域）に設定されています
