// File: Assets/_Project/Gameplay/Round/NetworkRoundState.cs
using System;
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
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
            BeginMeetingVote($"Body report by {BuildClientLabel(reportingClientId)}. Use voting pods: accuse a nearby suspect or skip.");
            SetPhase(RoundPhase.Reported, reportedDurationSeconds);
            _roundMessage.Value = new FixedString128Bytes($"Body reported by {BuildClientLabel(reportingClientId)}. Clock reduced.");
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
            BeginMeetingVote($"{BuildClientLabel(callerClientId)} called a meeting. Use voting pods: accuse a nearby suspect or skip.");
            SetPhase(RoundPhase.Reported, reportedDurationSeconds);

            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Emergency meeting" : sourceLabel;
            string penaltyText = penalty > 0f ? $" Clock reduced by {Mathf.RoundToInt(penalty)}s." : string.Empty;
            _roundMessage.Value = new FixedString128Bytes($"{source} called by {BuildClientLabel(callerClientId)}.{penaltyText}");
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
            _roundMessage.Value = new FixedString128Bytes($"{source} triggered by {actor}. Flood pressure accelerated.");
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
            _roundMessage.Value = new FixedString128Bytes($"{source} secured by {actor}. Flood pressure delayed.");
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
            _roundMessage.Value = new FixedString128Bytes($"{source}: {message}");
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
            _meetingSummary.Value = new FixedString128Bytes($"{source}: {BuildClientLabel(voterClientId)} voted against {BuildClientLabel(accusedClientId)}. {_meetingVotesCast.Value}/{_meetingEligibleVotes.Value} votes.");
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
            _meetingSummary.Value = new FixedString128Bytes($"{source}: {BuildClientLabel(voterClientId)} voted to skip. {_meetingVotesCast.Value}/{_meetingEligibleVotes.Value} votes.");
            TryCloseMeetingEarlyWhenComplete();
            return true;
        }

        private void BeginMeetingVote(string summary)
        {
            _meetingVotes.Clear();
            _meetingVotesCast.Value = 0;
            _meetingEligibleVotes.Value = CountEligibleMeetingVoters();
            _meetingEjectedClientId.Value = ulong.MaxValue;
            _meetingSummary.Value = new FixedString128Bytes(string.IsNullOrWhiteSpace(summary) ? "Meeting open. Vote or skip." : summary);
        }

        private void ResetMeetingVoteState(string summary)
        {
            _meetingVotes.Clear();
            _meetingVotesCast.Value = 0;
            _meetingEligibleVotes.Value = 0;
            _meetingEjectedClientId.Value = ulong.MaxValue;
            _meetingSummary.Value = new FixedString128Bytes(string.IsNullOrWhiteSpace(summary) ? "Meeting inactive." : summary);
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
                _meetingSummary.Value = new FixedString128Bytes("Meeting ended with no votes cast.");
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
                _meetingSummary.Value = new FixedString128Bytes($"Vote skipped/no majority. Accuse {bestVotes}, skip {skipVotes}, required {requiredVotes}.");
                _roundMessage.Value = new FixedString128Bytes("Meeting ended without an ejection.");
                return;
            }

            if (targetLifeState.ServerTrySetEliminated("Voted out by meeting"))
            {
                _meetingEjectedClientId.Value = bestTarget;
                _meetingSummary.Value = new FixedString128Bytes($"{BuildClientLabel(bestTarget)} was voted out with {bestVotes}/{eligible} votes.");
                _roundMessage.Value = new FixedString128Bytes($"{BuildClientLabel(bestTarget)} was voted out.");
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
            _roundMessage.Value = new FixedString128Bytes("Round starting. Roles assigned.");
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
            _roundMessage.Value = new FixedString128Bytes("Ship impact! Hold on.");
            SetObjective("Ship is crashing. Regain control.");
            ApplyCrashImpulseToPlayers();
        }

        private void StartFreeRoam(string message)
        {
            SetPhase(RoundPhase.FreeRoam, Mathf.Max(0.1f, RoundTimeRemaining));
            _roundMessage.Value = new FixedString128Bytes(message);
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
                _roundMessage.Value = new FixedString128Bytes($"Pressure shifted to {nextStage}. Routes are changing.");
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
                    Transform spawnPoint = spawnPoints[spawnIndex % spawnPoints.Count];
                    MoveObjectToSpawn(client.PlayerObject, spawnPoint);
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
                    Transform spawnPoint = spawnPoints[spawnIndex % spawnPoints.Count];
                    MoveObjectToSpawn(avatar.NetworkObject, spawnPoint);
                    spawnIndex++;
                }
            }
        }

        private void MoveObjectToSpawn(NetworkObject targetObject, Transform spawnPoint)
        {
            if (targetObject == null || spawnPoint == null)
            {
                return;
            }

            Vector3 spawnPosition = spawnPoint.position + (Vector3.up * spawnHeightOffset);
            float spawnYaw = spawnPoint.rotation.eulerAngles.y;

            if (targetObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                mover.ServerTeleportTo(spawnPosition, spawnYaw);
            }
            else
            {
                targetObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, spawnYaw, 0f));
            }
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
            int bleachIndex = players.Length >= 2 ? UnityEngine.Random.Range(0, players.Length) : -1;
            BleachSecondaryAbility assignedSecondary = BleachSecondaryAbility.None;
            if (bleachIndex >= 0)
            {
                assignedSecondary = (BleachSecondaryAbility)UnityEngine.Random.Range(1, 4);
            }

            for (int i = 0; i < players.Length; i++)
            {
                PlayerKillInputController controller = players[i];
                if (controller == null || !controller.IsSpawned)
                {
                    continue;
                }

                bool isBleach = i == bleachIndex;
                controller.ServerAssignRole(isBleach ? PlayerRole.Bleach : PlayerRole.Color, isBleach ? assignedSecondary : BleachSecondaryAbility.None);
            }
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
                BeginMeetingVote($"Scheduled meeting at {Mathf.RoundToInt(ScheduledVoteSeconds[i] / 60f * 10f) / 10f}m. Use voting pods to accuse or skip.");
                SetPhase(RoundPhase.Reported, reportedDurationSeconds);
                _roundMessage.Value = new FixedString128Bytes($"Emergency vote triggered at {Mathf.RoundToInt(ScheduledVoteSeconds[i] / 60f * 10f) / 10f}m mark.");
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
            _roundMessage.Value = new FixedString128Bytes(message);
            SetObjective("Round resolved: " + message);
            SetPhase(RoundPhase.Resolved, resolvedDurationSeconds);
        }

        private void HandleClientConnected(ulong _)
        {
            if (CurrentPhase == RoundPhase.Lobby || CurrentPhase == RoundPhase.PostRound)
            {
                _roundMessage.Value = new FixedString128Bytes("Player connected. Preparing next round.");
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
            _roundMessage.Value = new FixedString128Bytes("Waiting for players");
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
            _currentObjective.Value = new FixedString128Bytes(objective);
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
