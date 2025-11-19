# VOICEVOX/COEIROINK セットアップガイド

## 概要

このプロジェクトでは、VOICEVOXまたはCOEIROINKのTTSエンジンを使用して、シナリオ内のテキストを音声に変換します。Mac/Windowsの両方で動作します。

## インストール手順

### Windows

1. [VOICEVOX公式サイト](https://voicevox.hiroshiba.jp/)からWindows版をダウンロード
2. インストーラーを実行
3. 初回起動時に「WindowsによってPCが保護されました」と表示された場合は：
   - 「詳細情報」をクリック
   - 発行元が「Kazuyuki Hiroshiba」であることを確認
   - 「実行」を選択

### Mac

1. [VOICEVOX公式サイト](https://voicevox.hiroshiba.jp/)からMac版をダウンロード
2. ダウンロードしたファイルを開き、アプリケーションフォルダにドラッグ&ドロップ
3. 初回起動時は以下の手順で起動：
   - FinderでVOICEVOXアプリケーションを`Ctrl`キーを押しながらクリック
   - ショートカットメニューから「開く」を選択
   - 表示されるダイアログで「開く」をクリック
4. Apple Silicon搭載のMacの場合、初回起動時にRosettaのインストールを求められることがあります（案内に従ってインストール）

### COEIROINKを使用する場合

COEIROINKも同様の手順でインストールできます。公式サイトからダウンロードしてください。

## Unity側の設定

1. **TTSServiceコンポーネントを追加**
   - シーン内の適切なGameObjectに`TTSService`コンポーネントを追加

2. **設定項目**
   - **Engine Base URL**: `http://127.0.0.1:50021`（デフォルト）
     - エンジンが別のポートで起動している場合は変更
   - **Speaker ID**: 使用する話者ID
     - VOICEVOX: 0-3（デフォルト話者）
     - COEIROINK: 0-7（デフォルト話者）
     - 利用可能な話者IDはエンジンの`/speakers`エンドポイントで確認可能
   - **Connection Timeout**: 接続タイムアウト（秒、デフォルト: 5秒）
   - **Enable Debug Log**: デバッグログの表示（デフォルト: true）

3. **ScenarioRunnerとの連携**
   - `ScenarioRunner`コンポーネントの`ttsService`フィールドに`TTSService`を割り当て

4. **話者一覧の確認と適用（Unityエディタ）**
   - `TTSService`コンポーネントのInspectorにある「話者一覧を取得」をクリック
   - VOICEVOX/COEIROINKから利用可能な話者一覧が表示される
   - 任意のスタイル行の「適用」を押すと`Speaker ID`に反映される
   - ※エンジン未起動時は取得に失敗するため、事前に起動しておく

## 動作確認

1. **VOICEVOX/COEIROINKを起動**
   - アプリケーションを起動
   - エンジンが自動的に起動します（GUIアプリの場合）

2. **Unityエディタで再生**
   - UnityエディタでPlayボタンを押す
   - Consoleに「[TTSService] TTSエンジンに接続成功」と表示されればOK

3. **事前合成の確認**
   - `ScenarioRunner`の`Start()`が実行されると、シナリオ内の`type:"tts"`エントリが自動的に事前合成されます
   - Consoleに「[TTSService] 事前合成完了: X件のクリップをキャッシュ」と表示されます

## トラブルシューティング

### 接続エラーが発生する場合

- **エンジンが起動しているか確認**
  - VOICEVOX/COEIROINKのアプリケーションが起動しているか確認
  - エンジンが別のポートで起動している場合は、`Engine Base URL`を変更

- **ファイアウォール設定を確認**
  - Macの場合、システム環境設定のセキュリティとプライバシーでファイアウォールが有効になっている場合は、Unityへのアクセスを許可

- **URLの確認**
  - ブラウザで`http://127.0.0.1:50021/speakers`にアクセスして、エンジンが応答するか確認

### 音声が生成されない場合

- **話者IDが正しいか確認**
  - エンジンの`/speakers`エンドポイントで利用可能な話者IDを確認
  - Unity側の`Speaker ID`を正しい値に設定

- **テキストが正しいか確認**
  - シナリオJSONファイルの`text`フィールドが正しく設定されているか確認
  - 空文字列やnullの場合はスキップされます

### Macで動作しない場合

- **Rosettaのインストール**
  - Apple Silicon搭載のMacの場合、Rosettaが必要な場合があります
  - 初回起動時に案内に従ってインストール

- **権限の確認**
  - システム環境設定のセキュリティとプライバシーで、VOICEVOX/COEIROINKへのアクセスが許可されているか確認

## 複数開発環境での利用

- **各PCにインストールが必要**
  - Windows/Macの各開発環境で、それぞれVOICEVOX/COEIROINKをインストールする必要があります
  - エンジンはローカルHTTPサーバーとして動作するため、各PCで個別に起動します

- **バージョンの統一**
  - 音質の一貫性を保つため、すべての環境で同一バージョンを使用することを推奨します

- **ネットワーク経由での利用（オプション）**
  - 一方のPCをエンジンサーバーとしてLAN内で公開し、他方のPCからHTTPでアクセスする構成も可能です
  - その場合、`Engine Base URL`をサーバーPCのIPアドレスに変更（例: `http://192.168.1.100:50021`）
  - ただし、展示現場での冗長性やレイテンシを考慮すると、各PCにインストールしておくことが推奨されます

## API仕様

VOICEVOX/COEIROINKエンジンは以下のREST APIを提供します：

- `GET /speakers`: 利用可能な話者一覧を取得
- `GET /audio_query?text={text}&speaker={speaker_id}`: テキストから音声クエリを生成
- `POST /synthesis?speaker={speaker_id}`: 音声クエリからWAV音声を生成

詳細は[VOICEVOX公式ドキュメント](https://voicevox.hiroshiba.jp/)を参照してください。

