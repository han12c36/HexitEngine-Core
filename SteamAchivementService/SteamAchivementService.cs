using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    public class SteamAchievementService : LocalSingleton<SteamAchievementService>
    {
        /// <summary> 업적을 해제합니다. 이미 해제된 업적이면 아무것도 하지 않습니다. </summary>
        public void UnlockAchievement(string achievementId)
        {
            if (!CanUse(achievementId)) return;

            var achievement = new Achievement(achievementId);

            if (achievement.State) return;

            // Trigger는 해제와 동시에 Steam 서버에 저장까지 요청합니다.
            if (!achievement.Trigger())
            {
                Dev.LogWarn($"[Achievement] 해제 실패: '{achievementId}' (Steamworks에 등록된 API Name인지 확인)");
            }
        }

        /// <summary> 업적 해제 여부. Steam을 사용할 수 없으면 false. </summary>
        public bool IsAchievementUnlocked(string achievementId)
        {
            if (!CanUse(achievementId)) return false;

            return new Achievement(achievementId).State;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> [개발용] 업적을 다시 잠급니다. 출시 빌드에는 포함되지 않습니다. </summary>
        public void ClearAchievement(string achievementId)
        {
            if (!CanUse(achievementId)) return;

            new Achievement(achievementId).Clear();
        }
#endif

        private static bool CanUse(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId))
            {
                Dev.LogWarn("[Achievement] 업적 ID가 비어 있습니다.");
                return false;
            }

            return SteamClient.IsValid;
        }
    }
}