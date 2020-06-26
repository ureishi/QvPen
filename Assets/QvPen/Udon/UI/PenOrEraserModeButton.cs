using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QvPen.Udon.UI
{
    public class PenOrEraserModeButton : UdonSharpBehaviour
    {
        [SerializeField] private Text message;
        [SerializeField] private Transform pensParent;

        private bool use = true;

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
            use ^= true;

            message.text =
                $"<size=18>{(use ? "Disable" : "Enable")}</size>\n" +
                $"Pen ↔ Eraser\n" +
                $"<size=8>[Double-click the pen]</size>\n" +
                $"<size=14>(Local)</size>";

            foreach (var penManager in penManagers)
            {
                if (penManager) penManager.SetUseDoubleClick(use);
            }
        }
    }
}
