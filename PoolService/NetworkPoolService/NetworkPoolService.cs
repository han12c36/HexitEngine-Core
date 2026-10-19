using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;

namespace HexitEngine
{
    public class NetworkPoolService : ServerSingleton<NetworkPoolService>
    {
        private readonly Dictionary<int, NetworkObjectPool> pools = new();

        /// <summary> 대여 장부: 클라이언트 → 대여 중인 오브젝트들 </summary>
        private readonly Dictionary<ulong, HashSet<INetworkPoolable>> rentedByClient = new();

        /// <summary> 역방향 장부: 오브젝트 → 대여한 클라이언트 (반납 시 O(1) 조회) </summary>
        private readonly Dictionary<INetworkPoolable, ulong> rentedOwner = new();

        private bool isDisconnectBound;

        #region Lifecycle

        /// <summary>
        /// Config 존재만 확인합니다. 풀 생성은 네트워크가 필요하므로 OnNetSpawn에서 합니다.
        /// 기다릴 작업이 없으므로 즉시 완료됩니다.
        /// </summary>
        protected override UniTask OnInitializeAsync(CancellationToken ct)
        {
            if (NetworkPoolConfig.Instance == null)
            {
                Dev.LogWarn("[NetworkPool] NetworkPoolConfig가 없어 네트워크 풀을 사용하지 않습니다.");
            }

            return UniTask.CompletedTask;
        }

        /// <summary> 세션 시작: 풀 생성, 서버라면 클라이언트 이탈 감지 시작 </summary>
        protected override void OnNetSpawn()
        {
            CreatePools();

            if (IsServer) BindDisconnect();
        }

        /// <summary> 세션 종료: 장부와 풀을 모두 정리 </summary>
        protected override void OnNetDespawn()
        {
            UnbindDisconnect();

            rentedByClient.Clear();
            rentedOwner.Clear();

            foreach (var pool in pools.Values) pool.Clear();
            pools.Clear();
        }

        protected override void OnDestroyed()
        {
            UnbindDisconnect();
        }

        #endregion

        #region Rental / Return

        /// <summary>
        /// [서버] 풀에서 오브젝트를 대여합니다.
        /// ownerClientId를 지정하면 해당 클라이언트에게 보이게 하고 소유권을 넘긴 뒤 장부에 기록합니다.
        /// 지정하지 않으면 서버 소유로 대여합니다.
        /// </summary>
        public T Rental<T>(string poolKey, Vector3 position = default, Quaternion rotation = default, ulong? ownerClientId = null) where T : class, INetworkPoolable
        {
            if (!IsServer) { Dev.LogError("[NetworkPool] Rental은 서버에서만 호출할 수 있습니다."); return null; }
            if (!IsSpawned) { Dev.LogError("[NetworkPool] 세션이 시작되지 않았습니다."); return null; }
            if (string.IsNullOrEmpty(poolKey)) { Dev.LogError("[NetworkPool] poolKey가 비어 있습니다."); return null; }

            int key = HashUtility.GetHashCode(poolKey);

            if (!pools.TryGetValue(key, out var pool))
            {
                Dev.LogError($"[NetworkPool] '{poolKey}' 풀을 찾을 수 없습니다. (Config 등록 확인)");
                return null;
            }

            ulong owner = ownerClientId ?? NetworkManager.ServerClientId;

            var poolable = pool.Get(owner, position, rotation);
            if (poolable == null) return null;

            var netObj = poolable.NetObject;

            if (netObj == null || !netObj.IsSpawned)
            {
                Dev.LogError($"[NetworkPool] '{poolKey}'가 스폰되지 않았거나 NetworkObject가 없습니다.");
                pool.Return(poolable);
                return null;
            }

            if (ownerClientId.HasValue)
            {
                // 소유권 이전 중임을 표시 (OnGainedOwnership 등에서 참고)
                poolable.PendingOwnerId = owner;

                if (owner != NetworkManager.ServerClientId) netObj.NetworkShow(owner);
                if (netObj.OwnerClientId != owner) netObj.ChangeOwnership(owner);

                poolable.PendingOwnerId = null;

                RegisterRental(owner, poolable);
            }
            else if (netObj.OwnerClientId != NetworkManager.ServerClientId)
            {
                netObj.RemoveOwnership();
            }

            if (poolable is not T result)
            {
                Dev.LogError($"[NetworkPool] 타입 불일치: '{poolKey}'는 {typeof(T).Name}가 아닙니다.");
                Return(poolable);
                return null;
            }

            return result;
        }

        /// <summary> [서버] 대여한 오브젝트를 반납합니다. </summary>
        public void Return(INetworkPoolable poolable)
        {
            if (!IsServer) { Dev.LogError("[NetworkPool] Return은 서버에서만 호출할 수 있습니다."); return; }

            if (poolable is not Object unityObject || unityObject == null)
            {
                Dev.LogWarn("[NetworkPool] 반납하려는 객체가 없거나 이미 파괴되었습니다.");
                return;
            }

            // 1. 장부 말소
            UnregisterRental(poolable);

            // 2. 풀 반납
            ReturnToPool(poolable, excludeClientId: null);
        }

        /// <summary>
        /// 모든 클라이언트에게서 숨기고 소유권을 서버로 되돌린 뒤 풀에 넣습니다.
        /// excludeClientId: 이탈 중인 클라이언트는 숨김 처리 대상에서 제외합니다.
        /// </summary>
        private void ReturnToPool(INetworkPoolable poolable, ulong? excludeClientId)
        {
            var netObj = poolable.NetObject;
            if (netObj == null || !netObj.IsSpawned) return;

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId) continue;
                if (excludeClientId.HasValue && clientId == excludeClientId.Value) continue;

                if (netObj.IsNetworkVisibleTo(clientId)) netObj.NetworkHide(clientId);
            }

            if (!netObj.IsOwnedByServer) netObj.RemoveOwnership();

            poolable.PendingOwnerId = null;

            if (pools.TryGetValue(poolable.PoolKey, out var pool)) pool.Return(poolable);
        }

        #endregion

        #region Client Disconnect

        private void BindDisconnect()
        {
            if (isDisconnectBound || NetworkManager == null) return;

            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
            isDisconnectBound = true;
        }

        private void UnbindDisconnect()
        {
            if (!isDisconnectBound) return;

            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
            isDisconnectBound = false;
        }

        /// <summary> [서버] 나간 클라이언트가 대여 중이던 오브젝트를 모두 반납합니다. </summary>
        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            var items = UnregisterAllRentals(clientId);

            foreach (var item in items)
            {
                if (item is Object unityObject && unityObject != null) ReturnToPool(item, excludeClientId: clientId);
            }
        }

        #endregion

        #region Ledger

        private void RegisterRental(ulong clientId, INetworkPoolable poolable)
        {
            if (!rentedByClient.TryGetValue(clientId, out var items))
            {
                items = new HashSet<INetworkPoolable>();
                rentedByClient[clientId] = items;
            }

            items.Add(poolable);
            rentedOwner[poolable] = clientId;
        }

        private void UnregisterRental(INetworkPoolable poolable)
        {
            if (!rentedOwner.Remove(poolable, out ulong ownerId)) return;

            if (rentedByClient.TryGetValue(ownerId, out var items)) items.Remove(poolable);
        }

        /// <summary> 해당 클라이언트의 장부를 통째로 말소하고, 대여 중이던 목록을 반환합니다. </summary>
        private List<INetworkPoolable> UnregisterAllRentals(ulong clientId)
        {
            var result = new List<INetworkPoolable>();

            if (!rentedByClient.Remove(clientId, out var items)) return result;

            result.AddRange(items);
            foreach (var item in result) rentedOwner.Remove(item);

            return result;
        }

        #endregion

        #region Pools

        /// <summary> Config의 프리팹마다 풀을 생성합니다. 서버는 프리웜과 동시에 Spawn 합니다. </summary>
        private void CreatePools()
        {
            var config = NetworkPoolConfig.Instance;
            if (config == null) return;

            if (config.PrefabList == null)
            {
                Dev.LogError("[NetworkPool] Config의 프리팹 리스트가 할당되지 않았습니다.");
                return;
            }

            var registeredNames = new Dictionary<int, string>();

            foreach (var item in config.PrefabList)
            {
                if (item == null || item.prefab == null) { Dev.LogWarn("[NetworkPool] Config에 비어 있는 항목이 있습니다."); continue; }
                if (item.prefab.GetComponent<INetworkPoolable>() == null) { Dev.LogError($"[NetworkPool] '{item.prefab.name}'는 INetworkPoolable이 아닙니다."); continue; }

                string poolName = item.prefab.name;
                int key = HashUtility.GetHashCode(poolName);

                if (registeredNames.TryGetValue(key, out var existingName))
                {
                    if (existingName == poolName) Dev.LogWarn($"[NetworkPool] '{poolName}'가 중복 등록되어 있습니다.");
                    else Dev.LogError($"[NetworkPool] 해시 충돌: '{poolName}'와 '{existingName}'의 키가 겹칩니다. 프리팹 이름을 변경하세요.");
                    continue;
                }

                registeredNames[key] = poolName;
                pools[key] = new NetworkObjectPool(item.prefab, item.prewarm, key);
            }
        }

        #endregion
    }
}