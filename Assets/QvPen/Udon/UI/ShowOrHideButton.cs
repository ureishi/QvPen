using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QvPen.Udon.UI
{
    public class ShowOrHideButton : UdonSharpBehaviour
    {
        [SerializeField] private Text message;
        [SerializeField] private GameObject[] gameObjects;

        private bool isShown = true;

        public override void Interact()
        {
            isShown ^= true;
            
            message.text = $"{(isShown ? "Hide": "Show")}\n<size=14>(Local)</size>";
            foreach (var go in gameObjects)
            {
                go.SetActive(isShown);
            }
        }
    }
}
