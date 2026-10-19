using Unity.Netcode;
using UnityEngine;

namespace HexitEngine
{
    public interface IPoolable
    {
        /// <summary> 최초 생성 시. (프리웜 포함) </summary>
        void OnCreate();

        /// <summary> 반납 시. </summary>
        void OnReturn();

        /// <summary> 소속 풀의 키 (프리팹 이름의 해시). 풀이 생성 시 기록하며, 반납 시 소속 풀을 찾는 데 사용. </summary>
        int PoolKey { get; set; }

        GameObject GameObject { get; }
        Transform Transform { get; }
    }

    public interface ILocalPoolable : IPoolable
    {
        void OnRental(Vector3 position, Quaternion rotation);
    }

    public interface INetworkPoolable : IPoolable
    {
        NetworkObject NetObject { get; }

        /// <summary> 소유권 이전 중인 대상 클라이언트. 이전이 끝나면 null로 돌아갑니다. </summary>
        ulong? PendingOwnerId { get; set; }

        /// <summary> [서버] 풀에서 대여된 직후 호출됩니다. </summary>
        void OnRental(ulong ownerClientId, Vector3 position, Quaternion rotation);

        /// <summary> 해당 클라이언트에게 보여야 하는지 판정합니다. (NetworkObject.CheckObjectVisibility에 연결) </summary>
        bool EvaluateVisibility(ulong clientId);
    }
}