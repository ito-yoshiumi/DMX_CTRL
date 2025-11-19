# DMX_CTRL

Unityを使用したArt-Net/DMX制御システム。KineticLightなどのDMX対応ライトを制御し、音声入力やシナリオに応じてリアルタイムにライトを操作できます。

## プロジェクトの概要

### 体験のコンセプト

**「Encounter with the unknown（未知との遭遇）」**をテーマとしたインタラクティブな音と光の体験システムです。

- **体験**: スピーカー（合成音声/事前WAV）⇄ マイク入力（声/テルミン/オタマトーン）⇄ **DMXキネティックライト（ホイスト）**が「音で会話」する
- **テーマ**: 未知との共生・調和（『未知との遭遇』の5音と『かえるのうた』輪唱がモチーフ）
- **対象**: 小学生＋家族
- **装置前提**: DMX ホイスト（値255=約7m、100=約3m）。RGB で色変化

### システム概要

このプロジェクトは、UnityエンジンからArt-Netプロトコル経由でDMXデバイス（特にKineticLight）を制御するシステムです。複数NIC環境に対応し、音声入力やシナリオに基づいてライトの色、明るさ、高さなどをリアルタイムに制御できます。

## シナリオ

### ループ1（来客前）
1. 合成音声「わたしはみちのせいめいたい」＋ライト点灯
2. 5音シグナルを再生（ライトが同期動作）
3. 「あなたのあいさつもきかせて。さあマイクのまえにたって」

### ループ2（来客後）
1. オペレータが Enter → 「『わー』と言ってみて」
2. **音量/音程**に反応して 5台が上下/色変化
3. 「ドレミファソ」と歌ってみて → **低→高**の上下演出
4. 5音シグナルを**真似して1音ずつ録音×5**
5. **お手本＋参加者の声**を同時再生、ライトが共演
6. しめの一言 → 次の来場者待ち

> シナリオファイル: `docs/Scenario/scenario_ja.json` と `docs/Cues/*.json` に最小のサンプルを同梱

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
  - ディマー/ストロボの自動固定設定（常時点灯を保証）
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

### 音声の鳴らし方（実装ポリシー）

#### ハイブリッド方式（推奨）
- **台本が固定**でタイミング命のセリフ / 5音：**事前WAV**
- **誘導・多言語・名前呼び**：**ランタイムTTS**（起動時に合成→WAVキャッシュ）

#### TTS 選択肢
- **VOICEVOX**（Mac, 無料, 歌唱可）…歌や日本語トークに最適。ローカル HTTP API
- **COEIROINK**（Mac, 無料, トーク特化）…誘導セリフをローカル合成
- （必要があれば Azure/ELEVEN などのクラウド TTS も併用。展示では回線リスク回避のため**起動時キャッシュ**が必須）

#### 再生/同期
- 再生は **AudioSource**（Timeline/Playable でキュー）
- **ScenarioRunner** が JSON を読み、`type:"wav"` は事前配置、`type:"tts"` はキャッシュから取得
- 音声再生開始で **CuePlayer** が DMX キュー（高さ/色のキーフレーム）を走らせる

### 音→ライトのマッピング

#### ピッチ(Hz)→高さ(DMX 0-100)
1. `midi = 69 + 12 * log2(f/440)`
2. `t = saturate( (midi - midiMin) / (midiMax - midiMin) )`  *(初期: C4..C6)*
3. `dmxHeight = round( lerp(0, 100, t) )`  *(0=最上段、100=最下段/床)*
4. **スムージング**（0.2–0.4s）と **速度/加速度リミット**を必ず適用

#### 音量(RMS 0..1)→色(RGB)
- `hue = lerp(0.6, 0.0, rms)`（小=青→大=赤）
- HSV→RGB 変換 → DMX の R/G/B チャンネルへ

### 安全機能
- **SafetyManager.cs**: 非常停止（E-Stop）機能
  - DMX出力の即座にゼロ化
  - 緊急時の安全確保
- **可動域クランプ**: DMX 0-100（物理LIMとも整合）
- **速度/加速度上限**: 安全な動作を保証
- **通信冗長**: ネット不調時は TTS キャッシュ＋事前WAVのみで完結

### テスト・可視化機能
- **PitchStaffVisualizer.cs**: 5つのSquareを五線譜のように配置し、音程に応じて可視化
  - ピッチに応じた高さをDMX機器に送信
  - 音量に応じた色をDMX機器に送信
  - リアルタイムでの動作確認が可能

## システム要件

- Unity 6000.2.12f1 (Unity 6 LTS)
- Art-Net対応のDMXデバイス（KineticLight推奨）
- ネットワーク接続（有線/無線LAN）
- マイク（音声入力機能を使用する場合）
- VOICEVOX/COEIROINK（TTS機能を使用する場合）

## 使用方法

### 基本的なセットアップ

1. **ArtNetClient**をシーンに追加
   - 送信先IPアドレスを設定（例: 192.168.1.200）
   - Universe設定（Net: 0, Subnet: 0, Universe: 1）
   - ローカルIPのバインド設定（`bindLocalIp`、`autoSelectLocalIp`）

2. **KineticLightController**を追加
   - `ArtNetClient`への参照を設定
   - 各フィクスチャの開始アドレスとチャンネル構成を設定
   - デフォルト: 開始アドレス 17, 33, 49, 65, 81（各6チャンネル）
   - ディマー/ストロボの自動固定設定（`forceDimmerValue`、`forceStrobeValue`）

3. **シナリオ実行**（オプション）
   - `ScenarioRunner`を追加
   - `CuePlayer`への参照を設定
   - `AudioSource`を設定
   - シナリオJSONファイルを`Resources/Scenario/`に配置

4. **音声入力連携**（オプション）
   - `AudioInputManager`を追加
   - `PitchToHeightMapper`と`VolumeToColorMapper`を設定
   - マイクデバイス名を指定（空欄で自動選択）

5. **テスト・可視化**（オプション）
   - `PitchStaffVisualizer`を追加
   - `KineticLightController`への参照を設定（自動検出も可能）
   - `sendHeightToDmx`と`sendColorToDmx`を有効化

## DMXチャンネルマッピング（KineticLight）

各フィクスチャは6チャンネルを使用：
- Ch1: Red (0-255)
- Ch2: Green (0-255)
- Ch3: Blue (0-255)
- Ch4: Dimmer (0-255) - 通常は255（最大輝度）に固定
- Ch5: Strobe (0-255) - 通常は0（無効）に固定
- Ch6: Height (0-100) - 0=最上段、100=最下段/床

**現在の設定:**
- 開始アドレス: 17, 33, 49, 65, 81
- Universe: Net=0, Subnet=0, Universe=1
- 使用チャンネル範囲: Ch17〜Ch86

**注意**: `KineticLightController`は、高さや色を設定する際に自動的にディマーとストロボの値を固定します（`forceDimmerValue`、`forceStrobeValue`）。これにより、ライトが常に点灯状態を保ちます。

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
│   │   ├── KineticLightController.cs    # KineticLight制御
│   │   └── DmxFixture.cs                # DMXフィクスチャ定義
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
├── TESTING.md                            # テスト手順
├── VOICEVOX_SETUP.md                     # VOICEVOX設定手順
├── Scenario/
│   └── scenario_ja.json                  # シナリオサンプル
├── Cues/
│   ├── intro_light.json                  # イントロ用キューサンプル
│   └── 5note_light.json                  # 5音用キューサンプル
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
- [x] KineticLightController: ディマー/ストロボの自動固定設定（常時点灯を保証）
- [x] CuePlayer: Cues/*.json を読み、線形補間でキーフレーム適用
- [x] ScenarioRunner: scenario_ja.json を読み、WAV 再生＋Cue 起動
- [x] AudioInputManager: RMS と簡易ピッチをイベントで発火
- [x] PitchToHeightMapper/VolumeToColorMapper: マッピング＆スムージング
- [x] PitchStaffVisualizer: DMX出力機能（ピッチ→高さ、音量→色）
- [x] SafetyManager: 非常停止（出力ゼロ/Apply停止）

### P1（展示向け）
- [x] VOICEVOX/COEIROINK TTS: 起動時キャッシュ→AudioClip 化
- [ ] 参加者5音の録音＆ミックス再生
- [ ] OperatorPanel: 進行/スキップ/巻き戻し/音量/UI
- [ ] ログ保存（JSON/CSV）

### P2（拡張）
- [ ] YIN ピッチ推定の最適化（低レイテンシ）
- [ ] マルチ言語台本（scenario_en.json 他）
- [ ] 安全フェンス/人感のセンサー連携
- [ ] VOICEVOX 歌唱パートで**輪唱**を段階入替（カノン）
- [ ] ガイダンスの多言語切替（日本語/英語/中国語 など）
- [ ] セッションの保存（参加者5音の波形を残し、来場記録に）

## 注意事項

- Art-NetはUDPプロトコルを使用するため、ファイアウォール設定を確認してください
- DMXチャンネル番号は1ベース（1-512）です
- 高さ制御は0-100の範囲に制限されます（DMX値0=最上段、100=最下段/床）
- マイク入力はリアルタイム処理のため、低レイテンシのデバイスを推奨します
- ピッチ検出範囲は80-350Hz（大人の男性〜子供の声域）に設定されています
- ディマーとストロボの値は、`KineticLightController`の設定により自動的に固定されます（デフォルト: ディマー=255、ストロボ=0）

## ライセンス

このプロジェクトのライセンス情報は記載されていません。

## 関連ドキュメント

- [テスト手順](docs/TESTING.md)
- [VOICEVOX設定手順](docs/VOICEVOX_SETUP.md)
- [実装タスク一覧](docs/Tasks/todo.md)
