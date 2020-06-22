using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QvPen.Udon.UI
{
    public class PenOrEraserModeButton : UdonSharpBehaviour
    {
        [SerializeField] private Text message;
        [SerializeField] private PenManager[] penManagers;

        private bool use;

        public override void Interact()
        {
            use = !use;

            message.text =
                $"<size=18>{(use ? "Disable" : "Enable")}</size>\n" +
                $"Pen ↔ Eraser\n" +
                $"<size=8>[Double-click the pen]</size>\n" +
                $"<size=14>(Local)</size>";
            
            foreach (var go in penManagers)
            {
                go.SetUseDoubleClick(use);
            }
        }
    }
}
