using UdonSharp;
using UnityEngine;

namespace QvPen.UdonScript
{
    [DefaultExecutionOrder(30)]
    public abstract class QvPen_PenCallbackListener : UdonSharpBehaviour
    {
        public virtual void _OnPenPickup() { }
        public virtual void _OnPenDrop() { }
        public virtual void _OnPenColorChanged() { }
    }
}
