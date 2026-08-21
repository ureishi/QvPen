
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.Platform;
using VRC.SDK3.UdonNetworkCalling;
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
        public const string version = "v3.4.0-beta.1";

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

        private bool allowCallPen;
        public bool AllowCallPen => allowCallPen;

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

        [Header("ObjectSync")]
        [SerializeField]
        private UdonSharpBehaviour _alternativeObjectSync;
        [SerializeField]
        private string _respawnEventName = "_Respawn";

        // PenManager
        private QvPen_PenManager penManager;

        // Ink
        private int inkMeshLayer;
        private int inkColliderLayer;
        private int inkColliderLayerMask;
        private const float followSpeed = 32f;

        private Vector3 _inkChildLocalPosFromHand;
        private Quaternion _inkChildLocalRotFromHand;
        private VRCPlayerApi.TrackingDataType _pickupHandTrackingType;
        private bool _hasPickupHandTrackingType;

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
#if UNITY_STANDALONE
        bool isRoundedTrailShader = false;
#endif
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

            allowCallPen = penManager.AllowCallPen;

            manager = inkPoolRoot.GetComponent<QvPen_Manager>();
            manager.RegisterPen(penId, this);

            syncer._RegisterPen(this);

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

#if UNITY_STANDALONE
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
            inkColliderLayerMask = 1 << inkColliderLayer;

            inkPrefab.gameObject.layer = inkMeshLayer;
            trailRenderer.gameObject.layer = inkMeshLayer;
            inkPrefabCollider.gameObject.layer = inkColliderLayer;

#if UNITY_STANDALONE
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
#else
            var material = penManager.questInkMaterial;
            inkPrefab.material = material;
            trailRenderer.material = material;
            inkPrefab.widthMultiplier = inkWidth;
            trailRenderer.widthMultiplier = inkWidth;
#endif

            inkPrefab.colorGradient = penManager.colorGradient;
            trailRenderer.colorGradient = penManager.colorGradient;

            surftraceMask = penManager.surftraceMask;
        }

        public bool _MatchesPenId(Vector3 idVector)
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
        public const int FOOTER_ELEMENT_DRAW_COLOR_INFO = 5;
        public const int FOOTER_ELEMENT_DRAW_LENGTH = 6;

        public const int MIN_GRADIENT_KEY_COUNT = 2;
        public const int MAX_GRADIENT_KEY_COUNT = 8;
        public const int MAX_STROKE_POSITION_COUNT = 16384;
        public const int MAX_GRADIENT_DATA_LENGTH = MAX_GRADIENT_KEY_COUNT * 2 + MAX_GRADIENT_KEY_COUNT;
        public const int MAX_PACKED_DRAW_DATA_LENGTH =
            MAX_STROKE_POSITION_COUNT + MAX_GRADIENT_DATA_LENGTH + FOOTER_ELEMENT_DRAW_LENGTH;

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
            => data[data.Length - 1 - index];

        private static void SetData(Vector3[] data, int index, Vector3 element)
        {
            if (data != null && data.Length > index)
                data[data.Length - 1 - index] = element;
        }

        private static QvPen_Pen_Mode GetMode(Vector3[] data)
            => data != null && data.Length > 0 ? (QvPen_Pen_Mode)(int)GetData(data, FOOTER_ELEMENT_DATA_INFO).y : QvPen_Pen_Mode.None;

        private static int GetFooterLength(Vector3[] data)
            => data != null && data.Length > 0 ? Mathf.Clamp((int)GetData(data, FOOTER_ELEMENT_DATA_INFO).z, 0, data.Length) : 0;

        private static bool IsFiniteNormalized(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;

        private static bool TryGetGradientData(Gradient gradient,
            out GradientColorKey[] colorKeys, out GradientAlphaKey[] alphaKeys)
        {
            colorKeys = gradient.colorKeys;
            alphaKeys = gradient.alphaKeys;

            if (colorKeys == null || alphaKeys == null ||
                colorKeys.Length < MIN_GRADIENT_KEY_COUNT || colorKeys.Length > MAX_GRADIENT_KEY_COUNT ||
                alphaKeys.Length < MIN_GRADIENT_KEY_COUNT || alphaKeys.Length > MAX_GRADIENT_KEY_COUNT)
                return false;

            var mode = (int)gradient.mode;
            if (mode < (int)GradientMode.Blend || mode > (int)GradientMode.Fixed)
                return false;

            var previousTime = -1f;
            for (var i = 0; i < colorKeys.Length; i++)
            {
                var key = colorKeys[i];
                if (!IsFiniteNormalized(key.color.r) || !IsFiniteNormalized(key.color.g) ||
                    !IsFiniteNormalized(key.color.b) || !IsFiniteNormalized(key.time) || key.time < previousTime)
                    return false;

                previousTime = key.time;
            }

            previousTime = -1f;
            for (var i = 0; i < alphaKeys.Length; i++)
            {
                var key = alphaKeys[i];
                if (!IsFiniteNormalized(key.alpha) || !IsFiniteNormalized(key.time) || key.time < previousTime)
                    return false;

                previousTime = key.time;
            }

            return true;
        }

        private static int GetGradientDataLength(int colorKeyCount, int alphaKeyCount)
            => colorKeyCount * 2 + alphaKeyCount;

        private static int WriteGradientData(Vector3[] data, int index,
            GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
        {
            for (var i = 0; i < colorKeys.Length; i++)
            {
                var key = colorKeys[i];
                data[index++] = new Vector3(key.color.r, key.color.g, key.color.b);
                data[index++] = new Vector3(key.time, 0f, 0f);
            }

            for (var i = 0; i < alphaKeys.Length; i++)
            {
                var key = alphaKeys[i];
                data[index++] = new Vector3(key.alpha, key.time, 0f);
            }

            return index;
        }

        #endregion

        #region Unity events

        #region Screen mode

        private bool isScreenMode = false;
        private bool isPickupManipulationMode = false;
        private bool wasPickupManipulationMode = false;
        private const int pickupManipulationReleaseDelayFrames = 10;
        private int pickupManipulationReleaseFrame = -1;

        private float screenWidth = 1920;
        private float screenHeight = 1080;

#if UNITY_STANDALONE
        private VRCPlayerApi.TrackingData headTracking;
        private Vector3 headPos, center;
        private Quaternion headRot;
        private Vector2 _wh, wh, clampWH;
        // Wait for Udon Vector2.Set() bug fix
        private /*readonly*/ Vector2 mouseDelta = new Vector2();
        private float ratio, scalar;

        private float sensitivity = 0.006f;

        private void Update()
        {
            if (!isUser)
                return;

            if (isUserInVR)
                return;

            wasPickupManipulationMode = isPickupManipulationMode;
            isPickupManipulationMode = Input.anyKey &&
               (Input.GetKey(KeyCode.U) || Input.GetKey(KeyCode.I) || Input.GetKey(KeyCode.O) ||
                Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.L) ||
                Input.GetMouseButton(2) || Input.GetAxis("Mouse ScrollWheel") != 0f);

            if (wasPickupManipulationMode && !isPickupManipulationMode)
            {
                pickupManipulationReleaseFrame = Time.frameCount + pickupManipulationReleaseDelayFrames;
            }

            if (Input.GetKeyUp(KeyCode.Tab))
            {
                ExitScreenMode();
            }

            if (!Input.anyKey)
                return;

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                _UndoLastStroke();
            }
            else if (Input.GetKeyDown(KeyCode.Tab))
            {
                EnterScreenMode();
            }
            else if (Input.GetKey(KeyCode.Tab))
            {
                if (Input.GetKeyDown(KeyCode.Delete))
                {
                    _EraseOwnStrokes();
                }
                else if (Input.GetKey(KeyCode.Home))
                {
                    penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Respawn));
                }
                else if (Input.GetKey(KeyCode.UpArrow))
                {
                    sensitivity = Mathf.Min(sensitivity + 0.0001f, 0.01f);
                    Log($"Sensitivity -> {sensitivity:f4}");
                }
                else if (Input.GetKey(KeyCode.DownArrow))
                {
                    sensitivity = Mathf.Max(sensitivity - 0.0001f, 0.001f);
                    Log($"Sensitivity -> {sensitivity:f4}");
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

            clampWH = wh / (2f * screenWidth * 0.98f);
            ratio = 2f * screenHeight / wh.y;
        }

        private void ExitScreenMode()
        {
            isScreenMode = false;

            if (!isSurftraceMode)
                marker.enabled = false;

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));

            inkPositionChild.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            inkPositionChild.GetPositionAndRotation(out var inkPosition, out var inkRotation);
            trailRenderer.transform.SetPositionAndRotation(inkPosition, inkRotation);
        }
#endif
        #endregion Screen mode

        private void LateUpdate()
        {
            if (!isHeld)
                return;

#if UNITY_STANDALONE
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
                _wh += sensitivity * mouseDelta;
                _wh = Vector2.Min(Vector2.Max(_wh, -clampWH), clampWH);

                inkPositionChild.SetPositionAndRotation(center + headRot * _wh * scalar, headRot);
            }
#endif

            if (isSurftraceMode)
            {
                Vector3 inkPositionPosition;
#if UNITY_STANDALONE
                if (isScreenMode)
                    inkPositionPosition = inkPositionChild.position;
                else
#endif
                    inkPositionPosition = inkPosition.position;

                var closestPoint = surftraceTarget.ClosestPoint(inkPositionPosition);
                var distance = Vector3.Distance(closestPoint, inkPositionPosition);

#if UNITY_STANDALONE
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
                {
                    if (isUserInVR)
                    {
                        var deltaDistance = Time.deltaTime * followSpeed;

                        trailRenderer.transform.GetPositionAndRotation(out var trailPos, out var trailRot);
                        inkPositionChild.GetPositionAndRotation(out var inkPos, out var inkRot);

                        trailRenderer.transform.SetPositionAndRotation(
                            Vector3.Lerp(trailPos, inkPos, deltaDistance),
                            Quaternion.Lerp(trailRot, inkRot, deltaDistance));
                    }
                    else
                    {
                        float dt = Time.deltaTime;
                        const float baseDt = 1f / 30f;

                        float t;

                        if (dt <= baseDt)
                        {
                            t = dt * followSpeed;
                        }
                        else
                        {
                            float fpsFactor = baseDt / dt;
                            const float lowFpsSlowdownPower = 1.5f;
                            t = baseDt * followSpeed * Mathf.Pow(fpsFactor, lowFpsSlowdownPower);
                        }

                        trailRenderer.transform.GetPositionAndRotation(out var trailPos, out var trailRot);

                        Vector3 targetPos;
                        Quaternion targetRot;

                        var isPickupManipulationReleaseDelay = Time.frameCount < pickupManipulationReleaseFrame;

                        if (_hasPickupHandTrackingType && !isPickupManipulationMode && !isPickupManipulationReleaseDelay && !isScreenMode && !isSurftraceMode)
                        {
                            var handTracking = localPlayer.GetTrackingData(_pickupHandTrackingType);
                            targetPos = handTracking.position + handTracking.rotation * _inkChildLocalPosFromHand;
                            targetRot = handTracking.rotation * _inkChildLocalRotFromHand;
                        }
                        else
                        {
                            inkPositionChild.GetPositionAndRotation(out targetPos, out targetRot);

                            if (_hasPickupHandTrackingType)
                            {
                                var handTracking = localPlayer.GetTrackingData(_pickupHandTrackingType);
                                var invHandRot = Quaternion.Inverse(handTracking.rotation);
                                _inkChildLocalPosFromHand = invHandRot * (targetPos - handTracking.position);
                                _inkChildLocalRotFromHand = invHandRot * targetRot;
                            }
                        }

                        trailRenderer.transform.SetPositionAndRotation(
                            Vector3.Lerp(trailPos, targetPos, t), Quaternion.Lerp(trailRot, targetRot, t));
                    }
                }
                else
                {
                    inkPositionChild.GetPositionAndRotation(out var inkPosition, out var inkRotation);
                    trailRenderer.transform.SetPositionAndRotation(inkPosition, inkRotation);
                }
            }
        }

        private readonly Collider[] results4 = new Collider[4];
        public override void PostLateUpdate()
        {
            if (!isUser)
                return;

            if (isPointerEnabled)
            {
                var count = Physics.OverlapSphereNonAlloc(pointer.position, pointerRadius, results4, inkColliderLayerMask, QueryTriggerInteraction.Ignore);
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

#if UNITY_STANDALONE
            if (!isScreenMode)
#endif
                marker.enabled = false;

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));

            inkPositionChild.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            inkPositionChild.GetPositionAndRotation(out var inkPosition, out var inkRotation);
            trailRenderer.transform.SetPositionAndRotation(inkPosition, inkRotation);
        }

        #endregion Unity events

        #region VRChat events

        public override void OnPickup()
        {
            isUser = true;

            if (!isUserInVR && pickup.currentHand != VRC_Pickup.PickupHand.None)
            {
                _pickupHandTrackingType = pickup.currentHand == VRC_Pickup.PickupHand.Right
                    ? VRCPlayerApi.TrackingDataType.RightHand
                    : VRCPlayerApi.TrackingDataType.LeftHand;
                _hasPickupHandTrackingType = true;
            }

            manager.SetLastUsedPen(this);

            penManager._OnPenPickup();

            penManager._TakeOwnership();
            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager._MarkAsInUse));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));
        }

        public override void OnDrop()
        {
            isUser = false;
            _hasPickupHandTrackingType = false;

            penManager._OnPenDrop();

            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager._MarkAsAvailable));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));

            penManager._ClearSyncBuffer();

#if UNITY_STANDALONE
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
                            _UndoLastStroke();

                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterEraserIdleState));
                        break;
                    case QvPen_Pen_State.EraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));
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
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterDrawingState));
                        break;
                    case QvPen_Pen_State.EraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterErasingState));
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
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));
                    break;
                case QvPen_Pen_State.EraserUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterEraserIdleState));
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

        public override void OnScreenUpdate(ScreenUpdateData data)
        {
            screenWidth = Mathf.Max(1f, data.resolution.x);
            screenHeight = Mathf.Max(1f, data.resolution.y);
        }

        public void _SetDoubleClickEnabled(bool value)
        {
            useDoubleClick = value;

            if (isUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));
        }

        public void _SetLateSyncEnabled(bool value)
        {
            enabledLateSync = value;
        }

        public void _SetSurftraceEnabled(bool value)
        {
            useSurftraceMode = value;

            if (isUser)
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_EnterPenIdleState));
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

        [NetworkCallable]
        public void _EnterPenIdleState()
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

        [NetworkCallable]
        public void _EnterDrawingState()
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

        [NetworkCallable]
        public void _EnterEraserIdleState()
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

        [NetworkCallable]
        public void _EnterErasingState()
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
            {
                if (Utilities.IsValid(objectSync))
                    objectSync.Respawn();
                else if (Utilities.IsValid(_alternativeObjectSync))
                    _alternativeObjectSync.SendCustomEvent(_respawnEventName);
            }
        }

        public void _Clear()
        {
            manager.Clear(penId);
        }

        public void _EraseOwnStrokes()
        {
            _TakeOwnership();
            SendEraseOwnStrokes();
        }

        public void _UndoLastStroke()
        {
            _TakeOwnership();
            SendUndoLastStroke();
        }

        private void StartDrawing()
        {
            inkPositionChild.transform.GetPositionAndRotation(out var inkPosition, out var inkRotation);
            trailRenderer.transform.SetPositionAndRotation(inkPosition, inkRotation);
            trailRenderer.gameObject.SetActive(true);

            if (isUser && !isUserInVR && _hasPickupHandTrackingType)
            {
                var handTracking = localPlayer.GetTrackingData(_pickupHandTrackingType);
                var invHandRot = Quaternion.Inverse(handTracking.rotation);
                _inkChildLocalPosFromHand = invHandRot * (inkPosition - handTracking.position);
                _inkChildLocalRotFromHand = invHandRot * inkRotation;
            }
        }

        private void FinishDrawing()
        {
            if (isUser)
            {
                var inkId = penManager.InkId;

                var inkIdVector = QvPenUtilities.Int32ToVector3(inkId);
                var data = PackData(trailRenderer, QvPen_Pen_Mode.Draw, inkIdVector, localPlayerIdVector);

                if (data != null)
                {
                    AddLocalInkHistory(inkId);
                    penManager._IncrementInkId();
                    _SendData(data);
                }
            }

            trailRenderer.gameObject.SetActive(false);
            trailRenderer.Clear();
        }

        private Vector3[] PackData(TrailRenderer trailRenderer, QvPen_Pen_Mode mode, Vector3 inkIdVector, Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(trailRenderer) || mode != QvPen_Pen_Mode.Draw)
                return null;

            var positionCount = trailRenderer.positionCount;

            if (positionCount <= 0 || positionCount > MAX_STROKE_POSITION_COUNT)
                return null;

            var gradient = trailRenderer.colorGradient;
            if (!TryGetGradientData(gradient, out var colorKeys, out var alphaKeys))
                return null;

            var gradientDataLength = GetGradientDataLength(colorKeys.Length, alphaKeys.Length);
            var data = new Vector3[positionCount + gradientDataLength + GetFooterSize(mode)];

            trailRenderer.GetPositions(data);

            for (var left = 0; left < positionCount / 2; left++)
            {
                var right = positionCount - 1 - left;
                var position = data[left];
                data[left] = data[right];
                data[right] = position;
            }

            WriteGradientData(data, positionCount, colorKeys, alphaKeys);

            var modeAsInt = (int)mode; // Compiler bug

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3(localPlayerId, modeAsInt, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, ownerIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1f : 0f));
            SetData(data, FOOTER_ELEMENT_DRAW_COLOR_INFO,
                new Vector3(colorKeys.Length, alphaKeys.Length, (int)gradient.mode));

            return data;
        }

        public Vector3[] _PackData(LineRenderer lineRenderer, QvPen_Pen_Mode mode, Vector3 inkIdVector, Vector3 ownerIdVector)
        {
            if (!Utilities.IsValid(lineRenderer) || mode != QvPen_Pen_Mode.Draw)
                return null;

            var positionCount = lineRenderer.positionCount;

            if (positionCount <= 0 || positionCount > MAX_STROKE_POSITION_COUNT)
                return null;

            var gradient = lineRenderer.colorGradient;
            if (!TryGetGradientData(gradient, out var colorKeys, out var alphaKeys))
                return null;

            var gradientDataLength = GetGradientDataLength(colorKeys.Length, alphaKeys.Length);
            var data = new Vector3[positionCount + gradientDataLength + GetFooterSize(mode)];

            lineRenderer.GetPositions(data);
            WriteGradientData(data, positionCount, colorKeys, alphaKeys);

            var inkMeshLayer = lineRenderer.gameObject.layer;
            var inkColliderLayer = lineRenderer.GetComponentInChildren<MeshCollider>(true).gameObject.layer;

            var modeAsInt = (int)mode; // Compiler bug

            SetData(data, FOOTER_ELEMENT_DATA_INFO, new Vector3Int(localPlayerId, modeAsInt, GetFooterSize(mode)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_INK_ID, inkIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, ownerIdVector);
            SetData(data, FOOTER_ELEMENT_DRAW_INK_INFO, new Vector3Int(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1 : 0));
            SetData(data, FOOTER_ELEMENT_DRAW_COLOR_INFO,
                new Vector3(colorKeys.Length, alphaKeys.Length, (int)gradient.mode));

            return data;
        }

        public bool _PackDrawDataInto(LineRenderer lineRenderer, Vector3 inkIdVector, Vector3 ownerIdVector,
            Vector3[] positionBuffer, Vector3[] destination, int destinationIndex)
        {
            if (!Utilities.IsValid(lineRenderer) || positionBuffer == null || destination == null)
                return false;

            var positionCount = lineRenderer.positionCount;
            var gradient = lineRenderer.colorGradient;
            if (!TryGetGradientData(gradient, out var colorKeys, out var alphaKeys))
                return false;

            var gradientDataLength = GetGradientDataLength(colorKeys.Length, alphaKeys.Length);
            var dataLength = positionCount + gradientDataLength + FOOTER_ELEMENT_DRAW_LENGTH;

            if (positionCount <= 0 || positionCount > MAX_STROKE_POSITION_COUNT ||
                positionBuffer.Length < positionCount ||
                destinationIndex < 0 || destinationIndex + dataLength > destination.Length)
                return false;

            lineRenderer.GetPositions(positionBuffer);
            System.Array.Copy(positionBuffer, 0, destination, destinationIndex, positionCount);
            WriteGradientData(destination, destinationIndex + positionCount, colorKeys, alphaKeys);

            var dataEnd = destinationIndex + dataLength;
            var inkMeshLayer = lineRenderer.gameObject.layer;
            var inkColliderLayer = lineRenderer.GetComponentInChildren<MeshCollider>(true).gameObject.layer;

            destination[dataEnd - 1 - FOOTER_ELEMENT_DATA_INFO] =
                new Vector3Int(localPlayerId, (int)QvPen_Pen_Mode.Draw, FOOTER_ELEMENT_DRAW_LENGTH);
            destination[dataEnd - 1 - FOOTER_ELEMENT_PEN_ID] = penIdVector;
            destination[dataEnd - 1 - FOOTER_ELEMENT_INK_ID] = inkIdVector;
            destination[dataEnd - 1 - FOOTER_ELEMENT_OWNER_ID] = ownerIdVector;
            destination[dataEnd - 1 - FOOTER_ELEMENT_DRAW_INK_INFO] =
                new Vector3Int(inkMeshLayer, inkColliderLayer, enabledLateSync ? 1 : 0);
            destination[dataEnd - 1 - FOOTER_ELEMENT_DRAW_COLOR_INFO] =
                new Vector3(colorKeys.Length, alphaKeys.Length, (int)gradient.mode);

            return true;
        }

        public int _GetPackedDrawDataLength(LineRenderer lineRenderer)
        {
            if (!Utilities.IsValid(lineRenderer) || lineRenderer.positionCount <= 0 ||
                lineRenderer.positionCount > MAX_STROKE_POSITION_COUNT)
                return 0;

            var gradient = lineRenderer.colorGradient;
            if (!TryGetGradientData(gradient, out var colorKeys, out var alphaKeys))
                return 0;

            return lineRenderer.positionCount + GetGradientDataLength(colorKeys.Length, alphaKeys.Length)
                + FOOTER_ELEMENT_DRAW_LENGTH;
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
                    if (!TryGetDrawLayout(data, out var positionCount, out var colorKeyCount,
                        out var alphaKeyCount, out var gradientMode))
                        return;

                    CreateInkInstance(data, positionCount, colorKeyCount, alphaKeyCount, gradientMode);
                    break;
                case QvPen_Pen_Mode.Erase:
                    if (!IsValidFixedLengthProtocolData(data, mode))
                        return;

                    EraseInk(data);
                    break;
                case QvPen_Pen_Mode.EraseUserInk:
                    if (!IsValidFixedLengthProtocolData(data, mode))
                        return;

                    EraseUserInk(data);
                    break;
            }
        }

        public bool _IsDrawData(Vector3[] data)
            => GetMode(data) == QvPen_Pen_Mode.Draw;

        public bool _IsSameEraseOperation(Vector3[] first, Vector3[] second)
        {
            var mode = GetMode(first);
            if (mode != GetMode(second) || !IsValidFixedLengthProtocolData(first, mode) ||
                !IsValidFixedLengthProtocolData(second, mode))
                return false;

            if (GetData(first, FOOTER_ELEMENT_PEN_ID) != GetData(second, FOOTER_ELEMENT_PEN_ID))
                return false;

            switch (mode)
            {
                case QvPen_Pen_Mode.Erase:
                    return GetData(first, FOOTER_ELEMENT_INK_ID) == GetData(second, FOOTER_ELEMENT_INK_ID);
                case QvPen_Pen_Mode.EraseUserInk:
                    return GetData(first, FOOTER_ELEMENT_OWNER_ID) == GetData(second, FOOTER_ELEMENT_OWNER_ID);
                default:
                    return false;
            }
        }

        private bool IsValidFixedLengthProtocolData(Vector3[] data, QvPen_Pen_Mode mode)
        {
            var footerLength = GetFooterSize(mode);

            if (data == null || footerLength == 0 || data.Length < footerLength ||
                GetFooterLength(data) != footerLength)
                return false;

            return data.Length == footerLength;
        }

        private bool TryGetDrawLayout(Vector3[] data, out int positionCount,
            out int colorKeyCount, out int alphaKeyCount, out int gradientMode)
        {
            positionCount = 0;
            colorKeyCount = 0;
            alphaKeyCount = 0;
            gradientMode = 0;

            if (data == null || data.Length < FOOTER_ELEMENT_DRAW_LENGTH + 1 ||
                GetFooterLength(data) != FOOTER_ELEMENT_DRAW_LENGTH)
                return false;

            var inkInfo = GetData(data, FOOTER_ELEMENT_DRAW_INK_INFO);
            var inkMeshLayer = (int)inkInfo.x;
            var inkColliderLayer = (int)inkInfo.y;
            if (inkInfo.x != inkMeshLayer || inkInfo.y != inkColliderLayer ||
                inkMeshLayer < 0 || inkMeshLayer > 31 || inkColliderLayer < 0 || inkColliderLayer > 31)
                return false;

            var colorInfo = GetData(data, FOOTER_ELEMENT_DRAW_COLOR_INFO);
            colorKeyCount = (int)colorInfo.x;
            alphaKeyCount = (int)colorInfo.y;
            gradientMode = (int)colorInfo.z;

            if (colorInfo.x != colorKeyCount || colorInfo.y != alphaKeyCount || colorInfo.z != gradientMode ||
                colorKeyCount < MIN_GRADIENT_KEY_COUNT || colorKeyCount > MAX_GRADIENT_KEY_COUNT ||
                alphaKeyCount < MIN_GRADIENT_KEY_COUNT || alphaKeyCount > MAX_GRADIENT_KEY_COUNT ||
                gradientMode < (int)GradientMode.Blend || gradientMode > (int)GradientMode.Fixed)
                return false;

            var gradientDataLength = GetGradientDataLength(colorKeyCount, alphaKeyCount);
            positionCount = data.Length - FOOTER_ELEMENT_DRAW_LENGTH - gradientDataLength;

            if (positionCount <= 0 || positionCount > MAX_STROKE_POSITION_COUNT)
                return false;

            var index = positionCount;
            var previousTime = -1f;

            for (var i = 0; i < colorKeyCount; i++)
            {
                var color = data[index++];
                var time = data[index++].x;

                if (!IsFiniteNormalized(color.x) || !IsFiniteNormalized(color.y) ||
                    !IsFiniteNormalized(color.z) || !IsFiniteNormalized(time) || time < previousTime)
                    return false;

                previousTime = time;
            }

            previousTime = -1f;
            for (var i = 0; i < alphaKeyCount; i++)
            {
                var alphaKey = data[index++];

                if (!IsFiniteNormalized(alphaKey.x) || !IsFiniteNormalized(alphaKey.y) ||
                    alphaKey.y < previousTime)
                    return false;

                previousTime = alphaKey.y;
            }

            return index == data.Length - FOOTER_ELEMENT_DRAW_LENGTH;
        }

        private void ApplyDrawGradient(Vector3[] data, LineRenderer line,
            int positionCount, int colorKeyCount, int alphaKeyCount, int gradientMode)
        {
            var colorKeys = new GradientColorKey[colorKeyCount];
            var alphaKeys = new GradientAlphaKey[alphaKeyCount];
            var index = positionCount;

            for (var i = 0; i < colorKeyCount; i++)
            {
                var color = data[index++];
                var time = data[index++].x;
                colorKeys[i] = new GradientColorKey(new Color(color.x, color.y, color.z, 1f), time);
            }

            for (var i = 0; i < alphaKeyCount; i++)
            {
                var alphaKey = data[index++];
                alphaKeys[i] = new GradientAlphaKey(alphaKey.x, alphaKey.y);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            gradient.mode = (GradientMode)gradientMode;
            line.colorGradient = gradient;
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

        private void CreateInkInstance(Vector3[] data, int positionCount,
            int colorKeyCount, int alphaKeyCount, int gradientMode)
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

            var line = lineInstance.GetComponent<LineRenderer>();

            line.positionCount = positionCount;
            line.SetPositions(data);
            ApplyDrawGradient(data, line, positionCount, colorKeyCount, alphaKeyCount, gradientMode);

#if UNITY_STANDALONE
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

        private void SendEraseOwnStrokes()
        {
            var data = new Vector3[GetFooterSize(QvPen_Pen_Mode.EraseUserInk)];

            SetData(data, FOOTER_ELEMENT_DATA_INFO,
                new Vector3(localPlayerId, (int)QvPen_Pen_Mode.EraseUserInk, GetFooterSize(QvPen_Pen_Mode.EraseUserInk)));
            SetData(data, FOOTER_ELEMENT_PEN_ID, penIdVector);
            SetData(data, FOOTER_ELEMENT_OWNER_ID, localPlayerIdVector);

            _SendData(data);
        }

        private void SendUndoLastStroke()
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

            manager.RemoveUserStrokes(penId, ownerIdVector);
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

                        if (udon.DisableInteractive)
                            continue;

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

        const int PIDB = 360;
        const float PIDD = PIDB / 90f;

        public static Vector3 GetPlayerIdVector(int playerId)
        {
            var x = playerId;
            var y = x / PIDB;
            var z = y / PIDB;
            return new Vector3(x % PIDB, y % PIDB, z % PIDB) / PIDD;
        }

        public static int EulerAnglesToPlayerId(Vector3 v)
        {
            v *= PIDD;
            return Mathf.RoundToInt(v.x)
                + Mathf.RoundToInt(v.y) * PIDB
                + Mathf.RoundToInt(v.z) * (PIDB * PIDB);
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
