using UnityEngine;

namespace HexitEngine 
{
    public static class AppState
    {
        /// <summary> 앱이 종료 중인지 여부 </summary>
        public static bool IsQuitting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            IsQuitting = false;
            Application.quitting -= HandleQuitting;
            Application.quitting += HandleQuitting;
        }

        private static void HandleQuitting() => IsQuitting = true;
    }
}