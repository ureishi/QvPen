using UdonSharp;
using UnityEngine;

namespace QvPen.UdonScript.UI
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(30)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_ClearAllButton : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_Settings settings;

        public override void Interact()
        {
            foreach (var penManager in settings.penManagers)
                if (penManager)
                    penManager.Clear();
        }
    }
}
