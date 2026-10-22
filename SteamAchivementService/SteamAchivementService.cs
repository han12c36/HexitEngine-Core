using Steamworks;
using Steamworks.Data;

namespace HexitEngine
{
    public class SteamAchievementService : LocalSingleton<SteamAchievementService>
    {
        /// <summary> ������ �����մϴ�. �̹� ������ �����̸� �ƹ��͵� ���� �ʽ��ϴ�. </summary>
        public void UnlockAchievement(string achievementId)
        {
            if (!CanUse(achievementId)) return;

            var achievement = new Achievement(achievementId);

            if (achievement.State) return;

            // Trigger�� ������ ���ÿ� Steam ������ ������� ��û�մϴ�.
            if (!achievement.Trigger())
            {
                Dev.LogWarn($"[Achievement] ���� ����: '{achievementId}' (Steamworks�� ��ϵ� API Name���� Ȯ��)");
            }
        }

        /// <summary> ���� ���� ����. Steam�� ����� �� ������ false. </summary>
        public bool IsAchievementUnlocked(string achievementId)
        {
            if (!CanUse(achievementId)) return false;

            return new Achievement(achievementId).State;
        }
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
//#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> [���߿�] ������ �ٽ� ��޴ϴ�. ��� ���忡�� ���Ե��� �ʽ��ϴ�. </summary>
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
                Dev.LogWarn("[Achievement] ���� ID�� ��� �ֽ��ϴ�.");
                return false;
            }

            return SteamClient.IsValid;
        }
    }
}