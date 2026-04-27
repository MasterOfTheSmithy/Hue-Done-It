// File: Assets/_Project/Gameplay/Elimination/PlayerKillInputController.cs
using System;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerLifeState))]
    public sealed class PlayerKillInputController : NetworkBehaviour
    {
        private const int MaxSearchHits = 16;
        private const ulong InvalidNetworkObjectId = ulong.MaxValue;

        [Header("Input")]
        [SerializeField] private Key killKey = Key.F;
        [SerializeField] private Key secondaryKey = Key.Q;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float targetSearchRange = 2.75f;
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private float lineOfSightHeight = 0.8f;

        [Header("Bleach Primary")]
        [SerializeField, Min(0.1f)] private float primaryCooldownSeconds = 60f;
        [SerializeField, Min(0.1f)] private float failedPrimaryCooldownSeconds = 5f;
        [SerializeField, Min(0.1f)] private float primaryWindupSeconds = 2f;

        [Header("Bleach Secondary")]
        [SerializeField, Min(0.1f)] private float secondaryCooldownSeconds = 22f;
        [SerializeField, Min(0.1f)] private float mimicDurationSeconds = 10f;
        [SerializeField, Min(0.1f)] private float corruptRange = 4f;
        [SerializeField, Min(0.1f)] private float overloadRange = 3f;
        [SerializeField] private bool isTestKillerEnabled;

        [Header("Visual")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color deadTint = new(0.25f, 0.25f, 0.25f);
        [SerializeField] private Color bleachExposedTint = new(0.95f, 0.95f, 1f);

        private readonly NetworkVariable<byte> _role =
            new((byte)PlayerRole.Unassigned, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> _secondaryAbility =
            new((byte)BleachSecondaryAbility.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _primaryCooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _secondaryCooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _primaryWindupActive =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _primaryWindupEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _primaryTargetNetworkObjectId =
            new(InvalidNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _mimicEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _mimicTargetNetworkObjectId =
            new(InvalidNetworkObjectId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _overloadConsumed =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerLifeState _lifeState;
        private Collider[] _overlapResults;
        private MaterialPropertyBlock _propertyBlock;
        private Color _currentDisplayColor;
        private bool _hasAppliedDisplayColor;
        private bool _subscribedToNetworkVariables;
        private bool _subscribedToLifeState;

        public bool IsTestKillerEnabled => isTestKillerEnabled || CurrentRole == PlayerRole.Bleach;
        public PlayerRole CurrentRole => (PlayerRole)_role.Value;
        public BleachSecondaryAbility CurrentSecondaryAbility => (BleachSecondaryAbility)_secondaryAbility.Value;
        public bool IsBleachRole => CurrentRole == PlayerRole.Bleach;
        public bool IsPrimaryWindupActive => _primaryWindupActive.Value;
        public bool IsMimicking => IsBleachRole && GetServerTime() < _mimicEndServerTime.Value;
        public bool HasUsedOverload => _overloadConsumed.Value;
        public Color DisplayColor => _currentDisplayColor;

        private void Awake()
        {
            _lifeState = GetComponent<PlayerLifeState>();
            _overlapResults = new Collider[MaxSearchHits];
            _propertyBlock = new MaterialPropertyBlock();

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            SubscribeToState();
            ApplyVisualState(force: true);
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeFromState();
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            UnsubscribeFromState();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && IsClient && _lifeState != null && _lifeState.IsAlive)
            {
                ReadOwnerInput();
            }

            if (IsServer)
            {
                ResolveServerTimedEffects();
            }

            ApplyVisualState();
        }

        public void ServerAssignRole(PlayerRole role, BleachSecondaryAbility secondaryAbility)
        {
            if (!IsServer)
            {
                return;
            }

            _role.Value = (byte)role;
            _secondaryAbility.Value = (byte)secondaryAbility;
            _primaryCooldownEndServerTime.Value = 0f;
            _secondaryCooldownEndServerTime.Value = 0f;
            _primaryWindupActive.Value = false;
            _primaryWindupEndServerTime.Value = 0f;
            _primaryTargetNetworkObjectId.Value = InvalidNetworkObjectId;
            _mimicTargetNetworkObjectId.Value = InvalidNetworkObjectId;
            _mimicEndServerTime.Value = 0f;
            _overloadConsumed.Value = false;
            isTestKillerEnabled = role == PlayerRole.Bleach;
            ApplyVisualState(force: true);
        }

        public float GetPrimaryCooldownRemaining()
        {
            return Mathf.Max(0f, _primaryCooldownEndServerTime.Value - GetServerTime());
        }

        public float GetSecondaryCooldownRemaining()
        {
            return Mathf.Max(0f, _secondaryCooldownEndServerTime.Value - GetServerTime());
        }

        public float GetMimicRemaining()
        {
            return Mathf.Max(0f, _mimicEndServerTime.Value - GetServerTime());
        }

        public string BuildAbilityStatusLine()
        {
            if (CurrentRole != PlayerRole.Bleach)
            {
                return "Color kit: Report bodies / repair / survive";
            }

            string primary = IsPrimaryWindupActive
                ? $"Injecting {Mathf.CeilToInt(Mathf.Max(0f, _primaryWindupEndServerTime.Value - GetServerTime()))}s"
                : (GetPrimaryCooldownRemaining() > 0f ? $"Inject CD {Mathf.CeilToInt(GetPrimaryCooldownRemaining())}s" : "Inject ready");

            string secondary = CurrentSecondaryAbility switch
            {
                BleachSecondaryAbility.Mimic => IsMimicking
                    ? $"Mimic active {Mathf.CeilToInt(GetMimicRemaining())}s"
                    : (GetSecondaryCooldownRemaining() > 0f ? $"Mimic CD {Mathf.CeilToInt(GetSecondaryCooldownRemaining())}s" : "Mimic ready"),
                BleachSecondaryAbility.Corrupt => GetSecondaryCooldownRemaining() > 0f
                    ? $"Corrupt CD {Mathf.CeilToInt(GetSecondaryCooldownRemaining())}s"
                    : "Corrupt ready",
                BleachSecondaryAbility.Overload => _overloadConsumed.Value
                    ? "Overload spent"
                    : (GetSecondaryCooldownRemaining() > 0f ? $"Overload CD {Mathf.CeilToInt(GetSecondaryCooldownRemaining())}s" : "Overload ready"),
                _ => "No secondary"
            };

            return $"Bleach kit: {primary} // {secondary}";
        }

        private void SubscribeToState()
        {
            if (!_subscribedToNetworkVariables)
            {
                _role.OnValueChanged += HandleVisualStateChanged;
                _secondaryAbility.OnValueChanged += HandleVisualStateChanged;
                _primaryWindupActive.OnValueChanged += HandleVisualStateChanged;
                _mimicTargetNetworkObjectId.OnValueChanged += HandleVisualStateChanged;
                _mimicEndServerTime.OnValueChanged += HandleVisualStateChanged;
                _subscribedToNetworkVariables = true;
            }

            if (!_subscribedToLifeState && _lifeState != null)
            {
                _lifeState.LifeStateChanged += HandleLifeStateChanged;
                _subscribedToLifeState = true;
            }
        }

        private void UnsubscribeFromState()
        {
            if (_subscribedToNetworkVariables)
            {
                _role.OnValueChanged -= HandleVisualStateChanged;
                _secondaryAbility.OnValueChanged -= HandleVisualStateChanged;
                _primaryWindupActive.OnValueChanged -= HandleVisualStateChanged;
                _mimicTargetNetworkObjectId.OnValueChanged -= HandleVisualStateChanged;
                _mimicEndServerTime.OnValueChanged -= HandleVisualStateChanged;
                _subscribedToNetworkVariables = false;
            }

            if (_subscribedToLifeState && _lifeState != null)
            {
                _lifeState.LifeStateChanged -= HandleLifeStateChanged;
                _subscribedToLifeState = false;
            }
        }

        private void ReadOwnerInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (IsBleachRole && keyboard[killKey].wasPressedThisFrame && !IsPrimaryWindupActive)
            {
                if (TryFindClosestTarget(targetSearchRange, out NetworkObject target))
                {
                    RequestPrimaryBleachAttackServerRpc(target.NetworkObjectId);
                }
            }

            if (IsBleachRole && keyboard[secondaryKey].wasPressedThisFrame)
            {
                RequestUseSecondaryServerRpc();
            }
        }

        private void ResolveServerTimedEffects()
        {
            float serverTime = GetServerTime();
            if (_primaryWindupActive.Value && serverTime >= _primaryWindupEndServerTime.Value)
            {
                ResolvePrimaryWindup();
            }

            if (_mimicEndServerTime.Value > 0f && serverTime >= _mimicEndServerTime.Value)
            {
                _mimicEndServerTime.Value = 0f;
                _mimicTargetNetworkObjectId.Value = InvalidNetworkObjectId;
            }
        }

        private void ResolvePrimaryWindup()
        {
            _primaryWindupActive.Value = false;
            ulong targetNetworkObjectId = _primaryTargetNetworkObjectId.Value;
            _primaryTargetNetworkObjectId.Value = InvalidNetworkObjectId;

            if (!CanUseBleachPrimary(ignorePrimaryWindupState: true))
            {
                return;
            }

            if (!TryGetValidBleachTarget(targetNetworkObjectId, out NetworkObject targetObject))
            {
                _primaryCooldownEndServerTime.Value = GetServerTime() + failedPrimaryCooldownSeconds;
                return;
            }

            EliminationManager eliminationManager = FindFirstObjectByType<EliminationManager>();
            if (eliminationManager == null)
            {
                Debug.LogError("No EliminationManager found in scene.");
                _primaryCooldownEndServerTime.Value = GetServerTime() + failedPrimaryCooldownSeconds;
                return;
            }

            bool eliminated = eliminationManager.TryHandleEliminationRequest(NetworkObject, targetObject.NetworkObjectId);
            _primaryCooldownEndServerTime.Value = GetServerTime() + (eliminated ? primaryCooldownSeconds : failedPrimaryCooldownSeconds);
        }

        [ServerRpc]
        private void RequestPrimaryBleachAttackServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!CanUseBleachPrimary())
            {
                return;
            }

            if (!TryGetValidBleachTarget(targetNetworkObjectId, out NetworkObject targetObject))
            {
                _primaryCooldownEndServerTime.Value = GetServerTime() + failedPrimaryCooldownSeconds;
                return;
            }

            _primaryWindupActive.Value = true;
            _primaryWindupEndServerTime.Value = GetServerTime() + primaryWindupSeconds;
            _primaryTargetNetworkObjectId.Value = targetObject.NetworkObjectId;
        }

        [ServerRpc]
        private void RequestUseSecondaryServerRpc(ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!CanUseBleachSecondary())
            {
                return;
            }

            switch (CurrentSecondaryAbility)
            {
                case BleachSecondaryAbility.Mimic:
                    TryActivateMimic();
                    break;
                case BleachSecondaryAbility.Corrupt:
                    TryActivateCorrupt();
                    break;
                case BleachSecondaryAbility.Overload:
                    TryActivateOverload();
                    break;
            }
        }

        private bool CanUseBleachPrimary(bool ignorePrimaryWindupState = false)
        {
            if (!IsServer || !IsSpawned || _lifeState == null || !_lifeState.IsAlive || !IsBleachRole)
            {
                return false;
            }

            if (GetPrimaryCooldownRemaining() > 0f)
            {
                return false;
            }

            if (!ignorePrimaryWindupState && _primaryWindupActive.Value)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return roundState == null || roundState.IsFreeRoam;
        }

        private bool CanUseBleachSecondary()
        {
            if (!IsServer || !IsSpawned || _lifeState == null || !_lifeState.IsAlive || !IsBleachRole)
            {
                return false;
            }

            if (GetSecondaryCooldownRemaining() > 0f || _primaryWindupActive.Value)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return roundState == null || roundState.IsFreeRoam;
        }

        private void TryActivateMimic()
        {
            if (!TryFindClosestTarget(targetSearchRange + 2f, out NetworkObject target))
            {
                return;
            }

            _mimicTargetNetworkObjectId.Value = target.NetworkObjectId;
            _mimicEndServerTime.Value = GetServerTime() + mimicDurationSeconds;
            _secondaryCooldownEndServerTime.Value = GetServerTime() + secondaryCooldownSeconds;
        }

        private void TryActivateCorrupt()
        {
            PumpRepairTask[] tasks = FindObjectsByType<PumpRepairTask>(FindObjectsSortMode.None);
            foreach (PumpRepairTask task in tasks)
            {
                if (task == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, task.transform.position);
                if (distance > corruptRange)
                {
                    continue;
                }

                if (task.ServerTryApplyCorrupt(OwnerClientId))
                {
                    _secondaryCooldownEndServerTime.Value = GetServerTime() + secondaryCooldownSeconds;
                    return;
                }
            }
        }

        private void TryActivateOverload()
        {
            if (_overloadConsumed.Value)
            {
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, overloadRange, _overlapResults, targetMask, QueryTriggerInteraction.Collide);
            EliminationManager eliminationManager = FindFirstObjectByType<EliminationManager>();
            if (eliminationManager == null)
            {
                Debug.LogError("No EliminationManager found in scene.");
                return;
            }

            bool hitAny = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkObject candidate = hit.GetComponentInParent<NetworkObject>();
                if (candidate == null || candidate == NetworkObject)
                {
                    continue;
                }

                if (!candidate.TryGetComponent(out PlayerLifeState candidateLifeState) || !candidateLifeState.IsAlive)
                {
                    continue;
                }

                if (eliminationManager.TryHandleEliminationRequest(NetworkObject, candidate.NetworkObjectId))
                {
                    hitAny = true;
                }
            }

            if (!hitAny)
            {
                return;
            }

            _overloadConsumed.Value = true;
            _secondaryCooldownEndServerTime.Value = GetServerTime() + 9999f;
        }

        private bool TryFindClosestTarget(float searchRange, out NetworkObject target)
        {
            target = null;
            if (_overlapResults == null || _overlapResults.Length == 0)
            {
                _overlapResults = new Collider[MaxSearchHits];
            }

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, searchRange, _overlapResults, targetMask, QueryTriggerInteraction.Collide);
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkObject candidate = hit.GetComponentInParent<NetworkObject>();
                if (candidate == null || candidate == NetworkObject)
                {
                    continue;
                }

                if (!candidate.TryGetComponent(out PlayerLifeState candidateLifeState) || !candidateLifeState.IsAlive)
                {
                    continue;
                }

                float distanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                target = candidate;
            }

            return target != null;
        }

        private bool TryGetValidBleachTarget(ulong targetNetworkObjectId, out NetworkObject targetObject)
        {
            targetObject = null;
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return false;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject candidate) || candidate == NetworkObject)
            {
                return false;
            }

            if (!candidate.TryGetComponent(out PlayerLifeState targetLifeState) || !targetLifeState.IsAlive)
            {
                return false;
            }

            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance > targetSearchRange)
            {
                return false;
            }

            if (!HasLineOfSight(candidate.transform))
            {
                return false;
            }

            targetObject = candidate;
            return true;
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 origin = transform.position + (Vector3.up * lineOfSightHeight);
            Vector3 destination = target.position + (Vector3.up * lineOfSightHeight);
            Vector3 direction = destination - origin;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            NetworkObject targetNetworkObject = target.GetComponent<NetworkObject>();
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                NetworkObject hitObject = hit.collider.GetComponentInParent<NetworkObject>();
                if (hitObject == NetworkObject)
                {
                    continue;
                }

                return hitObject == targetNetworkObject;
            }

            return true;
        }

        private void ApplyVisualState(bool force = false)
        {
            if (bodyRenderer == null)
            {
                if (!TryResolveBodyRenderer())
                {
                    return;
                }
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Color targetColor = ResolveDisplayColor();
            if (!force && _hasAppliedDisplayColor && _currentDisplayColor.Equals(targetColor))
            {
                return;
            }

            _currentDisplayColor = targetColor;
            _hasAppliedDisplayColor = true;
            bodyRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_BaseColor", targetColor);
            _propertyBlock.SetColor("_Color", targetColor);
            bodyRenderer.SetPropertyBlock(_propertyBlock);
        }

        private bool TryResolveBodyRenderer()
        {
            if (bodyRenderer != null)
            {
                return true;
            }

            bodyRenderer = GetComponentInChildren<Renderer>();
            return bodyRenderer != null;
        }

        private Color ResolveDisplayColor()
        {
            if (_lifeState != null && !_lifeState.IsAlive)
            {
                return deadTint;
            }

            if (CurrentRole == PlayerRole.Bleach && IsPrimaryWindupActive)
            {
                return bleachExposedTint;
            }

            if (_mimicTargetNetworkObjectId.Value != InvalidNetworkObjectId && GetServerTime() < _mimicEndServerTime.Value)
            {
                if (NetworkManager != null && NetworkManager.SpawnManager != null &&
                    NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(_mimicTargetNetworkObjectId.Value, out NetworkObject mimicTarget) &&
                    mimicTarget.TryGetComponent(out PlayerKillInputController targetController))
                {
                    return targetController.DisplayColor;
                }
            }

            return GetStablePlayerColor(OwnerClientId);
        }

        private static Color GetStablePlayerColor(ulong clientId)
        {
            float hue = Mathf.Repeat((clientId * 0.173f) + 0.12f, 1f);
            return Color.HSVToRGB(hue, 0.55f, 0.95f);
        }

        private void HandleVisualStateChanged<T>(T _, T __)
        {
            ApplyVisualState(force: true);
        }

        private void HandleLifeStateChanged(PlayerLifeStateKind _, PlayerLifeStateKind __)
        {
            ApplyVisualState(force: true);
        }

        private float GetServerTime()
        {
            return NetworkManager == null ? 0f : (float)NetworkManager.ServerTime.Time;
        }
    }
}
