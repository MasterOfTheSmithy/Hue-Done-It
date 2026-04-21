using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Paint;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerColorProfile))]
    public sealed class NetworkPlayerPaintEmitter : NetworkBehaviour
    {
        [Header("Paint Query")]
        [SerializeField] private LayerMask stainMask = ~0;
        [SerializeField, Min(0.05f)] private float stainProbeDistance = 1.8f;

        [Header("Force Scaling")]
        [SerializeField, Min(0.01f)] private float forceToRadiusMultiplier = 0.06f;
        [SerializeField, Min(0.01f)] private float forceToIntensityMultiplier = 0.08f;
        [SerializeField] private Vector2 forceClamp = new(0f, 24f);
        [SerializeField] private Vector2 radiusClamp = new(0.06f, 2.8f);
        [SerializeField] private Vector2 intensityClamp = new(0.08f, 1.65f);
        [SerializeField, Min(0f)] private float permanentForceThreshold = 9.5f;

        [Header("Air/Fallback Splat")]
        [SerializeField] private bool spawnFallbackAirSplat = true;
        [SerializeField, Min(0.1f)] private float fallbackLifetimeSeconds = 3.5f;

        private PlayerColorProfile _colorProfile;
        private PlayerFloodZoneTracker _floodTracker;

        private void Awake()
        {
            _colorProfile = GetComponent<PlayerColorProfile>();
            _floodTracker = GetComponent<PlayerFloodZoneTracker>();
        }

        public void ServerEmitPaint(PaintEventKind kind, Vector3 position, Vector3 normal, float radius, float intensity)
        {
            Vector3 velocityDirection = transform.forward;
            if (TryGetComponent(out Rigidbody body) && body.velocity.sqrMagnitude > 0.001f)
            {
                velocityDirection = body.velocity.normalized;
            }

            float forceMagnitude = Mathf.Max(radius * 8f, intensity * 10f);
            PaintSplatType splatType = MapSplatType(kind);
            PaintSplatPermanence permanence = forceMagnitude >= permanentForceThreshold
                ? PaintSplatPermanence.Permanent
                : DefaultPermanenceFor(kind);

            ServerEmitPaint(kind, position, normal, radius, intensity, forceMagnitude, velocityDirection, splatType, permanence, -1);
        }

        public void ServerEmitPaint(
            PaintEventKind kind,
            Vector3 position,
            Vector3 normal,
            float radius,
            float intensity,
            float forceMagnitude,
            Vector3 velocityDirection,
            PaintSplatType splatType,
            PaintSplatPermanence permanence,
            int patternIndex)
        {
            if (!IsServer)
            {
                return;
            }

            PaintSplatData scaled = BuildScaledSplat(kind, position, normal, radius, intensity, forceMagnitude, velocityDirection, splatType, permanence, patternIndex);
            ReceivePaintClientRpc(
                (byte)scaled.EventKind,
                scaled.Position,
                scaled.Normal,
                scaled.Radius,
                scaled.Intensity,
                scaled.ForceMagnitude,
                scaled.VelocityDirection,
                (byte)scaled.SplatType,
                (byte)scaled.Permanence,
                scaled.PatternIndex,
                _colorProfile != null ? _colorProfile.PlayerColor : Color.white);
        }

        private PaintSplatData BuildScaledSplat(
            PaintEventKind kind,
            Vector3 position,
            Vector3 normal,
            float radius,
            float intensity,
            float forceMagnitude,
            Vector3 velocityDirection,
            PaintSplatType splatType,
            PaintSplatPermanence permanence,
            int patternIndex)
        {
            float clampedForce = Mathf.Clamp(forceMagnitude, forceClamp.x, forceClamp.y);
            float scaledRadius = Mathf.Clamp(radius + (clampedForce * forceToRadiusMultiplier), radiusClamp.x, radiusClamp.y);
            float scaledIntensity = Mathf.Clamp(intensity + (clampedForce * forceToIntensityMultiplier), intensityClamp.x, intensityClamp.y);
            Vector3 normalizedNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            Vector3 normalizedVelocity = velocityDirection.sqrMagnitude > 0.0001f ? velocityDirection.normalized : transform.forward;
            int selectedPatternIndex = patternIndex >= 0 ? patternIndex : ComputeStablePatternIndex(position, kind, clampedForce);

            if (permanence == PaintSplatPermanence.Temporary && clampedForce >= permanentForceThreshold)
            {
                permanence = PaintSplatPermanence.Permanent;
            }

            return new PaintSplatData
            {
                EventKind = kind,
                SplatType = splatType,
                Permanence = permanence,
                Position = position,
                Normal = normalizedNormal,
                Radius = scaledRadius,
                Intensity = scaledIntensity,
                ForceMagnitude = clampedForce,
                VelocityDirection = normalizedVelocity,
                PatternIndex = selectedPatternIndex
            };
        }

        private static int ComputeStablePatternIndex(Vector3 position, PaintEventKind kind, float force)
        {
            int hash = kind.GetHashCode();
            hash = (hash * 397) ^ Mathf.RoundToInt(position.x * 100f);
            hash = (hash * 397) ^ Mathf.RoundToInt(position.y * 100f);
            hash = (hash * 397) ^ Mathf.RoundToInt(position.z * 100f);
            hash = (hash * 397) ^ Mathf.RoundToInt(force * 100f);
            return Mathf.Abs(hash);
        }

        private static PaintSplatType MapSplatType(PaintEventKind kind)
        {
            return kind switch
            {
                PaintEventKind.Move => PaintSplatType.Footstep,
                PaintEventKind.Land => PaintSplatType.Landing,
                PaintEventKind.WallStick => PaintSplatType.WallImpact,
                PaintEventKind.WallLaunch => PaintSplatType.WallImpact,
                PaintEventKind.Punch => PaintSplatType.Punch,
                PaintEventKind.TaskInteract => PaintSplatType.TaskInteract,
                PaintEventKind.RagdollImpact => PaintSplatType.RagdollImpact,
                PaintEventKind.ThrownObjectImpact => PaintSplatType.ThrownObject,
                PaintEventKind.FloodBurst => PaintSplatType.Flood,
                PaintEventKind.FloodDrip => PaintSplatType.Flood,
                _ => PaintSplatType.Generic
            };
        }

        private static PaintSplatPermanence DefaultPermanenceFor(PaintEventKind kind)
        {
            return kind switch
            {
                PaintEventKind.RagdollImpact => PaintSplatPermanence.Permanent,
                PaintEventKind.TaskInteract => PaintSplatPermanence.Permanent,
                PaintEventKind.ThrownObjectImpact => PaintSplatPermanence.Permanent,
                _ => PaintSplatPermanence.Temporary
            };
        }

        private bool IsWetPresentation()
        {
            if (_floodTracker == null)
            {
                return false;
            }

            FloodZoneState state = _floodTracker.CurrentZoneState;
            return state is FloodZoneState.Wet or FloodZoneState.Flooding or FloodZoneState.Submerged;
        }

        [ClientRpc]
        private void ReceivePaintClientRpc(
            byte eventKind,
            Vector3 position,
            Vector3 normal,
            float radius,
            float intensity,
            float forceMagnitude,
            Vector3 velocityDirection,
            byte splatType,
            byte permanence,
            int patternIndex,
            Color color)
        {
            PaintSplatData splatData = new()
            {
                EventKind = (PaintEventKind)eventKind,
                SplatType = (PaintSplatType)splatType,
                Permanence = (PaintSplatPermanence)permanence,
                Position = position,
                Normal = normal,
                Radius = radius,
                Intensity = intensity,
                ForceMagnitude = forceMagnitude,
                VelocityDirection = velocityDirection,
                PatternIndex = patternIndex
            };

            bool wet = IsWetPresentation();
            bool applied = false;

            if (Physics.Raycast(position + (normal * 0.2f), -normal, out RaycastHit hit, stainProbeDistance, stainMask, QueryTriggerInteraction.Ignore))
            {
                StainReceiver receiver = hit.collider.GetComponentInParent<StainReceiver>();
                if (receiver != null)
                {
                    splatData.Position = hit.point;
                    splatData.Normal = hit.normal;
                    receiver.ApplyStain(color, splatData, wet);
                    applied = true;
                }
            }

            if (!applied && spawnFallbackAirSplat)
            {
                StainReceiver.SpawnGlobalSplat(color, splatData, wet, fallbackLifetimeSeconds);
            }
        }
    }
}
