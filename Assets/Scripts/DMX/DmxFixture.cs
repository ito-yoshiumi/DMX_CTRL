using System;
using UnityEngine;

namespace Encounter.DMX
{
    [Serializable]
    public class DmxFixture
    {
        [Tooltip("開始アドレス(1-512)")]
        public int startAddress = 1;

        [Header("Channel Offsets")]
        public int heightCh = 1;  // 相対
        public int redCh    = 2;
        public int greenCh  = 3;
        public int blueCh   = 4;
    }
}
