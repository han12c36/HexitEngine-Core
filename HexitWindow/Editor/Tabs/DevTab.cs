using UnityEngine;
using UnityEditor;

namespace HexitEngine
{
    public class DevTab : HexitEngineTab<DevLogConfig>
    {
        public DevTab()
        {
            TabName = "DevLog";
            HeaderTitle = "DevLog Configuration";
            Description = "DevLog는 프로젝트의 시스템 로그 및 커스텀 로깅 색상을 관리하는 중앙 설정 모듈입니다.";
        }

        // 만약 하단에 코드 영역 자체를 안 그리고 싶다면 아래 주석을 풀면 됩니다.
        // public override bool ShowCodeArea => false;

        public override void DrawContent()
        {
            DrawConfigSettings("DevLog Config Asset");

            GUILayout.Space(5);

            DrawConfigProperties("DevLog Color");
        }

        // 하단 코드 영역의 동작이나 UI를 통째로 바꾸고 싶다면 이렇게 오버라이드 하면 됩니다.
        /*
        public override void DrawCodeArea(EditorWindow window)
        {
            EditorGUILayout.BeginVertical(GUILayout.Height(100f));
            GUILayout.Label("Debug 전용 커스텀 하단 영역입니다!", EditorStyles.boldLabel);
            if(GUILayout.Button("모든 로그 지우기")) { Debug.ClearDeveloperConsole(); }
            EditorGUILayout.EndVertical();
        }
        */

        public override string GetCodeSnippet()
        {
            return @"using UnityEngine;
using H_Framework;

public class ExampleUsage : MonoBehaviour 
{
    void Start() 
    {
        // 커스텀 디버그 시스템 사용 예시
        H_Debug.Log(""프레임워크가 정상적으로 초기화되었습니다."", LogType.Success);
    }
}";
        }
    }
}