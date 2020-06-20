using UdonSharp;
using UnityEngine;

namespace QvPen.Udon.UI
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
