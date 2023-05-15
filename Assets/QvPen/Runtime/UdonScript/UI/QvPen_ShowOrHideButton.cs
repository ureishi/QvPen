using UdonSharp;
using UnityEngine;

namespace QvPen.UdonScript.UI
{
    [DefaultExecutionOrder(30)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ShowOrHideButton : UdonSharpBehaviour
    {
        [SerializeField]
        private GameObject[] gameObjects = { };

        [SerializeField]
        private bool isShown = true;

        [SerializeField]
        private GameObject displayObjectOn;
        [SerializeField]
        private GameObject displayObjectOff;

        private void Start()
        {
            if (!isShown)
                // if isShown is false, flip it to true and run UpdateActivity in 5 frames
                // doing this on the next frame, appears to make the pens no longer draw.
            {
                isShown = true;
                SendCustomEventDelayedFrames(nameof(OffOnStart),5);
            }
        }

        public override void Interact()
        {
            isShown ^= true;
            UpdateActivity();
        }

        private void UpdateActivity()
        {
            if (displayObjectOn)
                displayObjectOn.SetActive(isShown);
            if (displayObjectOff)
                displayObjectOff.SetActive(!isShown);

            foreach (var go in gameObjects)
                if (go)
                    go.SetActive(isShown);
        }
        
        public void OffOnStart()
        // proxy to flip isShown on start and to run UpdateActivity without havingto make it public
        {
            isShown = false;
            UpdateActivity();
        }
    }
}
