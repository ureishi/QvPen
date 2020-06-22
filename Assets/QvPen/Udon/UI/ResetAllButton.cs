using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon.UI
{
    public class ResetAllButton : UdonSharpBehaviour
    {
        [SerializeField] private Text message;
        [SerializeField] private PenManager[] penManagers;
        [SerializeField] private EraserManager[] eraserManagers;
        
        private VRCPlayerApi master;

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            OnPlayerListChanged(player);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            OnPlayerListChanged(player);
        }

        private void OnPlayerListChanged(VRCPlayerApi player)
        {
            if (player.isMaster)
            {
                master = player;
            }

            var masterName = master == null ? "master" : master.displayName;
            message.text =
                $"<size=18>Reset All</size>\n\n" +
                $"<size=8>[Only {masterName} can do]</size>\n\n" +
                $"<size=14>(Global)</size>";
        }

        public override void Interact()
        {
            if (!Networking.IsMaster) return;
            
            foreach (var penManager in penManagers)
            {
                penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.ResetAll));
            }
            foreach (var eraserManager in eraserManagers)
            {
                eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.ResetAll));
            }
        }
    }
}
