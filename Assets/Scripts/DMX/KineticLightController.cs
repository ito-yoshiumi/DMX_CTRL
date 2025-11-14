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

        private byte[] _dmx = new byte[512];

        public void SetFixtureHeight(int index, int dmxValue01_255)
        {
            if (!IsValid(index)) return;
            var f = fixtures[index];
            int addr = f.startAddress + f.heightCh - 2;
            Write(addr, (byte)Mathf.Clamp(dmxValue01_255, 0, 255));
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

        private bool IsValid(int index) => fixtures != null && index >= 0 && index < fixtures.Count;
    }
}
