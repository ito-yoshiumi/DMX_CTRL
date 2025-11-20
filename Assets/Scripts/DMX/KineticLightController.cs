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

        private byte[] _dmx = new byte[512];

        void Start()
        {
            ApplyFixtureDefaults();
            Apply();
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
            if (!IsValid(index)) return;
            var f = fixtures[index];
            int addr = f.startAddress + f.heightCh - 2;
            Write(addr, (byte)Mathf.Clamp(dmxValue01_255, 0, 255));
            ApplyFixtureDefaults(f);
        }

        public void SetFixtureColor(int index, Color color)
        {
            if (!IsValid(index)) return;
            var f = fixtures[index];
            int r = f.startAddress + f.redCh - 2;
            int g = f.startAddress + f.greenCh - 2;
            int b = f.startAddress + f.blueCh - 2;
            Write(r, (byte)Mathf.RoundToInt(color.r * 255f));
            Write(g, (byte)Mathf.RoundToInt(color.g * 255f));
            Write(b, (byte)Mathf.RoundToInt(color.b * 255f));
            ApplyFixtureDefaults(f);
        }

        public void Apply()
        {
            if (artNet == null) return;
            artNet.SendDmx(_dmx);
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
    }
}
