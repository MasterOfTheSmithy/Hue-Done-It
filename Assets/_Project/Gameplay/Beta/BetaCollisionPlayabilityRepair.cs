// File: Assets/_Project/Gameplay/Beta/BetaCollisionPlayabilityRepair.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(-780)]
    [DisallowMultipleComponent]
    public sealed class BetaCollisionPlayabilityRepair : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float repairIntervalSeconds = 1.5f;
        [SerializeField] private bool disableDecorativeColliders = true;
        [SerializeField] private bool applyLowFriction = true;

        private PhysicsMaterial _lowFrictionMaterial;
        private float _nextRepairTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRepairTime)
            {
                return;
            }

            _nextRepairTime = Time.unscaledTime + repairIntervalSeconds;
            Repair();
        }

        private void Repair()
        {
            if (_lowFrictionMaterial == null)
            {
                _lowFrictionMaterial = new PhysicsMaterial("HDI Beta Low Friction")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0.02f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }

            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef == null)
                {
                    continue;
                }

                if (disableDecorativeColliders && ShouldDisableCollider(colliderRef))
                {
                    colliderRef.enabled = false;
                    continue;
                }

                if (applyLowFriction && ShouldApplyLowFriction(colliderRef))
                {
                    colliderRef.sharedMaterial = _lowFrictionMaterial;
                }
            }

            CapsuleCollider[] playerCapsules = FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None);
            for (int i = 0; i < playerCapsules.Length; i++)
            {
                CapsuleCollider capsule = playerCapsules[i];
                if (capsule != null && capsule.GetComponentInParent<NetworkPlayerAuthoritativeMover>() != null)
                {
                    capsule.sharedMaterial = _lowFrictionMaterial;
                }
            }
        }

        private static bool ShouldDisableCollider(Collider colliderRef)
        {
            if (colliderRef == null || colliderRef.isTrigger)
            {
                return false;
            }

            Transform t = colliderRef.transform;
            if (t.GetComponentInParent<NetworkPlayerAuthoritativeMover>() != null ||
                t.GetComponentInParent<NetworkInteractable>() != null ||
                t.GetComponentInParent<NetworkObject>() != null ||
                t.GetComponentInParent<NetworkRepairTask>() != null ||
                t.GetComponentInParent<TaskObjectiveBase>() != null ||
                t.GetComponentInParent<FloodZone>() != null ||
                t.GetComponentInParent<FloodSequenceController>() != null)
            {
                return false;
            }

            string lower = t.name.ToLowerInvariant();
            bool decorativeName = lower.Contains("label") ||
                                  lower.Contains("sign") ||
                                  lower.Contains("beaconbody") ||
                                  lower.Contains("accent") ||
                                  lower.Contains("marker") ||
                                  lower.Contains("visual") ||
                                  lower.Contains("screen");

            bool gameplayName = lower.Contains("task") ||
                                lower.Contains("spawn") ||
                                lower.Contains("floor") ||
                                lower.Contains("deck") ||
                                lower.Contains("lane") ||
                                lower.Contains("route") ||
                                lower.Contains("hub") ||
                                lower.Contains("wall") ||
                                lower.Contains("rail") ||
                                lower.Contains("flood") ||
                                lower.Contains("player") ||
                                lower.Contains("network");

            if (decorativeName && !gameplayName)
            {
                return true;
            }

            Bounds bounds = colliderRef.bounds;
            bool tinySnag = bounds.size.y > 0.18f &&
                            bounds.size.y < 1.15f &&
                            Mathf.Max(bounds.size.x, bounds.size.z) < 0.85f &&
                            !gameplayName;

            return tinySnag;
        }

        private static bool ShouldApplyLowFriction(Collider colliderRef)
        {
            if (colliderRef == null || colliderRef.isTrigger)
            {
                return false;
            }

            string lower = colliderRef.name.ToLowerInvariant();
            if (lower.Contains("floor") ||
                lower.Contains("deck") ||
                lower.Contains("lane") ||
                lower.Contains("route") ||
                lower.Contains("hub") ||
                lower.Contains("walk") ||
                lower.Contains("platform") ||
                colliderRef.GetComponentInParent<NetworkPlayerAuthoritativeMover>() != null)
            {
                return true;
            }

            Bounds b = colliderRef.bounds;
            return b.size.y <= 0.18f && Mathf.Max(b.size.x, b.size.z) >= 2f;
        }
    }
}
