using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Eraser : UdonSharpBehaviour
    {
        // Layer 14: PickupNoEnvironment
        [SerializeField] private int inkPrefabLayer = 14;

        [SerializeField] private Material normal;
        [SerializeField] private Material erasing;

#pragma warning disable CS0108
        private Renderer renderer;
#pragma warning restore CS0108
        private VRC_Pickup pickup;

        private bool isErasing;

        private void Start()
        {
            renderer = GetComponent<Renderer>();
            renderer.enabled = true;

            pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            if (pickup)
            {
                pickup.InteractionText = nameof(Eraser);
                pickup.UseText = "Erase";
            }
            else
            {
                // For stand-alone erasers
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
                other &&
                other.gameObject &&
                other.gameObject.layer == inkPrefabLayer &&
                other.name.StartsWith("Ink") &&
                other.transform.parent.name == "InkPool" &&
                other.GetComponent<TrailRenderer>()
                )
            {
                Destroy(other.gameObject);
            }
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
