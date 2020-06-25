using UdonSharp;
using UnityEngine;

namespace QvPen.Udon.UI
{
    public class ClearAllButton : UdonSharpBehaviour
    {
        [SerializeField] private Transform pensParent;

        private PenManager[] penManagers;

        private void Start()
        {
            penManagers = new PenManager[pensParent.childCount];
            for (var i = 0; i < pensParent.childCount; i++)
            {
                penManagers[i] = pensParent.GetChild(i).GetComponent<PenManager>();
            }
        }

        public override void Interact()
        {
            foreach (var penManager in penManagers)
            {
                if (penManager) penManager.ClearAll();
            }
        }
    }
}
