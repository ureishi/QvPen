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

        private const float UndoHoldDurationSeconds = 0.31f;
        private const float ClearHoldDurationSeconds = 2f;

        private float undoDeadline;
        private float clearDeadline;

        private bool isInteracting;

        private bool isPickedUp;
        private const float TextVisibilityDurationSeconds = 5f;
        private float textVisibilityDeadline;

        private void Start()
        {
            SetIndicatorsActive(false);
            SetTextImagesActive(false);

            penManager.RegisterListener(this);
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value)
                return;

            if (isInteracting && Time.time < undoDeadline)
                UndoLastStroke();

            isInteracting = false;
            SetIndicatorsActive(false);
        }

        public override void Interact()
        {
            isInteracting = true;
            SetIndicatorsActive(true);

            undoDeadline = Time.time + UndoHoldDurationSeconds;
            clearDeadline = Time.time + ClearHoldDurationSeconds;
            textVisibilityDeadline = Time.time + TextVisibilityDurationSeconds;

            SendCustomEventDelayedSeconds(nameof(_LoopIndicator1), 0f);
            StartTextVisibilityLoop();
        }

        public override void _OnPenPickup()
        {
            isPickedUp = true;
            StartTextVisibilityLoop();
        }

        public override void _OnPenDrop()
        {
            isPickedUp = false;

            textVisibilityDeadline = Time.time + TextVisibilityDurationSeconds;
            StartTextVisibilityLoop();
        }

        private void SetIndicatorsActive(bool isActive)
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

        private void SetTextImagesActive(bool isActive)
        {
            if (Utilities.IsValid(ownTextImage))
                ownTextImage.gameObject.SetActive(isActive);

            if (Utilities.IsValid(allTextImage))
                allTextImage.gameObject.SetActive(isActive);
        }

        private void SetIndicatorValues(float ownIndicatorValue, float allIndicatorValue)
        {
            if (Utilities.IsValid(ownIndicator))
                ownIndicator.fillAmount = Mathf.Clamp01(ownIndicatorValue);

            if (Utilities.IsValid(allIndicator))
                allIndicator.fillAmount = Mathf.Clamp01(allIndicatorValue);
        }

        public void _LoopIndicator1()
        {
            if (!isInteracting)
                return;

            var time = Time.time;

            var undoTimeRemaining = undoDeadline - time;
            var clearTimeRemaining = clearDeadline - time;
            if (undoTimeRemaining <= 0f)
            {
                EraseOwnStrokes();

                SetIndicatorValues(1f, 1f - clearTimeRemaining / ClearHoldDurationSeconds);

                SendCustomEventDelayedFrames(nameof(_LoopIndicator2), 0);

                return;
            }

            SetIndicatorValues(1f - undoTimeRemaining / UndoHoldDurationSeconds,
                1f - clearTimeRemaining / ClearHoldDurationSeconds);

            SendCustomEventDelayedFrames(nameof(_LoopIndicator1), 0);
        }

        public void _LoopIndicator2()
        {
            if (!isInteracting)
                return;

            var clearTimeRemaining = clearDeadline - Time.time;
            if (clearTimeRemaining <= 0f)
            {
                Clear();

                SetIndicatorValues(0f, 0f);

                return;
            }

            SetIndicatorValues(0f, 1f - clearTimeRemaining / ClearHoldDurationSeconds);

            SendCustomEventDelayedFrames(nameof(_LoopIndicator2), 0);
        }

        private bool isTextVisibilityLoopActive;

        private void StartTextVisibilityLoop()
        {
            if (isTextVisibilityLoopActive)
                return;

            isTextVisibilityLoopActive = true;

            if (isPickedUp)
            {
                StopTextVisibilityLoop();
                return;
            }

            SetTextImagesActive(true);

            _LoopTextImageActive();
        }

        private void StopTextVisibilityLoop()
        {
            if (!isTextVisibilityLoopActive)
                return;

            isTextVisibilityLoopActive = false;

            SetTextImagesActive(false);
        }

        public void _LoopTextImageActive()
        {
            if (!isTextVisibilityLoopActive)
                return;

            if (isPickedUp)
            {
                StopTextVisibilityLoop();
                return;
            }

            var time = Time.time;

            var visibilityTimeRemaining = textVisibilityDeadline - time;
            if (visibilityTimeRemaining <= 0f)
            {
                StopTextVisibilityLoop();
                return;
            }

            SendCustomEventDelayedSeconds(nameof(_LoopTextImageActive), visibilityTimeRemaining / 2f);
        }

        private void EraseOwnStrokes()
        {
            if (Utilities.IsValid(penManager))
                penManager._EraseOwnStrokes();
        }

        private void UndoLastStroke()
        {
            if (Utilities.IsValid(penManager))
                penManager._UndoLastStroke();
        }

        private void Clear()
        {
            if (Utilities.IsValid(penManager))
                penManager.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(QvPen_PenManager.Clear));
        }
    }
}
