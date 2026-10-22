using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexitEngine
{
    public class LocalPoolSerivce : LocalSingleton<LocalPoolSerivce>
    {
        private readonly Dictionary<int, LocalObjectPool> pools = new();

        /// <summary> 풀 키(해시) → 프리팹 이름. 해시 충돌 검사용 </summary>
        private readonly Dictionary<int, string> poolNames = new();
        
        //private readonly Dictionary<int, List<int>> scenePoolKeys = new();
        private readonly Dictionary<SceneHandle, List<int>> scenePoolKeys = new();

        #region Lifecycle

        /// <summary>
        /// 전역 풀을 생성하고, 씬 로드/언로드 이벤트를 구독한 뒤, 이미 로드된 씬의 풀을 생성합니다.
        /// 기다릴 작업이 없으므로 즉시 완료됩니다.
        /// </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            var config = LocalPoolConfig.Instance;

            if (config == null)
            {
                Dev.LogWarn("[LocalPool] LocalPoolConfig가 없어 풀을 생성하지 않습니다.");
                return UniTask.CompletedTask;
            }

            // 1. 전역 풀 생성 (인스턴스는 이 서비스 아래 → 씬 전환에도 유지)
            if (config.GlobalPrefabList != null)
            {
                foreach (var item in config.GlobalPrefabList) TryCreatePool(item, transform, out _);
            }

            // 2. 씬 이벤트 구독
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;

            // 3. 구독 전에 이미 로드되어 있던 씬 처리 (부트스트랩이 씬 이동 없이 머무는 경우 등)
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) HandleSceneLoaded(scene, LoadSceneMode.Additive);
            }

            // 4. 메서드 종료 → 베이스가 IsInitialized = true 설정
            return UniTask.CompletedTask;
        }

        protected override void OnDestroyed()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;

            foreach (var pool in pools.Values) pool.Clear();

            pools.Clear();
            poolNames.Clear();
            scenePoolKeys.Clear();
        }

        #endregion

        #region Scene Pools

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var config = LocalPoolConfig.Instance;
            if (config == null || config.ScenePoolList == null) return;

            // 이미 처리한 씬이면 무시
            if (scenePoolKeys.ContainsKey(scene.handle)) return;

            var sceneData = config.ScenePoolList.Find(x => x.sceneName == scene.name);
            if (sceneData == null || sceneData.prefabList == null) return;

            // 씬 안에 컨테이너를 만들어 인스턴스가 씬과 함께 사라지도록 함
            var container = new GameObject($"[Pool] {scene.name}");
            SceneManager.MoveGameObjectToScene(container, scene);

            var ownedKeys = new List<int>();

            foreach (var item in sceneData.prefabList)
            {
                if (TryCreatePool(item, container.transform, out int key)) ownedKeys.Add(key);
            }

            if (ownedKeys.Count == 0)
            {
                Destroy(container);
                return;
            }

            scenePoolKeys[scene.handle] = ownedKeys;
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            // 언로드된 씬이 소유한 풀만 정리
            if (!scenePoolKeys.Remove(scene.handle, out var ownedKeys)) return;

            foreach (var key in ownedKeys)
            {
                if (pools.Remove(key, out var pool)) pool.Clear();
                poolNames.Remove(key);
            }
        }

        #endregion

        #region Rental / Return

        /// <summary> 풀에서 오브젝트를 대여합니다. poolKey는 프리팹 이름입니다. </summary>
        public T Rental<T>(string poolKey, Vector3 position = default, Quaternion rotation = default) where T : class, ILocalPoolable
        {
            if (!IsInitialized)
            {
                Dev.LogWarn($"[LocalPool] 초기화 전 대여 요청 무시: '{poolKey}'");
                return null;
            }

            if (string.IsNullOrEmpty(poolKey))
            {
                Dev.LogError("[LocalPool] poolKey가 비어 있습니다.");
                return null;
            }

            int key = HashUtility.GetHashCode(poolKey);

            if (!pools.TryGetValue(key, out var pool))
            {
                Dev.LogError($"[LocalPool] 풀을 찾을 수 없습니다: '{poolKey}' (Config 등록 또는 현재 씬 확인)");
                return null;
            }

            var poolable = pool.Get(position, rotation);
            if (poolable == null) return null;

            if (poolable is not T result)
            {
                Dev.LogError($"[LocalPool] 타입 불일치: '{poolKey}'는 {typeof(T).Name}가 아닙니다.");
                pool.Return(poolable);
                return null;
            }

            return result;
        }

        /// <summary> 대여한 오브젝트를 반납합니다. 소속 풀이 이미 사라졌다면 오브젝트를 파괴합니다. </summary>
        public void Return(ILocalPoolable poolable)
        {
            if (poolable is not Component comp || comp == null) return;

            if (pools.TryGetValue(poolable.PoolKey, out var pool))
            {
                pool.Return(poolable);
                return;
            }

            // 씬 풀이 정리된 뒤 늦게 반납된 경우 등
            Destroy(comp.gameObject);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 풀을 생성합니다. 같은 이름의 풀이 이미 있으면 생성하지 않고, 이름이 다른데 해시가 겹치면 에러를 남깁니다.
        /// </summary>
        /// <returns> 새로 생성했으면 true </returns>
        private bool TryCreatePool(PoolItem item, Transform parent, out int key)
        {
            key = 0;

            if (item == null || item.prefab == null)
            {
                Dev.LogWarn("[LocalPool] Config에 비어 있는 항목이 있습니다.");
                return false;
            }

            string poolName = item.prefab.name;
            key = HashUtility.GetHashCode(poolName);

            if (poolNames.TryGetValue(key, out var existingName))
            {
                if (existingName != poolName)
                {
                    Dev.LogError($"[LocalPool] 해시 충돌: '{poolName}'와 '{existingName}'의 키가 겹칩니다. 프리팹 이름을 변경하세요.");
                }

                // 같은 이름이면 이미 생성된 풀을 그대로 사용
                return false;
            }

            pools[key] = new LocalObjectPool(item.prefab, item.prewarm, key, parent);
            poolNames[key] = poolName;

            return true;
        }

        #endregion
    }
}