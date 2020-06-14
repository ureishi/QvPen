using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace ureishi.Udon.QvPen
{
    public class QvPen : UdonSharpBehaviour
    {
        [SerializeField] private GameObject inkPrefab;
        [SerializeField] private Transform spawnTarget;
        [SerializeField] private Transform inkPool;
        [SerializeField] private PenManager penManager;

        private bool isUser = false;
        private Vector3 defaultPosition;
        private Quaternion defaultRotation;
        private Transform inkInstance;
        private TrailRenderer inkTrail;
        private VRC_Pickup pickup;

        private PositionConstraint positionConstraint;
        private Toggle smoothFollowToggle;

        private void Start()
        {
            defaultPosition = transform.position;
            defaultRotation = transform.rotation;

            if (pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)))
            {
                pickup.InteractionText = nameof(Udon.QvPen);
                pickup.UseText = "Draw";
            }
            else { Debug.LogError("VRC_Pickup component is not found.", this); }

            if (inkPrefab) { inkPrefab.SetActive(false); }
            else { Debug.LogError("Ink Object is none.", this); }

            if (!inkPrefab.transform.GetComponent<TrailRenderer>())
            {
                Debug.LogError("Trail Renderer is not attached to Ink Prefab.", this);
            }

            if (!spawnTarget) { Debug.LogError("Spawn Target is none.", this); }

            if (positionConstraint = spawnTarget.GetComponent<PositionConstraint>())
            {
                positionConstraint.enabled = true;
            }
            else { Debug.LogError("Position Constraint component is not found.", this); }

            if (smoothFollowToggle = spawnTarget.GetComponent<Toggle>())
            {
                smoothFollowToggle.isOn = false;
            }
            else { Debug.LogError("Smooth Follow Enabler is not found.", this); }

            if (!inkPool) { Debug.LogError("Ink Prefab is none.", this); }

            if (!penManager) { Debug.LogError("Pen Manager is none.", this); }
        }
        public override void OnPickup()
        {
            isUser = true;
            positionConstraint.enabled = false;
            smoothFollowToggle.isOn = true;

            penManager.StartUsing();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(this.EventOnPickupUseUp));
        }
        public override void OnDrop()
        {
            isUser = false;
            positionConstraint.enabled = true;
            smoothFollowToggle.isOn = false;

            penManager.SendCustomEvent(nameof(penManager.EndUsing));
        }
        public override void OnPickupUseDown()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(this.EventOnPickupUseDown));
        }
        public override void OnPickupUseUp()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(this.EventOnPickupUseUp));
        }
        public void EventOnPickupUseDown()
        {
            inkTrail.enabled = true;
        }
        public void EventOnPickupUseUp()
        {
            if (inkInstance) inkInstance.SetParent(inkPool);
            inkInstance = null;
            SpawnInk();
        }
        private void SpawnInk()
        {
            inkInstance = VRCInstantiate(inkPrefab).transform;
            if (inkInstance)
            {
                inkInstance.SetParent(spawnTarget);
                inkInstance.localPosition = Vector3.zero;
                inkInstance.localRotation = Quaternion.identity;
                inkInstance.gameObject.SetActive(true);

                inkTrail = inkInstance.GetComponent<TrailRenderer>();
            }
            inkTrail.Clear();
            inkTrail.enabled = false;
        }
        public void Respawn()
        {
            if (pickup) pickup.Drop();
            transform.position = defaultPosition;
            transform.rotation = defaultRotation;
        }
        public void Clear()
        {
            for (int i = 0; i < inkPool.childCount; i++)
            {
                Destroy(inkPool.GetChild(i).gameObject);
            }
        }
    }
}