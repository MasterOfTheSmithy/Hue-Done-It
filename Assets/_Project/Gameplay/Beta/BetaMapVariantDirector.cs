// File: Assets/_Project/Gameplay/Beta/BetaMapVariantDirector.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds a stronger ship shell/roof pass, keeps most rooms enclosed, leaves a few skylight pockets,
    /// and gives each Undertint gameplay scene a slightly different but closely related layout feel.
    /// This is intentionally additive so it can sit on top of the generated beta layout.
    /// </summary>
    [DefaultExecutionOrder(945)]
    [DisallowMultipleComponent]
    public sealed class BetaMapVariantDirector : MonoBehaviour
    {
        private const string RuntimeRootName = "_BetaArenaRuntime";
        private const string VariantRootName = "__BetaProductionVariantLayer";

        [SerializeField, Min(0.1f)] private float retryIntervalSeconds = 1f;
        [SerializeField] private float roomRoofY = 4.15f;
        [SerializeField] private float hallRoofY = 3.95f;
        [SerializeField] private float roofThickness = 0.24f;
        [SerializeField] private float roofInset = 0.15f;
        [SerializeField] private float shellTrimY = 4.35f;
        [SerializeField] private float skylightPaneThickness = 0.08f;

        private float _nextAttemptTime;
        private bool _applied;

        private void Update()
        {
            if (_applied || Time.unscaledTime < _nextAttemptTime)
            {
                return;
            }

            _nextAttemptTime = Time.unscaledTime + retryIntervalSeconds;
            TryApply();
        }

        private void TryApply()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !BetaGameplaySceneCatalog.IsProductionGameplayScene(activeScene.name))
            {
                return;
            }

            GameObject runtimeRoot = GameObject.Find(RuntimeRootName);
            if (runtimeRoot == null)
            {
                return;
            }

            Transform variantRoot = runtimeRoot.transform.Find(VariantRootName);
            if (variantRoot == null)
            {
                GameObject go = new GameObject(VariantRootName);
                go.transform.SetParent(runtimeRoot.transform, false);
                variantRoot = go.transform;
            }

            ApplySharedShell(variantRoot);

            switch (BetaGameplaySceneCatalog.GetSceneVariantIndex(activeScene.name))
            {
                case 1:
                    ApplyAnnexVariant(variantRoot);
                    break;
                case 2:
                    ApplyOverflowVariant(variantRoot);
                    break;
                default:
                    ApplyCoreVariant(variantRoot);
                    break;
            }

            _applied = true;
        }

        private void ApplySharedShell(Transform root)
        {
            Color roof = new Color(0.17f, 0.19f, 0.23f);
            Color trim = new Color(0.12f, 0.15f, 0.19f);
            Color window = new Color(0.13f, 0.28f, 0.36f, 0.52f);

            EnsureSkylightRoof(root, "Roof_Room_Engine", new Vector3(-14f, roomRoofY, 8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), 2.2f, true, roof);
            EnsureSolidRoof(root, "Roof_Room_Pump", new Vector3(0f, roomRoofY, 8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), roof);
            EnsureSkylightRoof(root, "Roof_Room_Navigation", new Vector3(14f, roomRoofY, 8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), 2.0f, false, roof);
            EnsureSolidRoof(root, "Roof_Room_Cargo", new Vector3(-14f, roomRoofY, -8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), roof);
            EnsureSkylightRoof(root, "Roof_Room_Reactor", new Vector3(0f, roomRoofY, -8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), 2.8f, true, roof);
            EnsureSolidRoof(root, "Roof_Room_Lab", new Vector3(14f, roomRoofY, -8f), new Vector3(10f - roofInset, roofThickness, 8f - roofInset), roof);

            EnsureSolidRoof(root, "Roof_Hall_NorthRow", new Vector3(0f, hallRoofY, 8f), new Vector3(26f, roofThickness, 3.2f), roof * 0.96f);
            EnsureSolidRoof(root, "Roof_Hall_SouthRow", new Vector3(0f, hallRoofY, -8f), new Vector3(26f, roofThickness, 3.2f), roof * 0.96f);
            EnsureSolidRoof(root, "Roof_Hall_Center", new Vector3(0f, hallRoofY, 0f), new Vector3(6.2f, roofThickness, 11.2f), roof * 0.92f);
            EnsureSolidRoof(root, "Roof_Hall_WestConnector", new Vector3(-14f, hallRoofY, 0f), new Vector3(6.2f, roofThickness, 11.2f), roof * 0.92f);
            EnsureSolidRoof(root, "Roof_Hall_EastConnector", new Vector3(14f, hallRoofY, 0f), new Vector3(6.2f, roofThickness, 11.2f), roof * 0.92f);

            EnsureBlock(root, "ShellTrim_North", new Vector3(0f, shellTrimY, 16.05f), new Vector3(45f, 0.2f, 0.35f), trim);
            EnsureBlock(root, "ShellTrim_South", new Vector3(0f, shellTrimY, -16.05f), new Vector3(45f, 0.2f, 0.35f), trim);
            EnsureBlock(root, "ShellTrim_East", new Vector3(22.95f, shellTrimY, 0f), new Vector3(0.35f, 0.2f, 31.6f), trim);
            EnsureBlock(root, "ShellTrim_West", new Vector3(-22.95f, shellTrimY, 0f), new Vector3(0.35f, 0.2f, 31.6f), trim);
            EnsureBlock(root, "ShellWall_North", new Vector3(0f, 2.05f, 16.42f), new Vector3(45f, 4.1f, 0.32f), trim * 0.82f);
            EnsureBlock(root, "ShellWall_South", new Vector3(0f, 2.05f, -16.42f), new Vector3(45f, 4.1f, 0.32f), trim * 0.82f);
            EnsureBlock(root, "ShellWall_East", new Vector3(23.32f, 2.05f, 0f), new Vector3(0.32f, 4.1f, 31.6f), trim * 0.82f);
            EnsureBlock(root, "ShellWall_West", new Vector3(-23.32f, 2.05f, 0f), new Vector3(0.32f, 4.1f, 31.6f), trim * 0.82f);

            EnsureBlock(root, "Sightline_Frame_North", new Vector3(0f, 2.9f, 15.9f), new Vector3(7.4f, 1.6f, 0.12f), window, true);
            EnsureBlock(root, "Sightline_Frame_South", new Vector3(0f, 2.9f, -15.9f), new Vector3(7.4f, 1.6f, 0.12f), window, true);
        }

        private void ApplyCoreVariant(Transform root)
        {
            Color color = new Color(0.18f, 0.23f, 0.27f);
            EnsureBlock(root, "Core_ObservationDivider_West", new Vector3(-7f, 1.2f, 13.2f), new Vector3(1.25f, 2.4f, 0.3f), color);
            EnsureBlock(root, "Core_ObservationDivider_East", new Vector3(7f, 1.2f, -13.2f), new Vector3(1.25f, 2.4f, 0.3f), color);
            EnsureBlock(root, "Core_CeilingBeam_Fore", new Vector3(0f, 3.55f, 12.5f), new Vector3(18f, 0.22f, 0.4f), color * 1.05f);
            EnsureBlock(root, "Core_CeilingBeam_Aft", new Vector3(0f, 3.55f, -12.5f), new Vector3(18f, 0.22f, 0.4f), color * 1.05f);
        }

        private void ApplyAnnexVariant(Transform root)
        {
            Color annex = new Color(0.20f, 0.24f, 0.29f);
            EnsureBlock(root, "Annex_WestFloor", new Vector3(-19.1f, 0.1f, 0f), new Vector3(5.3f, 0.18f, 9f), annex * 0.95f);
            EnsureBlock(root, "Annex_WestNorth", new Vector3(-21.7f, 2f, 0f), new Vector3(0.32f, 4f, 9f), annex);
            EnsureBlock(root, "Annex_WestSouth", new Vector3(-16.45f, 2f, 0f), new Vector3(0.32f, 4f, 9f), annex);
            EnsureBlock(root, "Annex_WestRoof", new Vector3(-19.1f, 4.1f, 0f), new Vector3(5.3f, 0.22f, 9f), annex * 0.9f);
            EnsureBlock(root, "Annex_WestConnectorRoof", new Vector3(-17.4f, 3.92f, 0f), new Vector3(1.9f, 0.18f, 3.2f), annex * 0.9f);

            EnsureBlock(root, "Annex_EastObservationFloor", new Vector3(19.2f, 0.1f, 0f), new Vector3(4.9f, 0.18f, 6.8f), annex * 0.96f);
            EnsureBlock(root, "Annex_EastObservationRoof", new Vector3(19.2f, 4.05f, 0f), new Vector3(4.9f, 0.2f, 6.8f), annex * 0.88f);
            EnsureBlock(root, "Annex_EastObservationGlass", new Vector3(21.2f, 2.15f, 0f), new Vector3(0.16f, 3.8f, 6.8f), new Color(0.10f, 0.25f, 0.30f, 0.75f));
            EnsureBlock(root, "Annex_CenterBeam", new Vector3(0f, 3.55f, 0f), new Vector3(26f, 0.22f, 0.4f), new Color(0.15f, 0.18f, 0.22f));
        }

        private void ApplyOverflowVariant(Transform root)
        {
            Color overflow = new Color(0.19f, 0.22f, 0.26f);
            EnsureBlock(root, "Overflow_ForeCanopy", new Vector3(0f, 4.2f, 13.3f), new Vector3(17.5f, 0.18f, 2.2f), overflow * 0.88f);
            EnsureBlock(root, "Overflow_AftCanopy", new Vector3(0f, 4.2f, -13.3f), new Vector3(17.5f, 0.18f, 2.2f), overflow * 0.88f);
            EnsureBlock(root, "Overflow_WestSideCanopy", new Vector3(-14f, 4.0f, 0f), new Vector3(4.8f, 0.18f, 23f), overflow * 0.90f);
            EnsureBlock(root, "Overflow_EastSideCanopy", new Vector3(14f, 4.0f, 0f), new Vector3(4.8f, 0.18f, 23f), overflow * 0.90f);
            EnsureBlock(root, "Overflow_Bulkhead_West", new Vector3(-4.2f, 1.25f, 0f), new Vector3(0.28f, 2.5f, 6f), overflow);
            EnsureBlock(root, "Overflow_Bulkhead_East", new Vector3(4.2f, 1.25f, 0f), new Vector3(0.28f, 2.5f, 6f), overflow);
            EnsureBlock(root, "Overflow_CrossBrace_1", new Vector3(0f, 3.45f, 5.5f), new Vector3(8f, 0.2f, 0.35f), overflow * 1.08f);
            EnsureBlock(root, "Overflow_CrossBrace_2", new Vector3(0f, 3.45f, -5.5f), new Vector3(8f, 0.2f, 0.35f), overflow * 1.08f);
        }

        private void EnsureSkylightRoof(Transform root, string name, Vector3 center, Vector3 size, float skylightWidth, bool splitZ, Color color)
        {
            if (splitZ)
            {
                float segment = Mathf.Max(0.5f, (size.z - skylightWidth) * 0.5f);
                float offset = skylightWidth * 0.5f + segment * 0.5f;
                EnsureBlock(root, name + "_A", center + new Vector3(0f, 0f, -offset), new Vector3(size.x, size.y, segment), color);
                EnsureBlock(root, name + "_B", center + new Vector3(0f, 0f, offset), new Vector3(size.x, size.y, segment), color);
                EnsureBlock(root, name + "_Glass", center, new Vector3(size.x, skylightPaneThickness, skylightWidth), new Color(0.22f, 0.46f, 0.62f, 0.38f), true);
            }
            else
            {
                float segment = Mathf.Max(0.5f, (size.x - skylightWidth) * 0.5f);
                float offset = skylightWidth * 0.5f + segment * 0.5f;
                EnsureBlock(root, name + "_A", center + new Vector3(-offset, 0f, 0f), new Vector3(segment, size.y, size.z), color);
                EnsureBlock(root, name + "_B", center + new Vector3(offset, 0f, 0f), new Vector3(segment, size.y, size.z), color);
                EnsureBlock(root, name + "_Glass", center, new Vector3(skylightWidth, skylightPaneThickness, size.z), new Color(0.22f, 0.46f, 0.62f, 0.38f), true);
            }
        }

        private void EnsureSolidRoof(Transform root, string name, Vector3 center, Vector3 size, Color color)
        {
            EnsureBlock(root, name, center, size, color);
        }

        private static void EnsureBlock(Transform root, string name, Vector3 position, Vector3 scale, Color color, bool transparent = false)
        {
            Transform existing = root.Find(name);
            GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (go.transform.parent != root)
            {
                go.transform.SetParent(root, false);
            }

            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            go.isStatic = true;

            Collider colliderRef = go.GetComponent<Collider>();
            if (colliderRef == null)
            {
                colliderRef = go.AddComponent<BoxCollider>();
            }
            colliderRef.isTrigger = false;

            Renderer rendererRef = go.GetComponent<Renderer>();
            if (rendererRef != null)
            {
                Material material = rendererRef.sharedMaterial;
                if (material == null || material.shader == null || material.shader.name != "Standard")
                {
                    material = new Material(Shader.Find("Standard"));
                    rendererRef.sharedMaterial = material;
                }

                material.color = color;
                if (transparent)
                {
                    material.SetFloat("_Mode", 3f);
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                if (material.HasProperty("_Glossiness"))
                {
                    material.SetFloat("_Glossiness", transparent ? 0.38f : 0.08f);
                }

                rendererRef.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                rendererRef.receiveShadows = true;
            }
        }
    }
}
