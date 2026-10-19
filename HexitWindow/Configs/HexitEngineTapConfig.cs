using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

namespace HexitEngine
{
    // ScriptableObject를 상속받으며, 제네릭 싱글톤 패턴과 Preloaded 자동 등록을 지원하는 부모 클래스
    public abstract class HexitEngineTapConfig<T> : ScriptableObject where T : HexitEngineTapConfig<T>
    {
        private static T _instance;

        // 런타임 & 에디터 어디서든 H_DebugConfig.Instance 로 즉시 접근 가능
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    // PreloadedAssets에 등록되어 있다면 런타임에 Resources.FindObjectsOfTypeAll로 즉시 찾을 수 있습니다.
                    var assets = Resources.FindObjectsOfTypeAll<T>();
                    if (assets.Length > 0) _instance = assets[0];
                }
                return _instance;
            }
        }

#if UNITY_EDITOR
        protected virtual void OnEnable()
        {
            // 에셋이 로드되거나 생성될 때, 안전하게 한 템포 쉬고 PreloadedAssets에 자동 등록합니다.
            EditorApplication.delayCall += CheckAndRegister;
        }

        protected virtual void OnValidate()
        {
            // 에디터에서 값을 수정할 때 SaveAssets()를 호출하면 프리징이 걸리므로, 
            // 유니티 엔진에 "값이 변했으니 나중에 저장해 줘"라고 더티(Dirty) 플래그만 안전하게 남깁니다.
            EditorUtility.SetDirty(this);
        }

        private void CheckAndRegister()
        {
            if (this == null) return;

            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            if (!preloaded.Contains(this))
            {
                // 동일한 타입의 다른 Config가 등록되어 있다면 꼬이지 않게 청소합니다 (진정한 싱글톤 보장)
                preloaded.RemoveAll(x => x is T);
                preloaded.Add(this);
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            }
        }
#endif
    }
}