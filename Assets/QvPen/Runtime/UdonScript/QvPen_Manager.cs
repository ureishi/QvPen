using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0044
#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QvPen_Manager : UdonSharpBehaviour
    {
        [SerializeField]
        private bool allowCallPen = true;

        private readonly DataDictionary penDict = new DataDictionary();
        private readonly DataDictionary inkDictMap = new DataDictionary();

        public void Register(int penId, QvPen_Pen pen)
        {
            if (penDict.ContainsKey(penId))
                return;

            penDict[penId] = pen;
            inkDictMap[penId] = new DataDictionary();
        }

        #region Call Pen

        private void Update()
        {
            if (!allowCallPen || !Input.anyKeyDown)
                return;

            if (Input.GetKeyDown(KeyCode.Q) && Input.GetKey(KeyCode.Tab))
                CallPen();
        }

        private QvPen_Pen lastUsedPen;

        public void SetLastUsedPen(QvPen_Pen pen)
        {
            lastUsedPen = pen;
        }

        private void CallPen()
        {
            var pen = GetPen();

            if (Utilities.IsValid(pen))
            {
                var x = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                var forward = x.rotation * Vector3.forward;
                pen.transform.position = x.position + 0.5f * forward;
                pen.transform.LookAt(x.position + forward);
            }
        }

        private QvPen_Pen GetPen()
        {
            var pickupR = Networking.LocalPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Right);

            if (Utilities.IsValid(pickupR))
                return null;

            var pickupL = Networking.LocalPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Left);

            if (Utilities.IsValid(pickupL))
                return null;

            if (Utilities.IsValid(lastUsedPen) && !lastUsedPen.isHeld)
            {
                lastUsedPen._TakeOwnership();

                return lastUsedPen;
            }

            var penList = penDict.GetValues();

            var indexList = new int[penList.Count];

            for (int i = 0, n = penList.Count; i < n; i++)
                indexList[i] = i;

            Utilities.ShuffleArray(indexList);

            for (int i = 0, n = indexList.Length; i < n; i++)
            {
                var index = indexList[i];

                if (!penList.TryGetValue(index, TokenType.Reference, out var penToken))
                    continue;

                var pen = (QvPen_Pen)penToken.Reference;

                if (!pen.isHeld)
                {
                    pen._TakeOwnership();

                    lastUsedPen = pen;
                    return pen;
                }
            }

            return null;
        }

        #endregion

        #region Manage Ink

        public bool HasInk(int penId, int inkId)
        {
            if (!inkDictMap.TryGetValue(penId, TokenType.DataDictionary, out var inkDictToken))
                return false;

            if (!inkDictToken.DataDictionary.ContainsKey(inkId))
                return false;

            return true;
        }

        public void SetInk(int penId, int inkId, GameObject inkInstance)
        {
            inkDictMap[penId].DataDictionary[inkId] = inkInstance;
        }

        public bool RemoveInk(int penId, int inkId)
        {
            if (!HasInk(penId, inkId))
                return false;

            var inkDict = inkDictMap[penId].DataDictionary;

            if (!inkDict.TryGetValue(inkId, TokenType.Reference, out var inkToken))
                return false;

            var ink = (GameObject)inkToken.Reference;

            if (!Utilities.IsValid(ink))
            {
                inkDict.Remove(inkId);
                return false;
            }

            Destroy(ink.GetComponentInChildren<MeshCollider>(true).sharedMesh);
            Destroy(ink);

            inkDict.Remove(inkId);

            return true;
        }

        public bool RemoveUserInk(int penId, Vector3 ownerIdVector)
        {
            var inkDict = inkDictMap[penId].DataDictionary;

            var inkIdList = inkDict.GetKeys();

            var removed = new DataList();

            for (int i = 0, n = inkIdList.Count; i < n; i++)
            {
                if (!inkIdList.TryGetValue(i, TokenType.Int, out var inkIdToken))
                    continue;

                if (!inkDict.TryGetValue(inkIdToken, TokenType.Reference, out var inkToken))
                    continue;

                var ink = (GameObject)inkToken.Reference;

                if (!Utilities.IsValid(ink))
                {
                    inkDict.Remove(inkIdToken);
                    continue;
                }

                if (!QvPenUtilities.TryGetIdFromInk(ink, out var _discard1, out var _discard2, out var inkOwnerIdVector))
                    continue;

                if (inkOwnerIdVector != ownerIdVector)
                    continue;

                Destroy(ink.GetComponentInChildren<MeshCollider>(true).sharedMesh);
                Destroy(ink);

                removed.Add(inkIdToken);
            }

            for (int i = 0, n = removed.Count; i < n; i++)
            {
                if (!removed.TryGetValue(i, TokenType.Int, out var inkIdToken))
                    continue;

                inkDict.Remove(inkIdToken);
            }

            return true;
        }

        public void Clear(int penId)
        {
            if (!inkDictMap.TryGetValue(penId, TokenType.DataDictionary, out var inkDictToken))
                return;

            var inkDict = inkDictToken.DataDictionary;
            var inkTokens = inkDict.GetValues();

            for (int i = 0, n = inkTokens.Count; i < n; i++)
            {
                if (!inkTokens.TryGetValue(i, TokenType.Reference, out var inkToken))
                    continue;

                var ink = (GameObject)inkToken.Reference;

                if (!Utilities.IsValid(ink))
                    continue;

                Destroy(ink.GetComponentInChildren<MeshCollider>(true).sharedMesh);
                Destroy(ink);
            }

            inkDict.Clear();
        }

        #endregion

        #region Log

        private const string appName = nameof(QvPen_Manager);

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
