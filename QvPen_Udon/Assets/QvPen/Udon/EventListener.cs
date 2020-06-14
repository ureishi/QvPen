using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace ureishi.Udon.QvPen
{
    public class EventListener : UdonSharpBehaviour
    {
        [SerializeField] private Transform udonBroadcaster;
        [SerializeField] private UdonBehaviour udonBehaviour;

        void Start()
        {
            if (udonBroadcaster) transform.SetParent(udonBroadcaster);
        }
        public void AllReset()
        {
            if (udonBehaviour)
            {
                udonBehaviour.SendCustomEvent("Respawn");
                udonBehaviour.SendCustomEvent("Clear");
            }
        }
        public void AllClear()
        {
            if (udonBehaviour)
            {
                udonBehaviour.SendCustomEvent("Clear");
            }
        }
    }
}