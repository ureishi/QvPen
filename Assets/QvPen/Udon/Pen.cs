using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Pen : UdonSharpBehaviour
    {
        #region Local

        

        [SerializeField] private GameObject inkPrefab;
        [SerializeField] private Eraser eraser;
        
        [SerializeField] private Transform inkPosition;
        [SerializeField] private Transform spawnTarget;
        [SerializeField] private Transform inkPool;
        
        [SerializeField] private VRC_Pickup pickup;

        [SerializeField] private float followSpeed;
        
        private PenManager penManager;
        private bool isUser;

        // For ink
        private GameObject inkInstance;
        private int inkCount;
        
        // For respawn
        private Vector3 defaultPosition;
        private Quaternion defaultRotation;

        // For double click
        private float prevTime;
        private const float Interval = 0.2f;

        // Mode Pen or Eraser
        private const int ModePen = 0;
        private const int ModeEraser = 1;
        private int mode = ModePen;

        public void Init(PenManager manager)
        {
            penManager = manager;
            eraser.SetActive(false);
            inkPrefab.gameObject.SetActive(false);

            pickup.InteractionText = nameof(Pen);
            pickup.UseText = "Draw";

            defaultPosition = transform.position;
            defaultRotation = transform.rotation;
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
        
        public override void OnPickup()
        {
            isUser = true;
            penManager.StartUsing();
            
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeToPen)); 
        }
        
        public override void OnDrop()
        {
            isUser = false;
            penManager.EndUsing();
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(FinishDrawing) : nameof(FinishErasing));
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeToPen)); 
        }
        
        public override void OnPickupUseDown()
        {
            if (Time.time - prevTime < Interval)
            {
                prevTime = 0f;
                
                if (mode == ModePen)
                {
                    var childCount = inkPool.childCount;
                    if (childCount > 0)
                    {
                        Destroy(inkPool.GetChild(childCount - 1).gameObject);
                    }
                }

                SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(ChangeToEraser) : nameof(ChangeToPen));
                if (mode == ModePen)
                {
                    return;
                }
            }
            prevTime = Time.time;
            
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(StartDrawing) : nameof(StartErasing));
        }
        
        public override void OnPickupUseUp()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(FinishDrawing) : nameof(FinishErasing));
        }
        
        public void ChangeToPen()
        {
            mode = ModePen;
            eraser.SetActive(false);
        }
        
        public void ChangeToEraser()
        {
            mode = ModeEraser;
            eraser.SetActive(true);
        }

        private bool isDrawing;
        private bool isErasing;

        public void StartDrawing()
        {
            if (mode != ModePen)
            {
                ChangeToPen();
            }
            
            if(isDrawing) return;
            SpawnInk();
            isDrawing = true;
        }

        public void StartErasing()
        {
            if (mode != ModeEraser)
            {
                ChangeToEraser();
            }
            
            if(isErasing) return;
            eraser.StartErasing();
            isErasing = true;
        }

        public void FinishDrawing()
        {
            if (mode != ModePen)
            {
                ChangeToPen();
            }

            if (!isDrawing) return;
            if (inkInstance != null)
            {
                inkInstance.transform.SetParent(inkPool);
                CreateInkMeshCollider();
            }
            inkInstance = null;
            isDrawing = false;
        }

        private void CreateInkMeshCollider()
        {
            var trailRenderer = inkInstance.GetComponent<TrailRenderer>();
            var meshFilter = inkInstance.GetComponent<MeshFilter>();
            var meshCollider = inkInstance.GetComponent<MeshCollider>();
            
            var positionCount = trailRenderer.positionCount;
            if (positionCount < 2)
            {
                positionCount = 2;
            }

            var verticesParPoint = 2;
            var trianglesCountParPoint = 6;
            var positions = new Vector3[positionCount];
            var vertices = new Vector3[positionCount * verticesParPoint];
            var triangles = new int[(positionCount - 1) * trianglesCountParPoint];

            var colliderWidth = 0.005f;
            var offsetXP = new Vector3(colliderWidth, 0, 0);
            var offsetXN = new Vector3(-colliderWidth, 0, 0);

            trailRenderer.GetPositions(positions);
            if (positionCount == 2)
            {
                positions[0] =inkInstance.transform.position;
                positions[1] =inkInstance.transform.position + Vector3.down * colliderWidth;
            }

            // Create vertices
            for (var i = 0; i < positionCount; i++)
            {
                var position = inkInstance.transform.InverseTransformPoint(positions[i]);
                vertices[i * verticesParPoint + 0] = position + offsetXP;
                vertices[i * verticesParPoint + 1] = position + offsetXN;
            }

            // Create triangles
            for (var i = 0; i < positionCount - 1; i++)
            {
                triangles[i * trianglesCountParPoint + 0] = i * verticesParPoint + 0;
                triangles[i * trianglesCountParPoint + 1] = i * verticesParPoint + 1;
                triangles[i * trianglesCountParPoint + 2] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 3] = i * verticesParPoint + 3;
                triangles[i * trianglesCountParPoint + 4] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 5] = i * verticesParPoint + 1;
            }

            // Instantiate a mesh
            var mesh = meshFilter.mesh;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
        }

        private void SpawnInk()
        {
            inkInstance = VRCInstantiate(inkPrefab.gameObject);
            inkInstance.name = $"{inkPrefab.name}{inkCount++:000000}";

            inkInstance.transform.SetParent(spawnTarget);
            inkInstance.transform.localPosition = Vector3.zero;
            inkInstance.transform.localRotation = Quaternion.identity;
            inkInstance.SetActive(true);
            inkInstance.GetComponent<TrailRenderer>().enabled = true;
        }
        
        #endregion

        #region Network

        public void FinishErasing()
        {
            if (mode != ModeEraser)
            {
                ChangeToEraser();
            }
            
            if(!isErasing) return;
            eraser.FinishErasing();
            isErasing = false;
        }

        public void Respawn()
        {
            if (pickup) pickup.Drop();
            transform.position = defaultPosition;
            transform.rotation = defaultRotation;
        }
        
        public void Clear()
        {
            for (var i = 0; i < inkPool.childCount; i++)
            {
                Destroy(inkPool.GetChild(i).gameObject);
            }
        }

        #endregion
    }
}