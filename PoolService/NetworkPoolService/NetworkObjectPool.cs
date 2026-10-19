using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    public class NetworkObjectPool : BaseObjectPool<INetworkPoolable>
    {
        private readonly NetworkManager networkManager;

        public NetworkObjectPool(GameObject prefab, int prewarm, int poolKey) : base(prefab, poolKey)
        {
            networkManager = NetworkManager.Singleton;

            if (networkManager != null)
            {
                bool isSuccess = networkManager.PrefabHandler.AddHandler(this.prefab, new NetworkPooledPrefabInstanceHandler(this));
                if (!isSuccess) Dev.LogError($"[NetworkPool] '{this.prefab.name}'의 프리팹 핸들러 등록이 거부되었습니다.");
            }
            else Dev.LogError($"[NetworkPool] NetworkManager가 없습니다: {this.prefab.name}");

            InitializePrewarm(prewarm);
        }

        /// <summary> 인스턴스를 생성합니다. 서버라면 즉시 Spawn 합니다. </summary>
        protected override INetworkPoolable CreateNewInstance()
        {
            var poolable = CreateBaseInstance();
            if (poolable == null) return null;

            if (networkManager != null && networkManager.IsServer && networkManager.IsListening)
            {
                var netObj = poolable.GameObject.GetComponent<NetworkObject>();

                if (netObj != null && !netObj.IsSpawned)
                {
                    netObj.DontDestroyWithOwner = true;
                    netObj.CheckObjectVisibility = poolable.EvaluateVisibility;
                    netObj.Spawn();
                }
            }

            poolable.OnCreate();

            return poolable;
        }

        /// <summary> [서버] 대기열에서 꺼내거나 새로 만들어 대여 준비를 합니다. 외부에서 파괴된 인스턴스는 건너뜁니다. </summary>
        public INetworkPoolable Get(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            var poolable = DequeueAlive() ?? CreateNewInstance();
            if (poolable == null) return null;

            if (poolable.NetObject.IsOwner) poolable.Transform.SetPositionAndRotation(position, rotation);

            poolable.OnRental(ownerClientId, position, rotation);

            return poolable;
        }

        /// <summary> [클라이언트] 서버의 스폰 메시지에 연결할 로컬 인스턴스를 꺼냅니다. (PrefabHandler에서 호출) </summary>
        public INetworkPoolable ClientFetch()
        {
            return DequeueAlive() ?? CreateNewInstance();
        }

        protected override void HandleReturn(INetworkPoolable item)
        {
            if (item.NetObject.IsOwner) item.Transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary> 핸들러를 해제하고 모든 인스턴스를 정리합니다. 서버는 디스폰, 스폰되지 않은 인스턴스는 파괴합니다. </summary>
        public override void Clear()
        {
            if (networkManager == null) return;

            if (networkManager.PrefabHandler != null) networkManager.PrefabHandler.RemoveHandler(prefab);

            foreach (var item in allInstances)
            {
                if (!IsAlive(item)) continue;

                var netObj = item.NetObject;

                if (netObj != null && netObj.IsSpawned)
                {
                    if (networkManager.IsServer) netObj.Despawn(true);
                }
                else Object.Destroy(item.GameObject);
            }

            queue.Clear();
            allInstances.Clear();
        }
    }
}