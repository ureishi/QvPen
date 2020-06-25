using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon.UI
{
    public class InteractButton : UdonSharpBehaviour
    {
        [SerializeField] private bool isMasterOnlyEvent = false;
        [SerializeField] private bool isOwnerOnlyEvent = false;

        [SerializeField] private bool isGlobalEvent = false;
        [SerializeField] private UdonBehaviour udonBehaviour;
        [SerializeField] private string customEventName = "Unnamed";

        public override void Interact()
        {
            if (isMasterOnlyEvent && !Networking.IsMaster) return;
            if (isOwnerOnlyEvent && !Networking.IsOwner(gameObject)) return;
            if (!udonBehaviour) return;
            if (!isGlobalEvent)
                udonBehaviour.SendCustomEvent(customEventName);
            else
                udonBehaviour.SendCustomNetworkEvent(NetworkEventTarget.All, customEventName);
        }
    }
}
