using System.Collections;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Round
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMatchSimulationRunner : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float settleDelaySeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float actionDelaySeconds = 1.25f;
        [SerializeField, Min(0.1f)] private float maxWaitPerStepSeconds = 45f;

        private NetworkRoundState _roundState;
        private bool _hasStarted;
        private bool _lastWaitSucceeded;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }

            _roundState = FindFirstObjectByType<NetworkRoundState>();
            if (_roundState != null)
            {
                _roundState.PhaseChanged += HandlePhaseChanged;
            }

            if (_hasStarted)
            {
                return;
            }

            _hasStarted = true;
            StartCoroutine(RunSimulation());
        }

        public override void OnNetworkDespawn()
        {
            if (_roundState != null)
            {
                _roundState.PhaseChanged -= HandlePhaseChanged;
            }

            base.OnNetworkDespawn();
        }

        private IEnumerator RunSimulation()
        {
            Log("Simulation booted.");

            yield return WaitForCondition(() => ResolveRoundState() && _roundState.CurrentPhase == RoundPhase.FreeRoam, "round phase FreeRoam");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            yield return new WaitForSeconds(actionDelaySeconds);

            if (!TryResolveParticipants(out PlayerKillInputController bleach, out PlayerKillInputController victim, out PlayerKillInputController reporter))
            {
                Log("Simulation aborted: could not resolve bleach/victim/reporter participants.");
                yield break;
            }

            EliminationManager eliminationManager = FindFirstObjectByType<EliminationManager>();
            if (eliminationManager == null)
            {
                Log("Simulation aborted: EliminationManager missing.");
                yield break;
            }

            bool killAccepted = eliminationManager.TryHandleEliminationRequest(bleach.NetworkObject, victim.NetworkObjectId);
            Log($"Kill attempt: bleach={bleach.OwnerClientId}, victim={victim.OwnerClientId}, accepted={killAccepted}.");
            if (!killAccepted)
            {
                yield break;
            }

            yield return new WaitForSeconds(settleDelaySeconds);

            yield return WaitForCondition(() => TryFindRemainsForVictim(victim.OwnerClientId, out _), "victim remains spawn");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            PlayerRemains remains = null;
            TryFindRemainsForVictim(victim.OwnerClientId, out remains);

            BodyReportManager bodyReportManager = FindFirstObjectByType<BodyReportManager>();
            if (bodyReportManager == null)
            {
                Log("Simulation aborted: BodyReportManager missing.");
                yield break;
            }

            bool reported = bodyReportManager.TryReportBody(reporter.NetworkObject, remains);
            Log($"Body report attempt: reporter={reporter.OwnerClientId}, victim={victim.OwnerClientId}, accepted={reported}.");
            if (!reported)
            {
                yield break;
            }

            yield return WaitForCondition(() => _roundState != null && _roundState.CurrentPhase == RoundPhase.FreeRoam, "return to FreeRoam after report");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            yield return new WaitForSeconds(actionDelaySeconds);

            PumpRepairTask pump = FindFirstObjectByType<PumpRepairTask>();
            if (pump == null)
            {
                Log("Simulation aborted: PumpRepairTask missing.");
                yield break;
            }

            bool corrupt1 = pump.ServerTryApplyCorrupt(bleach.OwnerClientId);
            bool corrupt2 = pump.ServerTryApplyCorrupt(bleach.OwnerClientId);
            bool corrupt3 = pump.ServerTryApplyCorrupt(bleach.OwnerClientId);
            Log($"Pump corrupt sequence: [{corrupt1}, {corrupt2}, {corrupt3}], locked={pump.IsLocked}.");

            yield return WaitForCondition(() => _roundState != null && _roundState.CurrentPhase == RoundPhase.Resolved, "round resolved");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            Log($"Round resolved. Winner={_roundState.Winner}. Message='{_roundState.RoundMessage}'.");

            yield return WaitForCondition(() => _roundState != null && _roundState.CurrentPhase == RoundPhase.PostRound, "post-round phase");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            yield return WaitForCondition(() => _roundState != null && (_roundState.CurrentPhase == RoundPhase.Intro || _roundState.CurrentPhase == RoundPhase.Lobby), "next match loop start");
            if (!_lastWaitSucceeded)
            {
                yield break;
            }

            Log($"Simulation complete. Next phase={_roundState.CurrentPhase}.");
        }

        private bool TryResolveParticipants(out PlayerKillInputController bleach, out PlayerKillInputController victim, out PlayerKillInputController reporter)
        {
            bleach = null;
            victim = null;
            reporter = null;

            PlayerKillInputController[] players = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            foreach (PlayerKillInputController player in players)
            {
                if (player == null || !player.IsSpawned || !player.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
                {
                    continue;
                }

                if (player.CurrentRole == PlayerRole.Bleach)
                {
                    bleach = player;
                    break;
                }
            }

            if (bleach == null)
            {
                return false;
            }

            foreach (PlayerKillInputController player in players)
            {
                if (player == null || player == bleach || !player.IsSpawned || !player.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
                {
                    continue;
                }

                if (player.CurrentRole == PlayerRole.Color)
                {
                    victim = player;
                    break;
                }
            }

            if (victim == null)
            {
                return false;
            }

            foreach (PlayerKillInputController player in players)
            {
                if (player == null || player == bleach || player == victim || !player.IsSpawned || !player.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
                {
                    continue;
                }

                reporter = player;
                break;
            }

            reporter ??= bleach;
            return true;
        }

        private static bool TryFindRemainsForVictim(ulong victimClientId, out PlayerRemains remains)
        {
            remains = null;
            PlayerRemains[] all = FindObjectsByType<PlayerRemains>(FindObjectsSortMode.None);
            foreach (PlayerRemains candidate in all)
            {
                if (candidate != null && candidate.IsSpawned && candidate.VictimClientId == victimClientId)
                {
                    remains = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
                if (_roundState != null)
                {
                    _roundState.PhaseChanged += HandlePhaseChanged;
                }
            }

            return _roundState != null;
        }

        private IEnumerator WaitForCondition(System.Func<bool> predicate, string label)
        {
            _lastWaitSucceeded = false;
            float timeoutAt = Time.unscaledTime + maxWaitPerStepSeconds;
            while (Time.unscaledTime < timeoutAt)
            {
                if (predicate())
                {
                    _lastWaitSucceeded = true;
                    Log($"Reached step: {label}.");
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Log($"Simulation timeout waiting for step: {label}.");
        }

        private void HandlePhaseChanged(RoundPhase previous, RoundPhase current)
        {
            Log($"Phase changed: {previous} -> {current}.");
        }

        private void Log(string message)
        {
            double time = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
            Debug.Log($"[MatchSimulation t={time:0.00}] {message}");
        }
    }
}
