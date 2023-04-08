using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

#pragma warning disable CS0108
#pragma warning disable IDE0044
#pragma warning disable IDE0066, IDE0074
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [DefaultExecutionOrder(11)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class QvPen_Eraser : UdonSharpBehaviour
    {
        private const string _version = QvPen_Pen._version;

        [SerializeField]
        private Material normal;
        private Material erasing;

        [SerializeField]
        private Transform inkPoolRoot;

        private Renderer _renderer;
        private Renderer renderer => _renderer ? _renderer : (_renderer = GetComponentInChildren<Renderer>(true));

        private SphereCollider _sphereCollider;
        private SphereCollider sphereCollider => _sphereCollider ? _sphereCollider : (_sphereCollider = GetComponent<SphereCollider>());

        private VRC_Pickup _pickup;
        private VRC_Pickup pickup => _pickup ? _pickup : (_pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)));

        private VRCObjectSync _objectSync;
        private VRCObjectSync objectSync
            => _objectSync ? _objectSync : (_objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync)));

        private bool isUser;
        public bool IsUser => isUser;

        private bool isErasing;

        // EraserManager
        private QvPen_EraserManager manager;

        private float _eraserRadius = 0f;
        private float eraserRadius
        {
            get
            {
                if (_eraserRadius > 0f)
                    return _eraserRadius;
                else
                {
                    var s = transform.lossyScale;
                    _eraserRadius = Mathf.Min(s.x, s.y, s.z) * sphereCollider.radius;
                    return _eraserRadius;
                }
            }
        }

        private int inkColliderLayer;

        private const string inkPoolRootName = QvPen_Pen.inkPoolRootName;

        private VRCPlayerApi _localPlayer;
        private VRCPlayerApi localPlayer => _localPlayer ?? (_localPlayer = Networking.LocalPlayer);

        private int localPlayerId => VRC.SDKBase.Utilities.IsValid(localPlayer) ? localPlayer.playerId : -1;

        public void _Init(QvPen_EraserManager manager)
        {
            this.manager = manager;
            inkColliderLayer = manager.inkColliderLayer;

            //-> [DefaultExecutionOrder(11)]
            var inkPoolRootGO = GameObject.Find($"/{inkPoolRootName}");
            if (inkPoolRootGO)
            {
                inkPoolRoot = inkPoolRootGO.transform;
            }
            else
            {
                inkPoolRoot.name = inkPoolRootName;
                SetParentAndResetLocalTransform(inkPoolRoot, null);
                inkPoolRoot.SetAsFirstSibling();
#if !UNITY_EDITOR
                const string ureishi = nameof(ureishi);
                Log($"{nameof(QvPen)} {_version}");
#endif
            }

            erasing = renderer.sharedMaterial;

            pickup.InteractionText = "Eraser";
            pickup.UseText = "Erase";
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (isUser && isErasing)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPickupEvent));
        }

        public override void OnPickup()
        {
            isUser = true;

            sphereCollider.enabled = false;

            manager._TakeOwnership();
            manager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_EraserManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPickupEvent));
        }

        public override void OnDrop()
        {
            isUser = false;

            sphereCollider.enabled = true;

            manager._ClearSyncBuffer();
            manager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_EraserManager.EndUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnDropEvent));
        }

        public override void OnPickupUseDown()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartErasing));
        }

        public override void OnPickupUseUp()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FinishErasing));
        }

        public void OnPickupEvent() => renderer.sharedMaterial = normal;

        public void OnDropEvent() => renderer.sharedMaterial = erasing;

        public void StartErasing()
        {
            isErasing = true;
            renderer.sharedMaterial = erasing;
        }

        public void FinishErasing()
        {
            isErasing = false;
            renderer.sharedMaterial = normal;
        }

        #region Data protocol

        #region Base

        // Mode
        private const int MODE_UNKNOWN = QvPen_Pen.MODE_UNKNOWN;
        private const int MODE_DRAW = QvPen_Pen.MODE_DRAW;
        private const int MODE_ERASE = QvPen_Pen.MODE_ERASE;
        private const int MODE_DRAW_PLANE = QvPen_Pen.MODE_DRAW_PLANE;

        // Footer element
        private const int FOOTER_ELEMENT_DATA_INFO = QvPen_Pen.FOOTER_ELEMENT_DATA_INFO;
        private const int FOOTER_ELEMENT_PEN_ID = QvPen_Pen.FOOTER_ELEMENT_PEN_ID;

        private const int FOOTER_ELEMENT_DRAW_INK_INFO = QvPen_Pen.FOOTER_ELEMENT_DRAW_INK_INFO;
        private const int FOOTER_ELEMENT_DRAW_LENGTH = QvPen_Pen.FOOTER_ELEMENT_DRAW_LENGTH;

        private const int FOOTER_ELEMENT_ERASE_POINTER_POSITION = QvPen_Pen.FOOTER_ELEMENT_ERASE_POINTER_POSITION;
        private const int FOOTER_ELEMENT_ERASE_POINTER_RADIUS = QvPen_Pen.FOOTER_ELEMENT_ERASE_POINTER_RADIUS;
        private const int FOOTER_ELEMENT_ERASE_LENGTH = QvPen_Pen.FOOTER_ELEMENT_ERASE_LENGTH;

        #endregion

        private int GetFooterSize(int mode)
        {
            switch (mode)
            {
                case MODE_DRAW: return FOOTER_ELEMENT_DRAW_LENGTH;
                case MODE_ERASE: return FOOTER_ELEMENT_ERASE_LENGTH;
                case MODE_UNKNOWN: return 0;
                default: return 0;
            }
        }

        private Vector3 GetData(Vector3[] data, int index)
            => data.Length > index ? data[data.Length - 1 - index] : Vector3.negativeInfinity;

        private void SetData(Vector3[] data, int index, Vector3 element)
        {
            if (data.Length > index)
                data[data.Length - 1 - index] = element;
        }

        private int GetMode(Vector3[] data)
            => data.Length > 0 ? (int)GetData(data, 0).y : MODE_UNKNOWN;

        public void _SendData(Vector3[] data)
            => manager._SendData(data);

        #endregion

        private readonly Collider[] results = new Collider[4];
        public override void PostLateUpdate()
        {
            if (!isUser || !isHeld || !isErasing)
                return;

            var count = Physics.OverlapSphereNonAlloc(transform.position, eraserRadius, results, 1 << inkColliderLayer, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var other = results[i];

                if (other && other.transform.parent && other.transform.parent.parent && other.transform.parent.parent.parent
                 && other.transform.parent.parent.parent.parent == inkPoolRoot)
                {
                    var syncer = other.GetComponentInParent<QvPen_LateSync>();
                    if (syncer)
                    {
                        var pen = syncer.pen;
                        var penIdVector = pen.penIdVector;
                        var lineRenderer = other.GetComponentInParent<LineRenderer>();
                        if (lineRenderer && lineRenderer.positionCount > 0)
                        {
                            var data = new Vector3[GetFooterSize(MODE_ERASE)];

                            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, MODE_ERASE, GetFooterSize(MODE_ERASE)));
                            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
                            SetData(data, FOOTER_ELEMENT_ERASE_POINTER_POSITION, transform.position);
                            SetData(data, FOOTER_ELEMENT_ERASE_POINTER_RADIUS, Vector3.right * eraserRadius);

                            _SendData(data);
                        }
                    }
                }

                results[i] = null;
            }
        }

        public void _UnpackData(Vector3[] data)
        {
            if (data.Length == 0)
                return;

            switch (GetMode(data))
            {
                case MODE_ERASE:
                    if (isUser && VRCPlayerApi.GetPlayerCount() > 1)
                        tmpErasedData = data;
                    else
                        EraseInk(data);

                    break;
            }
        }

        private Vector3[] tmpErasedData;
        public void ExecuteEraseInk()
        {
            if (tmpErasedData != null)
                EraseInk(tmpErasedData);

            tmpErasedData = null;
        }

        private void EraseInk(Vector3[] data)
        {
            if (data.Length < GetFooterSize(MODE_ERASE))
                return;

            var pointerPosition = GetData(data, FOOTER_ELEMENT_ERASE_POINTER_POSITION);
            var radius = GetData(data, FOOTER_ELEMENT_ERASE_POINTER_RADIUS).x;
            var count = Physics.OverlapSphereNonAlloc(pointerPosition, radius, results, 1 << inkColliderLayer, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var other = results[i];
                Transform t;
                if (other && (t = other.transform.parent) && (t = t.parent) && (t = t.parent) && t.parent == inkPoolRoot)
                {
                    Destroy(other.GetComponent<MeshCollider>().sharedMesh);
                    Destroy(other.transform.parent.gameObject);
                }

                results[i] = null;
            }
        }

        [System.NonSerialized]
        public bool pickuped = false; // protected
        public bool isHeld => pickuped;

        public void _Respawn()
        {
            pickup.Drop();

            if (Networking.LocalPlayer.IsOwner(gameObject))
                objectSync.Respawn();
        }

        #region Utility

        private void SetParentAndResetLocalTransform(Transform child, Transform parent)
        {
            if (child)
            {
                child.SetParent(parent);
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
            }
        }

        #endregion

        #region Log

        private void Log(object o) => Debug.Log($"{logPrefix}{o}", this);
        private void Warning(object o) => Debug.LogWarning($"{logPrefix}{o}", this);
        private void Error(object o) => Debug.LogError($"{logPrefix}{o}", this);

        private readonly Color logColor = new Color(0xf2, 0x7d, 0x4a, 0xff) / 0xff;
        private string ColorBeginTag(Color c) => $"<color=\"#{ToHtmlStringRGB(c)}\">";
        private const string ColorEndTag = "</color>";

        private string _logPrefix;
        private string logPrefix
            => string.IsNullOrEmpty(_logPrefix)
                ? (_logPrefix = $"[{ColorBeginTag(logColor)}{nameof(QvPen)}.{nameof(QvPen.Udon)}.{nameof(QvPen_Eraser)}{ColorEndTag}] ") : _logPrefix;

        private string ToHtmlStringRGB(Color c)
        {
            c *= 0xff;
            return $"{Mathf.RoundToInt(c.r):x2}{Mathf.RoundToInt(c.g):x2}{Mathf.RoundToInt(c.b):x2}";
        }

        #endregion
    }
}
