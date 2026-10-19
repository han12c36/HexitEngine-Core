using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HexitEngine
{
    [Serializable]
    public class ScenePoolData
    {
#if UNITY_EDITOR
        [Tooltip("유니티 인스펙터에서 씬 에셋을 직접 드래그 앤 드롭하세요.")]
        public SceneAsset sceneAsset;
#endif
        [HideInInspector] public string sceneName;

        public List<PoolItem> prefabList = new();
    }

    [Serializable]
    public class LocalPoolConfig : HexitEngineTapConfig<LocalPoolConfig>
    {
        [SerializeField] private List<PoolItem> globalPrefabList = new();
        [SerializeField] private List<ScenePoolData> scenePoolList = new();

        public List<PoolItem> GlobalPrefabList => globalPrefabList;
        public List<ScenePoolData> ScenePoolList => scenePoolList;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            // 부모 클래스(H_PreloadedConfig)의 OnValidate 실행
            base.OnValidate();

            if (scenePoolList == null) return;

            // 씬 에셋이 할당되거나 변경될 때마다 자동으로 sceneName 문자열을 갱신
            foreach (var data in scenePoolList)
            {
                if (data.sceneAsset != null)
                {
                    data.sceneName = data.sceneAsset.name;
                }
                else
                {
                    data.sceneName = string.Empty;
                }
            }
        }
#endif
    }
}