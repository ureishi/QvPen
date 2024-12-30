using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable CS0108
#pragma warning disable IDE0044
#pragma warning disable IDE0066, IDE0074
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(11)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class QvPen_Eraser : UdonSharpBehaviour
    {
        private const string version = QvPen_Pen.version;

        [SerializeField]
        private Material normal;
        [SerializeField]
        private Material erasing;

        [SerializeField]
        private Transform inkPoolRoot;

        private QvPen_Manager manager;

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
        private QvPen_EraserManager eraserManager;

        private bool _isCheckedEraserRadius = false;
        private float _eraserRadius = 0f;
        private float eraserRadius
        {
            get
            {
                if (_isCheckedEraserRadius)
                {
                    return _eraserRadius;
                }
                else
                {
                    var s = transform.lossyScale;
                    _eraserRadius = Mathf.Min(s.x, s.y, s.z) * sphereCollider.radius;
                    _isCheckedEraserRadius = true;
                    return _eraserRadius;
                }
            }
        }

        private int inkColliderLayer;

        private const string inkPoolRootName = QvPen_Pen.inkPoolRootName;

        private VRCPlayerApi _localPlayer;
        private VRCPlayerApi localPlayer => _localPlayer ?? (_localPlayer = Networking.LocalPlayer);

        private int localPlayerId => VRC.SDKBase.Utilities.IsValid(localPlayer) ? localPlayer.playerId : -1;

        public void _Init(QvPen_EraserManager eraserManager)
        {
            this.eraserManager = eraserManager;
            inkColliderLayer = eraserManager.inkColliderLayer;

            var inkPoolRootGO = GameObject.Find($"/{inkPoolRootName}");
            if (Utilities.IsValid(inkPoolRootGO))
            {
                inkPoolRoot.gameObject.SetActive(false);
                inkPoolRoot = inkPoolRootGO.transform;
            }
            else
            {
                inkPoolRoot.name = inkPoolRootName;
                QvPenUtilities.SetParentAndResetLocalTransform(inkPoolRoot, null);
                inkPoolRoot.SetAsFirstSibling();
                inkPoolRoot.gameObject.SetActive(true);
#if !UNITY_EDITOR
                Log($"{nameof(QvPen)} {version}");
#endif
            }

            manager = inkPoolRoot.GetComponent<QvPen_Manager>();

            if (!Utilities.IsValid(erasing))
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

            eraserManager._TakeOwnership();
            eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_EraserManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPickupEvent));
        }

        public override void OnDrop()
        {
            isUser = false;

            sphereCollider.enabled = true;

            eraserManager._ClearSyncBuffer();
            eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_EraserManager.EndUsing));

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

        // Footer element
        private const int FOOTER_ELEMENT_DATA_INFO = QvPen_Pen.FOOTER_ELEMENT_DATA_INFO;
        private const int FOOTER_ELEMENT_PEN_ID = QvPen_Pen.FOOTER_ELEMENT_PEN_ID;
        private const int FOOTER_ELEMENT_INK_ID = QvPen_Pen.FOOTER_ELEMENT_INK_ID;

        private const int FOOTER_ELEMENT_DRAW_INK_INFO = QvPen_Pen.FOOTER_ELEMENT_DRAW_INK_INFO;
        private const int FOOTER_ELEMENT_DRAW_LENGTH = QvPen_Pen.FOOTER_ELEMENT_DRAW_LENGTH;

        private const int FOOTER_ELEMENT_ERASE_LENGTH = QvPen_Pen.FOOTER_ELEMENT_ERASE_LENGTH;

        #endregion

        private static int GetFooterSize(QvPen_Pen_Mode mode)
        {
            switch (mode)
            {
                case QvPen_Pen_Mode.Draw: return FOOTER_ELEMENT_DRAW_LENGTH;
                case QvPen_Pen_Mode.Erase: return FOOTER_ELEMENT_ERASE_LENGTH;
                case QvPen_Pen_Mode.None: return 0;
                default: return 0;
            }
        }

        private static Vector3 GetData(Vector3[] data, int index)
            => data != null && data.Length > index ? data[data.Length - 1 - index] : default;

        private static void SetData(Vector3[] data, int index, Vector3 element)
        {
            if (data != null && data.Length > index)
                data[data.Length - 1 - index] = element;
        }

        private static QvPen_Pen_Mode GetMode(Vector3[] data)
            => data != null && data.Length > 0 ? (QvPen_Pen_Mode)(int)GetData(data, FOOTER_ELEMENT_DATA_INFO).y : QvPen_Pen_Mode.None;

        public void _SendData(Vector3[] data)
            => eraserManager._SendData(data);

        #endregion

        private readonly Collider[] results4 = new Collider[4];
        public override void PostLateUpdate()
        {
            if (!isUser || !isHeld || !isErasing)
                return;

            var count = Physics.OverlapSphereNonAlloc(transform.position, eraserRadius, results4, 1 << inkColliderLayer, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var other = results4[i];

                Transform t;

                if (Utilities.IsValid(other)
                 && Utilities.IsValid(t = other.transform.parent)
                 && Utilities.IsValid(t.parent))
                {
                    var lineRenderer = other.GetComponentInParent<LineRenderer>();
                    if (Utilities.IsValid(lineRenderer)
                     && lineRenderer.positionCount > 0
                     && QvPenUtilities.TryGetIdFromInk(lineRenderer.gameObject, out var penIdVector, out var inkIdVector, out var _discard))
                    {
                        var data = new Vector3[GetFooterSize(QvPen_Pen_Mode.Erase)];

                        SetData(data, FOOTER_ELEMENT_DATA_INFO,
                            new Vector3(localPlayerId, (int)QvPen_Pen_Mode.Erase, GetFooterSize(QvPen_Pen_Mode.Erase)));
                        SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
                        SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);

                        _SendData(data);
                    }
                }

                results4[i] = null;
            }
        }

        public void _UnpackData(Vector3[] data)
        {
            var mode = GetMode(data);

            switch (mode)
            {
                case QvPen_Pen_Mode.Erase:
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
            if (data == null || data.Length < GetFooterSize(QvPen_Pen_Mode.Erase))
                return;

            var penIdVector = GetData(data, FOOTER_ELEMENT_PEN_ID);
            var inkIdVector = GetData(data, FOOTER_ELEMENT_INK_ID);

            var penId = QvPenUtilities.Vector3ToInt32(penIdVector);
            var inkId = QvPenUtilities.Vector3ToInt32(inkIdVector);

            manager.RemoveInk(penId, inkId);
        }

        [System.NonSerialized]
        public bool isPickedUp = false; // protected
        public bool isHeld => isPickedUp;

        public void _Respawn()
        {
            pickup.Drop();

            if (Networking.LocalPlayer.IsOwner(gameObject))
                objectSync.Respawn();
        }

        #region Log

        private const string appName = nameof(QvPen_Eraser);

        private void Log(object o) => Debug.Log($"{logPrefix}{o}", this);
        private void Warning(object o) => Debug.LogWarning($"{logPrefix}{o}", this);
        private void Error(object o) => Debug.LogError($"{logPrefix}{o}", this);

        private readonly Color logColor = new Color(0xf2, 0x7d, 0x4a, 0xff) / 0xff;
        private string ColorBeginTag(Color c) => $"<color=\"#{ToHtmlStringRGB(c)}\">";
        private const string ColorEndTag = "</color>";

        private string _logPrefix;
        private string logPrefix
            => !string.IsNullOrEmpty(_logPrefix)
                ? _logPrefix : (_logPrefix = $"[{ColorBeginTag(logColor)}{nameof(QvPen)}.{nameof(QvPen.Udon)}.{appName}{ColorEndTag}] ");

        private static string ToHtmlStringRGB(Color c)
        {
            c *= 0xff;
            return $"{Mathf.RoundToInt(c.r):x2}{Mathf.RoundToInt(c.g):x2}{Mathf.RoundToInt(c.b):x2}";
        }

        #endregion

    }
}
