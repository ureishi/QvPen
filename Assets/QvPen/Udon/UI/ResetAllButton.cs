using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon.UI
{
    public class ResetAllButton : UdonSharpBehaviour
    {
        [SerializeField] private PenManager[] penManagers;
        [SerializeField] private EraserManager[] eraserManagers;
        
        public override void Interact()
        {
            if (!Networking.IsMaster) return;
            
            foreach (var penManager in penManagers)
            {
                penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.ResetAll));
            }
            foreach (var eraserManager in eraserManagers)
            {
                eraserManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PenManager.ResetAll));
            }
        }
    }
}
