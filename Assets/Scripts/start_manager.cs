using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

namespace Encounter
{
    /// <summary>
    /// GAME_MAINシーンが呼び出された時にカウントダウンを行い、ゲームを開始するマネージャー
    /// カウントダウン中は主人公以外の指定されたコンポーネントをフリーズさせます
    /// </summary>
    public class start_manager : MonoBehaviour
    {
        [Header("Countdown Settings")]
        [Tooltip("カウントダウンの各数字の表示時間（秒）")]
        public float countdownInterval = 1.0f;
        
        [Tooltip("START表示の表示時間（秒）")]
        public float startDisplayDuration = 1.0f;

        [Header("UI Settings")]
        [Tooltip("カウントダウン/START表示の位置（画面中央からのオフセット）")]
        public Vector2 displayPosition = Vector2.zero;
        
        [Tooltip("表示フォントサイズ")]
        public int fontSize = 120;
        
        [Tooltip("カウントダウンの色")]
        public Color countdownColor = Color.white;
        
        [Tooltip("START表示の色")]
        public Color startColor = Color.yellow;

        [Header("Freeze Settings")]
        [Tooltip("カウントダウン中にフリーズさせるコンポーネントのリスト")]
        public List<MonoBehaviour> componentsToFreeze = new List<MonoBehaviour>();
        
        [Tooltip("主人公のGameObject（このオブジェクトはフリーズしません）")]
        public GameObject playerObject;

        [Header("Debug")]
        [Tooltip("デバッグログを表示するかどうか")]
        public bool enableDebugLog = true;

        private bool _isCountdownActive = false;
        private string _currentDisplayText = "";
        private bool _gameStarted = false;
        private List<bool> _originalComponentStates = new List<bool>();

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start()
        {
            // 現在のシーンがGAME_MAINかどうかを確認
            if (SceneManager.GetActiveScene().name == "GAME_MAIN")
            {
                StartCountdown();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[start_manager] シーンが読み込まれました: {scene.name}");
            }

            if (scene.name == "GAME_MAIN")
            {
                StartCountdown();
            }
        }

        /// <summary>
        /// カウントダウンを開始
        /// </summary>
        public void StartCountdown()
        {
            if (_isCountdownActive || _gameStarted)
            {
                return;
            }

            _isCountdownActive = true;
            StartCoroutine(CountdownCoroutine());
        }

        private IEnumerator CountdownCoroutine()
        {
            if (enableDebugLog)
            {
                Debug.Log("[start_manager] カウントダウンを開始します");
            }

            // フリーズ対象のコンポーネントの状態を保存して無効化
            FreezeComponents();

            // カウントダウン: 3, 2, 1
            for (int i = 3; i >= 1; i--)
            {
                _currentDisplayText = i.ToString();
                if (enableDebugLog)
                {
                    Debug.Log($"[start_manager] カウントダウン: {i}");
                }
                yield return new WaitForSeconds(countdownInterval);
            }

            // START表示
            _currentDisplayText = "START";
            if (enableDebugLog)
            {
                Debug.Log("[start_manager] START!");
            }
            yield return new WaitForSeconds(startDisplayDuration);

            // カウントダウン終了
            _currentDisplayText = "";
            _isCountdownActive = false;
            _gameStarted = true;

            // フリーズを解除
            UnfreezeComponents();

            if (enableDebugLog)
            {
                Debug.Log("[start_manager] ゲーム開始！");
            }
        }

        /// <summary>
        /// 指定されたコンポーネントをフリーズ（無効化）
        /// </summary>
        private void FreezeComponents()
        {
            _originalComponentStates.Clear();

            foreach (var component in componentsToFreeze)
            {
                if (component != null)
                {
                    // 主人公のオブジェクトに属するコンポーネントはスキップ
                    if (playerObject != null && component.gameObject == playerObject)
                    {
                        continue;
                    }

                    _originalComponentStates.Add(component.enabled);
                    component.enabled = false;

                    if (enableDebugLog)
                    {
                        Debug.Log($"[start_manager] コンポーネントをフリーズ: {component.GetType().Name} on {component.gameObject.name}");
                    }
                }
            }
        }

        /// <summary>
        /// フリーズを解除（元の状態に戻す）
        /// </summary>
        private void UnfreezeComponents()
        {
            for (int i = 0; i < componentsToFreeze.Count && i < _originalComponentStates.Count; i++)
            {
                var component = componentsToFreeze[i];
                if (component != null)
                {
                    // 主人公のオブジェクトに属するコンポーネントはスキップ
                    if (playerObject != null && component.gameObject == playerObject)
                    {
                        continue;
                    }

                    component.enabled = _originalComponentStates[i];

                    if (enableDebugLog)
                    {
                        Debug.Log($"[start_manager] コンポーネントのフリーズを解除: {component.GetType().Name} on {component.gameObject.name}");
                    }
                }
            }

            _originalComponentStates.Clear();
        }

        /// <summary>
        /// カウントダウン中かどうかを取得
        /// </summary>
        public bool IsCountdownActive()
        {
            return _isCountdownActive;
        }

        /// <summary>
        /// ゲームが開始されたかどうかを取得
        /// </summary>
        public bool IsGameStarted()
        {
            return _gameStarted;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(_currentDisplayText))
            {
                return;
            }

            // 画面中央に表示
            float centerX = Screen.width / 2f + displayPosition.x;
            float centerY = Screen.height / 2f + displayPosition.y;

            // フォントサイズを設定
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = fontSize;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = _currentDisplayText == "START" ? startColor : countdownColor;
            style.fontStyle = FontStyle.Bold;

            // テキストのサイズを計算
            Vector2 textSize = style.CalcSize(new GUIContent(_currentDisplayText));
            Rect labelRect = new Rect(centerX - textSize.x / 2f, centerY - textSize.y / 2f, textSize.x, textSize.y);

            // テキストを描画
            GUI.Label(labelRect, _currentDisplayText, style);
        }
    }
}


