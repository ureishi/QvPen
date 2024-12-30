using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_Settings : UdonSharpBehaviour
    {
        [System.NonSerialized]
        public string version = string.Empty;

        [SerializeField]
        private TextAsset versionText;

        [SerializeField]
        private Text information;
        [SerializeField]
        private TextMeshPro informationTMP;
        [SerializeField]
        private TextMeshProUGUI informationTMPU;

        [SerializeField]
        private Transform pensParent;
        [SerializeField]
        private Transform erasersParent;

        [System.NonSerialized]
        public QvPen_PenManager[] penManagers = { };

        [System.NonSerialized]
        public QvPen_EraserManager[] eraserManagers = { };

        private void Start()
        {
            if (Utilities.IsValid(versionText))
                version = versionText.text.Trim();

#if !UNITY_EDITOR
            const string ureishi = nameof(ureishi);
            Log($"{nameof(QvPen)} {version} - {ureishi}");
#endif

            var informationText =
                    $"<size=20></size>\n" +
                    $"<size=14>{version}</size>";

            if (Utilities.IsValid(information))
                information.text = informationText;
            if (Utilities.IsValid(informationTMP))
                informationTMP.text = informationText;
            if (Utilities.IsValid(informationTMPU))
                informationTMPU.text = informationText;

            if (Utilities.IsValid(pensParent))
                penManagers = pensParent.GetComponentsInChildren<QvPen_PenManager>();
            if (Utilities.IsValid(erasersParent))
                eraserManagers = erasersParent.GetComponentsInChildren<QvPen_EraserManager>();
        }

        #region Log

        private const string appName = nameof(QvPen_Settings);

        private void Log(object o) => Debug.Log($"{logPrefix}{o}", this);
        private void Warning(object o) => Debug.LogWarning($"{logPrefix}{o}", this);
        private void Error(object o) => Debug.LogError($"{logPrefix}{o}", this);

        private readonly Color logColor = new Color(0xf2, 0x7d, 0x4a, 0xff) / 0xff;
        private string ColorBeginTag(Color c) => $"<color=\"#{ToHtmlStringRGB(c)}\">";
        private const string ColorEndTag = "</color>";

        private string _logPrefix;
        private string logPrefix
            => !string.IsNullOrEmpty(_logPrefix)
                ? _logPrefix : (_logPrefix = $"[{ColorBeginTag(logColor)}{nameof(QvPen)}.{nameof(QvPen.Udon)}.{appName}{ColorEndTag}] ");

        private static string ToHtmlStringRGB(Color c)
        {
            c *= 0xff;
            return $"{Mathf.RoundToInt(c.r):x2}{Mathf.RoundToInt(c.g):x2}{Mathf.RoundToInt(c.b):x2}";
        }

        #endregion
    }
}
