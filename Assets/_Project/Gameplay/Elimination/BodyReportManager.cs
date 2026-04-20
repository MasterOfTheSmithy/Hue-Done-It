// File: Assets/_Project/Gameplay/Elimination/BodyReportManager.cs
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BodyReportManager : NetworkBehaviour
    {
        private NetworkRoundState _roundState;

        private void Awake()
        {
            _roundState = FindFirstObjectByType<NetworkRoundState>();
        }

        public bool TryReportBody(NetworkObject reporterObject, PlayerRemains remains)
        {
            if (!IsServer || reporterObject == null || remains == null)
            {
                return false;
            }

            if (_roundState == null)
            {
                Debug.LogError("BodyReportManager requires NetworkRoundState in the scene.");
                return false;
            }

            if (!_roundState.IsFreeRoam)
            {
                return false;
            }

            if (!reporterObject.TryGetComponent(out PlayerLifeState reporterLifeState) || !reporterLifeState.IsAlive)
            {
                return false;
            }

            if (remains.IsReported || !remains.ServerTryMarkReported())
            {
                return false;
            }

            bool phaseUpdated = _roundState.ServerTrySetReported(reporterObject.OwnerClientId, remains.VictimClientId);
            if (!phaseUpdated)
            {
                return false;
            }

            Debug.Log($"Body reported by client {reporterObject.OwnerClientId}. Victim={remains.VictimClientId}");
            return true;
        }
    }
}
