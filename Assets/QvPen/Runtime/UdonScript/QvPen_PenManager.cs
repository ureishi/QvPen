using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
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
                if (clearButton)
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
            pen.pickuped = true;

            if (respawnButton)
                respawnButton.SetActive(false);
            if (clearButton)
                SetClearButtonActive(false);
            if (inUseUI)
                inUseUI.SetActive(true);

            var owner = Networking.GetOwner(pen.gameObject);

            var text = owner != null ? owner.displayName : "Occupied";

            if (textInUse)
                textInUse.text = text;

            if (textInUseTMP)
                textInUseTMP.text = text;

            if (textInUseTMPU)
                textInUseTMPU.text = text;
        }

        public void EndUsing()
        {
            pen.pickuped = false;

            if (respawnButton)
                respawnButton.SetActive(true);
            if (clearButton)
                SetClearButtonActive(true);
            if (inUseUI)
                inUseUI.SetActive(false);

            textInUse.text = string.Empty;
        }

        private PositionConstraint clearButtonPositionConstraint;
        private RotationConstraint clearButtonRotationConstraint;

        private void SetClearButtonActive(bool isActive)
        {
            if (clearButton)
                clearButton.SetActive(isActive);
            else
                return;

            if (!isActive)
                return;

            EnableClearButtonConstraints();
        }

        private void EnableClearButtonConstraints()
        {
            if (clearButtonPositionConstraint)
                clearButtonPositionConstraint.enabled = true;
            if (clearButtonRotationConstraint)
                clearButtonRotationConstraint.enabled = true;

            SendCustomEventDelayedSeconds(nameof(_DisableClearButtonConstraints), 2f);
        }

        public void _DisableClearButtonConstraints()
        {
            if (clearButtonPositionConstraint)
                clearButtonPositionConstraint.enabled = false;
            if (clearButtonRotationConstraint)
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

                if (_syncedData != null)
                    pen._UnpackData(_syncedData);
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
                pen.ExecuteEraseInk();
            else
                pen.DestroyJustBeforeInk();
        }

        public void _ClearSyncBuffer()
        {
            syncedData = new Vector3[] { };
            isInUseSyncBuffer = false;
        }

        #endregion
    }
}
