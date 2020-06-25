using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace QvPen.Udon
{
    public class PenManager : UdonSharpBehaviour
    {
        [SerializeField] private Pen pen;

        [SerializeField] private Text textInUse;
        [SerializeField] private GameObject respawnButton;
        [SerializeField] private GameObject clearButton;

        [UdonSynced] private bool isInUse;
        [UdonSynced] private string userName;

        private string displayName = "Occupied";

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

            textInUse.text = displayName;
            textInUse.gameObject.SetActive(true);
            respawnButton.SetActive(false);
            clearButton.SetActive(false);
        }

        public void EndUsing()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            textInUse.text = "";
            textInUse.gameObject.SetActive(false);
            respawnButton.SetActive(true);
            clearButton.SetActive(true);
        }

        public override void OnPreSerialization()
        {
            userName = textInUse.text;
            isInUse = textInUse.gameObject.activeSelf;
        }

        public override void OnDeserialization()
        {
            textInUse.text = userName;
            textInUse.gameObject.SetActive(isInUse);
            respawnButton.SetActive(!isInUse);
            clearButton.SetActive(!isInUse);
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
