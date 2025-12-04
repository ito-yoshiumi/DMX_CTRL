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
- [ ] YIN ピッチ推定の最適化（低レイテンシ）
- [ ] マルチ言語台本（scenario_en.json 他）
- [ ] 安全フェンス/人感のセンサー連携
