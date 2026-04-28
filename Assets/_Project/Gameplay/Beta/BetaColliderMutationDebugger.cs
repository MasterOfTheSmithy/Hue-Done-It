// File: Assets/_Project/Gameplay/Beta/BetaColliderMutationDebugger.cs
using System.Collections.Generic;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Detects runtime collider/object mutations that can cause floor falling. Instead of disabling the
    /// declutter/collision systems globally, this logs what changed and repairs only critical floor/gameplay collision.
    /// </summary>
    [DefaultExecutionOrder(-875)]
    [DisallowMultipleComponent]
    public sealed class BetaColliderMutationDebugger : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float scanIntervalSeconds = 0.35f;
        [SerializeField] private bool reenableCriticalDisabledColliders = true;
        [SerializeField] private bool reactivateCriticalInactiveObjects = true;
        [SerializeField] private bool logSuspiciousMutations = true;
        [SerializeField, Min(0.25f)] private float perObjectLogCooldownSeconds = 3f;

        private readonly Dictionary<int, bool> _knownColliderEnabled = new();
        private readonly Dictionary<int, bool> _knownObjectActive = new();
        private readonly Dictionary<int, float> _lastLogTimes = new();
        private float _nextScanTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ScanColliders();
            ScanInactiveCriticalObjects();
        }

        private void ScanColliders()
        {
            Collider[] colliders = Resources.FindObjectsOfTypeAll<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef == null || !IsSceneObject(colliderRef.gameObject))
                {
                    continue;
                }

                int id = colliderRef.GetInstanceID();
                bool currentEnabled = colliderRef.enabled;
                if (!_knownColliderEnabled.TryGetValue(id, out bool previousEnabled))
                {
                    _knownColliderEnabled[id] = currentEnabled;
                    continue;
                }

                if (previousEnabled && !currentEnabled)
                {
                    bool critical = BetaFloorRegressionRecoveryDirector.IsCriticalCollider(colliderRef);
                    if (logSuspiciousMutations)
                    {
                        LogRateLimited(id, "[ColliderDebug] Collider disabled: " +
                                           BetaFloorRegressionRecoveryDirector.GetPath(colliderRef.transform) +
                                           " | critical=" + critical +
                                           " | bounds=" + colliderRef.bounds);
                    }

                    if (critical && reenableCriticalDisabledColliders)
                    {
                        colliderRef.enabled = true;
                        colliderRef.isTrigger = false;
                        currentEnabled = true;
                        Physics.SyncTransforms();
                        LogRateLimited(id + 17001, "[ColliderDebug] Re-enabled critical collider: " +
                                                    BetaFloorRegressionRecoveryDirector.GetPath(colliderRef.transform));
                    }
                }

                _knownColliderEnabled[id] = currentEnabled;
            }
        }

        private void ScanInactiveCriticalObjects()
        {
            if (!reactivateCriticalInactiveObjects)
            {
                return;
            }

            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject obj = objects[i];
                if (obj == null || !IsSceneObject(obj))
                {
                    continue;
                }

                int id = obj.GetInstanceID();
                bool active = obj.activeSelf;
                if (!_knownObjectActive.TryGetValue(id, out bool previousActive))
                {
                    _knownObjectActive[id] = active;
                    continue;
                }

                if (previousActive && !active)
                {
                    bool critical = BetaFloorRegressionRecoveryDirector.LooksWalkable(obj.name) ||
                                    BetaFloorRegressionRecoveryDirector.IsProtectedGameplayObject(obj.transform);
                    if (logSuspiciousMutations)
                    {
                        LogRateLimited(id + 32003, "[ColliderDebug] GameObject deactivated: " +
                                                    BetaFloorRegressionRecoveryDirector.GetPath(obj.transform) +
                                                    " | critical=" + critical);
                    }

                    if (critical)
                    {
                        obj.SetActive(true);
                        LogRateLimited(id + 32004, "[ColliderDebug] Re-activated critical object: " +
                                                    BetaFloorRegressionRecoveryDirector.GetPath(obj.transform));
                    }
                }

                _knownObjectActive[id] = obj.activeSelf;
            }
        }

        private static bool IsSceneObject(GameObject obj)
        {
            return obj != null &&
                   obj.scene.IsValid() &&
                   obj.hideFlags == HideFlags.None;
        }

        private void LogRateLimited(int id, string message)
        {
            if (_lastLogTimes.TryGetValue(id, out float lastTime) && Time.unscaledTime - lastTime < perObjectLogCooldownSeconds)
            {
                return;
            }

            _lastLogTimes[id] = Time.unscaledTime;
            Debug.Log(message);
        }
    }
}
