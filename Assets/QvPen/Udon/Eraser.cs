using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Eraser : UdonSharpBehaviour
    {
        // Layer 8 : Interactive
        // Layer 9 : Player
        [SerializeField] private int inkLayer = 9;
        [SerializeField] private int eraserLayer = 8;

        [SerializeField] private Material normal;
        [SerializeField] private Material erasing;

#pragma warning disable CS0108
        private Renderer renderer;
#pragma warning restore CS0108
        private VRC_Pickup pickup;

        private bool isErasing;

        // EraserManager
        private EraserManager eraserManager;

        private readonly string inkPrefix = "Ink";
        private readonly string inkPoolName = "obj_f6feca5f-9729-4367-ba6f-daeb5187a785";

        public void Init(EraserManager manager)
        {
            eraserManager = manager;

            gameObject.layer = eraserLayer;

            renderer = GetComponent<Renderer>();
            renderer.enabled = true;
            if (!eraserManager)
            {
                // For stand-alone erasers
                renderer.sharedMaterial = normal;
            }

            pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            if (pickup)
            {
                pickup.InteractionText = nameof(Eraser);
                pickup.UseText = "Erase";
            }
        }

        public override void OnPickup()
        {
            eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(EraserManager.StartUsing));

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPickupEvent));
        }

        public override void OnDrop()
        {
            eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(EraserManager.EndUsing));

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

        public void OnPickupEvent()
        {
            renderer.sharedMaterial = normal;
        }

        public void OnDropEvent()
        {
            renderer.sharedMaterial = erasing;
        }

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

        private void OnTriggerEnter(Collider other)
        {
            if (
                isErasing &&
                other &&
                other.gameObject &&
                other.gameObject.layer == inkLayer &&
                other.name.StartsWith(inkPrefix) &&
                other.transform.parent &&
                other.transform.parent.name == inkPoolName
                )
            {
                Destroy(other.gameObject);
            }
        }

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
    }
}
