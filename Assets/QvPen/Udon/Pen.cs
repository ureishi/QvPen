using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Pen : UdonSharpBehaviour
    {
        [SerializeField] private Ink inkPrefab;
        [SerializeField] private Eraser eraser;
        
        [SerializeField] private Transform inkPosition;
        [SerializeField] private Transform spawnTarget;
        [SerializeField] private Transform inkPool;
        
        [SerializeField] private VRC_Pickup pickup;

        [SerializeField] private float followSpeed;
        
        private PenManager penManager;
        private bool isUser;

        // For Ink
        private Ink ink;
        private GameObject inkInstance;
        private bool isInitializeInkRequired;
        private bool isInkInitialized;
        
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

            pickup.InteractionText = nameof(Udon.Pen);
            pickup.UseText = "Draw";

            defaultPosition = transform.position;
            defaultRotation = transform.rotation;
        }
        
        private void Update()
        {
            if (isInitializeInkRequired && !isInkInitialized)
            {
                ink = inkInstance.GetComponent<Ink>();
                if (ink != null)
                {
                    isInkInitialized = true;
                    isInitializeInkRequired = false;
                }
            }
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
            
            // UdonSharp v0.17Ç≈ÇÕthisÇïtÇØÇ»Ç¢Ç∆ÉGÉâÅ[
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(this.ChangeToPen)); 
        }
        
        public override void OnDrop()
        {
            isUser = false;
            penManager.EndUsing();
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(this.FinishDrawing) : nameof(this.FinishErasing));
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(this.ChangeToPen)); 
        }
        
        public override void OnPickupUseDown()
        {
            if (Time.time - prevTime < Interval)
            {
                prevTime = 0f;
                Destroy(inkInstance);
                SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(this.ChangeToEraser) : nameof(this.ChangeToPen));
                return;
            }
            prevTime = Time.time;
            
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(this.StartDrawing) : nameof(this.StartErasing));
        }
        
        public override void OnPickupUseUp()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, mode == ModePen ? nameof(this.FinishDrawing) : nameof(this.FinishErasing));
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
                if (isInkInitialized)
                {
                    ink.FinishDrawing();
                    Destroy(ink);
                }
            }
            inkInstance = null;
            isDrawing = false;
        }

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

        private void SpawnInk()
        {
            inkInstance = VRCInstantiate(inkPrefab.gameObject);
            inkInstance.name = inkPrefab.name;

            inkInstance.transform.SetParent(spawnTarget);
            inkInstance.transform.localPosition = Vector3.zero;
            inkInstance.transform.localRotation = Quaternion.identity;
            inkInstance.SetActive(true);
            inkInstance.GetComponent<TrailRenderer>().enabled = true;
            isInitializeInkRequired = true;
            isInkInitialized = false;
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
    }
}