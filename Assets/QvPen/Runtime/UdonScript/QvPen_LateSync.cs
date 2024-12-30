using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon.Common;
using Utilities = VRC.SDKBase.Utilities;

#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [AddComponentMenu("")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvPen_LateSync : UdonSharpBehaviour
    {
        public QvPen_Pen pen { get; set; }

        [SerializeField]
        private Transform inkPoolSynced;
        public Transform InkPoolSynced => inkPoolSynced;

        [SerializeField]
        private Transform inkPoolNotSynced;
        public Transform InkPoolNotSynced => inkPoolNotSynced;

        private LineRenderer[] linesBuffer = { };
        private int inkIndex = -1;

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
                StartSync();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
                SendCustomEventDelayedSeconds(nameof(StartSync), 1.84f * (1f + Random.value));
        }

        #region Footer

        // Footer element
        private const int FOOTER_ELEMENT_DATA_INFO = QvPen_Pen.FOOTER_ELEMENT_DATA_INFO;
        private const int FOOTER_ELEMENT_PEN_ID = QvPen_Pen.FOOTER_ELEMENT_PEN_ID;

        #endregion

        private bool forceStart = false;

        public void StartSync()
        {
            forceStart = true;
            retryCount = 0;

            SendBeginSignal();
        }

        [UdonSynced]
        private Vector3[] _syncedData;
        private Vector3[] syncedData
        {
            get => _syncedData;
            set
            {
                if (forceStart)
                {
                    _syncedData = new Vector3[] { pen.penIdVector, beginSignal };

                    if (Networking.IsOwner(gameObject))
                        _RequestSendPackage();
                }
                else
                {
                    _syncedData = value;

                    if (Networking.IsOwner(gameObject))
                        _RequestSendPackage();
                    else
                        UnpackData(_syncedData);
                }
            }
        }

        private bool _isNetworkSettled = false;
        private bool isNetworkSettled
            => _isNetworkSettled || (_isNetworkSettled = Networking.IsNetworkSettled);

        private bool isInUseSyncBuffer = false;
        public void _RequestSendPackage()
        {
            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
            {
                if (!isNetworkSettled)
                {
                    SendCustomEventDelayedSeconds(nameof(_RequestSendPackage), 1.84f);
                    return;
                }

                isInUseSyncBuffer = true;
                RequestSerialization();
            }
        }

        private void SendData(Vector3[] data)
        {
            if (!isInUseSyncBuffer)
                syncedData = data;
        }

        public override void OnPreSerialization()
            => _syncedData = syncedData;

        public override void OnDeserialization()
            => syncedData = _syncedData;

        private const int maxRetryCount = 3;
        private int retryCount = 0;
        private LineRenderer nextInk;
        public override void OnPostSerialization(SerializationResult result)
        {
            isInUseSyncBuffer = false;

            if (!result.success)
            {
                if (retryCount++ < maxRetryCount)
                    SendCustomEventDelayedSeconds(nameof(_RequestSendPackage), 1.84f);
            }
            else
            {
                retryCount = 0;

                var signal = GetCalibrationSignal(syncedData);
                if (signal == errorSignal)
                {
                    return;
                }
                else if (signal == beginSignal)
                {
                    forceStart = false;

                    linesBuffer = inkPoolSynced.GetComponentsInChildren<LineRenderer>();

                    inkIndex = -1;
                    nextInk = null;
                }
                else if (signal == endSignal)
                {
                    linesBuffer = new LineRenderer[] { };

                    syncedData = new Vector3[] { };
                    isInUseSyncBuffer = false;

                    return;
                }

                var ink = nextInk;

                if (!Utilities.IsValid(ink))
                    ink = GetNextInk();

                if (Utilities.IsValid(ink))
                {
                    var totalLength = 0;
                    var dataList = new DataList();
                    var lengthList = new DataList();

                    while (Utilities.IsValid(ink))
                    {
                        if (!QvPenUtilities.TryGetIdFromInk(ink.gameObject, out var _discard, out var inkIdVector, out var ownerIdVector))
                        {
                            ink = GetNextInk();
                            continue;
                        }

                        var data = pen._PackData(ink, QvPen_Pen_Mode.Draw, inkIdVector, ownerIdVector);
                        var length = data.Length;

                        dataList.Add(new DataToken(data));
                        lengthList.Add(length);
                        totalLength += length;

                        ink = GetNextInk();

                        if (!Utilities.IsValid(ink))
                        {
                            nextInk = null;
                            break;
                        }

                        if (totalLength + ink.positionCount > 80)
                        {
                            nextInk = ink;
                            break;
                        }
                    }

                    var lengthVectors = new Vector3[(lengthList.Count + 2) / 3];
                    for (int i = 0, n = lengthList.Count; i < n; i++)
                    {
                        if (!lengthList.TryGetValue(i, TokenType.Int, out var lengthToken))
                            continue;

                        lengthVectors[i / 3][i % 3] = lengthToken.Int;
                    }

                    var joinedData = new Vector3[2 + lengthVectors.Length + totalLength];
                    var index = 0;

                    joinedData[0] = pen.penIdVector;
                    index += 1;

                    joinedData[1] = new Vector3(lengthList.Count, joinedData.Length, 0f);
                    index += 1;

                    System.Array.Copy(lengthVectors, 0, joinedData, index, lengthVectors.Length);
                    index += lengthVectors.Length;

                    for (int i = 0, n = dataList.Count; i < n; i++)
                    {
                        if (!dataList.TryGetValue(i, TokenType.Reference, out var dataToken))
                            continue;

                        var data = (Vector3[])dataToken.Reference;
                        System.Array.Copy(data, 0, joinedData, index, data.Length);
                        index += data.Length;
                    }

                    dataList.Clear();
                    lengthList.Clear();

                    SendData(joinedData);
                }
                else
                {
                    SendEndSignal();
                }
            }
        }

        private readonly Vector3 beginSignal = new Vector3(2.7182818e8f, 1f, 6.2831853e4f);
        private readonly Vector3 endSignal = new Vector3(2.7182818e8f, 0f, 6.2831853e4f);
        private readonly Vector3 errorSignal = new Vector3(2.7182818e8f, -1f, 6.2831853e4f);

        private void UnpackData(Vector3[] data)
        {
            if (_syncedData == null || _syncedData.Length < 2)
                return;

            var penIdVector = GetPenIdVector(data);

            if (Utilities.IsValid(pen) && pen._CheckId(penIdVector))
            {
                var currentSyncState = pen.currentSyncState;

                if (currentSyncState == QvPen_Pen_SyncState.Finished)
                    return;

                var signal = GetCalibrationSignal(data);
                if (signal == beginSignal)
                {
                    if (currentSyncState == QvPen_Pen_SyncState.Idle)
                        pen.currentSyncState = QvPen_Pen_SyncState.Started;
                }
                else if (signal == endSignal)
                {
                    if (currentSyncState == QvPen_Pen_SyncState.Started)
                        pen.currentSyncState = QvPen_Pen_SyncState.Finished;
                }
                else if (data.Length > 2)
                {
                    var index = 1;

                    var length = (int)data[index].x;
                    var check = (int)data[index].y;
                    index += 1;

                    if (check != data.Length)
                        return;

                    var lengthVectors = new Vector3[(length + 2) / 3];

                    System.Array.Copy(data, index, lengthVectors, 0, lengthVectors.Length);
                    index += lengthVectors.Length;

                    for (var i = 0; i < length; i++)
                    {
                        var dataLength = (int)lengthVectors[i / 3][i % 3];
                        var stroke = new Vector3[dataLength];

                        System.Array.Copy(data, index, stroke, 0, dataLength);
                        index += dataLength;

                        pen._UnpackData(stroke, QvPen_Pen_Mode.Any);
                    }
                }
            }
        }

        private void SendBeginSignal()
            => SendData(new Vector3[] { pen.penIdVector, beginSignal });

        private void SendEndSignal()
            => SendData(new Vector3[] { pen.penIdVector, endSignal });

        private Vector3 GetCalibrationSignal(Vector3[] data)
            => data != null && data.Length > 1 ? data[1] : errorSignal;

        private Vector3 GetData(Vector3[] data, int index)
            => data != null && data.Length > index ? data[data.Length - 1 - index] : errorSignal;

        private Vector3 GetPenIdVector(Vector3[] data)
            => data != null && data.Length > FOOTER_ELEMENT_PEN_ID ? GetData(data, FOOTER_ELEMENT_PEN_ID) : errorSignal;

        private LineRenderer GetNextInk()
        {
            inkIndex = Mathf.Max(-1, inkIndex);

            while (++inkIndex < linesBuffer.Length)
            {
                var ink = linesBuffer[inkIndex];
                if (Utilities.IsValid(ink))
                    return ink;
            }

            return null;
        }

        #region Log

        private const string appName = nameof(QvPen_LateSync);

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