// File: Assets/_Project/Gameplay/Beta/BetaFloorCollisionRepair.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(-760)]
    public sealed class BetaFloorCollisionRepair : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float repairIntervalSeconds = 2f;

        private float _nextRepairTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRepairTime)
            {
                return;
            }

            _nextRepairTime = Time.unscaledTime + repairIntervalSeconds;
            RepairLikelyFloorColliders();
        }

        private static void RepairLikelyFloorColliders()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<Collider>() != null)
                {
                    continue;
                }

                string objectName = renderer.gameObject.name;
                if (!LooksLikeWalkableSurface(objectName))
                {
                    continue;
                }

                BoxCollider collider = renderer.gameObject.AddComponent<BoxCollider>();
                Bounds localBounds = renderer.localBounds;
                collider.center = localBounds.center;
                collider.size = new Vector3(
                    Mathf.Max(localBounds.size.x, 0.25f),
                    Mathf.Max(localBounds.size.y, 0.08f),
                    Mathf.Max(localBounds.size.z, 0.25f));
                collider.isTrigger = false;
            }
        }

        private static bool LooksLikeWalkableSurface(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("deck", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("route", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("lane", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("plaza", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("platform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("walk", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
