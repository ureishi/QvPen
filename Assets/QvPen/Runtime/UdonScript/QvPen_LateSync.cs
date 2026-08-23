using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Network;
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
        public QvPen_Pen pen { get; private set; }

        [SerializeField]
        private Transform inkPoolSynced;
        public Transform InkPoolSynced => inkPoolSynced;

        [SerializeField]
        private Transform inkPoolNotSynced;
        public Transform InkPoolNotSynced => inkPoolNotSynced;

        private DataList inkIdBuffer = new DataList();
        private int inkIndex = -1;

        public void _RegisterPen(QvPen_Pen pen)
        {
            this.pen = pen;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
            {
                joinGraceDeadline = Time.time + JoinGracePeriodSeconds;
                RequestSyncRound(player.playerId);
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject))
                return;

            RemoveTargetPlayer(currentRoundTargetIds, player.playerId);
            RemoveTargetPlayer(nextRoundTargetIds, player.playerId);

            // The packets are broadcast, so players waiting for the next
            // round can keep consuming the current round after its original
            // targets have left. Stop only when nobody still needs it.
            abortCurrentRound = isCurrentRoundTargeted &&
                currentRoundTargetIds.Count == 0 && nextRoundTargetIds.Count == 0;

        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ResetLocalSyncRoundState();

            if (VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
            {
                isOwnershipRecoveryScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_StartOwnershipRecoveryRound), 1.84f * (1f + Random.value));
            }
        }

        private const float JoinGracePeriodSeconds = 4f;
        private const int MinimumStrokePacketVectorTarget = 24;
        private const int InitialStrokePacketVectorTarget = 80;
        private const int MaximumStrokePacketVectorTarget = 256;
        private const int HealthyPacketsBeforeTargetIncrease = 3;
        private const int PacketTargetIncreaseStep = 8;
        private const float ModerateThroughputThreshold = 0.6f;
        private const float HighThroughputThreshold = 0.8f;
        private const float MinimumInterPacketDelaySeconds = 0.1f;
        private const float MaximumInterPacketDelaySeconds = 1.5f;
        private const int MaxStrokeCountPerPacket = 64;
        private readonly Vector3[] emptyPacketData = { };

        private bool isSyncRoundActive = false;
        private bool isRoundStartScheduled = false;
        private bool isCurrentRoundTargeted = false;
        private bool isGenericNextRoundRequired = false;
        private bool startForWaitingPlayers = false;
        private bool abortCurrentRound = false;
        private bool isOwnershipRecoveryScheduled = false;
        private bool isSendRetryScheduled = false;
        private bool isNextPacketScheduled = false;
        private float joinGraceDeadline = 0f;
        private float roundStartEarliestTime = 0f;
        private float nextPacketEarliestTime = 0f;
        private int currentStrokePacketVectorTarget = InitialStrokePacketVectorTarget;
        private int consecutiveHealthyStrokePackets = 0;

        public float LastNetworkSuffering { get; private set; }
        public float LastThroughputPercentage { get; private set; }
        public int LastReliableOutboundQueue { get; private set; }
        public float LastSendRetryDelay { get; private set; }
        public int LastSerializedByteCount { get; private set; }
        public int SuccessfulSerializationCount { get; private set; }
        public int FailedSerializationCount { get; private set; }
        public int CurrentStrokePacketVectorTarget => currentStrokePacketVectorTarget;
        public float LastInterPacketDelay { get; private set; }

        private readonly DataList currentRoundTargetIds = new DataList();
        private readonly DataList nextRoundTargetIds = new DataList();

        private void ResetLocalSyncRoundState()
        {
            isSyncRoundActive = false;
            isRoundStartScheduled = false;
            isCurrentRoundTargeted = false;
            isGenericNextRoundRequired = false;
            startForWaitingPlayers = false;
            abortCurrentRound = false;
            isOwnershipRecoveryScheduled = false;
            isSendRetryScheduled = false;
            isNextPacketScheduled = false;
            isSerializationInFlight = false;
            serializationRetryCount = 0;
            joinGraceDeadline = 0f;
            roundStartEarliestTime = 0f;
            nextPacketEarliestTime = 0f;
            currentStrokePacketVectorTarget = InitialStrokePacketVectorTarget;
            consecutiveHealthyStrokePackets = 0;
            inkIdBuffer = new DataList();
            inkIndex = -1;
            nextInk = null;
            currentRoundTargetIds.Clear();
            nextRoundTargetIds.Clear();
            packetInkList.Clear();
            packetLengthList.Clear();
            _syncedPacketKind = (int)QvPen_LateSyncPacketKind.None;
            _syncedData = emptyPacketData;
        }

        public void _StartOwnershipRecoveryRound()
        {
            if (!isOwnershipRecoveryScheduled)
                return;

            isOwnershipRecoveryScheduled = false;

            if (VRCPlayerApi.GetPlayerCount() <= 1 || !Networking.IsOwner(gameObject))
                return;

            isGenericNextRoundRequired = true;
            _StartSyncRound();
        }

        private void RequestSyncRound(int playerId)
        {
            AddTargetPlayer(nextRoundTargetIds, playerId);

            // A join may arrive after the last current target left but before
            // the deferred abort is processed. Keep the cursor moving so the
            // new player can consume the remainder of this round immediately.
            if (isSyncRoundActive)
                abortCurrentRound = false;

            if (isSyncRoundActive)
                return;

            if (!isRoundStartScheduled)
            {
                startForWaitingPlayers = true;
                _StartSyncRound();
            }
        }

        private void AddTargetPlayer(DataList targetIds, int playerId)
        {
            if (!targetIds.Contains(playerId))
                targetIds.Add(playerId);
        }

        private void RemoveTargetPlayer(DataList targetIds, int playerId)
        {
            for (int i = targetIds.Count - 1; i >= 0; i--)
            {
                if (!targetIds.TryGetValue(i, TokenType.Int, out var playerIdToken))
                    continue;

                if (playerIdToken.Int == playerId)
                {
                    targetIds.RemoveAt(i);
                    return;
                }
            }
        }

        private void MoveWaitingPlayersToCurrentRound()
        {
            currentRoundTargetIds.Clear();

            for (int i = 0, n = nextRoundTargetIds.Count; i < n; i++)
            {
                if (nextRoundTargetIds.TryGetValue(i, TokenType.Int, out var playerIdToken))
                    currentRoundTargetIds.Add(playerIdToken);
            }

            nextRoundTargetIds.Clear();
        }

        public void _StartSyncRound()
        {
            var isTargetedStart = startForWaitingPlayers && !isGenericNextRoundRequired;
            startForWaitingPlayers = false;
            isRoundStartScheduled = false;

            if (VRCPlayerApi.GetPlayerCount() <= 1 || !Networking.IsOwner(gameObject))
            {
                return;
            }

            var joinGraceTimeRemaining = joinGraceDeadline - Time.time;
            if (joinGraceTimeRemaining > 0f)
            {
                isRoundStartScheduled = true;
                roundStartEarliestTime = joinGraceDeadline;
                SendCustomEventDelayedSeconds(nameof(_StartNextSyncRound), joinGraceTimeRemaining);
                return;
            }

            if (isSyncRoundActive)
            {
                if (!isTargetedStart)
                    isGenericNextRoundRequired = true;
                return;
            }

            if (isTargetedStart && nextRoundTargetIds.Count == 0)
            {
                return;
            }

            MoveWaitingPlayersToCurrentRound();
            isCurrentRoundTargeted = isTargetedStart;
            isGenericNextRoundRequired = false;
            abortCurrentRound = false;
            isSyncRoundActive = true;
            serializationRetryCount = 0;
            SendBeginPacket();
        }

        public void _StartNextSyncRound()
        {
            if (!isRoundStartScheduled)
                return;

            var startDelayRemaining = roundStartEarliestTime - Time.time;
            if (startDelayRemaining > 0.001f)
            {
                SendCustomEventDelayedSeconds(nameof(_StartNextSyncRound), startDelayRemaining);
                return;
            }

            isRoundStartScheduled = false;
            startForWaitingPlayers = !isGenericNextRoundRequired;
            _StartSyncRound();
        }

        private void FinishSyncRound(bool retryCurrentRound)
        {
            if (retryCurrentRound)
            {
                if (isCurrentRoundTargeted)
                {
                    for (int i = 0, n = currentRoundTargetIds.Count; i < n; i++)
                    {
                        if (currentRoundTargetIds.TryGetValue(i, TokenType.Int, out var playerIdToken))
                            AddTargetPlayer(nextRoundTargetIds, playerIdToken.Int);
                    }
                }
                else
                {
                    isGenericNextRoundRequired = true;
                }
            }

            isSyncRoundActive = false;
            isCurrentRoundTargeted = false;
            abortCurrentRound = false;
            isSendRetryScheduled = false;
            isNextPacketScheduled = false;
            inkIdBuffer = new DataList();
            inkIndex = -1;
            nextInk = null;
            currentRoundTargetIds.Clear();
            packetInkList.Clear();
            packetLengthList.Clear();

            var shouldStartAnotherRound = isGenericNextRoundRequired || nextRoundTargetIds.Count > 0;

            if (shouldStartAnotherRound && !isRoundStartScheduled
                && VRCPlayerApi.GetPlayerCount() > 1 && Networking.IsOwner(gameObject))
            {
                isRoundStartScheduled = true;
                roundStartEarliestTime = Time.time;
                SendCustomEventDelayedFrames(nameof(_StartNextSyncRound), 1);
            }
        }

        [UdonSynced]
        private int _syncedPacketKind = (int)QvPen_LateSyncPacketKind.None;

        [UdonSynced]
        private Vector3[] _syncedData = { };

        private bool hasObservedSettledNetwork = false;

        private bool HasNetworkSettled()
        {
            if (!hasObservedSettledNetwork)
                hasObservedSettledNetwork = Networking.IsNetworkSettled;

            return hasObservedSettledNetwork;
        }

        private bool isSerializationInFlight = false;
        public void _RequestPacketSend()
        {
            if (!isSyncRoundActive || isSerializationInFlight
                || VRCPlayerApi.GetPlayerCount() <= 1 || !Networking.IsOwner(gameObject))
                return;

            if (!HasNetworkSettled() || IsNetworkUnderPressure())
            {
                ScheduleSendRetry();
                return;
            }

            isSerializationInFlight = true;
            RequestSerialization();
        }

        private void ScheduleSendRetry()
        {
            if (isSendRetryScheduled)
                return;

            isSendRetryScheduled = true;
            LastSendRetryDelay = GetSendRetryDelay();
            SendCustomEventDelayedSeconds(nameof(_RetryPacketSend), LastSendRetryDelay);
        }

        private float GetSendRetryDelay()
        {
            const float baseDelay = 1.84f;

            RefreshNetworkStats();

            var pressure = Mathf.Clamp01(Mathf.Max(LastNetworkSuffering, LastThroughputPercentage));
            pressure += Mathf.Min(LastReliableOutboundQueue, 4) * 0.25f;

            return Mathf.Clamp(baseDelay * (1f + pressure), baseDelay, baseDelay * 4f);
        }

        private void RefreshNetworkStats()
        {
            LastNetworkSuffering = Mathf.Max(0f, Stats.Suffering);
            LastThroughputPercentage = Mathf.Max(0f, Stats.ThroughputPercentage);
            LastReliableOutboundQueue = Mathf.Max(0, Stats.ReliableEventsInOutboundQueue(gameObject));
        }

        private bool IsNetworkUnderPressure()
        {
            RefreshNetworkStats();

            return Networking.IsClogged || LastNetworkSuffering > 0f ||
                LastThroughputPercentage >= HighThroughputThreshold || LastReliableOutboundQueue > 0;
        }

        public void _RetryPacketSend()
        {
            // A reset caused by ownership transfer or round completion invalidates
            // delayed callbacks left by the previous round.
            if (!isSendRetryScheduled)
                return;

            isSendRetryScheduled = false;
            _RequestPacketSend();
        }

        private void SendPacket(QvPen_LateSyncPacketKind packetKind, Vector3[] data)
        {
            if (isSerializationInFlight)
                return;

            _syncedPacketKind = (int)packetKind;
            _syncedData = data;
            _RequestPacketSend();
        }

        public override void OnDeserialization()
        {
            if (Networking.IsOwner(gameObject) || !Utilities.IsValid(pen))
                return;

            switch ((QvPen_LateSyncPacketKind)_syncedPacketKind)
            {
                case QvPen_LateSyncPacketKind.Begin:
                    // Every complete round is an idempotent convergence pass.
                    pen.currentSyncState = QvPen_Pen_SyncState.Started;
                    break;
                case QvPen_LateSyncPacketKind.StrokeData:
                    // A late joiner may miss Begin but can still apply every valid
                    // stroke received from the remainder of the current round.
                    // CreateInkInstance rejects duplicate pen/ink IDs, so the next
                    // complete round fills gaps without redrawing existing strokes.
                    UnpackStrokePacket(_syncedData);
                    break;
                case QvPen_LateSyncPacketKind.End:
                    if (pen.currentSyncState == QvPen_Pen_SyncState.Started)
                        pen.currentSyncState = QvPen_Pen_SyncState.Finished;
                    break;
            }
        }

        private const int MaxSerializationRetryCount = 3;
        private int serializationRetryCount = 0;
        private LineRenderer nextInk;
        private readonly DataList packetInkList = new DataList(8);
        private readonly DataList packetLengthList = new DataList(8);
        // Grows to the largest stroke seen and is reused by later packages.
        private Vector3[] packetPositionBuffer = { };
        public override void OnPostSerialization(SerializationResult result)
        {
            isSerializationInFlight = false;
            LastSerializedByteCount = result.byteCount;

            if (result.success)
                SuccessfulSerializationCount++;
            else
                FailedSerializationCount++;

            UpdateStrokePacketTarget(result.success);

            if (abortCurrentRound)
            {
                FinishSyncRound(false);
                return;
            }

            if (!result.success)
            {
                if (serializationRetryCount++ < MaxSerializationRetryCount)
                    ScheduleSendRetry();
                else
                    FinishSyncRound(true);
            }
            else
            {
                serializationRetryCount = 0;

                if ((QvPen_LateSyncPacketKind)_syncedPacketKind == QvPen_LateSyncPacketKind.Begin)
                {
                    // Keep one sorted key snapshot for the entire round so
                    // later additions cannot move the active send cursor.
                    inkIdBuffer = pen._GetSortedInkIds();

                    inkIndex = -1;
                    nextInk = null;
                }
                else if ((QvPen_LateSyncPacketKind)_syncedPacketKind == QvPen_LateSyncPacketKind.End)
                {
                    _syncedPacketKind = (int)QvPen_LateSyncPacketKind.None;
                    _syncedData = emptyPacketData;
                    FinishSyncRound(false);

                    return;
                }

                ScheduleNextRoundPacket();
            }
        }

        private void UpdateStrokePacketTarget(bool serializationSucceeded)
        {
            if ((QvPen_LateSyncPacketKind)_syncedPacketKind != QvPen_LateSyncPacketKind.StrokeData)
                return;

            RefreshNetworkStats();

            if (!serializationSucceeded || LastNetworkSuffering > 0f ||
                LastThroughputPercentage >= ModerateThroughputThreshold || LastReliableOutboundQueue > 0)
            {
                currentStrokePacketVectorTarget = Mathf.Max(MinimumStrokePacketVectorTarget,
                    currentStrokePacketVectorTarget / 2);
                consecutiveHealthyStrokePackets = 0;
                return;
            }

            if (++consecutiveHealthyStrokePackets < HealthyPacketsBeforeTargetIncrease)
                return;

            consecutiveHealthyStrokePackets = 0;
            currentStrokePacketVectorTarget = Mathf.Min(MaximumStrokePacketVectorTarget,
                currentStrokePacketVectorTarget + PacketTargetIncreaseStep);
        }

        private float GetInterPacketDelay()
        {
            RefreshNetworkStats();

            var pressure = Mathf.Clamp01(Mathf.Max(LastNetworkSuffering, LastThroughputPercentage));
            pressure += Mathf.Min(LastReliableOutboundQueue, 4) * 0.25f;

            return Mathf.Lerp(MinimumInterPacketDelaySeconds, MaximumInterPacketDelaySeconds,
                Mathf.Clamp01(pressure));
        }

        private void ScheduleNextRoundPacket()
        {
            if (isNextPacketScheduled || !isSyncRoundActive || !Networking.IsOwner(gameObject))
                return;

            isNextPacketScheduled = true;
            // A join only queues that player for the next round. It must not
            // pause or rewind the round that is already being transmitted.
            LastInterPacketDelay = GetInterPacketDelay();
            nextPacketEarliestTime = Time.time + LastInterPacketDelay;
            SendCustomEventDelayedSeconds(nameof(_SendNextRoundPacket), LastInterPacketDelay);
        }

        public void _SendNextRoundPacket()
        {
            if (!isNextPacketScheduled)
                return;

            var packetDelayRemaining = nextPacketEarliestTime - Time.time;
            if (packetDelayRemaining > 0.001f)
            {
                SendCustomEventDelayedSeconds(nameof(_SendNextRoundPacket), packetDelayRemaining);
                return;
            }

            isNextPacketScheduled = false;

            if (!isSyncRoundActive || isSerializationInFlight || !Networking.IsOwner(gameObject))
                return;

            if (abortCurrentRound)
            {
                FinishSyncRound(false);
                return;
            }

            if (IsNetworkUnderPressure())
            {
                ScheduleNextRoundPacket();
                return;
            }

            var ink = nextInk;

            if (!Utilities.IsValid(ink))
                ink = GetNextInk();

            if (!Utilities.IsValid(ink))
            {
                SendEndPacket();
                return;
            }

            var totalLength = 0;
            packetInkList.Clear();
            packetLengthList.Clear();
            var maxPositionCount = 0;

            while (Utilities.IsValid(ink))
            {
                if (!QvPenUtilities.TryGetIdFromInk(ink.gameObject, out var _discard,
                    out var inkIdVector, out var ownerIdVector))
                {
                    ink = GetNextInk();
                    continue;
                }

                var positionCount = ink.positionCount;
                var length = pen._GetPackedDrawDataLength(ink);

                if (length <= 0)
                {
                    ink = GetNextInk();
                    continue;
                }

                packetInkList.Add(new DataToken(ink));
                packetLengthList.Add(length);
                totalLength += length;
                maxPositionCount = Mathf.Max(maxPositionCount, positionCount);

                ink = GetNextInk();

                if (!Utilities.IsValid(ink))
                {
                    nextInk = null;
                    break;
                }

                var nextPackedLength = pen._GetPackedDrawDataLength(ink);
                if (nextPackedLength > 0 &&
                    totalLength + nextPackedLength > currentStrokePacketVectorTarget)
                {
                    nextInk = ink;
                    break;
                }
            }

            var lengthVectorCount = (packetLengthList.Count + 2) / 3;
            var joinedData = new Vector3[2 + lengthVectorCount + totalLength];
            joinedData[0] = pen.penIdVector;
            joinedData[1] = new Vector3(packetLengthList.Count, joinedData.Length, 0f);

            for (int i = 0, n = packetLengthList.Count; i < n; i++)
            {
                if (packetLengthList.TryGetValue(i, TokenType.Int, out var lengthToken))
                    joinedData[2 + i / 3][i % 3] = lengthToken.Int;
            }

            if (packetPositionBuffer.Length < maxPositionCount)
                packetPositionBuffer = new Vector3[maxPositionCount];

            var index = 2 + lengthVectorCount;
            for (int i = 0, n = packetInkList.Count; i < n; i++)
            {
                if (!packetInkList.TryGetValue(i, TokenType.Reference, out var inkToken) ||
                    !packetLengthList.TryGetValue(i, TokenType.Int, out var lengthToken))
                    continue;

                var packetInk = (LineRenderer)inkToken.Reference;
                if (!QvPenUtilities.TryGetIdFromInk(packetInk.gameObject, out var _discard,
                    out var inkIdVector, out var ownerIdVector) ||
                    !pen._PackDrawDataInto(packetInk, inkIdVector, ownerIdVector,
                        packetPositionBuffer, joinedData, index))
                {
                    FinishSyncRound(true);
                    return;
                }

                index += lengthToken.Int;
            }

            packetInkList.Clear();
            packetLengthList.Clear();

            SendPacket(QvPen_LateSyncPacketKind.StrokeData, joinedData);
        }

        private void UnpackStrokePacket(Vector3[] data)
        {
            if (data == null || data.Length < 3 || !pen._MatchesPenId(data[0]))
                return;

            var strokeCount = (int)data[1].x;
            var declaredPacketLength = (int)data[1].y;
            var lengthVectorCount = (strokeCount + 2) / 3;

            if (strokeCount <= 0 || strokeCount > MaxStrokeCountPerPacket ||
                declaredPacketLength != data.Length ||
                lengthVectorCount < 1 || 2 + lengthVectorCount > data.Length)
                return;

            var index = 2 + lengthVectorCount;

            // Validate the complete layout before allocating any stroke.
            for (var i = 0; i < strokeCount; i++)
            {
                var dataLength = (int)data[2 + i / 3][i % 3];

                if (dataLength < QvPen_Pen.FOOTER_ELEMENT_DRAW_LENGTH + 1 ||
                    dataLength > QvPen_Pen.MAX_PACKED_DRAW_DATA_LENGTH ||
                    index > data.Length - dataLength)
                    return;

                index += dataLength;
            }

            if (index != data.Length)
                return;

            index = 2 + lengthVectorCount;
            for (var i = 0; i < strokeCount; i++)
            {
                var dataLength = (int)data[2 + i / 3][i % 3];
                var stroke = new Vector3[dataLength];

                System.Array.Copy(data, index, stroke, 0, dataLength);
                index += dataLength;

                pen._UnpackData(stroke, QvPen_Pen_Mode.Any);
            }
        }

        private void SendBeginPacket()
            => SendPacket(QvPen_LateSyncPacketKind.Begin, emptyPacketData);

        private void SendEndPacket()
            => SendPacket(QvPen_LateSyncPacketKind.End, emptyPacketData);

        private LineRenderer GetNextInk()
        {
            inkIndex = Mathf.Max(-1, inkIndex);

            while (++inkIndex < inkIdBuffer.Count)
            {
                if (!inkIdBuffer.TryGetValue(inkIndex, TokenType.Int, out var inkIdToken))
                    continue;

                var inkObject = pen._GetInk(inkIdToken.Int);
                if (!Utilities.IsValid(inkObject) || inkObject.transform.parent != inkPoolSynced)
                    continue;

                var ink = inkObject.GetComponent<LineRenderer>();
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

    enum QvPen_LateSyncPacketKind : int
    {
        None = 0,
        Begin = 1,
        StrokeData = 2,
        End = 3
    }
}
