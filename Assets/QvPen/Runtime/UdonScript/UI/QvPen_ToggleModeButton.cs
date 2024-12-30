using UdonSharp;
using UnityEngine;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0044

namespace QvPen.UdonScript.UI
{
    enum QvPen_ToggleModeButton_Mode
    {
        [InspectorName("Nop")]
        Nop,
        [InspectorName("Use Double Click")]
        UseDoubleClick,
        [InspectorName("Enabled Late Sync")]
        EnabledSync,
        [InspectorName("Use Surftrace Mode")]
        UseSurftraceMode
    }

    [AddComponentMenu("")]
    [DefaultExecutionOrder(30)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ToggleModeButton : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_Settings settings;

        [SerializeField]
        private QvPen_ToggleModeButton_Mode mode = QvPen_ToggleModeButton_Mode.Nop;

        [SerializeField]
        private bool isOn = false;

        [SerializeField]
        private GameObject displayObjectOn;
        [SerializeField]
        private GameObject displayObjectOff;

        private void Start() => UpdateEnabled();

        public override void Interact()
        {
            isOn ^= true;
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            if (Utilities.IsValid(displayObjectOn))
                displayObjectOn.SetActive(isOn);
            if (Utilities.IsValid(displayObjectOff))
                displayObjectOff.SetActive(!isOn);

            switch (mode)
            {
                case QvPen_ToggleModeButton_Mode.UseDoubleClick:
                    foreach (var penManager in settings.penManagers)
                    {
                        if (Utilities.IsValid(penManager))
                            penManager._SetUsingDoubleClick(isOn);
                    }
                    break;
                case QvPen_ToggleModeButton_Mode.EnabledSync:
                    foreach (var penManager in settings.penManagers)
                    {
                        if (Utilities.IsValid(penManager))
                            penManager._SetEnabledLateSync(isOn);
                    }
                    break;
                case QvPen_ToggleModeButton_Mode.UseSurftraceMode:
                    foreach (var penManager in settings.penManagers)
                    {
                        if (Utilities.IsValid(penManager))
                            penManager._SetUsingSurftraceMode(isOn);
                    }
                    break;
            }
        }
    }
}
