
using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

namespace QvPen.Udon.UI
{
    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ClearButton : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_PenManager penManager;

        [SerializeField]
        private Image ownIndicator;

        [SerializeField]
        private Image allIndicator;

        private const float keepSeconds1 = 0.51f;
        private const float keepSeconds2 = 2f;

        private float targetTime1;
        private float targetTime2;

        private bool isInInteract;

        private void Start()
        {
            SetActiveIndicator(false);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value)
                return;

            if (isInInteract && Time.time < targetTime1)
                UndoDraw();

            isInInteract = false;
            SetActiveIndicator(false);
        }

        public override void Interact()
        {
            isInInteract = true;
            SetActiveIndicator(true);

            targetTime1 = Time.time + keepSeconds1;
            targetTime2 = Time.time + keepSeconds2;

            SendCustomEventDelayedSeconds(nameof(_Loop1), 0f);
        }

        private void SetActiveIndicator(bool isActive)
        {
            if (Utilities.IsValid(ownIndicator))
            {
                ownIndicator.gameObject.SetActive(isActive);
                ownIndicator.fillAmount = 0f;
            }

            if (Utilities.IsValid(allIndicator))
            {
                allIndicator.gameObject.SetActive(isActive);
                allIndicator.fillAmount = 0f;
            }
        }

        private void SeValueIndicator(float ownIndicatorValue, float allIndicatorValue)
        {
            if (Utilities.IsValid(ownIndicator))
                ownIndicator.fillAmount = Mathf.Clamp01(ownIndicatorValue);

            if (Utilities.IsValid(allIndicator))
                allIndicator.fillAmount = Mathf.Clamp01(allIndicatorValue);
        }

        public void _Loop1()
        {
            if (!isInInteract)
                return;

            var time = Time.time;

            var leaveTime1 = targetTime1 - time;
            var leaveTime2 = targetTime2 - time;
            if (leaveTime1 <= 0f)
            {
                EraseOwnInk();

                SeValueIndicator(1f, 1f - leaveTime2 / keepSeconds2);

                SendCustomEventDelayedFrames(nameof(_Loop2), 0);

                return;
            }

            SeValueIndicator(1f - leaveTime1 / keepSeconds1, 1f - leaveTime2 / keepSeconds2);

            SendCustomEventDelayedFrames(nameof(_Loop1), 0);
        }

        public void _Loop2()
        {
            if (!isInInteract)
                return;

            var leaveTime2 = targetTime2 - Time.time;
            if (leaveTime2 <= 0f)
            {
                Clear();

                SeValueIndicator(0f, 0f);

                return;
            }

            SeValueIndicator(0f, 1f - leaveTime2 / keepSeconds2);

            SendCustomEventDelayedFrames(nameof(_Loop2), 0);
        }

        private void EraseOwnInk()
        {
            if (Utilities.IsValid(penManager))
                penManager.EraseOwnInk();
        }

        private void UndoDraw()
        {
            if (Utilities.IsValid(penManager))
                penManager.UndoDraw();
        }

        private void Clear()
        {
            if (Utilities.IsValid(penManager))
                penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Clear));
        }
    }
}
