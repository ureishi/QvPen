using UdonSharp;
using UnityEngine;

namespace QvPen.UdonScript
{
    [DefaultExecutionOrder(30)]
    public abstract class QvPen_PenCallbackListener : UdonSharpBehaviour
    {
        public virtual void OnPenPickup() { }
        public virtual void OnPenDrop() { }
    }
}
