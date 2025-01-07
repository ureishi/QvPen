using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UI;
using VRC.SDK3.Data;
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

        private void Start()
        {
            pen._Init(this);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(pen.gameObject) && pen.IsUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartUsing));

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

        public void StartUsing()
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

        public void EndUsing()
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

        public void _SetUsingDoubleClick(bool value) => pen._SetUseDoubleClick(value);

        public void _SetEnabledLateSync(bool value) => pen._SetEnabledLateSync(value);

        public void _SetUsingSurftraceMode(bool value) => pen._SetUseSurftraceMode(value);

        public void ResetPen()
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

        public void UndoDraw()
        {
            if (pen.isPickedUp)
                return;

            _TakeOwnership();

            pen._UndoDraw();
        }

        public void EraseOwnInk()
        {
            if (pen.isPickedUp)
                return;

            _TakeOwnership();

            pen._EraseOwnInk();
        }

        #endregion

        #region Callback

        private readonly DataList listenerList = new DataList();

        public void Register(QvPen_PenCallbackListener listener)
        {
            if (!Utilities.IsValid(listener) || listenerList.Contains(listener))
                return;

            listenerList.Add(listener);
        }

        public void OnPenPickup()
        {
            for (int i = 0, n = listenerList.Count; i < n; i++)
            {
                if (!listenerList.TryGetValue(i, TokenType.Reference, out var listerToken))
                    continue;

                var listener = (QvPen_PenCallbackListener)listerToken.Reference;

                if (!Utilities.IsValid(listener))
                    continue;

                listener.OnPenPickup();
            }
        }

        public void OnPenDrop()
        {
            for (int i = 0, n = listenerList.Count; i < n; i++)
            {
                if (!listenerList.TryGetValue(i, TokenType.Reference, out var listerToken))
                    continue;

                var listener = (QvPen_PenCallbackListener)listerToken.Reference;

                if (!Utilities.IsValid(listener))
                    continue;

                listener.OnPenDrop();
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

                pen._UnpackData(_syncedData, QvPen_Pen_Mode.Any);
            }
        }

        [UdonSynced]
        private int inkId;
        public int InkId => inkId;

        public void _IncrementInkId() => inkId++;

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
        {
            if (Networking.IsOwner(gameObject))
                return;

            syncedData = _syncedData;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            isInUseSyncBuffer = false;

            if (result.success)
                pen._UnpackData(_syncedData, QvPen_Pen_Mode.Any);
            else
                pen._EraseAbandonedInk(_syncedData);
        }

        public void _ClearSyncBuffer()
        {
            syncedData = new Vector3[] { };
            isInUseSyncBuffer = false;
        }

        #endregion
    }
}
