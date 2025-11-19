using UnityEngine;
using Encounter.Audio;
using Encounter.Mapping;
using Encounter.DMX;

namespace Encounter.Testing
{
    /// <summary>
    /// 5つのSquareを五線譜のように配置し、音程に応じて上下に移動させる
    /// 各Squareは異なる音程範囲（低音→高音）に反応
    /// </summary>
    public class PitchStaffVisualizer : MonoBehaviour
    {
        [Header("References")]
        public AudioInputManager audioInputManager;

        [Header("Staff Configuration")]
        [Tooltip("5つのSquareスプライト（低音→高音の順）")]
        public SpriteRenderer[] squares = new SpriteRenderer[5];

        [Header("Position Settings")]
        [Tooltip("五線譜の上段Y座標（初期位置、約3メートル）")]
        public float staffTopY = 3f;
        [Tooltip("五線譜の下段Y座標（床の位置、0メートル）")]
        public float staffBottomY = 0f;
        [Tooltip("Square間のX間隔（100センチ = 1メートル）")]
        public float squareSpacing = 1f;
        [Tooltip("SquareのX座標の中心")]
        public float centerX = 0f;

        [Header("Pitch Mapping")]
        [Tooltip("各Squareが反応する音程範囲の最小値（Hz）。配列の要素数はSquareの数と一致させる")]
        public float[] pitchMinHz = new float[] { 80f, 150f, 200f, 250f, 300f }; // 各Squareの最小値
        [Tooltip("各Squareが反応する音程範囲の最大値（Hz）。配列の要素数はSquareの数と一致させる")]
        public float[] pitchMaxHz = new float[] { 150f, 200f, 250f, 300f, 350f }; // 各Squareの最大値

        [Header("Color Mapping")]
        public VolumeToColorMapper volumeMapper;

        [Header("DMX Output")]
        [Tooltip("KineticLightController（未設定なら自動検出）")]
        public KineticLightController kineticLightController;
        [Tooltip("ピッチに応じた高さをDMX機器にも送信するか")]
        public bool sendHeightToDmx = true;
        [Tooltip("音量に応じた色をDMX機器にも送信するか")]
        public bool sendColorToDmx = true;

        [Header("Debug")]
        [Tooltip("PitchToHeightMapperのデバッグログを表示するかどうか")]
        [SerializeField]
        private bool _enableDebugLog = true;

        public bool enableDebugLog
        {
            get => _enableDebugLog;
            set
            {
                if (_enableDebugLog != value)
                {
                    _enableDebugLog = value;
                    // 既存のPitchToHeightMapperにも適用
                    if (_pitchMappers != null)
                    {
                        for (int i = 0; i < _pitchMappers.Length; i++)
                        {
                            if (_pitchMappers[i] != null)
                            {
                                _pitchMappers[i].enableDebugLog = _enableDebugLog;
                            }
                        }
                    }
                }
            }
        }

        private PitchToHeightMapper[] _pitchMappers = new PitchToHeightMapper[5];
        private Vector3[] _initialPositions = new Vector3[5];
        private float _currentPitch = 0f;
        private float _currentRms = 0f;

        void Start()
        {
            // AudioInputManagerを検索
            if (audioInputManager == null)
            {
                audioInputManager = FindFirstObjectByType<AudioInputManager>();
                if (audioInputManager == null)
                {
                    Debug.LogError("[PitchStaffVisualizer] AudioInputManagerが見つかりません。");
                    return;
                }
            }

            if (kineticLightController == null && sendHeightToDmx)
            {
                kineticLightController = FindFirstObjectByType<KineticLightController>();
                if (kineticLightController == null)
                {
                    Debug.LogWarning("[PitchStaffVisualizer] KineticLightControllerが見つかりません。DMX送信をスキップします。");
                    sendHeightToDmx = false;
                    sendColorToDmx = false;
                }
            }

            if (enableDebugLog)
            {
                Debug.Log($"[PitchStaffVisualizer] AudioInputManagerを検出: {audioInputManager.name}");
            }

            // VolumeToColorMapperを検索または作成
            if (volumeMapper == null)
            {
                volumeMapper = GetComponent<VolumeToColorMapper>();
                if (volumeMapper == null)
                {
                    volumeMapper = gameObject.AddComponent<VolumeToColorMapper>();
                }
            }

            // 各Squareの初期位置とサイズを設定
            int squareCount = 0;
            for (int i = 0; i < squares.Length && i < 5; i++)
            {
                if (squares[i] != null)
                {
                    squareCount++;
                    float x = centerX + (i - 2) * squareSpacing; // -2, -1, 0, 1, 2の位置
                    _initialPositions[i] = new Vector3(x, staffTopY, 0f);
                    squares[i].transform.position = _initialPositions[i];
                    
                    // Squareのサイズを30センチ（0.3m）に設定
                    squares[i].transform.localScale = new Vector3(0.3f, 0.3f, 1f);

                    // 各SquareにPitchToHeightMapperを追加
                    _pitchMappers[i] = squares[i].gameObject.GetComponent<PitchToHeightMapper>();
                    if (_pitchMappers[i] == null)
                    {
                        _pitchMappers[i] = squares[i].gameObject.AddComponent<PitchToHeightMapper>();
                    }
                    
                    // 既存のコンポーネントの設定を更新
                    if (_pitchMappers[i] != null)
                    {
                        // pitchMinHz/pitchMaxHz配列から各Squareの音域範囲を設定
                        if (i < pitchMinHz.Length && i < pitchMaxHz.Length)
                        {
                            _pitchMappers[i].pitchMinHz = pitchMinHz[i];
                            _pitchMappers[i].pitchMaxHz = pitchMaxHz[i];
                        }
                        else
                        {
                            // 配列が不足している場合はデフォルト値を使用
                            _pitchMappers[i].pitchMinHz = 80f;
                            _pitchMappers[i].pitchMaxHz = 350f;
                            if (enableDebugLog)
                            {
                                Debug.LogWarning($"[PitchStaffVisualizer] Square{i}の音域範囲配列が不足しています。デフォルト値(80-350Hz)を使用します。");
                            }
                        }
                        
                        _pitchMappers[i].dmxMin = 0; // 上段（約3メートル）
                        _pitchMappers[i].dmxMax = 100; // 下段（床）
                        _pitchMappers[i].speed = 50f; // 等速運動の速度（DMX値/秒）
                        _pitchMappers[i].enableDebugLog = enableDebugLog; // デバッグログの設定を適用
                        _pitchMappers[i].Reset(); // 初期値をリセット
                        
                        if (enableDebugLog)
                        {
                            Debug.Log($"[PitchStaffVisualizer] Square{i}の音域範囲: {_pitchMappers[i].pitchMinHz}Hz ～ {_pitchMappers[i].pitchMaxHz}Hz");
                        }
                    }
                }
            }

            if (enableDebugLog)
            {
                Debug.Log($"[PitchStaffVisualizer] {squareCount}個のSquareを設定しました。");
            }

            // イベント購読
            if (audioInputManager != null)
            {
                audioInputManager.OnRms += OnRmsReceived;
                audioInputManager.OnPitchHz += OnPitchReceived;
                if (enableDebugLog)
                {
                    Debug.Log("[PitchStaffVisualizer] イベント購読完了");
                }
            }
            else
            {
                Debug.LogError("[PitchStaffVisualizer] AudioInputManagerがnullです。イベント購読できません。");
            }
        }

        void OnDestroy()
        {
            if (audioInputManager != null)
            {
                audioInputManager.OnRms -= OnRmsReceived;
                audioInputManager.OnPitchHz -= OnPitchReceived;
            }
        }

        void OnValidate()
        {
            // InspectorでenableDebugLogが変更されたときに、既存のPitchToHeightMapperにも適用
            if (_pitchMappers != null)
            {
                for (int i = 0; i < _pitchMappers.Length; i++)
                {
                    if (_pitchMappers[i] != null)
                    {
                        _pitchMappers[i].enableDebugLog = _enableDebugLog;
                    }
                }
            }
        }

        private void OnRmsReceived(float rms)
        {
            _currentRms = rms;
            UpdateColors();
        }

        private void OnPitchReceived(float pitchHz)
        {
            _currentPitch = pitchHz;
            // 音が無いときでも、最後のDMX値に向かって移動し続けるようにUpdatePositions()を呼び出す
            UpdatePositions();
            
            // ピッチが検出されていない場合のデバッグ（頻繁に出力されないように）
            if (enableDebugLog && pitchHz <= 0f && Time.frameCount % 60 == 0) // 約1秒に1回
            {
                Debug.Log($"[PitchStaffVisualizer] ピッチ未検出 (pitchHz: {pitchHz}) - 最後の位置に向かって移動中");
            }
        }

        private void UpdatePositions()
        {
            // グラフィックイコライザー風: 各Squareが自分の音域範囲にのみ反応
            // 中央値でDMX最大、範囲端で0、範囲外で0
            for (int i = 0; i < squares.Length && i < 5; i++)
            {
                if (squares[i] == null || _pitchMappers[i] == null) continue;

                // 各Squareの音域範囲を取得
                float minHz = i < pitchMinHz.Length ? pitchMinHz[i] : 80f;
                float maxHz = i < pitchMaxHz.Length ? pitchMaxHz[i] : 350f;
                float centerHz = (minHz + maxHz) * 0.5f; // 中央値
                float range = maxHz - minHz; // 範囲の幅

                int dmxHeight = 0;

                // ピッチが範囲内にある場合のみ計算
                if (_currentPitch > 0f && _currentPitch >= minHz && _currentPitch <= maxHz)
                {
                    // 中央値からの距離を計算（0～1、中央値で0、範囲端で1）
                    float distanceFromCenter = Mathf.Abs(_currentPitch - centerHz) / (range * 0.5f);
                    
                    // 距離に基づいてDMX値を計算（中央値で100、範囲端で0）
                    // 線形補間: DMX = 100 * (1 - distanceFromCenter)
                    float dmxFloat = 100f * (1f - Mathf.Clamp01(distanceFromCenter));
                    dmxHeight = Mathf.RoundToInt(dmxFloat);
                }
                else
                {
                    // 範囲外または無音の場合は0
                    dmxHeight = 0;
                }

                // グラフィックイコライザー風の計算結果を目標値として設定
                _pitchMappers[i].SetTargetDmxValue(dmxHeight);
                
                // 等速運動で目標位置に向かって移動（シミュレーション）
                int simulatedDmx = _pitchMappers[i].MapPitchHzToDmx(_currentPitch);
                
                // シミュレーション結果を使用（等速運動で滑らかに移動）
                dmxHeight = simulatedDmx;

                // DMX値(0-100)をY座標(staffTopY to staffBottomY)にマッピング
                // DMX 0 = 上段（staffTopY = 3.0）
                // DMX 100 = 下段（staffBottomY = 0.0）
                float t = Mathf.Clamp01((float)dmxHeight / 100f);
                float y = Mathf.Lerp(staffTopY, staffBottomY, t);

                // X座標は初期位置から取得、Y座標は計算した値を使用
                float x = centerX + (i - 2) * squareSpacing;
                Vector3 pos = new Vector3(x, y, 0f);
                squares[i].transform.position = pos;

                // デバッグログ（頻繁に出力されないように）
                if (enableDebugLog && Time.frameCount % 60 == 0 && i == 0) // 約1秒に1回、最初のSquareのみ
                {
                    Debug.Log($"[PitchStaffVisualizer] Square{i}: ピッチ: {_currentPitch:F1}Hz, 範囲: {minHz}-{maxHz}Hz, 中央値: {centerHz:F1}Hz, DMX: {dmxHeight}, Y: {y:F2}");
                }

                if (sendHeightToDmx && kineticLightController != null)
                {
                    kineticLightController.SetFixtureHeight(i, Mathf.Clamp(dmxHeight, 0, 100));
                }
            }

            if (sendHeightToDmx && kineticLightController != null)
            {
                kineticLightController.Apply();
            }
        }

        private void UpdateColors()
        {
            if (volumeMapper == null) return;

            // 音量に応じて色を変更（全Square同じ色）
            Color color = volumeMapper.MapRmsToColor(_currentRms);

            for (int i = 0; i < squares.Length && i < 5; i++)
            {
                if (squares[i] != null)
                {
                    squares[i].color = color;
                    if (sendColorToDmx && kineticLightController != null)
                    {
                        kineticLightController.SetFixtureColor(i, color);
                    }
                }
            }

            if (sendColorToDmx && kineticLightController != null)
            {
                kineticLightController.Apply();
            }
        }
    }
}

