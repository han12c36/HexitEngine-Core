using System;
using UnityEngine;
using static HexitEngine.MultiplayerService;

namespace HexitEngine.Sample
{
    public class ReadyMessageUI : SessionMessageUI
    {
        [Header("Title Keys (System_Table)")]
        [SerializeField] private string disconnectedTitleKey = "SYS_Title_Disconnected";
        [SerializeField] private string kickedTitleKey = "SYS_Title_Kicked";

        protected override void BindEvents(HEXITEngine hexit)
        {
            hexit.OnDisconnected += HandleDisconnected;
            hexit.OnKickedFromLobby += HandleKicked;
        }

        protected override void UnbindEvents(HEXITEngine hexit)
        {
            hexit.OnDisconnected -= HandleDisconnected;
            hexit.OnKickedFromLobby -= HandleKicked;
        }

        private void HandleDisconnected(object sender, OnDisconnectedEventArgs e) => ShowFatal(disconnectedTitleKey, e.reason);

        private void HandleKicked(object sender, EventArgs e) => ShowFatal(kickedTitleKey, string.Empty);
    }
}