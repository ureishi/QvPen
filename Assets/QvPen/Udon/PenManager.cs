using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class PenManager : UdonSharpBehaviour
    {
        [SerializeField] private Pen pen;

        [SerializeField] private GameObject respawnButton;
        [SerializeField] private GameObject clearButton;
        [SerializeField] private GameObject inUseUI;
        [SerializeField] private Text textInUse;

        private VRCPlayerApi localPlayer;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;

            pen.Init(this);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!localPlayer.IsOwner(pen.gameObject)) return;

            if (pen.IsHeld())
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartUsing));
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!localPlayer.IsOwner(pen.gameObject)) return;

            if (!pen.IsHeld())
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(EndUsing));
            }
        }

        public void StartUsing()
        {
            respawnButton.SetActive(false);
            clearButton.SetActive(false);
            inUseUI.SetActive(true);

            var owner = Networking.GetOwner(pen.gameObject);
            textInUse.text = owner != null ? owner.displayName : "Occupied";
        }

        public void EndUsing()
        {
            respawnButton.SetActive(true);
            clearButton.SetActive(true);
            inUseUI.SetActive(false);

            textInUse.text = "";
        }

        public void ResetAll()
        {
            pen.Respawn();
            pen.Clear();
        }

        public void ClearAll()
        {
            pen.Clear();
        }

        public void SetUseDoubleClick(bool value)
        {
            pen.SetUseDoubleClick(value);
        }
    }
}
