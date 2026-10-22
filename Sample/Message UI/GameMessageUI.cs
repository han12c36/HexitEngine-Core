using UnityEngine;
using static HexitEngine.MultiplayerService;

namespace HexitEngine.Sample
{
    public class GameMessageUI : SessionMessageUI
    {
        [Header("Title Keys (System_Table)")]
        [SerializeField] private string disconnectedTitleKey = "SYS_Title_Disconnected";

        protected override void BindEvents(HEXITEngine hexit)
        {
            hexit.OnDisconnected += HandleDisconnected;
        }

        protected override void UnbindEvents(HEXITEngine hexit)
        {
            hexit.OnDisconnected -= HandleDisconnected;
        }

        private void HandleDisconnected(object sender, OnDisconnectedEventArgs e) => ShowFatal(disconnectedTitleKey, e.reason);
    }
}