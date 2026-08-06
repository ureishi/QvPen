using UdonSharp;
using UnityEngine;

namespace QvPen.UdonScript.UI
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(30)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ShowOrHideButton : UdonSharpBehaviour
    {
        [Tooltip("The list of objects this turns On/Off.")]
        [SerializeField]
        private GameObject[] gameObjects = { };

        [Tooltip("The default state of the pens when the world starts.\nThe pens will start on when this is true, and they will start off when this is false.\n\nThis should be used to toggle the pens off instead of turning them off in the Hierarchy!!")]
        [SerializeField]
        private bool isShown = true;

        [Tooltip("The object that is used to indicate that the pens are On.")]
        [SerializeField]
        private GameObject displayObjectOn;
        [Tooltip("The object that is used to indicate that the pens are Off.")]
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
