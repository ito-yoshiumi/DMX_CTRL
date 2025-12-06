using UnityEngine;
using System.Collections.Generic;

namespace Encounter.DMX
{
    public class KineticLightController : MonoBehaviour
    {
        [Header("Art-Net Client")]
        public ArtNetClient artNet;

        [Header("Fixtures (低音→高音の順に配置)")]
        public List<DmxFixture> fixtures = new List<DmxFixture>();

        [Header("Fixture Defaults")]
        [Tooltip("RGB/高さを書き込むたびにディマー値を固定します")]
        public bool forceDimmerValue = true;
        [Range(0, 255)]
        public int dimmerValue = 255;
        [Tooltip("RGB/高さを書き込むたびにストロボ値を固定します")]
        public bool forceStrobeValue = true;
        [Range(0, 255)]
        public int strobeValue = 0;

        [Header("Simulation Settings")]
        [Tooltip("シミュレーション用のオブジェクト（Fixturesと同じ順序）")]
        public List<Transform> fixtureObjects = new List<Transform>();
        [Tooltip("DMX高さ 0 のときのY座標")]
        public float heightMinY = 5.0f; // 上（高い位置）
        [Tooltip("DMX高さ 100 のときのY座標")]
        public float heightMaxY = 0.0f; // 下（低い位置）
        [Tooltip("DMX高さ 0-100 を Y座標に反映するか")]
        public bool simulateHeight = true;
        [Tooltip("DMX色を マテリアルカラーに反映するか")]
        public bool simulateColor = true;

        [Header("DMX Debug")]
        [Tooltip("DMX送信の詳細ログを表示するか")]
        public bool enableDmxDebugLog = false;
        [Tooltip("DMX送信のログ間隔（秒）")]
        public float dmxLogInterval = 1.0f;

        private byte[] _dmx = new byte[512];
        private float _lastDmxLogTime = 0f;

        void Start()
        {
            if (artNet == null)
            {
                Debug.LogError("[KineticLightController] ArtNetClientが設定されていません！");
                return;
            }
            
            Debug.Log($"[KineticLightController] 初期化: フィクスチャ数={fixtures?.Count ?? 0}, forceDimmer={forceDimmerValue}, dimmerValue={dimmerValue}, forceStrobe={forceStrobeValue}, strobeValue={strobeValue}");
            
            ApplyFixtureDefaults();
            Apply();
            
            Debug.Log("[KineticLightController] 初期化完了: デフォルト値を送信しました");
        }

        void OnDisable()
        {
            SendBlackout();
        }

        void OnApplicationQuit()
        {
            SendBlackout();
        }

        public void SetFixtureHeight(int index, int dmxValue01_255)
        {
            if (!IsValid(index))
            {
                Debug.LogWarning($"[KineticLightController] SetFixtureHeight: 無効なインデックス {index} (fixtures.Count={fixtures.Count})");
                return;
            }
            var f = fixtures[index];
            int addr = f.startAddress + f.heightCh - 2;
            Write(addr, (byte)Mathf.Clamp(dmxValue01_255, 0, 255));
            ApplyFixtureDefaults(f);

            // Simulation
            if (simulateHeight)
            {
                if (fixtureObjects == null || index >= fixtureObjects.Count)
                {
                    // デバッグログは頻繁に出ると煩わしいので、最初の1回だけ
                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.LogWarning($"[KineticLightController] SetFixtureHeight: fixtureObjects[{index}] が設定されていません (Count: {fixtureObjects?.Count ?? 0})");
                    }
                }
                else if (fixtureObjects[index] != null)
                {
                    // DMX 0-100 (range in typical Kinetic Lights)
                    // dmxValue is 0-255, but typically 0-100 is used for height percentage
                    // Here we assume dmxValue01_255 is passed as 0-100 scale usually, but let's normalize 0-100.
                    // Note: CuePlayer passes 0-100.
                    
                    float t = Mathf.Clamp(dmxValue01_255, 0, 100) / 100f;
                    Vector3 pos = fixtureObjects[index].position;
                    pos.y = Mathf.Lerp(heightMinY, heightMaxY, t);
                    fixtureObjects[index].position = pos;
                }
            }
        }

        public void SetFixtureColor(int index, Color color)
        {
            if (!IsValid(index))
            {
                Debug.LogWarning($"[KineticLightController] SetFixtureColor: 無効なインデックス {index} (fixtures.Count={fixtures.Count})");
                return;
            }
            var f = fixtures[index];
            int r = f.startAddress + f.redCh - 2;
            int g = f.startAddress + f.greenCh - 2;
            int b = f.startAddress + f.blueCh - 2;
            Write(r, (byte)Mathf.RoundToInt(color.r * 255f));
            Write(g, (byte)Mathf.RoundToInt(color.g * 255f));
            Write(b, (byte)Mathf.RoundToInt(color.b * 255f));
            ApplyFixtureDefaults(f);

            // Simulation
            if (simulateColor)
            {
                if (fixtureObjects == null || index >= fixtureObjects.Count)
                {
                    // デバッグログは頻繁に出ると煩わしいので、最初の1回だけ
                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.LogWarning($"[KineticLightController] SetFixtureColor: fixtureObjects[{index}] が設定されていません (Count: {fixtureObjects?.Count ?? 0})");
                    }
                }
                else if (fixtureObjects[index] != null)
                {
                    var renderer = fixtureObjects[index].GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = color;
                        // Emissionも設定すると光って見える
                        renderer.material.SetColor("_EmissionColor", color);
                        if (color.grayscale > 0.1f)
                        {
                             renderer.material.EnableKeyword("_EMISSION");
                        }
                    }
                    else
                    {
                        if (Time.frameCount % 60 == 0)
                        {
                            Debug.LogWarning($"[KineticLightController] SetFixtureColor: fixtureObjects[{index}] に Renderer コンポーネントがありません");
                        }
                    }
                }
            }
        }

        private int _applyCount = 0;
        private float _lastApplyLogTime = 0f;
        private const float APPLY_LOG_INTERVAL = 5f;

        public void Apply()
        {
            if (artNet == null)
            {
                Debug.LogError("[KineticLightController] Apply: ArtNetClientがnullです");
                return;
            }
            
            _applyCount++;
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - _lastApplyLogTime > APPLY_LOG_INTERVAL)
            {
                Debug.Log($"[KineticLightController] Apply呼び出し: {_applyCount}回目");
                _lastApplyLogTime = currentTime;
            }
            
            // DMX送信の詳細ログ
            if (enableDmxDebugLog && currentTime - _lastDmxLogTime > dmxLogInterval)
            {
                LogDmxValues();
                _lastDmxLogTime = currentTime;
            }
            
            artNet.SendDmx(_dmx);
        }

        private void LogDmxValues()
        {
            if (fixtures == null || fixtures.Count == 0) return;

            string log = "[KineticLightController] DMX送信値: ";
            bool hasNonZero = false;
            
            for (int i = 0; i < fixtures.Count; i++)
            {
                var fixture = fixtures[i];
                int addr = fixture.startAddress - 1; // 0-based address
                
                if (addr >= 0 && addr < _dmx.Length)
                {
                    int r = addr + fixture.redCh - 1;
                    int g = addr + fixture.greenCh - 1;
                    int b = addr + fixture.blueCh - 1;
                    int h = addr + fixture.heightCh - 1;
                    
                    if (r < _dmx.Length && g < _dmx.Length && b < _dmx.Length && h < _dmx.Length)
                    {
                        byte rVal = _dmx[r];
                        byte gVal = _dmx[g];
                        byte bVal = _dmx[b];
                        byte hVal = _dmx[h];
                        
                        if (rVal > 0 || gVal > 0 || bVal > 0 || hVal > 0)
                        {
                            hasNonZero = true;
                            log += $"F{i}(R:{rVal} G:{gVal} B:{bVal} H:{hVal}) ";
                        }
                    }
                }
            }
            
            if (hasNonZero)
            {
                Debug.Log(log);
            }
            else
            {
                Debug.Log("[KineticLightController] DMX送信値: 全て0（消灯状態）");
            }
        }

        private void Write(int zeroBasedAddr, byte v)
        {
            if (zeroBasedAddr < 0 || zeroBasedAddr >= _dmx.Length) return;
            _dmx[zeroBasedAddr] = v;
        }

        private void ApplyFixtureDefaults()
        {
            if (fixtures == null) return;
            foreach (var fixture in fixtures)
            {
                ApplyFixtureDefaults(fixture);
            }
        }

        private void ApplyFixtureDefaults(DmxFixture fixture)
        {
            if (fixture == null) return;

            if (forceDimmerValue && fixture.dimmerCh > 0)
            {
                int dim = fixture.startAddress + fixture.dimmerCh - 2;
                Write(dim, (byte)Mathf.Clamp(dimmerValue, 0, 255));
            }

            if (forceStrobeValue && fixture.strobeCh > 0)
            {
                int strobe = fixture.startAddress + fixture.strobeCh - 2;
                Write(strobe, (byte)Mathf.Clamp(strobeValue, 0, 255));
            }
        }

        private void SendBlackout()
        {
            if (artNet == null) return;
            for (int i = 0; i < _dmx.Length; i++)
            {
                _dmx[i] = 0;
            }
            artNet.SendDmx(_dmx);
        }

        private bool IsValid(int index) => fixtures != null && index >= 0 && index < fixtures.Count;

        /// <summary>
        /// 指定したフィクスチャの現在のDMX値を取得
        /// </summary>
        public (int r, int g, int b, int height) GetFixtureDmxValues(int index)
        {
            if (!IsValid(index))
            {
                return (0, 0, 0, 0);
            }

            var fixture = fixtures[index];
            int addr = fixture.startAddress - 1; // 0-based address

            if (addr < 0 || addr >= _dmx.Length)
            {
                return (0, 0, 0, 0);
            }

            int r = 0, g = 0, b = 0, h = 0;

            if (fixture.redCh > 0)
            {
                int rAddr = addr + fixture.redCh - 1;
                if (rAddr >= 0 && rAddr < _dmx.Length)
                {
                    r = _dmx[rAddr];
                }
            }

            if (fixture.greenCh > 0)
            {
                int gAddr = addr + fixture.greenCh - 1;
                if (gAddr >= 0 && gAddr < _dmx.Length)
                {
                    g = _dmx[gAddr];
                }
            }

            if (fixture.blueCh > 0)
            {
                int bAddr = addr + fixture.blueCh - 1;
                if (bAddr >= 0 && bAddr < _dmx.Length)
                {
                    b = _dmx[bAddr];
                }
            }

            if (fixture.heightCh > 0)
            {
                int hAddr = addr + fixture.heightCh - 1;
                if (hAddr >= 0 && hAddr < _dmx.Length)
                {
                    h = _dmx[hAddr];
                }
            }

            return (r, g, b, h);
        }

        /// <summary>
        /// 指定したフィクスチャの現在の高さ（DMX値）を取得
        /// </summary>
        public int GetFixtureHeight(int index)
        {
            var values = GetFixtureDmxValues(index);
            return values.height;
        }

        /// <summary>
        /// 指定したフィクスチャの現在の色（DMX値）を取得
        /// </summary>
        public Color GetFixtureColor(int index)
        {
            var values = GetFixtureDmxValues(index);
            return new Color(values.r / 255f, values.g / 255f, values.b / 255f);
        }
    }
}
