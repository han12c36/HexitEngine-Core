using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexitEngine
{
    [Serializable]
    public class NetworkPoolConfig : HexitEngineTapConfig<NetworkPoolConfig>
    {
        [SerializeField] private List<PoolItem> prefabList;

        public List<PoolItem> PrefabList => prefabList;
    }
}