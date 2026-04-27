// File: Assets/_Project/Gameplay/Round/NetworkRoundState.cs
using System;
using HueDoneIt.Core.Netcode;
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Inventory;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Director;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Round
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRoundState : NetworkBehaviour
    {
        public enum PressureStage : byte
        {
            Early = 0,
            Mid = 1,
            Late = 2
        }

        [Header("Round Flow")]
        [SerializeField, Min(1)] private int minPlayersToAutoStart = 1;
        [SerializeField, Min(0.1f)] private float introDurationSeconds = 2f;
        [SerializeField, Min(0.1f)] private float crashDurationSeconds = 1f;
        [SerializeField, Min(5f)] private float roundDurationSeconds = 1200f;
        [SerializeField, Min(1f)] private float reportedDurationSeconds = 8f;
        [SerializeField, Min(1f)] private float resolvedDurationSeconds = 8f;
        [SerializeField, Min(1f)] private float postRoundDurationSeconds = 6f;

        [Header("Repair Win Quota")]
        [SerializeField, Range(0.1f, 1f)] private float maintenanceCompletionRatio = 0.6f;
        [SerializeField, Min(1)] private int minimumMaintenanceTasksRequired = 5;

        [Header("Critical System Win Quota")]
        [SerializeField, Range(0.1f, 1f)] private float criticalCompletionRatio = 0.75f;
        [SerializeField, Min(1)] private int minimumCriticalSystemsRequired = 4;

        [Header("Pressure Curve")]
        [SerializeField, Range(0.05f, 0.95f)] private float earlyStageEndNormalized = 0.33f;
        [SerializeField, Range(0.1f, 0.99f)] private float midStageEndNormalized = 0.72f;
        [SerializeField, Min(1f)] private float reportRoundTimePenaltySeconds = 12f;
        [SerializeField, Min(0f)] private float reportPostResumeGraceSeconds = 3f;
        [SerializeField, Min(0f)] private float lateWarningThresholdSeconds = 25f;

        [Header("Crash")]
        [SerializeField, Min(0f)] private float crashImpulseHorizontal = 2.4f;
        [SerializeField, Min(0f)] private float crashImpulseVertical = 1.2f;

        [Header("Spawn")]
        [SerializeField] private string spawnPointPrefix = "SpawnPoint_";
        [SerializeField] private float spawnHeightOffset = 0.05f;
        [SerializeField, Min(0.1f)] private float spawnCapsuleHeight = 2f;
        [SerializeField, Min(0.05f)] private float spawnCapsuleRadius = 0.45f;
        [SerializeField, Min(0.1f)] private float spawnFloorProbeDistance = 8f;
        [SerializeField, Min(0.1f)] private float spawnMinimumPlayerSeparation = 1.6f;
        [SerializeField, Min(0.1f)] private float spawnEscapeProbeDistance = 1.25f;
        [SerializeField] private LayerMask spawnSolidMask = ~0;
        [SerializeField] private LayerMask spawnHazardMask = ~0;
        [SerializeField] private bool logSpawnValidation;

        private readonly NetworkVariable<byte> _phase =
            new((byte)RoundPhase.Lobby, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _reportingClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _reportedVictimClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> _winner =
            new((byte)RoundWinner.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _phaseEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _roundEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _roundMessage =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _currentObjective =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> _pressureStage =
            new((byte)PressureStage.Early, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _pressure01 =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _sabotageEventCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _crewStabilizationEventCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _environmentEventCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _meetingVotesCast =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _meetingEligibleVotes =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _meetingEjectedClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _meetingSummary =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private const ulong SkipVoteClientId = ulong.MaxValue - 1UL;
        private readonly Dictionary<ulong, ulong> _meetingVotes = new();

        private float _resumeGraceEndTime;
        private readonly bool[] _scheduledVoteTriggered = new bool[3];
        private static readonly float[] ScheduledVoteSeconds = { 300f, 600f, 900f };

        public event Action<RoundPhase, RoundPhase> PhaseChanged;

        public RoundPhase CurrentPhase => (RoundPhase)_phase.Value;
        public RoundWinner Winner => (RoundWinner)_winner.Value;
        public ulong ReportingClientId => _reportingClientId.Value;
        public ulong ReportedVictimClientId => _reportedVictimClientId.Value;
        public string RoundMessage => _roundMessage.Value.ToString();
        public string CurrentObjective => _currentObjective.Value.ToString();
        public float PhaseTimeRemaining => Mathf.Max(0f, _phaseEndServerTime.Value - GetServerTime());
        public float RoundTimeRemaining => Mathf.Max(0f, _roundEndServerTime.Value - GetServerTime());
        public bool IsFreeRoam => CurrentPhase == RoundPhase.FreeRoam;
        public PressureStage CurrentPressureStage => (PressureStage)_pressureStage.Value;
        public float Pressure01 => _pressure01.Value;
        public int SabotageEventCount => _sabotageEventCount.Value;
        public int CrewStabilizationEventCount => _crewStabilizationEventCount.Value;
        public int EnvironmentEventCount => _environmentEventCount.Value;
        public int MeetingVotesCast => _meetingVotesCast.Value;
        public int MeetingEligibleVotes => _meetingEligibleVotes.Value;
        public ulong MeetingEjectedClientId => _meetingEjectedClientId.Value;
        public string MeetingSummary => _meetingSummary.Value.ToString();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _phase.OnValueChanged += HandlePhaseChanged;

            if (IsServer)
            {
                SetLobbyState();
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            switch (CurrentPhase)
            {
                case RoundPhase.Lobby:
                    if (CanStartRound())
                    {
                        StartIntro();
                    }
                    break;

                case RoundPhase.Intro:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        StartCrash();
                    }
                    break;

                case RoundPhase.Crash:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        StartFreeRoam("Impact stabilized. Find routes and start pump repairs.");
                    }
                    break;

                case RoundPhase.FreeRoam:
                    UpdatePressureState();
                    UpdateObjectiveForCurrentState();

                    if (RoundTimeRemaining <= 0f)
                    {
                        ResolveRound(RoundWinner.None, "Ship fully flooded at 20:00. All players lose.");
                        break;
                    }

                    TryTriggerScheduledVotes();
                    EvaluateWinConditions();
                    break;

                case RoundPhase.Reported:
                    UpdatePressureState();
                    UpdateObjectiveForCurrentState();

                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        ResolveMeetingVotes();
                        if (CurrentPhase != RoundPhase.Reported)
                        {
                            break;
                        }

                        _reportingClientId.Value = ulong.MaxValue;
                        _reportedVictimClientId.Value = ulong.MaxValue;

                        if (RoundTimeRemaining <= 0f)
                        {
                            ResolveRound(RoundWinner.Bleach, "The investigation ran out of time. The flood consumed the zone.");
                        }
                        else
                        {
                            _resumeGraceEndTime = GetServerTime() + reportPostResumeGraceSeconds;
                            StartFreeRoam("Report ended. Routes changed while you were paused.");
                            TriggerPostReportPressure();
                        }
                    }
                    break;

                case RoundPhase.Resolved:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        SetPhase(RoundPhase.PostRound, postRoundDurationSeconds);
                        SetObjective("Round complete. Resetting soon.");
                    }
                    break;

                case RoundPhase.PostRound:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        if (CanStartRound())
                        {
                            StartIntro();
                        }
                        else
                        {
                            SetLobbyState();
                        }
                    }
                    break;
            }
        }

        public bool ServerTrySetReported(ulong reportingClientId, ulong reportedVictimClientId)
        {
            if (!IsServer || CurrentPhase != RoundPhase.FreeRoam)
            {
                return false;
            }

            _reportingClientId.Value = reportingClientId;
            _reportedVictimClientId.Value = reportedVictimClientId;
            CancelActiveTasks("Body reported");
            ApplyReportTimePenalty();
            SetFloodFlowActive(false);
            BeginMeetingVote($"Body report by {BuildClientLabel(reportingClientId)}. Use voting pods: accuse a nearby suspect or skip.");
            SetPhase(RoundPhase.Reported, reportedDurationSeconds);
            _roundMessage.Value = FixedStringUtility.ToFixedString128($"Body reported by {BuildClientLabel(reportingClientId)}. Clock reduced.");
            SetObjective($"Report in progress. Vote at the meeting pods. Time penalty: {Mathf.RoundToInt(reportRoundTimePenaltySeconds)}s.");
            return true;
        }


        public bool ServerTryCallEmergencyVote(ulong callerClientId, string sourceLabel, float timePenaltySeconds)
        {
            if (!IsServer || CurrentPhase != RoundPhase.FreeRoam)
            {
                return false;
            }

            float now = GetServerTime();
            float penalty = Mathf.Max(0f, timePenaltySeconds);
            if (penalty > 0f && _roundEndServerTime.Value > now)
            {
                float minimumRemaining = Mathf.Max(20f, reportedDurationSeconds + postRoundDurationSeconds);
                _roundEndServerTime.Value = Mathf.Max(now + minimumRemaining, _roundEndServerTime.Value - penalty);
            }

            _reportingClientId.Value = callerClientId;
            _reportedVictimClientId.Value = ulong.MaxValue;
            CancelActiveTasks("Emergency meeting");
            SetFloodFlowActive(false);
            BeginMeetingVote($"{BuildClientLabel(callerClientId)} called a meeting. Use voting pods: accuse a nearby suspect or skip.");
            SetPhase(RoundPhase.Reported, reportedDurationSeconds);

            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Emergency meeting" : sourceLabel;
            string penaltyText = penalty > 0f ? $" Clock reduced by {Mathf.RoundToInt(penalty)}s." : string.Empty;
            _roundMessage.Value = FixedStringUtility.ToFixedString128($"{source} called by {BuildClientLabel(callerClientId)}.{penaltyText}");
            SetObjective("Emergency meeting in progress. Vote, inspect summaries, then resume tasks.");
            return true;
        }

        public bool ServerApplySabotagePressure(ulong instigatorClientId, string sourceLabel, float timePenaltySeconds)
        {
            if (!IsServer || CurrentPhase != RoundPhase.FreeRoam)
            {
                return false;
            }

            float now = GetServerTime();
            float penalty = Mathf.Max(0f, timePenaltySeconds);
            if (penalty > 0f && _roundEndServerTime.Value > now)
            {
                float minimumRemaining = Mathf.Max(20f, reportedDurationSeconds + postRoundDurationSeconds);
                _roundEndServerTime.Value = Mathf.Max(now + minimumRemaining, _roundEndServerTime.Value - penalty);
            }

            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Sabotage" : sourceLabel;
            _sabotageEventCount.Value = Mathf.Max(0, _sabotageEventCount.Value + 1);
            string actor = BuildClientLabel(instigatorClientId);
            _roundMessage.Value = FixedStringUtility.ToFixedString128($"{source} triggered by {actor}. Flood pressure accelerated.");
            SetObjective($"Sabotage active: stabilize critical systems and avoid surge routes. Time penalty: {Mathf.RoundToInt(penalty)}s.");
            UpdatePressureState();
            BroadcastPressureStageToFloodControllers();
            return true;
        }

        public bool ServerApplyCrewStabilization(ulong instigatorClientId, string sourceLabel, float timeBonusSeconds)
        {
            if (!IsServer || CurrentPhase != RoundPhase.FreeRoam)
            {
                return false;
            }

            float now = GetServerTime();
            float bonus = Mathf.Max(0f, timeBonusSeconds);
            if (bonus > 0f && _roundEndServerTime.Value > now)
            {
                float cappedEnd = now + Mathf.Max(roundDurationSeconds, 30f);
                _roundEndServerTime.Value = Mathf.Min(cappedEnd, _roundEndServerTime.Value + bonus);
            }

            _crewStabilizationEventCount.Value = Mathf.Max(0, _crewStabilizationEventCount.Value + 1);
            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Crew stabilization" : sourceLabel;
            string actor = BuildClientLabel(instigatorClientId);
            _roundMessage.Value = FixedStringUtility.ToFixedString128($"{source} secured by {actor}. Flood pressure delayed.");
            SetObjective($"Crew stabilization active: continue critical systems. Time recovered: {Mathf.RoundToInt(bonus)}s.");
            UpdatePressureState();
            BroadcastPressureStageToFloodControllers();
            return true;
        }

        public bool ServerAnnounceEnvironmentalEvent(string sourceLabel, string detail, float timePenaltySeconds)
        {
            if (!IsServer || CurrentPhase != RoundPhase.FreeRoam)
            {
                return false;
            }

            float now = GetServerTime();
            float penalty = Mathf.Max(0f, timePenaltySeconds);
            if (penalty > 0f && _roundEndServerTime.Value > now)
            {
                float minimumRemaining = Mathf.Max(20f, reportedDurationSeconds + postRoundDurationSeconds);
                _roundEndServerTime.Value = Mathf.Max(now + minimumRemaining, _roundEndServerTime.Value - penalty);
            }

            _environmentEventCount.Value = Mathf.Max(0, _environmentEventCount.Value + 1);
            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Environmental event" : sourceLabel;
            string message = string.IsNullOrWhiteSpace(detail) ? "Ship systems shifted." : detail;
            _roundMessage.Value = FixedStringUtility.ToFixedString128($"{source}: {message}");
            string penaltyText = penalty > 0f ? $" Time penalty: {Mathf.RoundToInt(penalty)}s." : string.Empty;
            SetObjective($"Environmental event: {message}{penaltyText}");
            UpdatePressureState();
            BroadcastPressureStageToFloodControllers();
            return true;
        }

        public bool HasMeetingVoteFrom(ulong voterClientId)
        {
            return _meetingVotes.ContainsKey(voterClientId);
        }

        public bool ServerRegisterMeetingVote(ulong voterClientId, ulong accusedClientId, string sourceLabel)
        {
            if (!IsServer || CurrentPhase != RoundPhase.Reported)
            {
                return false;
            }

            if (!IsClientAlive(voterClientId) || !IsClientAlive(accusedClientId) || voterClientId == accusedClientId)
            {
                return false;
            }

            if (_meetingVotes.ContainsKey(voterClientId))
            {
                return false;
            }

            _meetingVotes[voterClientId] = accusedClientId;
            UpdateMeetingVoteCounters();
            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Voting podium" : sourceLabel;
            _meetingSummary.Value = FixedStringUtility.ToFixedString128($"{source}: {BuildClientLabel(voterClientId)} voted against {BuildClientLabel(accusedClientId)}. {_meetingVotesCast.Value}/{_meetingEligibleVotes.Value} votes.");
            TryCloseMeetingEarlyWhenComplete();
            return true;
        }

        public bool ServerRegisterSkipMeetingVote(ulong voterClientId, string sourceLabel)
        {
            if (!IsServer || CurrentPhase != RoundPhase.Reported)
            {
                return false;
            }

            if (!IsClientAlive(voterClientId) || _meetingVotes.ContainsKey(voterClientId))
            {
                return false;
            }

            _meetingVotes[voterClientId] = SkipVoteClientId;
            UpdateMeetingVoteCounters();
            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Voting podium" : sourceLabel;
            _meetingSummary.Value = FixedStringUtility.ToFixedString128($"{source}: {BuildClientLabel(voterClientId)} voted to skip. {_meetingVotesCast.Value}/{_meetingEligibleVotes.Value} votes.");
            TryCloseMeetingEarlyWhenComplete();
            return true;
        }

        private void BeginMeetingVote(string summary)
        {
            _meetingVotes.Clear();
            _meetingVotesCast.Value = 0;
            _meetingEligibleVotes.Value = CountEligibleMeetingVoters();
            _meetingEjectedClientId.Value = ulong.MaxValue;
            _meetingSummary.Value = FixedStringUtility.ToFixedString128(string.IsNullOrWhiteSpace(summary) ? "Meeting open. Vote or skip." : summary);
        }

        private void ResetMeetingVoteState(string summary)
        {
            _meetingVotes.Clear();
            _meetingVotesCast.Value = 0;
            _meetingEligibleVotes.Value = 0;
            _meetingEjectedClientId.Value = ulong.MaxValue;
            _meetingSummary.Value = FixedStringUtility.ToFixedString128(string.IsNullOrWhiteSpace(summary) ? "Meeting inactive." : summary);
        }

        private void UpdateMeetingVoteCounters()
        {
            _meetingVotesCast.Value = _meetingVotes.Count;
            _meetingEligibleVotes.Value = CountEligibleMeetingVoters();
        }

        private void TryCloseMeetingEarlyWhenComplete()
        {
            int eligible = Mathf.Max(0, CountEligibleMeetingVoters());
            if (eligible <= 0 || _meetingVotes.Count < eligible)
            {
                return;
            }

            float now = GetServerTime();
            _phaseEndServerTime.Value = Mathf.Min(_phaseEndServerTime.Value, now + 1.25f);
        }

        private void ResolveMeetingVotes()
        {
            if (!IsServer || _meetingVotes.Count <= 0)
            {
                _meetingSummary.Value = FixedStringUtility.ToFixedString128("Meeting ended with no votes cast.");
                return;
            }

            Dictionary<ulong, int> tally = new();
            int skipVotes = 0;
            foreach (KeyValuePair<ulong, ulong> pair in _meetingVotes)
            {
                ulong target = pair.Value;
                if (target == SkipVoteClientId)
                {
                    skipVotes++;
                    continue;
                }

                if (!tally.ContainsKey(target))
                {
                    tally[target] = 0;
                }

                tally[target]++;
            }

            ulong bestTarget = ulong.MaxValue;
            int bestVotes = 0;
            bool tied = false;
            foreach (KeyValuePair<ulong, int> pair in tally)
            {
                if (pair.Value > bestVotes)
                {
                    bestTarget = pair.Key;
                    bestVotes = pair.Value;
                    tied = false;
                }
                else if (pair.Value == bestVotes)
                {
                    tied = true;
                }
            }

            int eligible = Mathf.Max(1, CountEligibleMeetingVoters());
            int requiredVotes = Mathf.Max(1, Mathf.CeilToInt(eligible * 0.5f));
            if (bestTarget == ulong.MaxValue || tied || bestVotes < requiredVotes || bestVotes <= skipVotes || !TryGetClientLifeState(bestTarget, out PlayerLifeState targetLifeState))
            {
                _meetingEjectedClientId.Value = ulong.MaxValue;
                _meetingSummary.Value = FixedStringUtility.ToFixedString128($"Vote skipped/no majority. Accuse {bestVotes}, skip {skipVotes}, required {requiredVotes}.");
                _roundMessage.Value = FixedStringUtility.ToFixedString128("Meeting ended without an ejection.");
                return;
            }

            if (targetLifeState.ServerTrySetEliminated("Voted out by meeting"))
            {
                _meetingEjectedClientId.Value = bestTarget;
                _meetingSummary.Value = FixedStringUtility.ToFixedString128($"{BuildClientLabel(bestTarget)} was voted out with {bestVotes}/{eligible} votes.");
                _roundMessage.Value = FixedStringUtility.ToFixedString128($"{BuildClientLabel(bestTarget)} was voted out.");
                EvaluateWinConditions();
            }
        }

        private int CountEligibleMeetingVoters()
        {
            int count = 0;
            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            for (int i = 0; i < lifeStates.Length; i++)
            {
                PlayerLifeState lifeState = lifeStates[i];
                if (lifeState != null && lifeState.IsAlive)
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsClientAlive(ulong clientId)
        {
            return TryGetClientLifeState(clientId, out PlayerLifeState lifeState) && lifeState.IsAlive;
        }

        private bool TryGetClientLifeState(ulong clientId, out PlayerLifeState lifeState)
        {
            lifeState = null;
            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            for (int i = 0; i < lifeStates.Length; i++)
            {
                PlayerLifeState candidate = lifeStates[i];
                if (candidate != null && candidate.OwnerClientId == clientId)
                {
                    lifeState = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool CanStartRound()
        {
            if (NetworkManager == null || NetworkManager.ConnectedClientsList.Count < minPlayersToAutoStart)
            {
                return false;
            }

            int spawnedPlayers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None).Length;
            return spawnedPlayers >= minPlayersToAutoStart;
        }

        private void StartIntro()
        {
            ResetWorldForRound();
            AssignRoles();
            _winner.Value = (byte)RoundWinner.None;
            _reportingClientId.Value = ulong.MaxValue;
            _reportedVictimClientId.Value = ulong.MaxValue;
            _roundEndServerTime.Value = GetServerTime() + roundDurationSeconds;
            _roundMessage.Value = FixedStringUtility.ToFixedString128("Round starting. Roles assigned.");
            _resumeGraceEndTime = 0f;
            _pressure01.Value = 0f;
            _pressureStage.Value = (byte)PressureStage.Early;
            _sabotageEventCount.Value = 0;
            _crewStabilizationEventCount.Value = 0;
            _environmentEventCount.Value = 0;
            ResetMeetingVoteState("Meeting inactive.");
            for (int i = 0; i < _scheduledVoteTriggered.Length; i++)
            {
                _scheduledVoteTriggered[i] = false;
            }
            SetObjective("Brace for impact.");
            SetPhase(RoundPhase.Intro, introDurationSeconds);
        }

        private void StartCrash()
        {
            SetPhase(RoundPhase.Crash, crashDurationSeconds);
            _roundMessage.Value = FixedStringUtility.ToFixedString128("Ship impact! Hold on.");
            SetObjective("Ship is crashing. Regain control.");
            ApplyCrashImpulseToPlayers();
        }

        private void StartFreeRoam(string message)
        {
            SetPhase(RoundPhase.FreeRoam, Mathf.Max(0.1f, RoundTimeRemaining));
            _roundMessage.Value = FixedStringUtility.ToFixedString128(message);
            SetFloodFlowActive(true);
            BroadcastPressureStageToFloodControllers();
            UpdateObjectiveForCurrentState();
        }

        private void ApplyReportTimePenalty()
        {
            float now = GetServerTime();
            float nextEnd = Mathf.Max(now, _roundEndServerTime.Value - reportRoundTimePenaltySeconds);
            _roundEndServerTime.Value = nextEnd;
        }

        private void UpdatePressureState()
        {
            if (_roundEndServerTime.Value <= 0f)
            {
                _pressure01.Value = 0f;
                _pressureStage.Value = (byte)PressureStage.Early;
                return;
            }

            float elapsed = Mathf.Clamp(roundDurationSeconds - RoundTimeRemaining, 0f, roundDurationSeconds);
            float normalized = roundDurationSeconds <= 0.01f ? 1f : Mathf.Clamp01(elapsed / roundDurationSeconds);
            _pressure01.Value = normalized;

            PressureStage nextStage = normalized < earlyStageEndNormalized
                ? PressureStage.Early
                : (normalized < midStageEndNormalized ? PressureStage.Mid : PressureStage.Late);

            if ((byte)nextStage != _pressureStage.Value)
            {
                _pressureStage.Value = (byte)nextStage;
                _roundMessage.Value = FixedStringUtility.ToFixedString128($"Pressure shifted to {nextStage}. Routes are changing.");
                BroadcastPressureStageToFloodControllers();
            }
        }

        private void UpdateObjectiveForCurrentState()
        {
            if (CurrentPhase != RoundPhase.FreeRoam && CurrentPhase != RoundPhase.Reported)
            {
                return;
            }

            PumpRepairTask pumpRepairTask = FindFirstObjectByType<PumpRepairTask>();
            FloodSequenceController floodController = FindFirstObjectByType<FloodSequenceController>();

            string stageLabel = CurrentPressureStage switch
            {
                PressureStage.Early => "Early",
                PressureStage.Mid => "Mid",
                _ => "Late"
            };

            string pumpLabel = pumpRepairTask == null
                ? "Pump status unknown"
                : (pumpRepairTask.IsLocked
                    ? "Pump locked"
                    : (pumpRepairTask.IsCompleted
                        ? "Pump repaired"
                        : $"Pump attempts: {pumpRepairTask.AttemptsRemaining}"));

            string floodLabel = floodController != null
                ? floodController.BuildRoundPressureHint()
                : "Flood pattern active";

            string urgency = RoundTimeRemaining <= lateWarningThresholdSeconds
                ? "LAST WINDOW"
                : (GetServerTime() < _resumeGraceEndTime ? "Reposition now" : "Choose route and commit");

            SetObjective($"{urgency} | {stageLabel} pressure | {pumpLabel} | {floodLabel} | {Mathf.CeilToInt(RoundTimeRemaining)}s left");
        }

        private void BroadcastPressureStageToFloodControllers()
        {
            FloodSequenceController[] floodControllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            foreach (FloodSequenceController controller in floodControllers)
            {
                controller.ServerSetPressureStage((FloodSequenceController.RoundPressureStage)_pressureStage.Value);
            }
        }

        private void TriggerPostReportPressure()
        {
            FloodSequenceController[] floodControllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            foreach (FloodSequenceController controller in floodControllers)
            {
                controller.ServerTriggerReportAftershock();
            }
        }

        private void ResetWorldForRound()
        {
            CleanupRemains();
            ResetTasks();
            ResetFloodControllers();
            ResetDirectorEvents();
            ResetPlayersAndMoveToSpawns();
        }

        private void ResetDirectorEvents()
        {
            NetworkRoundEventDirector[] directors = FindObjectsByType<NetworkRoundEventDirector>(FindObjectsSortMode.None);
            foreach (NetworkRoundEventDirector director in directors)
            {
                if (director != null)
                {
                    director.ServerResetForRound();
                }
            }
        }

        private void ResetPlayersAndMoveToSpawns()
        {
            List<Transform> spawnPoints = CollectSpawnPoints();
            List<NetworkClient> sortedClients = new(NetworkManager.ConnectedClientsList);
            sortedClients.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
            HashSet<ulong> movedNetworkObjectIds = new();
            List<Vector3> claimedSpawnPositions = new();

            int spawnIndex = 0;
            foreach (NetworkClient client in sortedClients)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                if (client.PlayerObject.TryGetComponent(out PlayerLifeState lifeState))
                {
                    lifeState.ServerResetForRound();
                }

                if (client.PlayerObject.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
                {
                    floodTracker.ServerResetFloodState();
                }

                if (client.PlayerObject.TryGetComponent(out PlayerStaminaState staminaState))
                {
                    staminaState.ServerResetForRound();
                }

                if (client.PlayerObject.TryGetComponent(out PlayerInventoryState inventoryState))
                {
                    inventoryState.ServerClearAll();
                }

                if (spawnPoints.Count > 0)
                {
                    if (TrySelectSafeSpawn(spawnPoints, spawnIndex, claimedSpawnPositions, out Vector3 spawnPosition, out float spawnYaw))
                    {
                        MoveObjectToSpawn(client.PlayerObject, spawnPosition, spawnYaw);
                        claimedSpawnPositions.Add(spawnPosition);
                    }
                    else
                    {
                        Transform spawnPoint = spawnPoints[spawnIndex % spawnPoints.Count];
                        MoveObjectToSpawn(client.PlayerObject, spawnPoint.position + (Vector3.up * spawnHeightOffset), spawnPoint.rotation.eulerAngles.y);
                        Debug.LogWarning($"NetworkRoundState: falling back to unvalidated spawn '{spawnPoint.name}' for client {client.ClientId}.");
                    }

                    movedNetworkObjectIds.Add(client.PlayerObject.NetworkObjectId);
                    spawnIndex++;
                }
            }

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (NetworkPlayerAvatar avatar in avatars)
            {
                if (avatar == null || !avatar.IsSpawned || avatar.NetworkObject == null)
                {
                    continue;
                }

                if (movedNetworkObjectIds.Contains(avatar.NetworkObjectId))
                {
                    continue;
                }

                if (avatar.TryGetComponent(out PlayerLifeState lifeState))
                {
                    lifeState.ServerResetForRound();
                }

                if (avatar.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
                {
                    floodTracker.ServerResetFloodState();
                }

                if (avatar.TryGetComponent(out PlayerStaminaState staminaState))
                {
                    staminaState.ServerResetForRound();
                }

                if (avatar.TryGetComponent(out PlayerInventoryState inventoryState))
                {
                    inventoryState.ServerClearAll();
                }

                if (spawnPoints.Count > 0)
                {
                    if (TrySelectSafeSpawn(spawnPoints, spawnIndex, claimedSpawnPositions, out Vector3 spawnPosition, out float spawnYaw))
                    {
                        MoveObjectToSpawn(avatar.NetworkObject, spawnPosition, spawnYaw);
                        claimedSpawnPositions.Add(spawnPosition);
                    }
                    else
                    {
                        Transform spawnPoint = spawnPoints[spawnIndex % spawnPoints.Count];
                        MoveObjectToSpawn(avatar.NetworkObject, spawnPoint.position + (Vector3.up * spawnHeightOffset), spawnPoint.rotation.eulerAngles.y);
                        Debug.LogWarning($"NetworkRoundState: falling back to unvalidated spawn '{spawnPoint.name}' for avatar {avatar.name}.");
                    }

                    spawnIndex++;
                }
            }
        }

        private void MoveObjectToSpawn(NetworkObject targetObject, Vector3 spawnPosition, float spawnYaw)
        {
            if (targetObject == null)
            {
                return;
            }

            if (targetObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                mover.ServerTeleportTo(spawnPosition, spawnYaw);
            }
            else
            {
                targetObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, spawnYaw, 0f));
            }
        }

        private bool TrySelectSafeSpawn(List<Transform> spawnPoints, int preferredIndex, List<Vector3> claimedPositions, out Vector3 spawnPosition, out float spawnYaw)
        {
            spawnPosition = Vector3.zero;
            spawnYaw = 0f;

            if (spawnPoints == null || spawnPoints.Count == 0)
            {
                return TryFindSafeFallback(Vector3.zero, claimedPositions, out spawnPosition, out spawnYaw);
            }

            string lastRejectReason = string.Empty;
            for (int offset = 0; offset < spawnPoints.Count; offset++)
            {
                Transform spawnPoint = spawnPoints[(preferredIndex + offset) % spawnPoints.Count];
                if (spawnPoint == null)
                {
                    continue;
                }

                if (TryResolveSafeSpawnCandidate(spawnPoint.position + (Vector3.up * spawnHeightOffset), claimedPositions, out spawnPosition, out lastRejectReason))
                {
                    spawnYaw = spawnPoint.rotation.eulerAngles.y;
                    if (logSpawnValidation)
                    {
                        Debug.Log($"NetworkRoundState: selected safe spawn '{spawnPoint.name}' at {spawnPosition}.");
                    }

                    return true;
                }

                if (logSpawnValidation)
                {
                    Debug.Log($"NetworkRoundState: rejected spawn '{spawnPoint.name}' ({lastRejectReason}).");
                }
            }

            Vector3 fallbackOrigin = spawnPoints[Mathf.Abs(preferredIndex) % spawnPoints.Count].position;
            if (TryFindSafeFallback(fallbackOrigin, claimedPositions, out spawnPosition, out spawnYaw))
            {
                Debug.LogWarning($"NetworkRoundState: authored spawns were unsafe; using fallback at {spawnPosition}. Last rejection: {lastRejectReason}");
                return true;
            }

            Debug.LogError($"NetworkRoundState: no safe spawn found. Last rejection: {lastRejectReason}");
            return false;
        }

        private bool TryFindSafeFallback(Vector3 origin, List<Vector3> claimedPositions, out Vector3 spawnPosition, out float spawnYaw)
        {
            spawnPosition = Vector3.zero;
            spawnYaw = 0f;

            Vector3[] directions =
            {
                Vector3.zero,
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right,
                (Vector3.forward + Vector3.left).normalized,
                (Vector3.forward + Vector3.right).normalized,
                (Vector3.back + Vector3.left).normalized,
                (Vector3.back + Vector3.right).normalized
            };

            for (int ring = 0; ring < 7; ring++)
            {
                float radius = ring * 2.25f;
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3 candidate = origin + (directions[i] * radius) + Vector3.up;
                    if (!TryResolveSafeSpawnCandidate(candidate, claimedPositions, out spawnPosition, out _))
                    {
                        continue;
                    }

                    Vector3 look = (Vector3.zero - spawnPosition);
                    look.y = 0f;
                    spawnYaw = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look.normalized, Vector3.up).eulerAngles.y : 0f;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveSafeSpawnCandidate(Vector3 authoredPosition, List<Vector3> claimedPositions, out Vector3 spawnPosition, out string rejectReason)
        {
            rejectReason = string.Empty;
            spawnPosition = authoredPosition;

            if (!TryProjectSpawnToFloor(authoredPosition, out spawnPosition, out rejectReason))
            {
                return false;
            }

            float minDistanceSqr = spawnMinimumPlayerSeparation * spawnMinimumPlayerSeparation;
            for (int i = 0; i < claimedPositions.Count; i++)
            {
                Vector3 claimed = claimedPositions[i];
                Vector3 delta = spawnPosition - claimed;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistanceSqr)
                {
                    rejectReason = "too close to another selected spawn";
                    return false;
                }
            }

            GetSpawnCapsule(spawnPosition, out Vector3 pointA, out Vector3 pointB, out float radius);

            Collider[] solidHits = Physics.OverlapCapsule(pointA, pointB, radius, spawnSolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < solidHits.Length; i++)
            {
                Collider hit = solidHits[i];
                if (ShouldIgnoreSpawnCollider(hit))
                {
                    continue;
                }

                rejectReason = "inside solid collider " + hit.name;
                return false;
            }

            Collider[] hazardHits = Physics.OverlapCapsule(pointA, pointB, radius, spawnHazardMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hazardHits.Length; i++)
            {
                Collider hit = hazardHits[i];
                if (hit == null)
                {
                    continue;
                }

                FloodZone zone = hit.GetComponentInParent<FloodZone>();
                if (zone != null && !zone.IsSafe)
                {
                    rejectReason = "inside active flood zone " + zone.ZoneId;
                    return false;
                }

                NetworkBleachLeakHazard hazard = hit.GetComponentInParent<NetworkBleachLeakHazard>();
                if (hazard != null && !hazard.IsSuppressed)
                {
                    rejectReason = "inside active hazard " + hazard.DisplayName;
                    return false;
                }

                if (hit.name.IndexOf("Kill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rejectReason = "inside kill volume " + hit.name;
                    return false;
                }
            }

            if (!HasUsableEscapeSpace(spawnPosition))
            {
                rejectReason = "no usable escape space";
                return false;
            }

            return true;
        }

        private bool TryProjectSpawnToFloor(Vector3 authoredPosition, out Vector3 spawnPosition, out string rejectReason)
        {
            Vector3 rayOrigin = authoredPosition + (Vector3.up * Mathf.Max(1f, spawnCapsuleHeight));
            float rayDistance = Mathf.Max(4f, spawnFloorProbeDistance + spawnCapsuleHeight + 2f);
            int floorMask = spawnSolidMask.value != 0 ? spawnSolidMask.value : ~0;

            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit floorHit, rayDistance, floorMask, QueryTriggerInteraction.Ignore) &&
                !Physics.Raycast(rayOrigin, Vector3.down, out floorHit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                // Beta fallback: some generated floor planes use a layer not present in the authored spawn mask.
                // Do not fail the whole round for that. Put the blob at a conservative playable height and let
                // the collision stabilizer/depenetration pass resolve small overlaps.
                float halfHeight = Mathf.Max(spawnCapsuleRadius, spawnCapsuleHeight * 0.5f);
                spawnPosition = authoredPosition;
                spawnPosition.y = Mathf.Max(spawnPosition.y, halfHeight + 0.35f);
                rejectReason = string.Empty;
                return true;
            }

            if (floorHit.normal.y < 0.45f)
            {
                spawnPosition = authoredPosition;
                rejectReason = "floor normal too steep";
                return false;
            }

            float resolvedHalfHeight = Mathf.Max(spawnCapsuleRadius, spawnCapsuleHeight * 0.5f);
            spawnPosition = floorHit.point + Vector3.up * (resolvedHalfHeight + 0.05f);
            rejectReason = string.Empty;
            return true;
        }

        private void GetSpawnCapsule(Vector3 position, out Vector3 pointA, out Vector3 pointB, out float radius)
        {
            radius = Mathf.Max(0.05f, spawnCapsuleRadius);
            float halfHeight = Mathf.Max(radius, spawnCapsuleHeight * 0.5f);
            float segmentHalf = Mathf.Max(0f, halfHeight - radius);
            pointA = position + (Vector3.up * segmentHalf);
            pointB = position - (Vector3.up * segmentHalf);
        }

        private static bool ShouldIgnoreSpawnCollider(Collider hit)
        {
            if (hit == null || hit.isTrigger)
            {
                return true;
            }

            return hit.GetComponentInParent<NetworkPlayerAvatar>() != null ||
                   hit.GetComponentInParent<PlayerRemains>() != null;
        }

        private bool HasUsableEscapeSpace(Vector3 spawnPosition)
        {
            Vector3 origin = spawnPosition + (Vector3.up * 0.25f);
            Vector3[] directions =
            {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right
            };

            int openDirections = 0;
            for (int i = 0; i < directions.Length; i++)
            {
                if (!Physics.Raycast(origin, directions[i], spawnEscapeProbeDistance, spawnSolidMask, QueryTriggerInteraction.Ignore))
                {
                    openDirections++;
                }
            }

            return openDirections >= 2;
        }

        private void ResetTasks()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            foreach (NetworkRepairTask task in tasks)
            {
                if (task != null)
                {
                    task.ServerResetTask();
                }
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            foreach (TaskObjectiveBase task in advancedTasks)
            {
                if (task != null)
                {
                    task.ServerResetTask();
                }
            }
        }

        private void CancelActiveTasks(string reason)
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            foreach (NetworkRepairTask task in tasks)
            {
                if (task != null)
                {
                    task.TryCancelTask(reason);
                }
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            foreach (TaskObjectiveBase task in advancedTasks)
            {
                if (task != null)
                {
                    task.ServerReleaseActiveOperator(reason);
                }
            }
        }

        private void ResetFloodControllers()
        {
            FloodSequenceController[] floodControllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            foreach (FloodSequenceController controller in floodControllers)
            {
                controller.ServerResetForRound();
                controller.ServerSetFlowActive(false);
                controller.ServerSetPressureStage(FloodSequenceController.RoundPressureStage.Early);
            }

            FloodZone[] floodZones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in floodZones)
            {
                zone.ServerResetToInitialState();
            }
        }

        private void SetFloodFlowActive(bool active)
        {
            FloodSequenceController[] floodControllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            foreach (FloodSequenceController controller in floodControllers)
            {
                controller.ServerSetFlowActive(active);
            }
        }

        private void CleanupRemains()
        {
            PlayerRemains[] remains = FindObjectsByType<PlayerRemains>(FindObjectsSortMode.None);
            foreach (PlayerRemains remain in remains)
            {
                if (remain == null || remain.NetworkObject == null || !remain.IsSpawned)
                {
                    continue;
                }

                remain.NetworkObject.Despawn(true);
            }
        }

        private void AssignRoles()
        {
            PlayerKillInputController[] players = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            if (players.Length == 0)
            {
                return;
            }

            Shuffle(players);
            int bleachCount = CalculateBleachCount(players.Length);

            for (int i = 0; i < players.Length; i++)
            {
                PlayerKillInputController controller = players[i];
                if (controller == null || !controller.IsSpawned)
                {
                    continue;
                }

                bool isBleach = i < bleachCount;
                BleachSecondaryAbility secondary = isBleach
                    ? (BleachSecondaryAbility)UnityEngine.Random.Range(1, 4)
                    : BleachSecondaryAbility.None;
                controller.ServerAssignRole(isBleach ? PlayerRole.Bleach : PlayerRole.Color, secondary);
            }

            Debug.Log($"NetworkRoundState: assigned {bleachCount} Bleach role(s) for {players.Length} spawned participant(s).");
        }

        private static int CalculateBleachCount(int playerCount)
        {
            if (playerCount >= 7)
            {
                return 2;
            }

            if (playerCount >= 2)
            {
                return 1;
            }

            return 0;
        }

        public void GetMaintenanceProgress(out int completed, out int required, out int total, out int locked)
        {
            completed = 0;
            total = 0;
            locked = 0;

            NetworkRepairTask[] maintenanceTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            foreach (NetworkRepairTask task in maintenanceTasks)
            {
                if (task == null)
                {
                    continue;
                }

                total++;
                if (task.IsCompleted)
                {
                    completed++;
                }
                else if (task.CurrentState == RepairTaskState.Locked)
                {
                    locked++;
                }
            }

            required = CalculateRequiredMaintenanceTasks(total);
        }

        public void GetCriticalSystemProgress(out int completed, out int total, out int locked)
        {
            GetCriticalSystemProgress(out completed, out int _, out total, out locked);
        }

        public void GetCriticalSystemProgress(out int completed, out int required, out int total, out int locked)
        {
            completed = 0;
            total = 0;
            locked = 0;

            TaskObjectiveBase[] criticalTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            foreach (TaskObjectiveBase task in criticalTasks)
            {
                if (task == null)
                {
                    continue;
                }

                total++;
                if (task.IsCompleted)
                {
                    completed++;
                }
                else if (task.IsLocked)
                {
                    locked++;
                }
            }

            required = CalculateRequiredCriticalSystems(total);
        }

        private void EvaluateWinConditions()
        {
            NetworkRepairTask[] maintenanceTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            int totalMaintenanceTasks = 0;
            int completedMaintenanceTasks = 0;
            int lockedMaintenanceTasks = 0;
            foreach (NetworkRepairTask task in maintenanceTasks)
            {
                if (task == null)
                {
                    continue;
                }

                totalMaintenanceTasks++;
                if (task.IsCompleted)
                {
                    completedMaintenanceTasks++;
                }
                else if (task.CurrentState == RepairTaskState.Locked)
                {
                    lockedMaintenanceTasks++;
                }
            }

            TaskObjectiveBase[] criticalTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            int totalCriticalTasks = 0;
            int completedCriticalTasks = 0;
            int lockedCriticalTasks = 0;
            foreach (TaskObjectiveBase task in criticalTasks)
            {
                if (task == null)
                {
                    continue;
                }

                totalCriticalTasks++;
                if (task.IsCompleted)
                {
                    completedCriticalTasks++;
                }
                else if (task.IsLocked)
                {
                    lockedCriticalTasks++;
                }
            }

            int aliveColors = 0;
            int aliveBleach = 0;
            int totalPlayers = 0;
            PlayerKillInputController[] players = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            foreach (PlayerKillInputController controller in players)
            {
                if (controller == null || !controller.TryGetComponent(out PlayerLifeState lifeState))
                {
                    continue;
                }

                totalPlayers++;
                if (!lifeState.IsAlive)
                {
                    continue;
                }

                if (controller.CurrentRole == PlayerRole.Bleach)
                {
                    aliveBleach++;
                }
                else if (controller.CurrentRole == PlayerRole.Color)
                {
                    aliveColors++;
                }
            }

            int requiredCriticalTasks = CalculateRequiredCriticalSystems(totalCriticalTasks);
            int remainingAvailableCritical = Mathf.Max(0, totalCriticalTasks - lockedCriticalTasks);
            if (requiredCriticalTasks > 0 && remainingAvailableCritical < requiredCriticalTasks && totalCriticalTasks > 0)
            {
                ResolveRound(RoundWinner.None, "Too many critical systems locked out. Ship stabilization is no longer reachable.");
                return;
            }
            int requiredMaintenanceTasks = CalculateRequiredMaintenanceTasks(totalMaintenanceTasks);
            int remainingAvailableMaintenance = Mathf.Max(0, totalMaintenanceTasks - lockedMaintenanceTasks);
            if (requiredMaintenanceTasks > 0 && remainingAvailableMaintenance < requiredMaintenanceTasks && totalMaintenanceTasks > 0)
            {
                ResolveRound(RoundWinner.None, "Too many maintenance routes locked out. Ship stabilization is no longer reachable.");
                return;
            }

            bool criticalComplete = requiredCriticalTasks == 0 || completedCriticalTasks >= requiredCriticalTasks;
            bool maintenanceComplete = requiredMaintenanceTasks == 0 || completedMaintenanceTasks >= requiredMaintenanceTasks;
            if (criticalComplete && maintenanceComplete)
            {
                if (aliveBleach == 0)
                {
                    ResolveRound(RoundWinner.Color, "Critical systems stabilized and bleach creatures eliminated. Innocents win.");
                }
                else
                {
                    ResolveRound(RoundWinner.Bleach, "Ship stabilized, but bleach creatures survived hidden. Bleach victory by deception.");
                }

                return;
            }

            if (aliveColors == 0 && totalPlayers > 0)
            {
                ResolveRound(RoundWinner.Bleach, "All color players were diffused.");
                return;
            }

            if (aliveBleach == 0 && totalPlayers > 1)
            {
                ResolveRound(RoundWinner.Color, "The bleach threat was neutralized.");
            }
        }

        private int CalculateRequiredMaintenanceTasks(int totalMaintenanceTasks)
        {
            if (totalMaintenanceTasks <= 0)
            {
                return 0;
            }

            int ratioCount = Mathf.CeilToInt(totalMaintenanceTasks * maintenanceCompletionRatio);
            int required = Mathf.Max(minimumMaintenanceTasksRequired, ratioCount);
            return Mathf.Clamp(required, 1, totalMaintenanceTasks);
        }

        private int CalculateRequiredCriticalSystems(int totalCriticalSystems)
        {
            if (totalCriticalSystems <= 0)
            {
                return 0;
            }

            int ratioCount = Mathf.CeilToInt(totalCriticalSystems * criticalCompletionRatio);
            int required = Mathf.Max(minimumCriticalSystemsRequired, ratioCount);
            return Mathf.Clamp(required, 1, totalCriticalSystems);
        }

        private void TryTriggerScheduledVotes()
        {
            float elapsed = Mathf.Clamp(roundDurationSeconds - RoundTimeRemaining, 0f, roundDurationSeconds);
            for (int i = 0; i < ScheduledVoteSeconds.Length; i++)
            {
                if (_scheduledVoteTriggered[i] || elapsed < ScheduledVoteSeconds[i])
                {
                    continue;
                }

                _scheduledVoteTriggered[i] = true;
                _reportingClientId.Value = ulong.MaxValue;
                _reportedVictimClientId.Value = ulong.MaxValue;
                CancelActiveTasks("Scheduled vote");
                SetFloodFlowActive(false);
                BeginMeetingVote($"Scheduled meeting at {Mathf.RoundToInt(ScheduledVoteSeconds[i] / 60f * 10f) / 10f}m. Use voting pods to accuse or skip.");
                SetPhase(RoundPhase.Reported, reportedDurationSeconds);
                _roundMessage.Value = FixedStringUtility.ToFixedString128($"Emergency vote triggered at {Mathf.RoundToInt(ScheduledVoteSeconds[i] / 60f * 10f) / 10f}m mark.");
                SetObjective("Emergency vote in progress. Cast a vote or skip before the timer ends.");
                break;
            }
        }

        private void ResolveRound(RoundWinner winner, string message)
        {
            if (!IsServer || CurrentPhase == RoundPhase.Resolved || CurrentPhase == RoundPhase.PostRound)
            {
                return;
            }

            CancelActiveTasks("Round resolved");
            SetFloodFlowActive(false);
            _winner.Value = (byte)winner;
            _roundMessage.Value = FixedStringUtility.ToFixedString128(message);
            SetObjective("Round resolved: " + message);
            SetPhase(RoundPhase.Resolved, resolvedDurationSeconds);
        }

        private void HandleClientConnected(ulong _)
        {
            if (CurrentPhase == RoundPhase.Lobby || CurrentPhase == RoundPhase.PostRound)
            {
                _roundMessage.Value = FixedStringUtility.ToFixedString128("Player connected. Preparing next round.");
            }
        }

        private void HandleClientDisconnected(ulong _)
        {
            if (NetworkManager == null || NetworkManager.ConnectedClientsList.Count == 0)
            {
                SetLobbyState();
                return;
            }

            if (CurrentPhase is RoundPhase.FreeRoam or RoundPhase.Reported)
            {
                EvaluateWinConditions();
            }
        }

        private void SetLobbyState()
        {
            _reportingClientId.Value = ulong.MaxValue;
            _reportedVictimClientId.Value = ulong.MaxValue;
            _winner.Value = (byte)RoundWinner.None;
            _roundEndServerTime.Value = 0f;
            _roundMessage.Value = FixedStringUtility.ToFixedString128("Waiting for players");
            _pressure01.Value = 0f;
            _pressureStage.Value = (byte)PressureStage.Early;
            _sabotageEventCount.Value = 0;
            _crewStabilizationEventCount.Value = 0;
            _environmentEventCount.Value = 0;
            ResetMeetingVoteState("Waiting for meeting phase.");
            for (int i = 0; i < _scheduledVoteTriggered.Length; i++)
            {
                _scheduledVoteTriggered[i] = false;
            }
            SetObjective("Awaiting players.");
            SetPhase(RoundPhase.Lobby, 0f);
        }

        private void SetObjective(string objective)
        {
            _currentObjective.Value = FixedStringUtility.ToFixedString128(objective);
        }

        private void SetPhase(RoundPhase phase, float durationSeconds)
        {
            _phase.Value = (byte)phase;
            _phaseEndServerTime.Value = durationSeconds > 0f ? GetServerTime() + durationSeconds : 0f;
        }

        private void HandlePhaseChanged(byte previousValue, byte currentValue)
        {
            PhaseChanged?.Invoke((RoundPhase)previousValue, (RoundPhase)currentValue);
        }

        private List<Transform> CollectSpawnPoints()
        {
            List<Transform> results = new();
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (Transform candidate in transforms)
            {
                if (candidate != null && candidate.name.StartsWith(spawnPointPrefix, StringComparison.Ordinal))
                {
                    results.Add(candidate);
                }
            }

            results.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return results;
        }

        private void ApplyCrashImpulseToPlayers()
        {
            Vector3 baseImpulse = new Vector3(0f, crashImpulseVertical, -crashImpulseHorizontal);
            NetworkPlayerAuthoritativeMover[] movers = FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
            foreach (NetworkPlayerAuthoritativeMover mover in movers)
            {
                if (mover != null && mover.IsSpawned)
                {
                    Vector3 impulse = mover.transform.TransformDirection(baseImpulse);
                    mover.ServerApplyKnockback(impulse);
                }
            }
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                T temp = list[i];
                list[i] = list[swapIndex];
                list[swapIndex] = temp;
            }
        }

        private string BuildClientLabel(ulong clientId)
        {
            return clientId == ulong.MaxValue ? "Unknown" : $"Player {clientId}";
        }

        private float GetServerTime()
        {
            return NetworkManager == null ? 0f : (float)NetworkManager.ServerTime.Time;
        }
    }
}
