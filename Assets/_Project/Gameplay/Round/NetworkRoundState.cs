// File: Assets/_Project/Gameplay/Round/NetworkRoundState.cs
using System;
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
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
        [Header("Round Flow")]
        [SerializeField, Min(1)] private int minPlayersToAutoStart = 1;
        [SerializeField, Min(0.1f)] private float introDurationSeconds = 4f;
        [SerializeField, Min(5f)] private float roundDurationSeconds = 150f;
        [SerializeField, Min(1f)] private float reportedDurationSeconds = 8f;
        [SerializeField, Min(1f)] private float resolvedDurationSeconds = 8f;
        [SerializeField, Min(1f)] private float postRoundDurationSeconds = 6f;

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

        public event Action<RoundPhase, RoundPhase> PhaseChanged;

        public RoundPhase CurrentPhase => (RoundPhase)_phase.Value;
        public RoundWinner Winner => (RoundWinner)_winner.Value;
        public ulong ReportingClientId => _reportingClientId.Value;
        public ulong ReportedVictimClientId => _reportedVictimClientId.Value;
        public string RoundMessage => _roundMessage.Value.ToString();
        public float PhaseTimeRemaining => Mathf.Max(0f, _phaseEndServerTime.Value - GetServerTime());
        public float RoundTimeRemaining => Mathf.Max(0f, _roundEndServerTime.Value - GetServerTime());
        public bool IsFreeRoam => CurrentPhase == RoundPhase.FreeRoam;

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
                        StartFreeRoam($"Repair the pump before it locks. Time remaining: {roundDurationSeconds:0}s.");
                    }
                    break;

                case RoundPhase.FreeRoam:
                    if (RoundTimeRemaining <= 0f)
                    {
                        ResolveRound(RoundWinner.Bleach, "Time expired. The flood consumed the zone.");
                        break;
                    }

                    EvaluateWinConditions();
                    break;

                case RoundPhase.Reported:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        _reportingClientId.Value = ulong.MaxValue;
                        _reportedVictimClientId.Value = ulong.MaxValue;

                        if (RoundTimeRemaining <= 0f)
                        {
                            ResolveRound(RoundWinner.Bleach, "The investigation ran out of time. The flood consumed the zone.");
                        }
                        else
                        {
                            StartFreeRoam("Investigation dispersed. Return to the pump.");
                        }
                    }
                    break;

                case RoundPhase.Resolved:
                    if (GetServerTime() >= _phaseEndServerTime.Value)
                    {
                        SetPhase(RoundPhase.PostRound, postRoundDurationSeconds);
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
            SetPhase(RoundPhase.Reported, reportedDurationSeconds);
            _roundMessage.Value = new FixedString128Bytes($"Body reported by {BuildClientLabel(reportingClientId)}. Freeze and reassess.");
            return true;
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
            SetPhase(RoundPhase.Intro, introDurationSeconds);
        }

        private void StartFreeRoam(string message)
        {
            SetPhase(RoundPhase.FreeRoam, Mathf.Max(0.1f, RoundTimeRemaining));
            _roundMessage.Value = new FixedString128Bytes(message);
        }

        private void ResetWorldForRound()
        {
            CleanupRemains();
            ResetTasks();
            ResetFloodControllers();
            ResetPlayersAndMoveToSpawns();
        }

        private void ResetPlayersAndMoveToSpawns()
        {
            List<Transform> spawnPoints = CollectSpawnPoints();
            List<NetworkClient> sortedClients = new(NetworkManager.ConnectedClientsList);
            sortedClients.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));

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

                if (spawnPoints.Count > 0)
                {
                    Transform spawnPoint = spawnPoints[spawnIndex % spawnPoints.Count];
                    Vector3 spawnPosition = spawnPoint.position + (Vector3.up * spawnHeightOffset);
                    float spawnYaw = spawnPoint.rotation.eulerAngles.y;

                    if (client.PlayerObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
                    {
                        mover.ServerTeleportTo(spawnPosition, spawnYaw);
                    }
                    else
                    {
                        client.PlayerObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, spawnYaw, 0f));
                    }

                    spawnIndex++;
                }
            }
        }

        private void ResetTasks()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            foreach (NetworkRepairTask task in tasks)
            {
                task.ServerResetTask();
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
        }

        private void ResetFloodControllers()
        {
            FloodSequenceController[] floodControllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            foreach (FloodSequenceController controller in floodControllers)
            {
                controller.ServerResetForRound();
            }

            FloodZone[] floodZones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in floodZones)
            {
                zone.ServerResetToInitialState();
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

        private void EvaluateWinConditions()
        {
            PumpRepairTask pumpRepairTask = FindFirstObjectByType<PumpRepairTask>();
            if (pumpRepairTask != null)
            {
                if (pumpRepairTask.IsCompleted)
                {
                    ResolveRound(RoundWinner.Color, "Pump repaired. The flood was released safely.");
                    return;
                }

                if (pumpRepairTask.IsLocked)
                {
                    ResolveRound(RoundWinner.Bleach, "Pump locked after three failed attempts.");
                    return;
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

        private void ResolveRound(RoundWinner winner, string message)
        {
            if (!IsServer || CurrentPhase == RoundPhase.Resolved || CurrentPhase == RoundPhase.PostRound)
            {
                return;
            }

            CancelActiveTasks("Round resolved");
            _winner.Value = (byte)winner;
            _roundMessage.Value = new FixedString128Bytes(message);
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
            SetPhase(RoundPhase.Lobby, 0f);
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
