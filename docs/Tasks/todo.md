# TODO — Cursor実装タスク（優先順）

## P0（MVP）
- [x] ArtNetClient: UDP で Art-Net パケットを送出（固定ユニバース）
- [x] KineticLightController: 5台分の高さ/色を DMX に反映 → Apply()
- [x] CuePlayer: Cues/*.json を読み、線形補間でキー フレーム適用
- [x] ScenarioRunner: scenario_ja.json を読み、WAV 再生＋Cue 起動
- [x] AudioInputManager: RMS と簡易ピッチをイベントで発火
- [x] PitchToHeightMapper/VolumeToColorMapper: マッピング＆スムージング
- [x] SafetyManager: 非常停止（出力ゼロ/Apply停止）

## P1（展示向け）
- [x] VOICEVOX/COEIROINK TTS: 起動時キャッシュ→AudioClip 化
- [x] 参加者5音の録音＆ミックス再生
- [x] OperatorPanel: 進行/スキップ/巻き戻し/音量/UI
- [x] ログ保存（JSON/CSV）
- [x] 音声検出による自動録音開始（発声タイミングをトリガーに）
- [x] 音声検出閾値の設定UI
- [x] 音声入力レベルのリアルタイム表示UI

## P2（拡張）
- [x] YIN ピッチ推定の最適化（低レイテンシ）
- [ ] マルチ言語台本（scenario_en.json 他）
- [ ] 安全フェンス/人感のセンサー連携

## 修正が必要な問題

### 録音中のライト制御のフリッカー問題
- **問題**: 録音中（`waitForVoiceTrigger`時および通常録音時）にライトがフリッカーしている
- **現状**: 
  - 無音時：消灯（意図通り）
  - 音声あり時：TTSモーションと同じ色相変化で連続点灯を試みているが、フリッカーが発生
- **期待される動作**:
  - 無音時：消灯（点滅なし）
  - 音声あり時：TTSが話しているときと同じように鮮やかに連続点灯（フリッカーなし）
- **修正方針**: 
  - 実際のDMXフィクスチャで動作確認後、フリッカーの原因を特定して修正
  - 更新レート、色相変化の計算方法、Apply()の呼び出しタイミングなどを調整