// File: Assets/_Project/Gameplay/Beta/BetaRoomDeclutterDirector.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Beta traversal cleanup. This removes likely decorative blockers from the main hub/lane/task-pocket volumes
    /// without touching interactables, networked gameplay objects, players, tasks, or flood systems.
    /// </summary>
    [DefaultExecutionOrder(-770)]
    [DisallowMultipleComponent]
    public sealed class BetaRoomDeclutterDirector : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float scanIntervalSeconds = 2.5f;
        [SerializeField] private bool disableVisualClutter = true;
        [SerializeField] private bool disableClutterColliders = true;

        private float _nextScanTime;

        private static readonly Bounds[] ClearVolumes =
        {
            new Bounds(new Vector3(0f, 1f, 0f), new Vector3(18f, 5f, 18f)),
            new Bounds(new Vector3(0f, 1f, 17f), new Vector3(10f, 5f, 32f)),
            new Bounds(new Vector3(0f, 1f, -17f), new Vector3(10f, 5f, 32f)),
            new Bounds(new Vector3(-17f, 1f, 0f), new Vector3(32f, 5f, 10f)),
            new Bounds(new Vector3(17f, 1f, 0f), new Vector3(32f, 5f, 10f)),
            new Bounds(new Vector3(0f, 1f, 31f), new Vector3(21f, 5f, 12f)),
            new Bounds(new Vector3(0f, 1f, -31f), new Vector3(21f, 5f, 12f)),
            new Bounds(new Vector3(-31f, 1f, 0f), new Vector3(12f, 5f, 21f)),
            new Bounds(new Vector3(31f, 1f, 0f), new Vector3(12f, 5f, 21f))
        };

        private void Start()
        {
            Declutter();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            Declutter();
        }

        private void Declutter()
        {
            if (disableClutterColliders)
            {
                Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider colliderRef = colliders[i];
                    if (colliderRef == null || !colliderRef.enabled || colliderRef.isTrigger)
                    {
                        continue;
                    }

                    if (!IsInsideClearVolume(colliderRef.bounds) || IsProtectedGameplayObject(colliderRef.transform))
                    {
                        continue;
                    }

                    if (LooksLikeClutter(colliderRef.name) || LooksLikeSmallTraversalSnag(colliderRef.bounds))
                    {
                        colliderRef.enabled = false;
                    }
                }
            }

            if (disableVisualClutter)
            {
                Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer rendererRef = renderers[i];
                    if (rendererRef == null || !rendererRef.enabled)
                    {
                        continue;
                    }

                    if (!IsInsideClearVolume(rendererRef.bounds) || IsProtectedGameplayObject(rendererRef.transform))
                    {
                        continue;
                    }

                    if (LooksLikeClutter(rendererRef.name) && !LooksLikeNavigationVisual(rendererRef.name))
                    {
                        rendererRef.enabled = false;
                    }
                }
            }
        }

        private static bool IsInsideClearVolume(Bounds bounds)
        {
            for (int i = 0; i < ClearVolumes.Length; i++)
            {
                if (ClearVolumes[i].Intersects(bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProtectedGameplayObject(Transform t)
        {
            if (t == null)
            {
                return true;
            }

            return t.GetComponentInParent<NetworkObject>() != null ||
                   t.GetComponentInParent<NetworkInteractable>() != null ||
                   t.GetComponentInParent<NetworkRepairTask>() != null ||
                   t.GetComponentInParent<TaskObjectiveBase>() != null ||
                   t.GetComponentInParent<NetworkPlayerAvatar>() != null ||
                   t.GetComponentInParent<NetworkPlayerAuthoritativeMover>() != null;
        }

        private static bool LooksLikeClutter(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            string lower = objectName.ToLowerInvariant();
            return lower.Contains("clutter") ||
                   lower.Contains("debris") ||
                   lower.Contains("filler") ||
                   lower.Contains("junk") ||
                   lower.Contains("loose") ||
                   lower.Contains("decor") ||
                   lower.Contains("crate") ||
                   lower.Contains("barrel") ||
                   lower.Contains("box") ||
                   lower.Contains("pipe") ||
                   lower.Contains("canister") ||
                   lower.Contains("cone") ||
                   lower.Contains("prop");
        }

        private static bool LooksLikeNavigationVisual(string objectName)
        {
            string lower = objectName == null ? string.Empty : objectName.ToLowerInvariant();
            return lower.Contains("floor") ||
                   lower.Contains("lane") ||
                   lower.Contains("route") ||
                   lower.Contains("hub") ||
                   lower.Contains("trim") ||
                   lower.Contains("label") ||
                   lower.Contains("warning") ||
                   lower.Contains("task support");
        }

        private static bool LooksLikeSmallTraversalSnag(Bounds bounds)
        {
            Vector3 size = bounds.size;
            float footprint = Mathf.Max(size.x, size.z);
            return size.y > 0.16f && size.y < 1.35f && footprint < 1.25f;
        }
    }
}
