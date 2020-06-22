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
        [SerializeField] private GameObject inUseButton;
        [SerializeField] private Text textInUse;

        [UdonSynced] private bool isInUse;
        [UdonSynced] private string userName = "";

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
            textInUse.text = displayName;
            respawnButton.SetActive(false);
            clearButton.SetActive(false);
            inUseButton.SetActive(true);
        }
        
        public void EndUsing()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
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

        public void ResetAll()
        {
            if (pen)
            {
                pen.Respawn();
                pen.Clear();
            }
        }
        
        public void ClearAll()
        {
            if (pen)
            {
                pen.Clear();
            }
        }
        
        public void SetUseDoubleClick(bool value)
        {
            if (pen)
            {
                pen.SetUseDoubleClick(value);
            }
        }
        
    }
}