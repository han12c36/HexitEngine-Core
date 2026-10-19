using System;
using UnityEngine;

namespace HexitEngine
{
    [Serializable]
    public class PoolItem
    {
        public GameObject prefab;

        [Min(0)]
        public int prewarm;
    }
}