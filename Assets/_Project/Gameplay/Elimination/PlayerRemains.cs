// File: Assets/_Project/Gameplay/Elimination/PlayerRemains.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerRemains : NetworkInteractable
    {
        private readonly NetworkVariable<ulong> _victimClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _victimPlayerObjectId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _victimName =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isInitialized =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isReported =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public ulong VictimClientId => _victimClientId.Value;
        public ulong VictimPlayerObjectId => _victimPlayerObjectId.Value;
        public string VictimName => _victimName.Value.ToString();
        public bool IsReported => _isReported.Value;

        public void ServerInitialize(ulong victimClientId, ulong victimPlayerObjectId, string victimName)
        {
            if (!IsServer || _isInitialized.Value)
            {
                return;
            }

            _victimClientId.Value = victimClientId;
            _victimPlayerObjectId.Value = victimPlayerObjectId;
            _victimName.Value = string.IsNullOrWhiteSpace(victimName)
                ? new FixedString64Bytes($"Player {victimClientId}")
                : new FixedString64Bytes(victimName);
            _isInitialized.Value = true;
        }

        public bool ServerTryMarkReported()
        {
            if (!IsServer || _isReported.Value || !_isInitialized.Value)
            {
                return false;
            }

            _isReported.Value = true;
            return true;
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (!_isInitialized.Value || _isReported.Value || !context.IsServer)
            {
                return false;
            }

            if (context.InteractorObject == null || !context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return roundState == null || roundState.IsFreeRoam;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (_isReported.Value)
            {
                return "Body already reported";
            }

            if (context.InteractorObject != null &&
                context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) &&
                !lifeState.IsAlive)
            {
                return "Eliminated players cannot report";
            }

            string targetName = string.IsNullOrWhiteSpace(VictimName) ? $"Player {VictimClientId}" : VictimName;
            return $"Report Body ({targetName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer)
            {
                return false;
            }

            BodyReportManager reportManager = FindFirstObjectByType<BodyReportManager>();
            if (reportManager == null)
            {
                Debug.LogError("No BodyReportManager found in scene.");
                return false;
            }

            return reportManager.TryReportBody(context.InteractorObject, this);
        }
    }
}
