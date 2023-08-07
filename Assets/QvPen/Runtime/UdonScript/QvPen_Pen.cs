using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [DefaultExecutionOrder(10)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class QvPen_Pen : UdonSharpBehaviour
    {
        public const string _version = "v3.2.9";

        #region Field

        [Header("Pen")]
        [SerializeField]
        private TrailRenderer trailRenderer;
        [SerializeField]
        private LineRenderer inkPrefab;

        [SerializeField]
        private Transform inkPosition;
        private Transform inkPositionChild;
        [SerializeField]
        private Transform inkPoolRoot;
        [System.NonSerialized]
        public Transform inkPool;
        [System.NonSerialized]
        public Transform inkPoolSynced;// { get; private set; }
        private Transform inkPoolNotSynced;

        private QvPen_LateSync syncer;

        [Header("Pointer")]
        [SerializeField]
        private Transform pointer;

        private float _pointerRadius = 0f;
        private float pointerRadius
        {
            get
            {
                if (_pointerRadius > 0f)
                    return _pointerRadius;
                else
                {
                    var sphereCollider = pointer.GetComponent<SphereCollider>();
                    sphereCollider.enabled = false;
                    var s = pointer.lossyScale;
                    _pointerRadius = Mathf.Min(s.x, s.y, s.z) * sphereCollider.radius;
                    return _pointerRadius;
                }
            }
        }
        [SerializeField]
        private float _pointerRadiusMultiplierForDesktop = 3f;
        private float pointerRadiusMultiplierForDesktop => isUserInVR ? 1f : Mathf.Abs(_pointerRadiusMultiplierForDesktop);
        [SerializeField]
        private Material pointerMaterialNormal;
        [SerializeField]
        private Material pointerMaterialActive;

        [Header("Screen")]
        [SerializeField]
        private Canvas screenOverlay;
        [SerializeField]
        private Renderer marker;

        [Header("Other")]
        [SerializeField]
        private bool canBeErasedWithOtherPointers = true;

        private bool enabledLateSync = true;

        private MeshCollider _inkPrefabCollider;
        private MeshCollider inkPrefabCollider
            => _inkPrefabCollider ? _inkPrefabCollider : (_inkPrefabCollider = inkPrefab.GetComponentInChildren<MeshCollider>(true));
        private GameObject lineInstance;

        private bool isUser;
        public bool IsUser => isUser;

        // Components
        private VRC_Pickup _pickup;
        private VRC_Pickup pickup => _pickup ? _pickup : (_pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)));

        private VRCObjectSync _objectSync;
        private VRCObjectSync objectSync
            => _objectSync ? _objectSync : (_objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync)));

        // PenManager
        private QvPen_PenManager manager;

        // Ink
        private int inkMeshLayer;
        private int inkColliderLayer;
        private const float followSpeed = 30f;
        private int inkNo;

        // Pointer
        private bool isPointerEnabled;
        private Renderer pointerRenderer;

        // Double click
        private bool useDoubleClick = true;
        private const float clickTimeInterval = 0.2f;
        private float prevClickTime;
        private float clickPosInterval = 0.01f; // Default: Quest
        private Vector3 prevClickPos;

        // State
        private const int StatePenIdle = 0;
        private const int StatePenUsing = 1;
        private const int StateEraserIdle = 2;
        private const int StateEraserUsing = 3;
        private int currentState = StatePenIdle;
        private string nameofCurrentState
        {
            get
            {
                switch (currentState)
                {
                    case StatePenIdle: return nameof(StatePenIdle);
                    case StatePenUsing: return nameof(StatePenUsing);
                    case StateEraserIdle: return nameof(StateEraserIdle);
                    case StateEraserUsing: return nameof(StateEraserUsing);
                    default: return string.Empty;
                }
            }
        }

        // Sync state
        [System.NonSerialized]
        public int currentSyncState = SYNC_STATE_Idle;
        public const int SYNC_STATE_Idle = 0;
        public const int SYNC_STATE_Started = 1;
        public const int SYNC_STATE_Finished = 2;

        // Ink pool
        public const string inkPoolRootName = "QvPen_Objects";
        public const string inkPoolName = "InkPool";
        private int penId;
        public Vector3 penIdVector { get; private set; }
        private string penIdString;

        private const string inkPrefix = "Ink";
        private float inkWidth;

        private VRCPlayerApi _localPlayer;
        private VRCPlayerApi localPlayer => _localPlayer ?? (_localPlayer = Networking.LocalPlayer);

        private int localPlayerId => VRC.SDKBase.Utilities.IsValid(localPlayer) ? localPlayer.playerId : -1;

        private bool isCheckedIsUserInVR = false;
        private bool/*?*/ _isUserInVR;
        private bool/*?*/ isUserInVR => isCheckedIsUserInVR ? _isUserInVR
            : (isCheckedIsUserInVR = VRC.SDKBase.Utilities.IsValid(localPlayer)) && (_isUserInVR = localPlayer.IsUserInVR());

        #endregion Field

        public void _Init(QvPen_PenManager manager)
        {
            this.manager = manager;
            _UpdateInkData();

            inkPool = inkPoolRoot.Find(inkPoolName);

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

            SetParentAndResetLocalTransform(inkPool, inkPoolRoot);

            penId = string.IsNullOrEmpty(Networking.GetUniqueName(gameObject))
                ? 0 : Networking.GetUniqueName(gameObject).GetHashCode();
            penIdVector = new Vector3((penId >> 24) & 0x00ff, (penId >> 12) & 0x0fff, penId & 0x0fff);
            penIdString = $"0x{(int)penIdVector.x:x2}{(int)penIdVector.y:x3}{(int)penIdVector.z:x3}";
            inkPool.name = $"{inkPoolName} ({penIdString})";

            syncer = inkPool.GetComponent<QvPen_LateSync>();
            if (syncer)
                syncer.pen = this;

            inkPoolSynced = inkPool.Find("Synced");
            inkPoolNotSynced = inkPool.Find("NotSynced");

#if !UNITY_EDITOR
            Log($"QvPen ID: {penIdString}");
#endif

            inkPositionChild = inkPosition.Find("InkPositionChild");

            pickup.InteractionText = nameof(QvPen);
            pickup.UseText = "Draw";

            pointerRenderer = pointer.GetComponent<Renderer>();
            pointer.gameObject.SetActive(false);
            pointer.transform.localScale *= pointerRadiusMultiplierForDesktop;

            marker.transform.localScale = Vector3.one * inkWidth;

#if !UNITY_ANDROID
            if (isUserInVR)
                clickPosInterval = 0.005f;
            else
                clickPosInterval = 0.001f;
#endif
        }

        public void _UpdateInkData()
        {
            inkWidth = manager.inkWidth;
            inkMeshLayer = manager.inkMeshLayer;
            inkColliderLayer = manager.inkColliderLayer;

            inkPrefab.gameObject.layer = inkMeshLayer;
            trailRenderer.gameObject.layer = inkMeshLayer;
            inkPrefabCollider.gameObject.layer = inkColliderLayer;

#if UNITY_ANDROID
            var material = manager.questInkMaterial;
            inkPrefab.widthMultiplier = inkWidth;
            trailRenderer.widthMultiplier = inkWidth;
#else
            var material = manager.pcInkMaterial;
            if (material && material.shader == manager.roundedTrailShader)
            {
                inkPrefab.widthMultiplier = 0f;
                trailRenderer.widthMultiplier = 0f;
                material.SetFloat("_Width", inkWidth);
            }
            else
            {
                inkPrefab.widthMultiplier = inkWidth;
                trailRenderer.widthMultiplier = inkWidth;
            }
#endif

            inkPrefab.material = material;
            trailRenderer.material = material;
            inkPrefab.colorGradient = manager.colorGradient;
            trailRenderer.colorGradient = CreateReverseGradient(manager.colorGradient);

            surftraceMask = manager.surftraceMask;
        }

        public bool _CheckId(Vector3 idVector)
            => idVector == penIdVector;

        #region Data protocol

        #region Base

        // Mode
        public const int MODE_UNKNOWN = -1;
        public const int MODE_DRAW = 1;
        public const int MODE_ERASE = 2;
        public const int MODE_DRAW_PLANE = 3;

        // Footer element
        public const int FOOTER_ELEMENT_DATA_INFO = 0;
        public const int FOOTER_ELEMENT_PEN_ID = 1;

        public const int FOOTER_ELEMENT_DRAW_INK_INFO = 2;
        //public const int FOOTER_ELEMENT_DRAW_INK_WIDTH = ;
        //public const int FOOTER_ELEMENT_DRAW_INK_COLOR = ;
        public const int FOOTER_ELEMENT_DRAW_LENGTH = 3;

        public const int FOOTER_ELEMENT_ERASE_POINTER_POSITION = 2;
        public const int FOOTER_ELEMENT_ERASE_POINTER_RADIUS = 3;
        public const int FOOTER_ELEMENT_ERASE_LENGTH = 4;

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

        private int currentDrawMode = MODE_DRAW;

        #endregion

        private Vector3 GetData(Vector3[] data, int index)
            => data.Length > index ? data[data.Length - 1 - index] : Vector3.negativeInfinity;

        private void SetData(Vector3[] data, int index, Vector3 element)
        {
            if (data.Length > index)
                data[data.Length - 1 - index] = element;
        }

        private int GetMode(Vector3[] data)
            => data.Length > 0 ? (int)GetData(data, FOOTER_ELEMENT_DATA_INFO).y : MODE_UNKNOWN;

        private int GetFooterLength(Vector3[] data)
            => data.Length > 0 ? (int)GetData(data, FOOTER_ELEMENT_DATA_INFO).z : 0;

        #endregion

        #region Unity events

        #region Screen mode
#if !UNITY_ANDROID
        private VRCPlayerApi.TrackingData headTracking;
        private Vector3 headPos, center;
        private Quaternion headRot;
        private Vector2 _wh, wh, clampWH;
        // Wait for Udon Vector2.Set() bug fix
        private /*readonly*/ Vector2 mouseDelta = new Vector2();
        private float ratio, scalar;

        private float sensitivity = 0.75f;
        private bool isScreenMode = false;
        private void Update()
        {
            if (isUserInVR || !isUser)
                return;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                EnterScreenMode();
            }
            else if (Input.GetKeyUp(KeyCode.Tab))
            {
                ExitScreenMode();
            }
            else if (Input.GetKey(KeyCode.Tab))
            {
                if (Input.GetKeyDown(KeyCode.Delete))
                {
                    manager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(manager.Clear));
                }
                else if (Input.GetKey(KeyCode.UpArrow))
                {
                    sensitivity = Mathf.Min(sensitivity + 0.001f, 1.5f);
                    Log($"Sensitivity -> {sensitivity:f3}");
                }
                else if (Input.GetKey(KeyCode.DownArrow))
                {
                    sensitivity = Mathf.Max(sensitivity - 0.001f, 0.5f);
                    Log($"Sensitivity -> {sensitivity:f3}");
                }
            }
        }

        private void EnterScreenMode()
        {
            isScreenMode = true;

            marker.enabled = true;

            _wh = Vector2.zero;
            screenOverlay.gameObject.SetActive(true);
            wh = screenOverlay.GetComponent<RectTransform>().rect.size;
            screenOverlay.gameObject.SetActive(false);
            clampWH = wh / (2f * 1920f * 0.98f);
            ratio = 2f * 1080f / wh.y;
        }

        private void ExitScreenMode()
        {
            isScreenMode = false;

            if (!isSurftraceMode)
                marker.enabled = false;

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));

            inkPositionChild.localPosition = Vector3.zero;
            inkPositionChild.localRotation = Quaternion.identity;
            trailRenderer.transform.SetPositionAndRotation(inkPositionChild.position, inkPositionChild.rotation);
        }
#endif
        #endregion Screen mode

        private void LateUpdate()
        {
            if (!isHeld)
                return;

#if !UNITY_ANDROID
            if (!isUserInVR && isUser && Input.GetKey(KeyCode.Tab))
            {
                headTracking = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                headPos = headTracking.position;
                headRot = headTracking.rotation;

                center = headRot * Vector3.forward * Vector3.Dot(headRot * Vector3.forward, transform.position - headPos);
                scalar = ratio * Vector3.Dot(headRot * Vector3.forward, center);
                center += headPos;

                // Wait for Udon Vector2.Set() bug fix
                // mouseDelta.Set(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                {
                    mouseDelta.x = Input.GetAxis("Mouse X");
                    mouseDelta.y = Input.GetAxis("Mouse Y");
                }
                _wh += sensitivity * Time.deltaTime * mouseDelta;
                _wh = Vector2.Min(Vector2.Max(_wh, -clampWH), clampWH);

                inkPositionChild.SetPositionAndRotation(center + headRot * _wh * scalar, headRot);
            }
#endif

            if (isSurftraceMode)
            {
                Vector3 inkPositionPosition;
#if !UNITY_ANDROID
                if (isScreenMode)
                    inkPositionPosition = inkPositionChild.position;
                else
#endif
                    inkPositionPosition = inkPosition.position;

                var closestPoint = surftraceTarget.ClosestPoint(inkPositionPosition);
                var distance = Vector3.Distance(closestPoint, inkPositionPosition);

#if !UNITY_ANDROID
                inkPositionChild.position = Vector3.MoveTowards(closestPoint, inkPositionPosition, inkWidth / 1.999f);
#else
                inkPositionChild.position = Vector3.MoveTowards(closestPoint, inkPositionPosition, inkWidth / 1.9f);
#endif

                if (distance > surftraceMaxDistance)
                    ExitSurftraceMode();
            }

            if (!isPointerEnabled)
            {
                if (isUser)
                    trailRenderer.transform.SetPositionAndRotation(
                        Vector3.Lerp(trailRenderer.transform.position, inkPositionChild.position, Time.deltaTime * followSpeed),
                        Quaternion.Lerp(trailRenderer.transform.rotation, inkPositionChild.rotation, Time.deltaTime * followSpeed));
                else
                    trailRenderer.transform.SetPositionAndRotation(inkPositionChild.position, inkPositionChild.rotation);
            }
        }

        private readonly Collider[] results = new Collider[4];
        public override void PostLateUpdate()
        {
            if (!isUser)
                return;

            if (isPointerEnabled)
            {
                var count = Physics.OverlapSphereNonAlloc(pointer.position, pointerRadius, results, 1 << inkColliderLayer, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++)
                {
                    var other = results[i];

                    if (other && other.transform.parent && other.transform.parent.parent)
                    {
                        if (canBeErasedWithOtherPointers
                          ? other.transform.parent.parent.parent && other.transform.parent.parent.parent.parent == inkPoolRoot
                          : other.transform.parent.parent.parent == inkPool
                        )
                        {
                            var lineRenderer = other.GetComponentInParent<LineRenderer>();
                            if (lineRenderer && lineRenderer.positionCount > 0)
                            {
                                var data = new Vector3[GetFooterSize(MODE_ERASE)];

                                SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, MODE_ERASE, GetFooterSize(MODE_ERASE)));
                                SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
                                SetData(data, FOOTER_ELEMENT_ERASE_POINTER_POSITION, pointer.position);
                                SetData(data, FOOTER_ELEMENT_ERASE_POINTER_RADIUS, Vector3.right * pointerRadius);

                                _SendData(data);
                            }
                        }
                        //else if (
                        //    false
                        //)
                        //{
                        //
                        //}
                    }

                    results[i] = null;
                }
            }
        }

        // Surftrace mode
        private bool useSurftraceMode = true;

        private const float surftraceMaxDistance = 1f;
        private const float surftraceEnterDistance = 0.05f;

        private int surftraceMask = ~0;

        private Collider surftraceTarget = null;
        private bool isSurftraceMode => surftraceTarget;

        private void OnTriggerEnter(Collider other)
        {
            if (isUser && useSurftraceMode && VRC.SDKBase.Utilities.IsValid(other) && !other.isTrigger)
            {
                if ((1 << other.gameObject.layer & surftraceMask) == 0)
                    return;

                // other.GetType().IsSubclassOf(typeof(MeshCollider))
                if (other.GetType() == typeof(MeshCollider) && !((MeshCollider)other).convex)
                    return;

                var distance = Vector3.Distance(other.ClosestPoint(inkPosition.position), inkPosition.position);
                if (distance < surftraceEnterDistance)
                    EnterSurftraceMode(other);
            }
        }

        private void EnterSurftraceMode(Collider target)
        {
            surftraceTarget = target;
            marker.enabled = true;
        }

        private void ExitSurftraceMode()
        {
            surftraceTarget = null;

#if !UNITY_ANDROID
            if (!isScreenMode)
#endif
                marker.enabled = false;

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));

            inkPositionChild.localPosition = Vector3.zero;
            inkPositionChild.localRotation = Quaternion.identity;
            trailRenderer.transform.SetPositionAndRotation(inkPositionChild.position, inkPositionChild.rotation);
        }

        #endregion Unity events

        #region VRChat events

        public override void OnPickup()
        {
            isUser = true;

            manager._TakeOwnership();
            manager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        public override void OnDrop()
        {
            isUser = false;

            manager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.EndUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));

            manager._ClearSyncBuffer();

#if !UNITY_ANDROID
            ExitScreenMode();
#endif
            ExitSurftraceMode();
        }

        public override void OnPickupUseDown()
        {
            if (useDoubleClick
             && Time.time - prevClickTime < clickTimeInterval
             && Vector3.Distance(inkPosition.position, prevClickPos) < clickPosInterval
            )
            {
                prevClickTime = 0f;
                switch (currentState)
                {
                    case StatePenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(DestroyJustBeforeInk));
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                        break;
                    case StateEraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                        break;
                    default:
                        Error($"Unexpected state : {nameofCurrentState} at {nameof(OnPickupUseDown)} Double Clicked");
                        break;
                }
            }
            else
            {
                prevClickTime = Time.time;
                prevClickPos = inkPosition.position;
                switch (currentState)
                {
                    case StatePenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenUsing));
                        break;
                    case StateEraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseUsing));
                        break;
                    default:
                        Error($"Unexpected state : {nameofCurrentState} at {nameof(OnPickupUseDown)}");
                        break;
                }
            }
        }

        public override void OnPickupUseUp()
        {
            switch (currentState)
            {
                case StatePenUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                    break;
                case StateEraserUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                    break;
                case StatePenIdle:
                    Log($"Change state : {nameof(StateEraserIdle)} to {nameofCurrentState}");
                    break;
                case StateEraserIdle:
                    Log($"Change state : {nameof(StatePenIdle)} to {nameofCurrentState}");
                    break;
                default:
                    Error($"Unexpected state : {nameofCurrentState} at {nameof(OnPickupUseUp)}");
                    break;
            }
        }

        public void _SetUseDoubleClick(bool value)
        {
            useDoubleClick = value;

            if (isUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        public void _SetEnabledLateSync(bool value)
        {
            enabledLateSync = value;
        }

        public void _SetUseSurftraceMode(bool value)
        {
            useSurftraceMode = value;

            if (isUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        private GameObject justBeforeInk;
        public void DestroyJustBeforeInk()
        {
            if (justBeforeInk)
            {
                Destroy(justBeforeInk);
                justBeforeInk = null;
                inkNo--;
            }
        }

        private void OnEnable()
        {
            if (inkPool)
                inkPool.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (inkPool)
                inkPool.gameObject.SetActive(false);
        }

        private void OnDestroy() => Destroy(inkPool);

        #endregion

        #region ChangeState

        public void ChangeStateToPenIdle()
        {
            switch (currentState)
            {
                case StatePenUsing:
                    FinishDrawing();
                    break;
                case StateEraserIdle:
                    ChangeToPen();
                    break;
                case StateEraserUsing:
                    DisablePointer();
                    ChangeToPen();
                    break;
            }
            currentState = StatePenIdle;
        }

        public void ChangeStateToPenUsing()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    StartDrawing();
                    break;
                case StateEraserIdle:
                    ChangeToPen();
                    StartDrawing();
                    break;
                case StateEraserUsing:
                    DisablePointer();
                    ChangeToPen();
                    StartDrawing();
                    break;
            }
            currentState = StatePenUsing;
        }

        public void ChangeStateToEraseIdle()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    ChangeToEraser();
                    break;
                case StatePenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    break;
                case StateEraserUsing:
                    DisablePointer();
                    break;
            }
            currentState = StateEraserIdle;
        }

        public void ChangeStateToEraseUsing()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    ChangeToEraser();
                    EnablePointer();
                    break;
                case StatePenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    EnablePointer();
                    break;
                case StateEraserIdle:
                    EnablePointer();
                    break;
            }
            currentState = StateEraserUsing;
        }

        #endregion

        [System.NonSerialized]
        public bool pickuped = false; // protected
        public bool isHeld => pickuped;

        public void _Respawn()
        {
            pickup.Drop();

            if (Networking.IsOwner(gameObject))
                objectSync.Respawn();
        }

        public void _Clear()
        {
            foreach (Transform ink in inkPoolSynced)
                Destroy(ink.gameObject);

            foreach (Transform ink in inkPoolNotSynced)
                Destroy(ink.gameObject);

            inkNo = 0;
        }

        private void StartDrawing()
        {
            trailRenderer.gameObject.SetActive(true);
        }

        private void FinishDrawing()
        {
            if (isUser)
            {
                var data = PackData(trailRenderer, currentDrawMode);

                _SendData(data);
            }

            trailRenderer.gameObject.SetActive(false);
            trailRenderer.Clear();
        }

        private Vector3[] PackData(TrailRenderer trailRenderer, int mode)
        {
            if (!trailRenderer)
                return null;

            justBeforeInk = null;

            var positionCount = trailRenderer.positionCount;

            if (positionCount == 0)
                return null;

            var data = new Vector3[positionCount + GetFooterSize(mode)];

            trailRenderer.GetPositions(data);

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, mode, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1f : 0f));

            return data;
        }

        public Vector3[] _PackData(LineRenderer lineRenderer, int mode)
        {
            if (!lineRenderer)
                return null;

            var positionCount = lineRenderer.positionCount;

            if (positionCount == 0)
                return null;

            var data = new Vector3[positionCount + GetFooterSize(mode)];

            lineRenderer.GetPositions(data);

            var inkMeshLayer = (float)lineRenderer.gameObject.layer;
            var inkColliderLayer = (float)lineRenderer.GetComponentInChildren<MeshCollider>(true).gameObject.layer;

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, mode, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1f : 0f));

            return data;
        }

        public void _SendData(Vector3[] data) => manager._SendData(data);

        private void EnablePointer()
        {
            isPointerEnabled = true;
            pointerRenderer.sharedMaterial = pointerMaterialActive;
        }

        private void DisablePointer()
        {
            isPointerEnabled = false;
            pointerRenderer.sharedMaterial = pointerMaterialNormal;
        }

        private void ChangeToPen()
        {
            DisablePointer();
            pointer.gameObject.SetActive(false);
        }

        private void ChangeToEraser()
        {
            pointer.gameObject.SetActive(true);
        }

        public void _UnpackData(Vector3[] data)
        {
            if (data.Length == 0)
                return;

            switch (GetMode(data))
            {
                case MODE_DRAW:
                    CreateInkInstance(data);
                    break;
                case MODE_ERASE:
                    if (isUser && VRCPlayerApi.GetPlayerCount() > 1)
                        tmpErasedData = data;
                    else
                        EraseInk(data);

                    break;
            }
        }

        #region Draw Line

        private void CreateInkInstance(Vector3[] data)
        {
            lineInstance = Instantiate(inkPrefab.gameObject);
            lineInstance.name = $"{inkPrefix} ({inkNo++})";

            var inkInfo = GetData(data, FOOTER_ELEMENT_DRAW_INK_INFO);
            lineInstance.layer = (int)inkInfo.x;
            lineInstance.GetComponentInChildren<MeshCollider>(true).gameObject.layer = (int)inkInfo.y;
            SetParentAndResetLocalTransform(lineInstance.transform, (int)inkInfo.z == 1 ? inkPoolSynced : inkPoolNotSynced);

            var line = lineInstance.GetComponent<LineRenderer>();
            line.positionCount = data.Length - GetFooterLength(data);
            line.SetPositions(data);

            CreateInkCollider(line);

            lineInstance.SetActive(true);

            justBeforeInk = lineInstance;
        }

        private void CreateInkCollider(LineRenderer lineRenderer)
        {
            var inkCollider = lineRenderer.GetComponentInChildren<MeshCollider>(true);
            inkCollider.name = "InkCollider";
            SetParentAndResetLocalTransform(inkCollider.transform, lineInstance.transform);

            var mesh = new Mesh();

            {
                var tmpWidthMultiplier = lineRenderer.widthMultiplier;

                lineRenderer.widthMultiplier = inkWidth;
                lineRenderer.BakeMesh(mesh);
                lineRenderer.widthMultiplier = tmpWidthMultiplier;
            }

            inkCollider.GetComponent<MeshCollider>().sharedMesh = mesh;
            inkCollider.gameObject.SetActive(true);
        }

        #endregion

        #region Erase Line

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

        #endregion

        #region Utility

        private Gradient CreateReverseGradient(Gradient sourceGradient)
        {
            var newGradient = new Gradient();
            var newColorKeys = new GradientColorKey[sourceGradient.colorKeys.Length];
            var newAlphaKeys = new GradientAlphaKey[sourceGradient.alphaKeys.Length];

            {
                var length = newColorKeys.Length;
                for (var i = 0; i < length; i++)
                {
                    var colorKey = sourceGradient.colorKeys[length - 1 - i];
                    newColorKeys[i] = new GradientColorKey(colorKey.color, 1f - colorKey.time);
                }
            }

            {
                var length = newAlphaKeys.Length;
                for (var i = 0; i < length; i++)
                {
                    var alphaKey = sourceGradient.alphaKeys[length - 1 - i];
                    newAlphaKeys[i] = new GradientAlphaKey(alphaKey.alpha, 1f - alphaKey.time);
                }
            }

            newGradient.SetKeys(newColorKeys, newAlphaKeys);

            return newGradient;
        }

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
                ? (_logPrefix = $"[{ColorBeginTag(logColor)}{nameof(QvPen)}.{nameof(QvPen.Udon)}.{nameof(QvPen_Pen)}{ColorEndTag}] ") : _logPrefix;

        private string ToHtmlStringRGB(Color c)
        {
            c *= 0xff;
            return $"{Mathf.RoundToInt(c.r):x2}{Mathf.RoundToInt(c.g):x2}{Mathf.RoundToInt(c.b):x2}";
        }

        #endregion
    }
}
