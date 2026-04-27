// File: Assets/_Project/Gameplay/Environment/NetworkVotingPodium.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkVotingPodium : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string podiumId = "vote-podium";
        [SerializeField] private string displayName = "Voting Podium";
        [SerializeField] private string accusePrompt = "Vote Nearest Suspect";
        [SerializeField] private string skipPrompt = "Vote Skip";
        [SerializeField] private bool skipVote;

        [Header("Voting")]
        [SerializeField, Min(1f)] private float targetSearchRadius = 5.5f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color accuseColor = new(1f, 0.35f, 0.18f, 1f);
        [SerializeField] private Color skipColor = new(0.35f, 0.9f, 1f, 1f);
        [SerializeField] private Color inactiveColor = new(0.2f, 0.22f, 0.28f, 1f);

        private MaterialPropertyBlock _block;

        public string PodiumId => podiumId;
        public string DisplayName => displayName;
        public bool IsSkipVote => skipVote;

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyColor(skipVote ? skipColor : accuseColor);
        }

        private void Update()
        {
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            ApplyColor(roundState != null && roundState.CurrentPhase == RoundPhase.Reported ? (skipVote ? skipColor : accuseColor) : inactiveColor);
        }

        public void ConfigureRuntime(string id, string label, string accuseText, string skipText, bool isSkipVote, float searchRadius, Renderer renderer = null)
        {
            podiumId = string.IsNullOrWhiteSpace(id) ? podiumId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            accusePrompt = string.IsNullOrWhiteSpace(accuseText) ? accusePrompt : accuseText;
            skipPrompt = string.IsNullOrWhiteSpace(skipText) ? skipPrompt : skipText;
            skipVote = isSkipVote;
            targetSearchRadius = Mathf.Max(1f, searchRadius);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyColor(skipVote ? skipColor : accuseColor);
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (context.InteractorObject == null)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return roundState != null && roundState.CurrentPhase == RoundPhase.Reported;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState == null || roundState.CurrentPhase != RoundPhase.Reported)
            {
                return displayName + ": meeting inactive";
            }

            if (context.InteractorObject != null && roundState.HasMeetingVoteFrom(context.InteractorClientId))
            {
                return displayName + ": vote locked";
            }

            if (skipVote)
            {
                return skipPrompt;
            }

            if (TryFindNearestVoteTarget(context.InteractorObject, out NetworkObject targetObject))
            {
                return $"{accusePrompt}: Player {targetObject.OwnerClientId}";
            }

            return accusePrompt + ": stand near suspect";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState == null || roundState.CurrentPhase != RoundPhase.Reported)
            {
                return false;
            }

            if (skipVote)
            {
                return roundState.ServerRegisterSkipMeetingVote(context.InteractorClientId, displayName);
            }

            if (!TryFindNearestVoteTarget(context.InteractorObject, out NetworkObject targetObject))
            {
                return false;
            }

            return roundState.ServerRegisterMeetingVote(context.InteractorClientId, targetObject.OwnerClientId, displayName);
        }

        private bool TryFindNearestVoteTarget(NetworkObject voterObject, out NetworkObject targetObject)
        {
            targetObject = null;
            if (voterObject == null)
            {
                return false;
            }

            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            float bestDistanceSqr = float.MaxValue;
            float maxDistanceSqr = targetSearchRadius * targetSearchRadius;

            for (int i = 0; i < lifeStates.Length; i++)
            {
                PlayerLifeState lifeState = lifeStates[i];
                if (lifeState == null || !lifeState.IsAlive || lifeState.NetworkObject == null || lifeState.NetworkObject == voterObject)
                {
                    continue;
                }

                float distanceSqr = (lifeState.transform.position - voterObject.transform.position).sqrMagnitude;
                if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                targetObject = lifeState.NetworkObject;
            }

            return targetObject != null;
        }

        private void ApplyColor(Color color)
        {
            statusRenderer ??= GetComponentInChildren<Renderer>();
            if (statusRenderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            statusRenderer.SetPropertyBlock(_block);
        }
    }
}
