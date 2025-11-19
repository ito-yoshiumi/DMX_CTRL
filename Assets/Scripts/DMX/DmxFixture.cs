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
        [Tooltip("高さ（リフト）のチャンネル番号 (1-based)")]
        public int heightCh = 6;
        [Tooltip("Red / Green / Blue のチャンネル番号 (1-based)")]
        public int redCh    = 1;
        public int greenCh  = 2;
        public int blueCh   = 3;
        [Tooltip("ディマーとストロボのチャンネル番号 (1-based)")]
        public int dimmerCh = 4;
        public int strobeCh = 5;
    }
}
