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
        [SerializeField, Min(0f)] private float stretchMultiplier = 0.085f;
        [SerializeField] private Vector2 forceClamp = new(0f, 24f);
        [SerializeField] private Vector2 radiusClamp = new(0.05f, 1.65f);
        [SerializeField] private Vector2 intensityClamp = new(0.06f, 1.15f);
        [SerializeField] private Vector2 stretchClamp = new(1f, 3.25f);
        [SerializeField, Min(0f)] private float permanentForceThreshold = 9.5f;

        [Header("Air/Fallback Splat")]
        [SerializeField] private bool spawnFallbackAirSplat = true;
        [SerializeField, Min(0.1f)] private float fallbackLifetimeSeconds = 3.5f;

        [Header("Budget")]
        [SerializeField, Min(1)] private int maxReplicatedPaintEventsPerSecond = 8;

        private PlayerColorProfile _colorProfile;
        private PlayerFloodZoneTracker _floodTracker;
        private float _paintBudgetWindowStart;
        private int _paintEventsThisWindow;

        private void Awake()
        {
            _colorProfile = GetComponent<PlayerColorProfile>();
            _floodTracker = GetComponent<PlayerFloodZoneTracker>();
        }

        public void ServerEmitPaint(PaintEventKind kind, Vector3 position, Vector3 normal, float radius, float intensity)
        {
            Vector3 velocityDirection = transform.forward;
            if (TryGetComponent(out Rigidbody body) && body.linearVelocity.sqrMagnitude > 0.001f)
            {
                velocityDirection = body.linearVelocity.normalized;
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

            if (!TryConsumePaintBudget())
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
                scaled.TangentDirection,
                (byte)scaled.SplatType,
                (byte)scaled.Permanence,
                scaled.PatternIndex,
                scaled.PatternSeed,
                scaled.StretchAmount,
                scaled.RotationDegrees,
                _colorProfile != null ? _colorProfile.PlayerColor : Color.white);
        }

        private bool TryConsumePaintBudget()
        {
            float now = Time.unscaledTime;
            if (now - _paintBudgetWindowStart >= 1f)
            {
                _paintBudgetWindowStart = now;
                _paintEventsThisWindow = 0;
            }

            if (_paintEventsThisWindow >= maxReplicatedPaintEventsPerSecond)
            {
                return false;
            }

            _paintEventsThisWindow++;
            return true;
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
            Vector3 tangentDirection = BuildSurfaceTangent(normalizedNormal, normalizedVelocity);
            int selectedPatternIndex = patternIndex >= 0 ? patternIndex : ComputeStablePatternIndex(position, kind, clampedForce);
            int patternSeed = ComputePatternSeed(kind, clampedForce, position);

            if (permanence == PaintSplatPermanence.Temporary && clampedForce >= permanentForceThreshold)
            {
                permanence = PaintSplatPermanence.Permanent;
            }

            ApplyEventTuning(kind, ref splatType, ref permanence, ref scaledRadius, ref scaledIntensity, clampedForce, normalizedNormal, normalizedVelocity, ref tangentDirection, out float stretchAmount, out float rotationDegrees);

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
                TangentDirection = tangentDirection,
                PatternIndex = selectedPatternIndex,
                PatternSeed = patternSeed,
                StretchAmount = stretchAmount,
                RotationDegrees = rotationDegrees
            };
        }

        private void ApplyEventTuning(
            PaintEventKind kind,
            ref PaintSplatType splatType,
            ref PaintSplatPermanence permanence,
            ref float radius,
            ref float intensity,
            float forceMagnitude,
            Vector3 normal,
            Vector3 velocityDirection,
            ref Vector3 tangentDirection,
            out float stretchAmount,
            out float rotationDegrees)
        {
            stretchAmount = Mathf.Clamp(1f + (forceMagnitude * stretchMultiplier), stretchClamp.x, stretchClamp.y);
            rotationDegrees = 0f;

            switch (kind)
            {
                case PaintEventKind.Move:
                    splatType = PaintSplatType.MoveSmear;
                    permanence = PaintSplatPermanence.Temporary;
                    radius *= 0.58f;
                    intensity *= 0.72f;
                    stretchAmount *= 1.45f;
                    tangentDirection = BuildSurfaceTangent(normal, tangentDirection);
                    break;

                case PaintEventKind.Land:
                    splatType = PaintSplatType.Landing;
                    radius *= Mathf.Lerp(0.92f, 1.28f, Mathf.InverseLerp(3f, forceClamp.y, forceMagnitude));
                    intensity *= Mathf.Lerp(0.88f, 1.18f, Mathf.InverseLerp(2f, forceClamp.y, forceMagnitude));
                    stretchAmount *= 1.15f;
                    break;

                case PaintEventKind.WallStick:
                    splatType = PaintSplatType.WallScrape;
                    permanence = PaintSplatPermanence.Temporary;
                    radius *= 0.78f;
                    intensity *= 0.88f;
                    stretchAmount *= 1.65f;
                    tangentDirection = BuildSurfaceTangent(normal, Vector3.down);
                    rotationDegrees = 90f;
                    break;

                case PaintEventKind.WallLaunch:
                    splatType = PaintSplatType.WallLaunchBurst;
                    radius *= 1.05f;
                    intensity *= 1.18f;
                    stretchAmount *= 1.35f;
                    tangentDirection = BuildSurfaceTangent(normal, velocityDirection);
                    break;

                case PaintEventKind.Punch:
                    splatType = PaintSplatType.Punch;
                    radius *= 0.9f;
                    intensity *= 1.22f;
                    stretchAmount *= 1.40f;
                    tangentDirection = BuildSurfaceTangent(normal, velocityDirection);
                    break;

                case PaintEventKind.RagdollImpact:
                    splatType = PaintSplatType.HeavyImpact;
                    permanence = PaintSplatPermanence.Permanent;
                    radius *= 1.15f;
                    intensity *= 1.10f;
                    stretchAmount *= 1.55f;
                    tangentDirection = BuildSurfaceTangent(normal, velocityDirection);
                    break;

                case PaintEventKind.TaskInteract:
                    splatType = PaintSplatType.TaskInteract;
                    permanence = PaintSplatPermanence.Permanent;
                    radius *= 0.88f;
                    intensity *= 0.95f;
                    stretchAmount *= 1.08f;
                    tangentDirection = BuildSurfaceTangent(normal, tangentDirection);
                    break;

                case PaintEventKind.ThrownObjectImpact:
                    splatType = PaintSplatType.ThrownObject;
                    radius *= 1.1f;
                    intensity *= 1.12f;
                    stretchAmount *= 1.5f;
                    if (forceMagnitude >= permanentForceThreshold * 0.9f)
                    {
                        permanence = PaintSplatPermanence.Permanent;
                    }

                    tangentDirection = BuildSurfaceTangent(normal, velocityDirection);
                    break;

                case PaintEventKind.FloodBurst:
                    splatType = PaintSplatType.Flood;
                    permanence = PaintSplatPermanence.Temporary;
                    stretchAmount *= 0.95f;
                    break;

                case PaintEventKind.FloodDrip:
                    splatType = PaintSplatType.Flood;
                    permanence = PaintSplatPermanence.Temporary;
                    radius *= 0.72f;
                    intensity *= 0.68f;
                    stretchAmount *= 1.55f;
                    tangentDirection = BuildSurfaceTangent(normal, Vector3.down);
                    break;
            }

            radius = Mathf.Clamp(radius, radiusClamp.x, radiusClamp.y);
            intensity = Mathf.Clamp(intensity, intensityClamp.x, intensityClamp.y);
            stretchAmount = Mathf.Clamp(stretchAmount, stretchClamp.x, stretchClamp.y);
        }

        private static Vector3 BuildSurfaceTangent(Vector3 normal, Vector3 preferredDirection)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(preferredDirection, normal);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.7f ? Vector3.right : Vector3.up);
            }

            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.right;
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

        private static int ComputePatternSeed(PaintEventKind kind, float forceMagnitude, Vector3 position)
        {
            int seed = ((int)kind + 11) * 16777619;
            seed ^= Mathf.RoundToInt(forceMagnitude * 100f) * 31;
            seed ^= Mathf.RoundToInt(position.x * 100f) * 17;
            seed ^= Mathf.RoundToInt(position.y * 100f) * 13;
            seed ^= Mathf.RoundToInt(position.z * 100f) * 7;
            return seed & int.MaxValue;
        }

        private static PaintSplatType MapSplatType(PaintEventKind kind)
        {
            return kind switch
            {
                PaintEventKind.Move => PaintSplatType.MoveSmear,
                PaintEventKind.Land => PaintSplatType.Landing,
                PaintEventKind.WallStick => PaintSplatType.WallScrape,
                PaintEventKind.WallLaunch => PaintSplatType.WallLaunchBurst,
                PaintEventKind.Punch => PaintSplatType.Punch,
                PaintEventKind.TaskInteract => PaintSplatType.TaskInteract,
                PaintEventKind.RagdollImpact => PaintSplatType.HeavyImpact,
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


        private StainReceiver ResolveReceiver(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                return null;
            }

            StainReceiver receiver = hit.collider.GetComponent<StainReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            receiver = hit.collider.GetComponentInParent<StainReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            Renderer renderer = ResolveRendererForHit(hit);
            if (renderer == null)
            {
                return null;
            }

            if (renderer.GetComponentInParent<NetworkPlayerAvatar>() != null)
            {
                return null;
            }

            receiver = renderer.GetComponent<StainReceiver>();
            if (receiver == null)
            {
                receiver = renderer.gameObject.AddComponent<StainReceiver>();
            }

            receiver.ConfigureTargetRenderer(renderer);
            return receiver;
        }

        private static Renderer ResolveRendererForHit(RaycastHit hit)
        {
            Renderer best = hit.collider.GetComponent<Renderer>();
            float bestDistance = best != null ? Vector3.Distance(best.bounds.ClosestPoint(hit.point), hit.point) : float.MaxValue;

            Renderer[] childRenderers = hit.collider.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                Renderer candidate = childRenderers[i];
                if (candidate == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.bounds.ClosestPoint(hit.point), hit.point);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            Renderer[] parentRenderers = hit.collider.GetComponentsInParent<Renderer>(true);
            for (int i = 0; i < parentRenderers.Length; i++)
            {
                Renderer candidate = parentRenderers[i];
                if (candidate == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.bounds.ClosestPoint(hit.point), hit.point);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }


        private static bool ShouldSubmitTexturePaint(PaintSplatData splatData)
        {
            if (splatData.Permanence == PaintSplatPermanence.Permanent)
            {
                return true;
            }

            if (splatData.ForceMagnitude >= 7f)
            {
                return true;
            }

            switch (splatData.EventKind)
            {
                case PaintEventKind.Move:
                case PaintEventKind.WallStick:
                case PaintEventKind.FloodDrip:
                    return false;
                default:
                    return true;
            }
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
            Vector3 tangentDirection,
            byte splatType,
            byte permanence,
            int patternIndex,
            int patternSeed,
            float stretchAmount,
            float rotationDegrees,
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
                TangentDirection = tangentDirection,
                PatternIndex = patternIndex,
                PatternSeed = patternSeed,
                StretchAmount = stretchAmount,
                RotationDegrees = rotationDegrees
            };

            bool wet = IsWetPresentation();
            bool applied = false;

            bool hitUsesSurfacePaint = false;

            if (Physics.Raycast(position + (normal * 0.2f), -normal, out RaycastHit hit, stainProbeDistance, stainMask, QueryTriggerInteraction.Ignore))
            {
                splatData.Position = hit.point;
                splatData.Normal = hit.normal;

                hitUsesSurfacePaint =
                    hit.collider.GetComponentInParent<PaintSurfaceChunk>() != null ||
                    hit.collider.GetComponentInParent<PaintSurfaceOverlayRenderer>() != null ||
                    hit.collider.GetComponentInParent<WaterPaintReceiver>() != null;

                // Texture painting is intentionally reserved for meaningful impacts. Movement smears can
                // fire many times per second, and applying both texture stamps and projected decals for every
                // movement event is the main paint-lag failure mode.
                if (ShouldSubmitTexturePaint(splatData))
                {
                    applied = PaintWorldManager.SubmitLegacy(splatData, color);
                }

                StainReceiver receiver = ResolveReceiver(hit);
                if (receiver != null)
                {
                    receiver.ApplyLocalizedSplat(color, splatData, wet);
                    applied = true;
                }
            }

            if (!applied && !hitUsesSurfacePaint && spawnFallbackAirSplat)
            {
                StainReceiver.SpawnGlobalSplat(color, splatData, wet, fallbackLifetimeSeconds);
            }
        }
    }
}
