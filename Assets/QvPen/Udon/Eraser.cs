using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Eraser : UdonSharpBehaviour
    {
        [SerializeField] private VRC_Pickup pickup;
        
        [SerializeField] private Renderer renderer;
        
        [SerializeField] private Material normal;
        [SerializeField] private Material erasing;

        // For respawn
        private Vector3 defaultPosition;
        private Quaternion defaultRotation;

        private bool isErasing;

        private void Start()
        {
            renderer.enabled = true;
            renderer.sharedMaterial = normal;
            
            defaultPosition = transform.position;
            defaultRotation = transform.rotation;
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
                other != null &&
                other.gameObject != null &&
                other.gameObject.layer == 17 &&
                other.gameObject.name == "Ink"
                )
            {
                Destroy(other.gameObject);
            }
        }

        public void Respawn()
        {
            if (pickup) pickup.Drop();
            transform.position = defaultPosition;
            transform.rotation = defaultRotation;
        }
    }
}
