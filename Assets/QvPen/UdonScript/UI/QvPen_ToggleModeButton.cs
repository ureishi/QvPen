using UdonSharp;
using UnityEngine;

#pragma warning disable IDE0044

namespace QvPen.UdonScript.UI
{
    [DefaultExecutionOrder(30)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ToggleModeButton : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_Settings settings;

        [Header("0:Noop, 1:DoubleClick, 2:LateSync, 3:Surftrace")]
        [SerializeField]
        private int mode = MODE_Noop;

        [SerializeField]
        private bool isOn = false;

        [SerializeField]
        private GameObject displayObjectOn;
        [SerializeField]
        private GameObject displayObjectOff;

        // Mode
        public const int MODE_Noop = 0;
        public const int MODE_UseDoubleClick = 1;
        public const int MODE_EnabledSync = 2;
        public const int MODE_UseSurftraceMode = 3;

        private void Start() => UpdateEnabled();

        public override void Interact()
        {
            isOn ^= true;
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            if (displayObjectOn)
                displayObjectOn.SetActive(isOn);
            if (displayObjectOff)
                displayObjectOff.SetActive(!isOn);

            foreach (var penManager in settings.penManagers)
                if (penManager)
                    switch (mode)
                    {
                        case MODE_UseDoubleClick:
                            penManager._SetUsingDoubleClick(isOn);
                            break;
                        case MODE_EnabledSync:
                            penManager._SetEnabledLateSync(isOn);
                            break;
                        case MODE_UseSurftraceMode:
                            penManager._SetUsingSurftraceMode(isOn);
                            break;
                    }
        }
    }
}
