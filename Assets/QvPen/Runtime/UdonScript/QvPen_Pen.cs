
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(10)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class QvPen_Pen : UdonSharpBehaviour
    {
        public const string version = "v3.3.5";

        #region Field

        [Header("Pen")]
        [SerializeField]
        private TrailRenderer trailRenderer;
        [SerializeField]
        private LineRenderer inkPrefab;

        [SerializeField]
        private Transform inkPosition;
        [SerializeField]
        private Transform inkPositionChild;

        [SerializeField]
        private Transform inkPoolRoot;
        private Transform inkPool;
        private Transform inkPoolSynced;
        private Transform inkPoolNotSynced;

        private QvPen_Manager manager;

        [SerializeField]
        private QvPen_LateSync syncer;

        [Header("Pointer")]
        [SerializeField]
        private Transform pointer;

        private bool _isCheckedPointerRadius = false;
        private float _pointerRadius = 0f;
        private float pointerRadius
        {
            get
            {
                if (_isCheckedPointerRadius)
                {
                    return _pointerRadius;
                }
                else
                {
                    var sphereCollider = pointer.GetComponent<SphereCollider>();
                    sphereCollider.enabled = false;
                    var s = pointer.lossyScale;
                    _pointerRadius = Mathf.Max(0.01f, Mathf.Min(s.x, s.y, s.z)) * sphereCollider.radius;
                    _isCheckedPointerRadius = true;
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
            => Utilities.IsValid(_inkPrefabCollider)
                ? _inkPrefabCollider : (_inkPrefabCollider = inkPrefab.GetComponentInChildren<MeshCollider>(true));
        //private GameObject lineInstance;

        private bool isUser;
        public bool IsUser => isUser;

        // Components
        private VRCPickup _pickup;
        private VRCPickup pickup
            => Utilities.IsValid(_pickup)
                ? _pickup : (_pickup = (VRCPickup)GetComponent(typeof(VRCPickup)));

        private VRCObjectSync _objectSync;
        private VRCObjectSync objectSync
            => Utilities.IsValid(_objectSync)
                ? _objectSync : (_objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync)));

        // PenManager
        private QvPen_PenManager penManager;

        // Ink
        private int inkMeshLayer;
        private int inkColliderLayer;
        private const float followSpeed = 32f;

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
        private QvPen_Pen_State currentState = QvPen_Pen_State.PenIdle;

        // Sync state
        [System.NonSerialized]
        public QvPen_Pen_SyncState currentSyncState = QvPen_Pen_SyncState.Idle;

        // Ink pool
        public const string inkPoolRootName = "QvPen_Objects";
        public const string inkPoolName = "InkPool";
        private int penId;
        public Vector3 penIdVector { get; private set; }
        private string penIdString;

        private const string inkPrefix = "Ink";
        private float inkWidth;
        bool isRoundedTrailShader = false;
        MaterialPropertyBlock propertyBlock;

        private VRCPlayerApi _localPlayer;
        private VRCPlayerApi localPlayer => _localPlayer ?? (_localPlayer = Networking.LocalPlayer);

        private bool _isCheckedLocalPlayerId = false;
        private int _localPlayerId;
        private int localPlayerId
            => _isCheckedLocalPlayerId
                ? _localPlayerId
                : (_isCheckedLocalPlayerId = Utilities.IsValid(localPlayer))
                    ? _localPlayerId = localPlayer.playerId
                    : 0;

        private bool _isCheckedLocalPlayerIdVector = false;
        private Vector3 _localPlayerIdVector;
        private Vector3 localPlayerIdVector
        {
            get
            {
                if (_isCheckedLocalPlayerIdVector)
                    return _localPlayerIdVector;

                _localPlayerIdVector = QvPenUtilities.GetPlayerIdVector(localPlayerId);
                _isCheckedLocalPlayerIdVector = true;
                return _localPlayerIdVector;
            }
        }

        private bool _isCheckedIsUserInVR = false;
        private bool _isUserInVR;
        private bool isUserInVR => _isCheckedIsUserInVR
            ? _isUserInVR
            : (_isCheckedIsUserInVR = Utilities.IsValid(localPlayer)) && (_isUserInVR = localPlayer.IsUserInVR());

        //private long TimeStamp => ((System.DateTimeOffset)Networking.GetNetworkDateTime()).ToUnixTimeSeconds();

        private readonly DataList localInkHistory = new DataList();

        #endregion Field

        public void _Init(QvPen_PenManager penManager)
        {
            this.penManager = penManager;
            _UpdateInkData();

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

            inkPool = syncer.transform;
            QvPenUtilities.SetParentAndResetLocalTransform(inkPool, inkPoolRoot);

            var unique = Networking.GetUniqueName(gameObject);
            penId = string.IsNullOrEmpty(unique) ? 0 : unique.GetHashCode();
            penIdVector = QvPenUtilities.Int32ToVector3(penId);
            penIdString = $"0x{(int)penIdVector.x:x2}{(int)penIdVector.y:x3}{(int)penIdVector.z:x3}";
            inkPool.name = $"{inkPoolName} ({penIdString})";

            manager = inkPoolRoot.GetComponent<QvPen_Manager>();
            manager.Register(penId, this);

            syncer.pen = this;

            inkPoolSynced = syncer.InkPoolSynced;
            inkPoolNotSynced = syncer.InkPoolNotSynced;

#if !UNITY_EDITOR
            Log($"QvPen ID: {penIdString}");
#endif

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
            inkWidth = penManager.inkWidth;
            inkMeshLayer = penManager.inkMeshLayer;
            inkColliderLayer = penManager.inkColliderLayer;

            inkPrefab.gameObject.layer = inkMeshLayer;
            trailRenderer.gameObject.layer = inkMeshLayer;
            inkPrefabCollider.gameObject.layer = inkColliderLayer;

#if UNITY_ANDROID
            var material = penManager.questInkMaterial;
            inkPrefab.material = material;
            trailRenderer.material = material;
            inkPrefab.widthMultiplier = inkWidth;
            trailRenderer.widthMultiplier = inkWidth;
#else
            var material = penManager.pcInkMaterial;

            inkPrefab.material = material;
            trailRenderer.material = material;

            if (Utilities.IsValid(material))
            {
                var shader = material.shader;
                if (Utilities.IsValid(shader))
                {
                    isRoundedTrailShader = shader == penManager.roundedTrailShader;
                    isRoundedTrailShader |= shader.name.Contains("rounded_trail");
                }
            }

            if (isRoundedTrailShader)
            {
                inkPrefab.widthMultiplier = 0f;
                propertyBlock = new MaterialPropertyBlock();
                inkPrefab.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat("_Width", inkWidth);
                inkPrefab.SetPropertyBlock(propertyBlock);

                trailRenderer.widthMultiplier = 0f;
                propertyBlock.Clear();
                trailRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat("_Width", inkWidth);
                trailRenderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                inkPrefab.widthMultiplier = inkWidth;
                trailRenderer.widthMultiplier = inkWidth;
            }
#endif

            inkPrefab.colorGradient = penManager.colorGradient;
            trailRenderer.colorGradient = penManager.colorGradient;

            surftraceMask = penManager.surftraceMask;
        }

        public bool _CheckId(Vector3 idVector)
            => idVector == penIdVector;

        #region Data protocol

        #region Base

        // Footer element
        public const int FOOTER_ELEMENT_DATA_INFO = 0;
        public const int FOOTER_ELEMENT_PEN_ID = 1;
        public const int FOOTER_ELEMENT_INK_ID = 2;
        public const int FOOTER_ELEMENT_OWNER_ID = 3;

        public const int FOOTER_ELEMENT_ANY_LENGTH = 4;

        public const int FOOTER_ELEMENT_DRAW_INK_INFO = 4;
        public const int FOOTER_ELEMENT_DRAW_LENGTH = 5;

        public const int FOOTER_ELEMENT_ERASE_LENGTH = 4;
        public const int FOOTER_ELEMENT_ERASE_USER_INK_LENGTH = 4;

        private static int GetFooterSize(QvPen_Pen_Mode mode)
        {
            switch (mode)
            {
                case QvPen_Pen_Mode.Draw: return FOOTER_ELEMENT_DRAW_LENGTH;
                case QvPen_Pen_Mode.Erase: return FOOTER_ELEMENT_ERASE_LENGTH;
                case QvPen_Pen_Mode.EraseUserInk: return FOOTER_ELEMENT_ERASE_USER_INK_LENGTH;
                case QvPen_Pen_Mode.Any: return FOOTER_ELEMENT_ANY_LENGTH;
                case QvPen_Pen_Mode.None: return 0;
                default: return 0;
            }
        }

        #endregion

        private static Vector3 GetData(Vector3[] data, int index)
            => data != null && data.Length > index ? data[data.Length - 1 - index] : default;

        private static void SetData(Vector3[] data, int index, Vector3 element)
        {
            if (data != null && data.Length > index)
                data[data.Length - 1 - index] = element;
        }

        private static QvPen_Pen_Mode GetMode(Vector3[] data)
            => data != null && data.Length > 0 ? (QvPen_Pen_Mode)(int)GetData(data, FOOTER_ELEMENT_DATA_INFO).y : QvPen_Pen_Mode.None;

        private static int GetFooterLength(Vector3[] data)
            => data != null && data.Length > 0 ? Mathf.Clamp((int)GetData(data, FOOTER_ELEMENT_DATA_INFO).z, 0, data.Length) : 0;

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

            if (Input.GetKeyUp(KeyCode.Tab))
            {
                ExitScreenMode();
            }

            if (!Input.anyKey)
                return;

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                _UndoDraw();
            }
            else if (Input.GetKeyDown(KeyCode.Tab))
            {
                EnterScreenMode();
            }
            else if (Input.GetKey(KeyCode.Tab))
            {
                if (Input.GetKeyDown(KeyCode.Delete))
                {
                    penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Clear));
                }
                else if (Input.GetKey(KeyCode.Home))
                {
                    penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Respawn));
                }
                else if (Input.GetKey(KeyCode.UpArrow))
                {
                    sensitivity = Mathf.Min(sensitivity + 0.001f, 5.0f);
                    Log($"Sensitivity -> {sensitivity:f3}");
                }
                else if (Input.GetKey(KeyCode.DownArrow))
                {
                    sensitivity = Mathf.Max(sensitivity - 0.001f, 0.01f);
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

            inkPositionChild.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
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

        private readonly Collider[] results4 = new Collider[4];
        public override void PostLateUpdate()
        {
            if (!isUser)
                return;

            if (isPointerEnabled)
            {
                var count = Physics.OverlapSphereNonAlloc(pointer.position, pointerRadius, results4, 1 << inkColliderLayer, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++)
                {
                    var other = results4[i];

                    Transform t1, t2, t3;

                    if (Utilities.IsValid(other)
                        && Utilities.IsValid(t1 = other.transform.parent)
                        && Utilities.IsValid(t2 = t1.parent))
                    {
                        if (canBeErasedWithOtherPointers
                          ? Utilities.IsValid(t3 = t2.parent) && t3.parent == inkPoolRoot
                          : t2.parent == inkPool
                        )
                        {
                            var lineRenderer = other.GetComponentInParent<LineRenderer>();
                            if (Utilities.IsValid(lineRenderer) && lineRenderer.positionCount > 0)
                            {
                                SendEraseInk(lineRenderer.gameObject);
                            }
                        }
                    }

                    results4[i] = null;
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
            if (isUser && useSurftraceMode && Utilities.IsValid(other) && !other.isTrigger)
            {
                if ((1 << other.gameObject.layer & surftraceMask) == 0)
                    return;

                //if (other.GetType().IsSubclassOf(typeof(MeshCollider)) && !((MeshCollider)other).convex)
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

            inkPositionChild.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            trailRenderer.transform.SetPositionAndRotation(inkPositionChild.position, inkPositionChild.rotation);
        }

        #endregion Unity events

        #region VRChat events

        public override void OnPickup()
        {
            isUser = true;

            manager.SetLastUsedPen(this);

            penManager.OnPenPickup();

            penManager._TakeOwnership();
            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        public override void OnDrop()
        {
            isUser = false;

            penManager.OnPenDrop();

            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.EndUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));

            penManager._ClearSyncBuffer();

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
                    case QvPen_Pen_State.PenIdle:
                        if (Vector3.Distance(inkPosition.position, prevClickPos) > 0f)
                            _UndoDraw();

                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                        break;
                    case QvPen_Pen_State.EraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                        break;
                    default:
                        Error($"Unexpected state : {currentState.ToStr()} at {nameof(OnPickupUseDown)} Double Clicked");
                        break;
                }
            }
            else
            {
                prevClickTime = Time.time;
                prevClickPos = inkPosition.position;
                switch (currentState)
                {
                    case QvPen_Pen_State.PenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenUsing));
                        break;
                    case QvPen_Pen_State.EraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseUsing));
                        InteractOtherUdon();
                        break;
                    default:
                        Error($"Unexpected state : {currentState.ToStr()} at {nameof(OnPickupUseDown)}");
                        break;
                }
            }
        }

        public override void OnPickupUseUp()
        {
            switch (currentState)
            {
                case QvPen_Pen_State.PenUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                    break;
                case QvPen_Pen_State.EraserUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                    break;
                case QvPen_Pen_State.PenIdle:
                    Log($"Change state : {nameof(QvPen_Pen_State.EraserIdle)} to {currentState.ToStr()}");
                    break;
                case QvPen_Pen_State.EraserIdle:
                    Log($"Change state : {nameof(QvPen_Pen_State.PenIdle)} to {currentState.ToStr()}");
                    break;
                default:
                    Error($"Unexpected state : {currentState.ToStr()} at {nameof(OnPickupUseUp)}");
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

        private void OnEnable()
        {
            if (Utilities.IsValid(inkPool))
                inkPool.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (Utilities.IsValid(inkPool))
                inkPool.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            _Clear();

            if (Utilities.IsValid(inkPool))
                Destroy(inkPool.gameObject);
        }

        #endregion

        #region ChangeState

        public void ChangeStateToPenIdle()
        {
            switch (currentState)
            {
                case QvPen_Pen_State.PenUsing:
                    FinishDrawing();
                    break;
                case QvPen_Pen_State.EraserIdle:
                    ChangeToPen();
                    break;
                case QvPen_Pen_State.EraserUsing:
                    DisablePointer();
                    ChangeToPen();
                    break;
            }
            currentState = QvPen_Pen_State.PenIdle;
        }

        public void ChangeStateToPenUsing()
        {
            switch (currentState)
            {
                case QvPen_Pen_State.PenIdle:
                    StartDrawing();
                    break;
                case QvPen_Pen_State.EraserIdle:
                    ChangeToPen();
                    StartDrawing();
                    break;
                case QvPen_Pen_State.EraserUsing:
                    DisablePointer();
                    ChangeToPen();
                    StartDrawing();
                    break;
            }
            currentState = QvPen_Pen_State.PenUsing;
        }

        public void ChangeStateToEraseIdle()
        {
            switch (currentState)
            {
                case QvPen_Pen_State.PenIdle:
                    ChangeToEraser();
                    break;
                case QvPen_Pen_State.PenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    break;
                case QvPen_Pen_State.EraserUsing:
                    DisablePointer();
                    break;
            }
            currentState = QvPen_Pen_State.EraserIdle;
        }

        public void ChangeStateToEraseUsing()
        {
            switch (currentState)
            {
                case QvPen_Pen_State.PenIdle:
                    ChangeToEraser();
                    EnablePointer();
                    break;
                case QvPen_Pen_State.PenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    EnablePointer();
                    break;
                case QvPen_Pen_State.EraserIdle:
                    EnablePointer();
                    break;
            }
            currentState = QvPen_Pen_State.EraserUsing;
        }

        #endregion

        public bool _TakeOwnership()
        {
            if (Networking.IsOwner(gameObject))
            {
                return true;
            }
            else
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                return Networking.IsOwner(gameObject);
            }
        }

        [System.NonSerialized]
        public bool isPickedUp = false; // protected
        public bool isHeld => isPickedUp;

        public void _Respawn()
        {
            pickup.Drop();

            if (Networking.IsOwner(gameObject))
                objectSync.Respawn();
        }

        public void _Clear()
        {
            manager.Clear(penId);
        }

        public void _EraseOwnInk()
        {
            _TakeOwnership();
            SendEraseOwnInk();
        }

        public void _UndoDraw()
        {
            _TakeOwnership();
            SendUndoDraw();
        }

        private void StartDrawing()
        {
            trailRenderer.gameObject.SetActive(true);
        }

        private void FinishDrawing()
        {
            if (isUser)
            {
                var inkId = penManager.InkId;

                var inkIdVector = QvPenUtilities.Int32ToVector3(inkId);
                var data = PackData(trailRenderer, QvPen_Pen_Mode.Draw, inkIdVector, localPlayerIdVector);

                AddLocalInkHistory(inkId);

                penManager._IncrementInkId();

                _SendData(data);
            }

            trailRenderer.gameObject.SetActive(false);
            trailRenderer.Clear();
        }

        private Vector3[] PackData(TrailRenderer trailRenderer, QvPen_Pen_Mode mode, Vector3 inkIdVector, Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(trailRenderer))
                return null;

            var positionCount = trailRenderer.positionCount;

            if (positionCount == 0)
                return null;

            var positions = new Vector3[positionCount];

            trailRenderer.GetPositions(positions);

            System.Array.Reverse(positions);

            var data = new Vector3[positionCount + GetFooterSize(mode)];

            System.Array.Copy(positions, data, positionCount);

            var modeAsInt = (int)mode; // Compiler bug

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, modeAsInt, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, ownerIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1f : 0f));

            return data;
        }

        public Vector3[] _PackData(LineRenderer lineRenderer, QvPen_Pen_Mode mode, Vector3 inkIdVector, Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(lineRenderer))
                return null;

            var positionCount = lineRenderer.positionCount;

            if (positionCount == 0)
                return null;

            var positions = new Vector3[positionCount];

            lineRenderer.GetPositions(positions);

            System.Array.Reverse(positions);

            var data = new Vector3[positionCount + GetFooterSize(mode)];

            System.Array.Copy(positions, data, positionCount);

            var inkMeshLayer = lineRenderer.gameObject.layer;
            var inkColliderLayer = lineRenderer.GetComponentInChildren<MeshCollider>(true).gameObject.layer;

            var modeAsInt = (int)mode; // Compiler bug

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3Int(localPlayerId, modeAsInt, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, ownerIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3Int(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1 : 0));

            return data;
        }

        public void _SendData(Vector3[] data) => penManager._SendData(data);

        private void EnablePointer()
        {
            isPointerEnabled = true;

            if (Utilities.IsValid(pointerRenderer))
                pointerRenderer.sharedMaterial = pointerMaterialActive;
        }

        private void DisablePointer()
        {
            isPointerEnabled = false;

            if (Utilities.IsValid(pointerRenderer))
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

        public void _UnpackData(Vector3[] data, QvPen_Pen_Mode targetMode)
        {
            var mode = GetMode(data);

            if (targetMode != QvPen_Pen_Mode.Any && mode != targetMode)
                return;

            switch (mode)
            {
                case QvPen_Pen_Mode.Draw:
                    CreateInkInstance(data);
                    break;
                case QvPen_Pen_Mode.Erase:
                    EraseInk(data);
                    break;
                case QvPen_Pen_Mode.EraseUserInk:
                    EraseUserInk(data);
                    break;
            }
        }

        public void _EraseAbandonedInk(Vector3[] data)
        {
            var mode = GetMode(data);

            if (mode != QvPen_Pen_Mode.Draw)
                return;

            EraseInk(data);
        }

        private void AddLocalInkHistory(int inkId)
        {
            if (localInkHistory.Count > 1024)
                localInkHistory.RemoveAt(0);

            localInkHistory.Add(inkId);
        }

        private bool TryGetLastLocalInk(out int inkId)
        {
            for (int i = localInkHistory.Count - 1; i >= 0; i--)
            {
                if (!localInkHistory.TryGetValue(i, TokenType.Int, out var inkIdToken))
                    continue;

                inkId = inkIdToken.Int;

                if (!manager.HasInk(penId, inkId))
                {
                    localInkHistory.RemoveAt(i);
                    continue;
                }

                return true;
            }

            inkId = default;
            return false;
        }

        #region Draw Line

        private void CreateInkInstance(Vector3[] data)
        {
            var penIdVector = GetData(data, FOOTER_ELEMENT_PEN_ID);
            var inkIdVector = GetData(data, FOOTER_ELEMENT_INK_ID);

            var penId = QvPenUtilities.Vector3ToInt32(penIdVector);
            var inkId = QvPenUtilities.Vector3ToInt32(inkIdVector);

            if (manager.HasInk(penId, inkId))
                return;

            var playerIdVector = GetData(data, FOOTER_ELEMENT_OWNER_ID);

            var lineInstance = Instantiate(inkPrefab.gameObject);
            lineInstance.name = $"{inkPrefix} ({inkId})";

            if (!QvPenUtilities.TrySetIdFromInk(lineInstance, penIdVector, inkIdVector, playerIdVector))
            {
                Warning($"Failed TrySetIdFromInk pen: {penId}, ink: {inkId}");
                Destroy(lineInstance);
                return;
            }

            manager.SetInk(penId, inkId, lineInstance);

            var inkInfo = GetData(data, FOOTER_ELEMENT_DRAW_INK_INFO);
            lineInstance.layer = (int)inkInfo.x;
            lineInstance.GetComponentInChildren<MeshCollider>(true).gameObject.layer = (int)inkInfo.y;
            QvPenUtilities.SetParentAndResetLocalTransform(
                lineInstance.transform, (int)inkInfo.z == 1 ? inkPoolSynced : inkPoolNotSynced);

            var positionCount = data.Length - GetFooterLength(data);

            var line = lineInstance.GetComponent<LineRenderer>();

            line.positionCount = positionCount;
            line.SetPositions(data);

#if !UNITY_ANDROID
            if (isRoundedTrailShader)
            {
                if (!Utilities.IsValid(propertyBlock))
                    propertyBlock = new MaterialPropertyBlock();
                else
                    propertyBlock.Clear();

                line.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat("_Width", inkWidth);
                line.SetPropertyBlock(propertyBlock);
            }
            else
            {
                line.widthMultiplier = inkWidth;
            }
#endif

            CreateInkCollider(line);

            lineInstance.SetActive(true);
        }

        private void CreateInkCollider(LineRenderer lineRenderer)
        {
            var inkCollider = lineRenderer.GetComponentInChildren<MeshCollider>(true);
            inkCollider.name = "InkCollider";

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

        private void SendEraseInk(Vector3 penIdVector, Vector3 inkIdVector)
        {
            var data = new Vector3[GetFooterSize(QvPen_Pen_Mode.Erase)];

            SetData(data, FOOTER_ELEMENT_DATA_INFO,
                new Vector3(localPlayerId, (int)QvPen_Pen_Mode.Erase, GetFooterSize(QvPen_Pen_Mode.Erase)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);

            _SendData(data);
        }

        private void SendEraseInk(int penId, int inkId)
        {
            SendEraseInk(QvPenUtilities.Int32ToVector3(penId), QvPenUtilities.Int32ToVector3(inkId));
        }

        private void SendEraseInk(GameObject ink)
        {
            if (Utilities.IsValid(ink)
             && QvPenUtilities.TryGetIdFromInk(ink, out var penIdVector, out var inkIdVector, out var _discard))
            {
                SendEraseInk(penIdVector, inkIdVector);
            }
        }

        private void SendEraseOwnInk()
        {
            var data = new Vector3[GetFooterSize(QvPen_Pen_Mode.EraseUserInk)];

            SetData(data, FOOTER_ELEMENT_DATA_INFO,
                new Vector3(localPlayerId, (int)QvPen_Pen_Mode.EraseUserInk, GetFooterSize(QvPen_Pen_Mode.EraseUserInk)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, localPlayerIdVector);

            _SendData(data);
        }

        private void SendUndoDraw()
        {
            if (!TryGetLastLocalInk(out var inkId))
                return;

            SendEraseInk(penId, inkId);
        }

        private void EraseInk(Vector3[] data)
        {
            if (data.Length < GetFooterSize(QvPen_Pen_Mode.Erase))
                return;

            var penIdVector = GetData(data, FOOTER_ELEMENT_PEN_ID);
            var inkIdVector = GetData(data, FOOTER_ELEMENT_INK_ID);

            var penId = QvPenUtilities.Vector3ToInt32(penIdVector);
            var inkId = QvPenUtilities.Vector3ToInt32(inkIdVector);

            manager.RemoveInk(penId, inkId);
        }

        private void EraseUserInk(Vector3[] data)
        {
            if (data.Length < GetFooterSize(QvPen_Pen_Mode.EraseUserInk))
                return;

            var ownerIdVector = GetData(data, FOOTER_ELEMENT_OWNER_ID);

            var penId = QvPenUtilities.Vector3ToInt32(penIdVector);

            manager.RemoveUserInk(penId, ownerIdVector);
        }

        #endregion

        #region Tool

        private const string UDON_EVENT_INTERACT = "_interact";

        private readonly Collider[] results32 = new Collider[32];
        private void InteractOtherUdon()
        {
            var count = Physics.OverlapSphereNonAlloc(pointer.position, pointerRadius, results32, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var other = results32[i];

                if (Utilities.IsValid(other))
                {
                    var udonComponents = other.GetComponents(typeof(VRC.Udon.UdonBehaviour));

                    foreach (var udonComponent in udonComponents)
                    {
                        if (!Utilities.IsValid(udonComponent))
                            continue;

                        var udon = (VRC.Udon.UdonBehaviour)udonComponent;
                        udon.SendCustomEvent(UDON_EVENT_INTERACT);
                    }
                }

                results32[i] = null;
            }

            System.Array.Clear(results32, 0, results32.Length);
        }

        #endregion

        #region Log

        private const string appName = nameof(QvPen_Pen);

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

    #region QvPenUtilities

    public static class QvPenUtilities
    {
        public static void SetParentAndResetLocalTransform(Transform child, Transform parent)
        {
            if (!Utilities.IsValid(child))
                return;

            child.SetParent(parent);
            child.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            child.localScale = Vector3.one;
        }

        public static Vector3 Int32ToVector3(int v)
            => new Vector3((v >> 24) & 0x00ff, (v >> 12) & 0x0fff, v & 0x0fff);

        public static int Vector3ToInt32(Vector3 v)
            => ((int)v.x & 0x00ff) << 24 | ((int)v.y & 0x0fff) << 12 | ((int)v.z & 0x0fff);

        public static Vector3 GetPlayerIdVector(int playerId)
        {
            var x = playerId;
            var y = x / 360;
            var z = y / 360;
            return new Vector3(x % 360, y % 360, z % 360);
        }

        public static bool TryGetIdFromInk(GameObject ink,
            out Vector3 penIdVector, out Vector3 inkIdVector, out Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(ink))
            {
                penIdVector = default;
                inkIdVector = default;
                ownerIdVector = default;
                return false;
            }

            if (ink.transform.childCount < 2)
            {
                penIdVector = default;
                inkIdVector = default;
                ownerIdVector = default;
                return false;
            }

            var idHolder = ink.transform.GetChild(1);
            if (!Utilities.IsValid(idHolder))
            {
                penIdVector = default;
                inkIdVector = default;
                ownerIdVector = default;
                return false;
            }

            penIdVector = idHolder.localPosition;
            inkIdVector = idHolder.localScale;
            ownerIdVector = idHolder.localEulerAngles;
            return true;
        }

        public static bool TrySetIdFromInk(GameObject ink,
             Vector3 penIdVector, Vector3 inkIdVector, Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(ink))
                return false;

            if (ink.transform.childCount < 2)
                return false;

            var idHolder = ink.transform.GetChild(1);
            if (!Utilities.IsValid(idHolder))
                return false;

            idHolder.localPosition = penIdVector;
            idHolder.localScale = inkIdVector;
            idHolder.localEulerAngles = ownerIdVector;
            return true;
        }
    }

    #endregion

    #region Enum

    public enum QvPen_Pen_SyncState
    {
        Idle,
        Started,
        Finished
    }

    enum QvPen_Pen_State
    {
        PenIdle,
        PenUsing,
        EraserIdle,
        EraserUsing
    }

    public enum QvPen_Pen_Mode
    {
        None,
        Any,
        Draw,
        Erase,
        EraseUserInk
    }

    static class QvPen_Pen_Extension
    {
        internal static string ToStr(this QvPen_Pen_State state)
        {
            switch (state)
            {
                case QvPen_Pen_State.PenIdle: return nameof(QvPen_Pen_State.PenIdle);
                case QvPen_Pen_State.PenUsing: return nameof(QvPen_Pen_State.PenUsing);
                case QvPen_Pen_State.EraserIdle: return nameof(QvPen_Pen_State.EraserIdle);
                case QvPen_Pen_State.EraserUsing: return nameof(QvPen_Pen_State.EraserUsing);
                default: return "(QvPen_Pen_State.???)";
            }
        }
    }

    #endregion
}
