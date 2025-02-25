using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(20)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvPen_EraserManager : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_Eraser eraser;

        // Layer 9 : Player
        public int inkColliderLayer = 9;

        [SerializeField]
        private GameObject respawnButton;
        [SerializeField]
        private GameObject inUseUI;

        [SerializeField]
        private Text textInUse;
        [SerializeField]
        private TextMeshPro textInUseTMP;
        [SerializeField]
        private TextMeshProUGUI textInUseTMPU;

        private void Start() => eraser._Init(this);

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer.IsOwner(eraser.gameObject) && eraser.IsUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartUsing));
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.IsOwner(eraser.gameObject) && !eraser.IsUser)
                eraser.OnDrop();
        }

        public void StartUsing()
        {
            eraser.isPickedUp = true;

            respawnButton.SetActive(false);
            inUseUI.SetActive(true);

            var owner = Networking.GetOwner(eraser.gameObject);

            var text = owner != null ? owner.displayName : "Occupied";

            if (Utilities.IsValid(textInUse))
                textInUse.text = text;

            if (Utilities.IsValid(textInUseTMP))
                textInUseTMP.text = text;

            if (Utilities.IsValid(textInUseTMPU))
                textInUseTMPU.text = text;
        }

        public void EndUsing()
        {
            eraser.isPickedUp = false;

            respawnButton.SetActive(true);
            inUseUI.SetActive(false);

            if (Utilities.IsValid(textInUse))
                textInUse.text = string.Empty;

            if (Utilities.IsValid(textInUseTMP))
                textInUseTMP.text = string.Empty;

            if (Utilities.IsValid(textInUseTMPU))
                textInUseTMPU.text = string.Empty;
        }

        public void ResetEraser() => eraser._Respawn();

        public void Respawn() => eraser._Respawn();

        #region Network

        public bool _TakeOwnership()
        {
            if (Networking.IsOwner(gameObject))
            {
                _ClearSyncBuffer();
                return true;
            }
            else
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                return Networking.IsOwner(gameObject);
            }
        }

        private bool _isNetworkSettled = false;
        private bool isNetworkSettled
            => _isNetworkSettled || (_isNetworkSettled = Networking.IsNetworkSettled);

        [UdonSynced]
        private Vector3[] _syncedData;
        private Vector3[] syncedData
        {
            get => _syncedData;
            set
            {
                if (!isNetworkSettled)
                    return;

                _syncedData = value;

                RequestSendPackage();

                eraser._UnpackData(_syncedData);
            }
        }

        private bool isInUseSyncBuffer = false;
        private void RequestSendPackage()
        {
            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject) && !isInUseSyncBuffer)
            {
                isInUseSyncBuffer = true;
                RequestSerialization();
            }
        }

        public void _SendData(Vector3[] data)
        {
            if (!isInUseSyncBuffer)
                syncedData = data;
        }

        public override void OnPreSerialization()
            => _syncedData = syncedData;

        public override void OnDeserialization()
            => syncedData = _syncedData;

        public override void OnPostSerialization(SerializationResult result)
        {
            isInUseSyncBuffer = false;

            if (result.success)
                eraser.ExecuteEraseInk();
        }

        public void _ClearSyncBuffer()
        {
            syncedData = new Vector3[] { };
            isInUseSyncBuffer = false;
        }

        #endregion
    }
}
