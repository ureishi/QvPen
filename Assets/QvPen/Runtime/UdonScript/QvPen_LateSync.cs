using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

#pragma warning disable IDE0090, IDE1006

namespace QvPen.UdonScript
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvPen_LateSync : UdonSharpBehaviour
    {
        public QvPen_Pen pen { get; set; }

        private LineRenderer[] linesBuffer = { };
        private int inkIndex = -1;

        private VRCPlayerApi master = null;

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (master == null || player.playerId < master.playerId)
                master = player;

            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
                StartSync();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            master = player;

            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
                SendCustomEventDelayedSeconds(nameof(StartSync), 1.84f * (1f + Random.value));

        }

        #region Data protocol

        #region Base

        // Pen mode
        private const int MODE_UNKNOWN = QvPen_Pen.MODE_UNKNOWN;
        private const int MODE_DRAW = QvPen_Pen.MODE_DRAW;
        private const int MODE_ERASE = QvPen_Pen.MODE_ERASE;
        private const int MODE_DRAW_PLANE = QvPen_Pen.MODE_DRAW_PLANE;

        // Footer element
        private const int FOOTER_ELEMENT_DATA_INFO = QvPen_Pen.FOOTER_ELEMENT_DATA_INFO;
        private const int FOOTER_ELEMENT_PEN_ID = QvPen_Pen.FOOTER_ELEMENT_PEN_ID;

        //private const int FOOTER_ELEMENT_DRAW_INK_INFO = QvPen_Pen.FOOTER_ELEMENT_DRAW_INK_INFO;
        //private const int FOOTER_ELEMENT_DRAW_LENGTH = QvPen_Pen.FOOTER_ELEMENT_DRAW_LENGTH;

        //private const int FOOTER_ELEMENT_ERASE_POINTER_POSITION = QvPen_Pen.FOOTER_ELEMENT_ERASE_POINTER_POSITION;
        //private const int FOOTER_ELEMENT_ERASE_LENGTH = QvPen_Pen.FOOTER_ELEMENT_ERASE_LENGTH;

        #endregion

        // Pen sync mode
        private const int SYNC_STATE_Idle = QvPen_Pen.SYNC_STATE_Idle;
        private const int SYNC_STATE_Started = QvPen_Pen.SYNC_STATE_Started;
        private const int SYNC_STATE_Finished = QvPen_Pen.SYNC_STATE_Finished;

        #endregion

        private bool forceStart = false;

        public void StartSync()
        {
            forceStart = true;
            retryCount = 0;

            SendBiginSignal();
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
                    else if (_syncedData != null && _syncedData.Length > 0)
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
                    return;
                else if (signal == beginSignal)
                {
                    forceStart = false;

                    linesBuffer = pen.inkPoolSynced.GetComponentsInChildren<LineRenderer>();

                    inkIndex = -1;
                }
                else if (signal == endSignal)
                {
                    linesBuffer = new LineRenderer[] { };

                    syncedData = new Vector3[] { };
                    isInUseSyncBuffer = false;

                    return;
                }

                var ink = GetNextInk();
                if (ink)
                    SendData(pen._PackData(ink, MODE_DRAW));
                else
                    SendEndSignal();
            }
        }

        private readonly Vector3 beginSignal = new Vector3(2.7182818e8f, 1f, 6.2831853e4f);
        private readonly Vector3 endSignal = new Vector3(2.7182818e8f, 0f, 6.2831853e4f);
        private readonly Vector3 errorSignal = new Vector3(2.7182818e8f, -1f, 6.2831853e4f);

        private void UnpackData(Vector3[] data)
        {
            var penIdVector = GetPenIdVector(data);

            if (pen && pen._CheckId(penIdVector))
            {
                if (pen.currentSyncState == SYNC_STATE_Finished)
                    return;

                var signal = GetCalibrationSignal(data);
                if (signal == beginSignal)
                {
                    if (pen.currentSyncState == SYNC_STATE_Idle)
                        pen.currentSyncState = SYNC_STATE_Started;
                }
                else if (signal == endSignal)
                {
                    if (pen.currentSyncState == SYNC_STATE_Started)
                        pen.currentSyncState = SYNC_STATE_Finished;
                }
                else
                    pen._UnpackData(data);
            }
        }

        private void SendBiginSignal()
            => SendData(new Vector3[] { pen.penIdVector, beginSignal });

        private void SendEndSignal()
            => SendData(new Vector3[] { pen.penIdVector, endSignal });

        private Vector3 GetCalibrationSignal(Vector3[] data)
            => data.Length > 1 ? data[1] : errorSignal;

        private Vector3 GetData(Vector3[] data, int index)
            => data.Length > index ? data[data.Length - 1 - index] : errorSignal;

        private Vector3 GetPenIdVector(Vector3[] data)
            => data.Length > FOOTER_ELEMENT_PEN_ID ? GetData(data, FOOTER_ELEMENT_PEN_ID) : errorSignal;

        private LineRenderer GetNextInk()
        {
            inkIndex = Mathf.Max(-1, inkIndex);

            while (++inkIndex < linesBuffer.Length)
            {
                var ink = linesBuffer[inkIndex];
                if (ink)
                    return ink;
            }

            return null;
        }

        #region Log

        private void Log(object o) => Debug.Log($"{logPrefix}{o}", this);
        private void Warning(object o) => Debug.LogWarning($"{logPrefix}{o}", this);
        private void Error(object o) => Debug.LogError($"{logPrefix}{o}", this);

        private readonly Color logColor = new Color(0xf2, 0x7d, 0x4a, 0xff) / 0xff;
        private string ColorBeginTag(Color c) => $"<color=\"#{ToHtmlStringRGB(c)}\">";
        private const string ColorEndTag = "</color>";

        private string _logPrefix;
        private string logPrefix
            => string.IsNullOrEmpty(_logPrefix)
                ? (_logPrefix = $"[{ColorBeginTag(logColor)}{nameof(QvPen)}.{nameof(QvPen.Udon)}.{nameof(QvPen_LateSync)}{ColorEndTag}] ") : _logPrefix;

        private string ToHtmlStringRGB(Color c)
        {
            c *= 0xff;
            return $"{Mathf.RoundToInt(c.r):x2}{Mathf.RoundToInt(c.g):x2}{Mathf.RoundToInt(c.b):x2}";
        }

        #endregion
    }
}