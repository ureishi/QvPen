using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Pen : UdonSharpBehaviour
    {
        // Layer 9 : Player
        [SerializeField] private int inkLayer = 9;

        [SerializeField] private GameObject inkPrefab;
        [SerializeField] private GameObject colliderHolder;
        [SerializeField] private Eraser eraser;

        [SerializeField] private Transform inkPosition;
        [SerializeField] private Transform spawnTarget;
        [SerializeField] private Transform inkPool;

        [SerializeField] private float followSpeed; //= 32;

        private bool isUser;
        private VRC_Pickup pickup;

        // PenManager
        private PenManager penManager;

        // Ink
        private GameObject inkInstance;
        private int inkCount;

        // Double click
        private bool useDoubleClick = true;
        private float prevClickTime;
        private const float ClickInterval = 0.184f;

        // State
        private const int StatePenIdle = 0;
        private const int StatePenUsing = 1;
        private const int StateEraserIdle = 2;
        private const int StateEraserUsing = 3;
        private int currentState = StatePenIdle;

        private readonly string inkPrefix = "Ink";
        private readonly string inkPoolName = "obj_f6feca5f-9729-4367-ba6f-daeb5187a785";

        public void Init(PenManager manager)
        {
            penManager = manager;

            inkPool.name = inkPoolName;
            colliderHolder.layer = inkLayer;

            inkPrefab.SetActive(false);
            eraser.gameObject.SetActive(false);

            pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            pickup.InteractionText = nameof(Pen);
            pickup.UseText = "Draw";

            // PenManager : Manager, EraserManager : Manager, Init(Manager manager)
            eraser.Init(null);
        }

        private void LateUpdate()
        {
            if (isUser)
            {
                spawnTarget.position = Vector3.Lerp(spawnTarget.position, inkPosition.position, Time.deltaTime * followSpeed);
                spawnTarget.rotation = Quaternion.Lerp(spawnTarget.rotation, inkPosition.rotation, Time.deltaTime * followSpeed);
            }
            else
            {
                spawnTarget.position = inkPosition.position;
                spawnTarget.rotation = inkPosition.rotation;
            }
        }

        #region Events

        public override void OnPickup()
        {
            isUser = true;
            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        public override void OnDrop()
        {
            isUser = false;
            penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.EndUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        public override void OnPickupUseDown()
        {
            if (useDoubleClick && Time.time - prevClickTime < ClickInterval)
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
                        Debug.LogError($"Unexpected state : {currentState} at {nameof(OnPickupUseDown)} Double Clicked");
                        break;
                }
            }
            else
            {
                prevClickTime = Time.time;
                switch (currentState)
                {
                    case StatePenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenUsing));
                        break;
                    case StateEraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseUsing));
                        break;
                    default:
                        Debug.LogError($"Unexpected state : {currentState} at {nameof(OnPickupUseDown)}");
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
                    Debug.Log($"Change state : {StateEraserIdle} to {currentState}");
                    break;
                case StateEraserIdle:
                    Debug.Log($"Change state : {StatePenIdle} to {currentState}");
                    break;
                default:
                    Debug.Log($"Unexpected state : {currentState} at {nameof(OnPickupUseUp)}");
                    break;
            }
        }

        public void SetUseDoubleClick(bool value)
        {
            useDoubleClick = value;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }

        #endregion

        public void DestroyJustBeforeInk()
        {
            var childCount = inkPool.childCount;
            if (childCount > 0)
            {
                Destroy(inkPool.GetChild(childCount - 1).gameObject);
            }
        }

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
                    FinishErasing();
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
                    FinishErasing();
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
                    FinishErasing();
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
                    StartErasing();
                    break;
                case StatePenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    StartErasing();
                    break;
                case StateEraserIdle:
                    StartErasing();
                    break;
            }
            currentState = StateEraserUsing;
        }

        #endregion

        public bool IsHeld()
        {
            return pickup.IsHeld;
        }

        public void Respawn()
        {
            pickup.Drop();
            if (Networking.LocalPlayer.IsOwner(gameObject))
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        public void Clear()
        {
            for (var i = 0; i < inkPool.childCount; i++)
            {
                Destroy(inkPool.GetChild(i).gameObject);
            }
        }

        private void StartDrawing()
        {
            inkInstance = VRCInstantiate(inkPrefab);

            inkInstance.transform.SetParent(spawnTarget);
            inkInstance.transform.localPosition = Vector3.zero;
            inkInstance.transform.localRotation = Quaternion.identity;
            inkInstance.SetActive(true);
            inkInstance.GetComponent<TrailRenderer>().enabled = true;
        }

        private void FinishDrawing()
        {
            if (inkInstance != null)
            {
                var colliderHolderInstance = VRCInstantiate(colliderHolder);
                colliderHolderInstance.name = $"{inkPrefix} ({inkCount++})";

                colliderHolderInstance.transform.position = inkInstance.transform.position;
                colliderHolderInstance.transform.rotation = inkInstance.transform.rotation;

                inkInstance.transform.SetParent(colliderHolderInstance.transform);
                colliderHolderInstance.transform.SetParent(inkPool);

                CreateInkMeshCollider(colliderHolderInstance);

                Destroy(colliderHolderInstance.GetComponent<MeshFilter>());
            }

            inkInstance = null;
        }

        private void StartErasing()
        {
            eraser.StartErasing();
        }

        private void FinishErasing()
        {
            eraser.FinishErasing();
        }

        private void ChangeToPen()
        {
            eraser.FinishErasing();
            eraser.gameObject.SetActive(false);
        }

        private void ChangeToEraser()
        {
            eraser.gameObject.SetActive(true);
        }

        private void CreateInkMeshCollider(GameObject colliderHolder)
        {
            var meshFilter = colliderHolder.GetComponent<MeshFilter>();
            var meshCollider = colliderHolder.GetComponent<MeshCollider>();

            var trailRenderer = inkInstance.GetComponent<TrailRenderer>();

            var positionCount = trailRenderer.positionCount;
            if (positionCount < 2)
            {
                positionCount = 2;
            }

            const int verticesParPoint = 2;
            const int trianglesParPoint = 6;
            var positions = new Vector3[positionCount];
            var vertices = new Vector3[positionCount * verticesParPoint];
            var triangles = new int[(positionCount - 1) * trianglesParPoint];

            const float colliderWidth = 0.005f;
            var offsetX = Vector3.right * colliderWidth / 2f;

            trailRenderer.GetPositions(positions);
            if (positionCount == 2)
            {
                var offsetZ = Vector3.forward * colliderWidth / 2f;
                positions[0] = inkInstance.transform.position - offsetZ;
                positions[1] = inkInstance.transform.position + offsetZ;
            }

            // Create vertices
            for (var i = 0; i < positionCount; i++)
            {
                var position = inkInstance.transform.InverseTransformPoint(positions[i]);
                vertices[i * verticesParPoint + 0] = position + offsetX;
                vertices[i * verticesParPoint + 1] = position - offsetX;
            }

            // Create triangles
            for (var i = 0; i < positionCount - 1; i++)
            {
                triangles[i * trianglesParPoint + 0] = i * verticesParPoint + 0;
                triangles[i * trianglesParPoint + 1] = i * verticesParPoint + 1;
                triangles[i * trianglesParPoint + 2] = i * verticesParPoint + 2;
                triangles[i * trianglesParPoint + 3] = i * verticesParPoint + 3;
                triangles[i * trianglesParPoint + 4] = i * verticesParPoint + 2;
                triangles[i * trianglesParPoint + 5] = i * verticesParPoint + 1;
            }

            // Instantiate a mesh
            var mesh = meshFilter.mesh;
            // mesh.Clear();

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
    }
}
