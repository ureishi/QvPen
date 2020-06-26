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
        [SerializeField] private Transform pensParent;
        [SerializeField] private Transform erasersParent;

        private PenManager[] penManagers;
        private EraserManager[] eraserManagers;

        private VRCPlayerApi master;

        private void Start()
        {
            master = Networking.LocalPlayer;

            penManagers = new PenManager[pensParent.childCount];
            for (var i = 0; i < pensParent.childCount; i++)
            {
                penManagers[i] = pensParent.GetChild(i).GetComponent<PenManager>();
            }

            eraserManagers = new EraserManager[erasersParent.childCount];
            for (var i = 0; i < erasersParent.childCount; i++)
            {
                eraserManagers[i] = erasersParent.GetChild(i).GetComponent<EraserManager>();
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player.playerId <= master.playerId)
            {
                master = player;
                UpdateMessage();
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == master)
            {
                master = Networking.GetOwner(gameObject);
                UpdateMessage();
            }
        }

        private void UpdateMessage()
        {
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
                if (penManager) penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.ResetAll));
            }

            foreach (var eraserManager in eraserManagers)
            {
                if (eraserManager) eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(EraserManager.ResetAll));
            }
        }
    }
}
