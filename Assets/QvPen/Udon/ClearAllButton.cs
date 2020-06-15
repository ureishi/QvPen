using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvPen.Udon
{
    public class ClearAllButton : UdonSharpBehaviour
    {
        [SerializeField] private PenManager[] penManagers;

        public override void Interact()
        {
            foreach (var penManager in penManagers)
            {
                penManager.ClearAll();
            }
        }
    }
}
