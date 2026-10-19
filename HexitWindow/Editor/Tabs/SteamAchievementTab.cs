using UnityEngine;
using UnityEditor;
using Steamworks; // Facepunch.Steamworks

namespace HexitEngine
{
    public class SteamAchievementTab : HexitEngineTabBase
    {
        // 베이스 클래스의 가상 프로퍼티 오버라이드
        public override bool ShowCodeArea => false;

        public SteamAchievementTab()
        {
            // 베이스 클래스의 protected set 프로퍼티 할당
            // (할당 시 베이스 클래스에서 TabNameContent를 자동 생성함)
            TabName = "Steam Achievements";
            HeaderTitle = "Steam 업적 관리 (Facepunch)";
            Description = "주의: 스팀 클라이언트가 실행 중이고 앱이 초기화(SteamClient.IsValid)된 에디터 Play 모드에서만 작동합니다.";
        }

        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnDisable()
        {
            // 사용하지 않더라도 추상 메서드이므로 구현 필요
        }

        public override void DrawContent()
        {
            GUILayout.Space(10);

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("도전과제 및 통계 전체 초기화 (Reset All)", GUILayout.Height(40)))
            {
                ResetAllAchievements();
            }
            GUI.backgroundColor = Color.white;
        }

        private void ResetAllAchievements()
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[Steamworks] SteamClient가 유효하지 않습니다. 게임이 실행 중인지 확인하세요.");
                return;
            }

            // true 전달 시 통계와 도전과제를 모두 초기화
            SteamUserStats.ResetAll(true);
            SteamUserStats.StoreStats();

            Debug.Log("<b>[Steamworks]</b> 모든 도전과제 및 통계가 초기화되었습니다.");
        }

        public override string GetCodeSnippet()
        {
            // ShowCodeArea가 false이므로 빈 문자열 반환 (추상 메서드 구현)
            return string.Empty;
        }
    }
}