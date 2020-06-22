using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Eraser : UdonSharpBehaviour
    {
        // For stand-alone erasers
        [SerializeField] private VRC_Pickup pickup;
        
        [SerializeField] private Renderer renderer;
        
        [SerializeField] private Material normal;
        [SerializeField] private Material erasing;

        private bool isErasing;

        private void Start()
        {
            renderer.enabled = true;
            if (pickup == null)
            {
                renderer.sharedMaterial = normal;
            }
        }

        public void SetActive(bool value)
        {
            gameObject.SetActive(value);
        }

        public override void OnPickupUseDown()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(StartErasing));
        }

        public override void OnPickupUseUp()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FinishErasing));
        }

        public override void OnPickup()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnPickupEvent));
        }

        public override void OnDrop()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnDropEvent));
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

        public void OnPickupEvent()
        {
            renderer.sharedMaterial = normal;
        }

        public void OnDropEvent()
        {
            renderer.sharedMaterial = erasing;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (
                isErasing &&
                other != null &&
                other.gameObject != null &&
                other.gameObject.layer == 17 &&
                other.gameObject.name.StartsWith("Ink")
                )
            {
                Destroy(other.gameObject);
            }
        }

        public void Respawn()
        {
            if (pickup) pickup.Drop();
            if (Networking.LocalPlayer.IsOwner(gameObject))
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }
    }
}
