using UnityEngine;
using Encounter.Audio;
using Encounter.Mapping;

namespace Encounter.Testing
{
    /// <summary>
    /// DMX機器に送っているリアルタイムの数値（最新の目標値）を可視化するコンポーネント
    /// 四角い枠だけのスプライトで表示
    /// </summary>
    public class DmxRealtimeVisualizer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("KineticLightController（実際にDMX機器に送信している値を見るため）")]
        public Encounter.DMX.KineticLightController kineticLightController;

        [Header("Visualization Settings")]
        [Tooltip("表示するフィクスチャ数（0の場合はKineticLightControllerのフィクスチャ数を使用）")]
        public int fixtureCount = 0;
        [Tooltip("スプライト間のX間隔")]
        public float spriteSpacing = 1f;
        [Tooltip("スプライトのX座標の中心")]
        public float centerX = 0f;

        [Header("Position Settings")]
        [Tooltip("上段Y座標（DMX 0 = 約3メートル）")]
        public float topY = 3f;
        [Tooltip("下段Y座標（DMX 100 = 床）")]
        public float bottomY = 0f;

        [Header("Sprite Settings")]
        [Tooltip("スプライトのサイズ")]
        public float spriteSize = 0.3f;
        [Tooltip("枠の太さ（ピクセル）")]
        public int borderWidth = 2;
        [Tooltip("枠の色")]
        public Color borderColor = Color.yellow;

        [Header("Update Settings")]
        [Tooltip("更新間隔（秒）。0の場合は毎フレーム更新")]
        public float updateInterval = 0.1f;

        private SpriteRenderer[] _spriteRenderers;
        private GameObject[] _spriteObjects;
        private float _lastUpdateTime = 0f;

        void Start()
        {
            // KineticLightControllerを検索
            if (kineticLightController == null)
            {
                kineticLightController = FindFirstObjectByType<Encounter.DMX.KineticLightController>();
            }

            if (kineticLightController == null)
            {
                Debug.LogError("[DmxRealtimeVisualizer] KineticLightControllerが見つかりません。");
                return;
            }

            // フィクスチャ数が0の場合は、KineticLightControllerから取得
            if (fixtureCount <= 0 && kineticLightController.fixtures != null)
            {
                fixtureCount = kineticLightController.fixtures.Count;
            }

            if (fixtureCount <= 0)
            {
                Debug.LogWarning("[DmxRealtimeVisualizer] フィクスチャ数が0です。スプライトを作成できません。");
                return;
            }

            // スプライトを作成
            CreateSprites();
        }

        void OnDestroy()
        {
            // スプライトを削除
            if (_spriteObjects != null)
            {
                for (int i = 0; i < _spriteObjects.Length; i++)
                {
                    if (_spriteObjects[i] != null)
                    {
                        Destroy(_spriteObjects[i]);
                    }
                }
            }
        }

        void Update()
        {
            // 更新間隔をチェック
            if (updateInterval > 0f)
            {
                if (Time.time - _lastUpdateTime < updateInterval)
                {
                    return;
                }
                _lastUpdateTime = Time.time;
            }

            UpdatePositions();
        }

        private void CreateSprites()
        {
            // フィクスチャ数を決定
            int count = fixtureCount;
            if (count <= 0 && kineticLightController != null && kineticLightController.fixtures != null)
            {
                count = kineticLightController.fixtures.Count;
            }
            if (count <= 0)
            {
                count = 5; // デフォルト値
                Debug.LogWarning("[DmxRealtimeVisualizer] フィクスチャ数が0です。デフォルト値5を使用します。");
            }

            _spriteRenderers = new SpriteRenderer[count];
            _spriteObjects = new GameObject[count];

            for (int i = 0; i < count; i++)
            {
                // 四角い枠だけのスプライトを作成
                Sprite frameSprite = CreateFrameSprite();

                // GameObjectを作成
                GameObject spriteObj = new GameObject($"DmxRealtimeSprite_{i}");
                spriteObj.transform.SetParent(transform);
                spriteObj.transform.localPosition = Vector3.zero;

                // SpriteRendererを追加
                SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
                sr.sprite = frameSprite;
                sr.color = borderColor;
                sr.sortingOrder = 10; // 前面に表示

                _spriteRenderers[i] = sr;
                _spriteObjects[i] = spriteObj;
            }
        }

        private Sprite CreateFrameSprite()
        {
            int textureSize = 64; // テクスチャのサイズ
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);

            // 透明で初期化
            Color[] pixels = new Color[textureSize * textureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            // 枠を描画（上下左右の境界線のみ）
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    // 上端、下端、左端、右端の境界線を描画
                    bool isTopBorder = y >= textureSize - borderWidth;
                    bool isBottomBorder = y < borderWidth;
                    bool isLeftBorder = x < borderWidth;
                    bool isRightBorder = x >= textureSize - borderWidth;
                    
                    if (isTopBorder || isBottomBorder || isLeftBorder || isRightBorder)
                    {
                        pixels[y * textureSize + x] = borderColor;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            // スプライトを作成
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                100f // pixels per unit
            );

            return sprite;
        }

        private void UpdatePositions()
        {
            if (_spriteRenderers == null || kineticLightController == null) return;

            int fixtureCount = kineticLightController.fixtures != null ? kineticLightController.fixtures.Count : 0;
            if (fixtureCount == 0)
            {
                return;
            }

            for (int i = 0; i < _spriteRenderers.Length && i < fixtureCount; i++)
            {
                if (_spriteRenderers[i] == null) continue;

                // 実際にDMX機器に送信されている高さ値を取得
                int actualDmxHeight = kineticLightController.GetFixtureHeight(i);

                // DMX値(0-100)をY座標(topY to bottomY)にマッピング
                // DMX 0 = 上段（topY）
                // DMX 100 = 下段（bottomY）
                float t = Mathf.Clamp01((float)actualDmxHeight / 100f);
                float y = Mathf.Lerp(topY, bottomY, t);

                // X座標を計算（中央を基準に配置）
                int totalCount = _spriteRenderers.Length;
                float x = centerX + (i - (totalCount - 1) / 2f) * spriteSpacing;

                // 位置を更新
                _spriteObjects[i].transform.position = new Vector3(x, y, 0f);
                _spriteObjects[i].transform.localScale = new Vector3(spriteSize, spriteSize, 1f);
            }
        }
    }
}

