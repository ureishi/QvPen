using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
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
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(MarkAsInUse));
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.IsOwner(eraser.gameObject) && !eraser.IsUser)
                eraser.OnDrop();
        }

        public void MarkAsInUse()
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

        public void MarkAsAvailable()
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

        private bool hasObservedSettledNetwork = false;

        private bool HasNetworkSettled()
        {
            if (!hasObservedSettledNetwork)
                hasObservedSettledNetwork = Networking.IsNetworkSettled;

            return hasObservedSettledNetwork;
        }

        [UdonSynced]
        private Vector3[] _syncedData = { };

        private readonly DataList pendingSyncData = new DataList();
        private bool isSerializationInProgress = false;
        private bool isSendRetryScheduled = false;
        private int serializationRetryCount = 0;
        private const int MaxSerializationRetryCount = 3;
        private const float SendRetryDelaySeconds = 0.25f;

        public int LastSerializedByteCount { get; private set; }
        public int PendingSyncCount => pendingSyncData.Count;

        private void RequestPacketSend()
        {
            if (isSerializationInProgress || pendingSyncData.Count == 0 || !Networking.IsOwner(gameObject))
                return;

            if (!HasNetworkSettled() || Networking.IsClogged)
            {
                ScheduleSendRetry();
                return;
            }

            if (!pendingSyncData.TryGetValue(0, TokenType.Reference, out var dataToken))
            {
                pendingSyncData.RemoveAt(0);
                RequestPacketSend();
                return;
            }

            _syncedData = (Vector3[])dataToken.Reference;
            isSerializationInProgress = true;
            RequestSerialization();
        }

        private void ScheduleSendRetry()
        {
            if (isSendRetryScheduled)
                return;

            isSendRetryScheduled = true;
            SendCustomEventDelayedSeconds(nameof(_RetryPacketSend), SendRetryDelaySeconds);
        }

        public void _RetryPacketSend()
        {
            isSendRetryScheduled = false;
            RequestPacketSend();
        }

        public void _SendData(Vector3[] data)
        {
            if (data == null || data.Length == 0)
                return;

            if (VRCPlayerApi.GetPlayerCount() <= 1)
            {
                eraser._UnpackData(data);
                return;
            }

            if (!Networking.IsOwner(gameObject) || HasPendingEraseOperation(data))
                return;

            pendingSyncData.Add(new DataToken(data));
            RequestPacketSend();
        }

        private bool HasPendingEraseOperation(Vector3[] data)
        {
            for (int i = 0, n = pendingSyncData.Count; i < n; i++)
            {
                if (!pendingSyncData.TryGetValue(i, TokenType.Reference, out var dataToken))
                    continue;

                if (eraser._IsSameEraseOperation(data, (Vector3[])dataToken.Reference))
                    return true;
            }

            return false;
        }

        public override void OnDeserialization()
        {
            if (!Networking.IsOwner(gameObject) && _syncedData != null && _syncedData.Length > 0)
                eraser._UnpackData(_syncedData);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            isSerializationInProgress = false;
            LastSerializedByteCount = result.byteCount;

            if (result.success)
            {
                serializationRetryCount = 0;
                if (pendingSyncData.Count > 0)
                    pendingSyncData.RemoveAt(0);
                eraser._UnpackData(_syncedData);
                eraser._ApplyPendingErase();
            }
            else if (++serializationRetryCount > MaxSerializationRetryCount)
            {
                serializationRetryCount = 0;
                if (pendingSyncData.Count > 0)
                    pendingSyncData.RemoveAt(0);
            }

            RequestPacketSend();
        }

        public void _ClearSyncBuffer()
        {
            _syncedData = new Vector3[] { };
            pendingSyncData.Clear();
            isSerializationInProgress = false;
            serializationRetryCount = 0;
        }

        #endregion
    }
}
