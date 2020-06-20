using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QvPen.Udon.UI
{
    public class Interact_uGUI : UdonSharpBehaviour
    {
        [SerializeField] private Toggle toggle;
        public override void Interact()
        {
            if (toggle) toggle.isOn ^= true;
        }
    }
}