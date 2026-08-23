using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(20)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvPen_PenManager : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_Pen pen;

        public Gradient colorGradient = new Gradient();

        public float inkWidth = 0.005f;

        // Layer 0 : Default
        // Layer 9 : Player
        public int inkMeshLayer = 0;
        public int inkColliderLayer = 9;

        public Material pcInkMaterial;
        public Material questInkMaterial;

        public LayerMask surftraceMask = ~0;

        [SerializeField]
        private GameObject respawnButton;
        [SerializeField]
        private GameObject clearButton;
        [SerializeField]
        private GameObject inUseUI;

        [SerializeField]
        private Text textInUse;
        [SerializeField]
        private TextMeshPro textInUseTMP;
        [SerializeField]
        private TextMeshProUGUI textInUseTMPU;

        [SerializeField]
        private Shader _roundedTrailShader;
        public Shader roundedTrailShader => _roundedTrailShader;

        [SerializeField]
        private bool allowCallPen = true;
        public bool AllowCallPen => allowCallPen;

        private void Start()
        {
            pen._Init(this);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(pen.gameObject) && pen.IsUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_MarkAsInUse));

            if (player.isLocal)
            {
                if (Utilities.IsValid(clearButton))
                {
                    clearButtonPositionConstraint = clearButton.GetComponent<PositionConstraint>();
                    clearButtonRotationConstraint = clearButton.GetComponent<RotationConstraint>();

                    EnableClearButtonConstraints();
                }
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.IsOwner(pen.gameObject) && !pen.IsUser)
                pen.OnDrop();
        }

        [NetworkCallable]
        public void _MarkAsInUse()
        {
            pen.isPickedUp = true;

            if (Utilities.IsValid(respawnButton))
                respawnButton.SetActive(false);
            if (Utilities.IsValid(clearButton))
                SetClearButtonActive(false);
            if (Utilities.IsValid(inUseUI))
                inUseUI.SetActive(true);

            var owner = Networking.GetOwner(pen.gameObject);

            var text = owner != null ? owner.displayName : "Occupied";

            if (Utilities.IsValid(textInUse))
                textInUse.text = text;

            if (Utilities.IsValid(textInUseTMP))
                textInUseTMP.text = text;

            if (Utilities.IsValid(textInUseTMPU))
                textInUseTMPU.text = text;
        }

        [NetworkCallable]
        public void _MarkAsAvailable()
        {
            pen.isPickedUp = false;

            if (Utilities.IsValid(respawnButton))
                respawnButton.SetActive(true);
            if (Utilities.IsValid(clearButton))
                SetClearButtonActive(true);
            if (Utilities.IsValid(inUseUI))
                inUseUI.SetActive(false);

            if (Utilities.IsValid(textInUse))
                textInUse.text = string.Empty;

            if (Utilities.IsValid(textInUseTMP))
                textInUseTMP.text = string.Empty;

            if (Utilities.IsValid(textInUseTMPU))
                textInUseTMPU.text = string.Empty;
        }

        private PositionConstraint clearButtonPositionConstraint;
        private RotationConstraint clearButtonRotationConstraint;

        private void SetClearButtonActive(bool isActive)
        {
            if (Utilities.IsValid(clearButton))
                clearButton.SetActive(isActive);
            else
                return;

            if (!isActive)
                return;

            EnableClearButtonConstraints();
        }

        private void EnableClearButtonConstraints()
        {
            if (Utilities.IsValid(clearButtonPositionConstraint))
                clearButtonPositionConstraint.enabled = true;
            if (Utilities.IsValid(clearButtonRotationConstraint))
                clearButtonRotationConstraint.enabled = true;

            SendCustomEventDelayedSeconds(nameof(_DisableClearButtonConstraints), 2f);
        }

        public void _DisableClearButtonConstraints()
        {
            if (Utilities.IsValid(clearButtonPositionConstraint))
                clearButtonPositionConstraint.enabled = false;
            if (Utilities.IsValid(clearButtonRotationConstraint))
                clearButtonRotationConstraint.enabled = false;
        }

        #region API

        public void _SetWidth(float width)
        {
            inkWidth = width;
            pen._UpdateInkData();
        }

        public void _SetMeshLayer(int layer)
        {
            inkMeshLayer = layer;
            pen._UpdateInkData();
        }

        public void _SetColliderLayer(int layer)
        {
            inkColliderLayer = layer;
            pen._UpdateInkData();
        }

        public void _SetColor(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a, 1f),
                });

            _SetColorGradient(gradient);
        }

        public void _SetColorGradient(Gradient gradient)
        {
            if (gradient == null)
                return;

            colorGradient = gradient;
            pen._UpdateInkData();
            NotifyPenColorChanged(gradient);
        }

        public void _SetDoubleClickEnabled(bool value) => pen._SetDoubleClickEnabled(value);

        public void _SetLateSyncEnabled(bool value) => pen._SetLateSyncEnabled(value);

        public void _SetSurftraceEnabled(bool value) => pen._SetSurftraceEnabled(value);

        [NetworkCallable]
        public void _ResetPen()
        {
            Clear();
            Respawn();
        }

        public void Respawn()
        {
            pen._Respawn();
            SetClearButtonActive(true);
        }

        public void Clear()
        {
            _ClearSyncBuffer();
            pen._Clear();
        }

        public void _UndoLastStroke()
        {
            if (pen.isPickedUp)
                return;

            _TakeOwnership();

            pen._UndoLastStroke();
        }

        public void _EraseOwnStrokes()
        {
            if (pen.isPickedUp)
                return;

            _TakeOwnership();

            pen._EraseOwnStrokes();
        }

        #endregion

        #region Callback

        private readonly DataList listenerList = new DataList();

        public void RegisterListener(QvPen_PenCallbackListener listener)
        {
            if (!Utilities.IsValid(listener) || listenerList.Contains(listener))
                return;

            listenerList.Add(listener);
        }

        public void _OnPenPickup()
        {
            for (int i = 0, n = listenerList.Count; i < n; i++)
            {
                if (!listenerList.TryGetValue(i, TokenType.Reference, out var listenerToken))
                    continue;

                var listener = (QvPen_PenCallbackListener)listenerToken.Reference;

                if (!Utilities.IsValid(listener))
                    continue;

                listener._OnPenPickup();
            }
        }

        public void _OnPenDrop()
        {
            for (int i = 0, n = listenerList.Count; i < n; i++)
            {
                if (!listenerList.TryGetValue(i, TokenType.Reference, out var listenerToken))
                    continue;

                var listener = (QvPen_PenCallbackListener)listenerToken.Reference;

                if (!Utilities.IsValid(listener))
                    continue;

                listener._OnPenDrop();
            }
        }

        private void NotifyPenColorChanged(Gradient gradient)
        {
            for (int i = 0, n = listenerList.Count; i < n; i++)
            {
                if (!listenerList.TryGetValue(i, TokenType.Reference, out var listenerToken))
                    continue;

                var listener = (QvPen_PenCallbackListener)listenerToken.Reference;

                if (!Utilities.IsValid(listener))
                    continue;

                listener._OnPenColorChanged(gradient);
            }
        }

        #endregion

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

        [UdonSynced]
        private int inkId;
        public int InkId => inkId;

        public void _IncrementInkId() => inkId++;

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
                pen._UnpackData(data, QvPen_Pen_Mode.Any);
                return;
            }

            if (!Networking.IsOwner(gameObject))
                return;

            // Draw immediately so the local stroke remains visible while waiting for
            // serialization. Erase only after successful serialization.
            if (pen._IsDrawData(data))
                pen._UnpackData(data, QvPen_Pen_Mode.Draw);
            else if (HasPendingEraseOperation(data))
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

                if (pen._IsSameEraseOperation(data, (Vector3[])dataToken.Reference))
                    return true;
            }

            return false;
        }

        public override void OnDeserialization()
        {
            if (Networking.IsOwner(gameObject))
                return;

            if (_syncedData != null && _syncedData.Length > 0)
                pen._UnpackData(_syncedData, QvPen_Pen_Mode.Any);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            isSerializationInProgress = false;
            LastSerializedByteCount = result.byteCount;

            if (result.success)
            {
                serializationRetryCount = 0;
                if (!pen._IsDrawData(_syncedData))
                    pen._UnpackData(_syncedData, QvPen_Pen_Mode.Any);

                if (pendingSyncData.Count > 0)
                    pendingSyncData.RemoveAt(0);
            }
            else if (++serializationRetryCount > MaxSerializationRetryCount)
            {
                serializationRetryCount = 0;
                pen._EraseAbandonedInk(_syncedData);
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
