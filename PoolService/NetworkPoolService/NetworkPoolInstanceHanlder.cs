using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    /// <summary> 클라이언트 전용 동작 </summary>
    public class NetworkPooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
    {
        private readonly NetworkObjectPool m_Pool;

        public NetworkPooledPrefabInstanceHandler(NetworkObjectPool pool) { m_Pool = pool; }

        // 서버가 Spawn 또는 NetworkShow 호출 시 동기화 받는 클라이언트에서 실행됩니다.
        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            var poolable = m_Pool.Get(ownerClientId, position, rotation);

            return poolable.NetObject;
        }

        // 서버가 Despawn 또는 NetworkHide 호출 시 동기화 받는 클라이언트에서 실행됩니다.
        public void Destroy(NetworkObject networkObject)
        {
            var poolable = networkObject.GetComponent<INetworkPoolable>();

            // OnReturn
            m_Pool.Return(poolable);
        }
    }
}