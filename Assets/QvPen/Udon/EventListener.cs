using UdonSharp;
using UnityEngine;

namespace ureishi.Udon.QvPen
{
    public class EventListener : UdonSharpBehaviour
    {
        [SerializeField] private Transform udonBroadcaster;
        [SerializeField] private QvPen qvPen;

        void Start()
        {
            if (udonBroadcaster) transform.SetParent(udonBroadcaster);
        }
        public void ResetAll()
        {
            if (qvPen)
            {
                qvPen.Respawn();
                qvPen.Clear();
            }
        }
        public void ClearAll()
        {
            if (qvPen)
            {
                qvPen.Clear();
            }
        }
    }
}