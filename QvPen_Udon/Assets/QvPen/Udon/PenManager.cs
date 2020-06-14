using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace ureishi.Udon.QvPen
{
    public class PenManager : UdonSharpBehaviour
    {
        [SerializeField] private GameObject respawnButton;
        [SerializeField] private GameObject clearButton;
        [SerializeField] private GameObject inUseButton;
        [SerializeField] private Text textInUse;

        [UdonSynced] private bool isInUse = false;
        [UdonSynced] private string userName = "";

        string displayName;

        private void Start()
        {
            if (Networking.LocalPlayer != null)
                displayName = Networking.LocalPlayer.displayName;
        }
        public void StartUsing()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            textInUse.text = displayName;
            respawnButton.SetActive(false);
            clearButton.SetActive(false);
            inUseButton.SetActive(true);
        }
        public void EndUsing()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            textInUse.text = "";
            respawnButton.SetActive(true);
            clearButton.SetActive(true);
            inUseButton.SetActive(false);
        }
        public override void OnPreSerialization()
        {
            userName = textInUse.text;
            isInUse = inUseButton.activeSelf;
        }

        public override void OnDeserialization()
        {
            textInUse.text = userName;
            respawnButton.SetActive(!isInUse);
            clearButton.SetActive(!isInUse);
            inUseButton.SetActive(isInUse);
        }
    }
}