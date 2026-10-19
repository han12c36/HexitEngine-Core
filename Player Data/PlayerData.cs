using System;
using Unity.Collections;
using Unity.Netcode;

namespace HexitEngine
{
    public struct PlayerData : IEquatable<PlayerData>, INetworkSerializable
    {
        public static PlayerData EmptyData
        {
            get
            {
                var playerData = default(PlayerData);
                playerData.Clear();
                return playerData;
            }
        }

        /// <summary> NGO 세션 ID (재접속하면 바뀜) </summary>
        public ulong clientId;

        /// <summary> 슬롯 번호 (세션 동안 고정, 게임 로직용). 로비 대기 중에는 나간 사람의 번호가 재사용될 수 있습니다. </summary>
        public int playerId;

        /// <summary> 영구 식별자 (매칭, 음소거, 강퇴의 기준) </summary>
        public ulong steamId;

        /// <summary> Steam 닉네임 </summary>
        public FixedString64Bytes steamName;

        /// <summary> 인게임 이름 (비어 있으면 steamName 사용) </summary>
        public FixedString64Bytes displayName;

        /// <summary> 연결 여부. 게임 중 이탈하면 슬롯은 유지한 채 false가 됩니다. </summary>
        public bool isConnected;

        public string AuthId => steamId.ToString();
        public string PlayerName => displayName.IsEmpty ? steamName.ToString() : displayName.ToString();

        public PlayerData(ulong clientId, int playerId, ulong steamId, string steamName, string displayName, bool isConnected)
        {
            this.clientId = clientId;
            this.playerId = playerId;
            this.steamId = steamId;
            this.steamName = ToFixedName(steamName);
            this.displayName = ToFixedName(displayName);
            this.isConnected = isConnected;
        }

        public static FixedString64Bytes ToFixedName(string value)
        {
            var result = new FixedString64Bytes();
            if (!string.IsNullOrEmpty(value)) result.CopyFromTruncated(value);
            return result;
        }

        public bool IsEmpty() => clientId == ulong.MaxValue;

        public void Clear()
        {
            clientId = ulong.MaxValue;
            playerId = -1;
            steamId = 0;
            steamName = default;
            displayName = default;
            isConnected = false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref playerId);
            serializer.SerializeValue(ref steamId);
            serializer.SerializeValue(ref steamName);
            serializer.SerializeValue(ref displayName);
            serializer.SerializeValue(ref isConnected);
        }

        public bool Equals(PlayerData other)
        {
            return
                clientId == other.clientId &&
                playerId == other.playerId &&
                steamId == other.steamId &&
                steamName == other.steamName &&
                displayName == other.displayName &&
                isConnected == other.isConnected;
        }

        public override bool Equals(object obj) => obj is PlayerData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(clientId, playerId, steamId, isConnected);
    }
}