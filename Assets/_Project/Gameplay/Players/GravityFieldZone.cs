// File: Assets/_Project/Gameplay/Players/GravityFieldZone.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    public sealed class GravityFieldZone : MonoBehaviour
    {
        [Header("Field Rules")]
        [SerializeField, Range(0f, 1f)] private float gravityMultiplier = 0.35f;
        [SerializeField] private string displayName = "Low Gravity Field";
        [SerializeField] private bool active = true;

        [Header("Movement Feel")]
        [SerializeField, Min(0f)] private float paintIntensityMultiplier = 1.2f;
        [SerializeField] private bool unlimitedWallStick = true;

        public float GravityMultiplier => active ? Mathf.Clamp01(gravityMultiplier) : 1f;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Gravity Field" : displayName;
        public bool IsActive => active;
        public float PaintIntensityMultiplier => Mathf.Max(0f, paintIntensityMultiplier);
        public bool UnlimitedWallStick => unlimitedWallStick;

        public void ConfigureRuntime(string nextDisplayName, float nextGravityMultiplier, bool nextUnlimitedWallStick, float nextPaintIntensityMultiplier)
        {
            displayName = string.IsNullOrWhiteSpace(nextDisplayName) ? displayName : nextDisplayName;
            gravityMultiplier = Mathf.Clamp01(nextGravityMultiplier);
            unlimitedWallStick = nextUnlimitedWallStick;
            paintIntensityMultiplier = Mathf.Max(0f, nextPaintIntensityMultiplier);
            active = true;
        }

        private void Reset()
        {
            Collider colliderRef = GetComponent<Collider>();
            if (colliderRef != null)
            {
                colliderRef.isTrigger = true;
            }
        }

        private void OnValidate()
        {
            gravityMultiplier = Mathf.Clamp01(gravityMultiplier);
            paintIntensityMultiplier = Mathf.Max(0f, paintIntensityMultiplier);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = gravityMultiplier <= 0.05f
                ? new Color(0.75f, 0.45f, 1f, 0.35f)
                : new Color(0.25f, 0.85f, 1f, 0.35f);

            Collider colliderRef = GetComponent<Collider>();
            if (colliderRef is BoxCollider box)
            {
                Matrix4x4 previous = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.matrix = previous;
            }
            else
            {
                Gizmos.DrawWireCube(transform.position, transform.lossyScale);
            }
        }
    }
}
