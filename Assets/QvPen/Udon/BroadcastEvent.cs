using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class BroadcastEvent : UdonSharpBehaviour
    {
        [SerializeField] private bool isMasterOnlyEvent = false;
        [SerializeField] private bool isOwnerOnlyEvent = false;

        [SerializeField] private bool isGlobalEvent = false;
        [SerializeField] private Transform broadcastTransform;
        [SerializeField] private string customEventName = "Unnamed";

        public override void Interact()
        {
            if (isMasterOnlyEvent && !Networking.IsMaster) return;
            if (isOwnerOnlyEvent && !Networking.IsOwner(gameObject)) return;
            if (!broadcastTransform) return;
            for (var i = 0; i < broadcastTransform.childCount; i++)
            {
                var udonBehaviour = (UdonBehaviour)broadcastTransform.GetChild(i).GetComponent(typeof(UdonBehaviour));
                if (!udonBehaviour) continue;
                if (!isGlobalEvent)
                    udonBehaviour.SendCustomEvent(customEventName);
                else
                    udonBehaviour.SendCustomNetworkEvent(NetworkEventTarget.All, customEventName);
            }
        }
    }
}