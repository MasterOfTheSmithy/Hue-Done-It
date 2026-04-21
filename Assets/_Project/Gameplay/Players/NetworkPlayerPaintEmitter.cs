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

        private PlayerColorProfile _colorProfile;
        private PlayerFloodZoneTracker _floodTracker;

        private void Awake()
        {
            _colorProfile = GetComponent<PlayerColorProfile>();
            _floodTracker = GetComponent<PlayerFloodZoneTracker>();
        }

        public void ServerEmitPaint(PaintEventKind kind, Vector3 position, Vector3 normal, float radius, float intensity)
        {
            if (!IsServer)
            {
                return;
            }

            ReceivePaintClientRpc((byte)kind, position, normal, radius, intensity, _colorProfile != null ? _colorProfile.PlayerColor : Color.white);
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
        private void ReceivePaintClientRpc(byte eventKind, Vector3 position, Vector3 normal, float radius, float intensity, Color color)
        {
            PaintEventKind kind = (PaintEventKind)eventKind;
            bool wet = IsWetPresentation();

            if (Physics.Raycast(position + (normal * 0.2f), -normal, out RaycastHit hit, stainProbeDistance, stainMask, QueryTriggerInteraction.Ignore))
            {
                StainReceiver receiver = hit.collider.GetComponentInParent<StainReceiver>();
                if (receiver != null)
                {
                    receiver.ApplyStain(color, hit.point, hit.normal, radius, intensity, wet);
                }
            }

            SpawnAirSplat(kind, position, normal, radius, intensity, color, wet);
        }

        private static void SpawnAirSplat(PaintEventKind kind, Vector3 position, Vector3 normal, float radius, float intensity, Color color, bool wet)
        {
            GameObject splat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            splat.name = $"Paint_{kind}";
            splat.transform.position = position;
            splat.transform.rotation = Quaternion.LookRotation(normal);
            float size = Mathf.Clamp(radius * (0.6f + intensity), 0.05f, 1.6f);
            splat.transform.localScale = new Vector3(size, size * 0.35f, size);

            Collider colliderRef = splat.GetComponent<Collider>();
            if (colliderRef != null)
            {
                Destroy(colliderRef);
            }

            Renderer renderer = splat.GetComponent<Renderer>();
            Material material = new(Shader.Find("Universal Render Pipeline/Unlit"));
            Color renderColor = color;
            renderColor.a = wet ? 0.35f : 0.65f;
            material.color = renderColor;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Object.Destroy(splat, wet ? 3.5f : 4.5f);
        }
    }
}
