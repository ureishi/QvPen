using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace QvPen.Udon
{
    public class PenManager : UdonSharpBehaviour
    {
        [SerializeField] private Pen pen;

        [SerializeField] private GameObject respawnButton;
        [SerializeField] private GameObject clearButton;
        [SerializeField] private GameObject inUseUI;
        [SerializeField] private Text textInUse;

        [UdonSynced] private bool isInUse;

        private string displayName;

        private void Start()
        {
            if (Networking.LocalPlayer != null)
            {
                displayName = Networking.LocalPlayer.displayName;
            }

            pen.Init(this);
        }

        public void StartUsing()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            respawnButton.SetActive(false);
            clearButton.SetActive(false);
            inUseUI.SetActive(true);
            textInUse.text = displayName;
        }

        public void EndUsing()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            respawnButton.SetActive(true);
            clearButton.SetActive(true);
            inUseUI.SetActive(false);
            textInUse.text = "Occupied";
        }

        public override void OnPreSerialization()
        {
            isInUse = inUseUI.activeSelf;
        }

        public override void OnDeserialization()
        {
            var owner = Networking.GetOwner(pen.gameObject);
            respawnButton.SetActive(!isInUse);
            clearButton.SetActive(!isInUse);
            inUseUI.SetActive(isInUse);
            textInUse.text = owner != null ? owner.displayName : "Occupied";
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
