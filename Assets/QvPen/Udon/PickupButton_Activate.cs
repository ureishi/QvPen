using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvPen.Udon
{
    public class PickupButton_Activate : UdonSharpBehaviour
    {
        [SerializeField] private GameObject[] gameObjects;

        private VRC_Pickup pickup;

        private bool isActive = true;

        private void Start()
        {
            if (pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)))
            {
                pickup.InteractionText = $"{(!isActive ? "A" : "Dea")}ctivate (Local)";
                pickup.UseText = "";
                pickup.pickupable = true;
                GetComponent<Rigidbody>().isKinematic = true;
            }
        }
        public override void OnPickup()
        {
            if (pickup) pickup.Drop();
        }
        public override void OnDrop()
        {
            isActive ^= true;
            pickup.InteractionText = $"{(!isActive ? "A" : "Dea")}ctivate (Local)";
            foreach (var go in gameObjects)
            {
                go.SetActive(isActive);
            }
        }
    }
}