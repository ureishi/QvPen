using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ureishi.Udon.QvPen
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