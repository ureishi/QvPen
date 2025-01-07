using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using Utilities = VRC.SDKBase.Utilities;

namespace QvPen.Udon.UI
{
    using QvPen.UdonScript;

    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ClearButton : QvPen_PenCallbackListener
    {
        [SerializeField]
        private QvPen_PenManager penManager;

        [SerializeField]
        private Image ownTextImage;

        [SerializeField]
        private Image ownIndicator;

        [SerializeField]
        private Image allTextImage;

        [SerializeField]
        private Image allIndicator;

        private const float keepSeconds1 = 0.51f;
        private const float keepSeconds2 = 2f;

        private float targetTime1;
        private float targetTime2;

        private bool isInInteract;

        private bool isPickedUp;
        private const float keepSeconds_TextImage = 5f;
        private float targetTime_TextImage;

        private void Start()
        {
            SetActiveIndicator(false);
            SetActiveTextImage(false);

            penManager.Register(this);
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
            targetTime_TextImage = Time.time + keepSeconds_TextImage;

            SendCustomEventDelayedSeconds(nameof(_LoopIndicator1), 0f);
            Enter_LoopTextImageActive();
        }

        public override void OnPenPickup()
        {
            isPickedUp = true;
            Enter_LoopTextImageActive();
        }

        public override void OnPenDrop()
        {
            isPickedUp = false;

            targetTime_TextImage = Time.time + keepSeconds_TextImage;
            Enter_LoopTextImageActive();
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

        private void SetActiveTextImage(bool isActive)
        {
            if (Utilities.IsValid(ownTextImage))
                ownTextImage.gameObject.SetActive(isActive);

            if (Utilities.IsValid(allTextImage))
                allTextImage.gameObject.SetActive(isActive);
        }

        private void SeValueIndicator(float ownIndicatorValue, float allIndicatorValue)
        {
            if (Utilities.IsValid(ownIndicator))
                ownIndicator.fillAmount = Mathf.Clamp01(ownIndicatorValue);

            if (Utilities.IsValid(allIndicator))
                allIndicator.fillAmount = Mathf.Clamp01(allIndicatorValue);
        }

        public void _LoopIndicator1()
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

                SendCustomEventDelayedFrames(nameof(_LoopIndicator2), 0);

                return;
            }

            SeValueIndicator(1f - leaveTime1 / keepSeconds1, 1f - leaveTime2 / keepSeconds2);

            SendCustomEventDelayedFrames(nameof(_LoopIndicator1), 0);
        }

        public void _LoopIndicator2()
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

            SendCustomEventDelayedFrames(nameof(_LoopIndicator2), 0);
        }

        private bool isIn_LoopTextImageActive;

        private void Enter_LoopTextImageActive()
        {
            if (isIn_LoopTextImageActive)
                return;

            isIn_LoopTextImageActive = true;

            if (isPickedUp)
            {
                Exit_LoopTextImageActive();
                return;
            }

            SetActiveTextImage(true);

            _LoopTextImageActive();
        }

        private void Exit_LoopTextImageActive()
        {
            if (!isIn_LoopTextImageActive)
                return;

            isIn_LoopTextImageActive = false;

            SetActiveTextImage(false);
        }

        public void _LoopTextImageActive()
        {
            if (!isIn_LoopTextImageActive)
                return;

            if (isPickedUp)
            {
                Exit_LoopTextImageActive();
                return;
            }

            var time = Time.time;

            var leaveTime_TextImage = targetTime_TextImage - time;
            if (leaveTime_TextImage <= 0f)
            {
                Exit_LoopTextImageActive();
                return;
            }

            SendCustomEventDelayedSeconds(nameof(_LoopTextImageActive), leaveTime_TextImage / 2f);
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
