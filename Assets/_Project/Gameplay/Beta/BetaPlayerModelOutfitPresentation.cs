// File: Assets/_Project/Gameplay/Beta/BetaPlayerModelOutfitPresentation.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Runtime outfit/model rig. It loads GLB-imported prefabs from Resources/HueDoneItPlayerModels when Unity can
    /// import them, maps the replicated player color onto the outfit materials, and swaps to a white/blue reveal
    /// presentation when a Bleach/Peroxide player is exposed by attacking or venting.
    /// </summary>
    [DefaultExecutionOrder(640)]
    [DisallowMultipleComponent]
    public sealed class BetaPlayerModelOutfitPresentation : MonoBehaviour
    {
        private const string RigRootName = "__BetaPlayerOutfitRig";
        private const string ModelResourcesFolder = "HueDoneItPlayerModels/";

        private static readonly string[] OutfitModelNames =
        {
            "Hy3D_textured_00021_",
            "Hy3D_textured_00022_",
            "Hy3D_textured_00023_",
            "Hy3D_textured_00024_",
            "Hy3D_textured_00025_",
            "Hy3D_textured_00030_"
        };

        // This uploaded model has the lightest source texture and is used for the exposed peroxide look.
        private const string RevealedPeroxideModelName = "Hy3D_textured_00026_";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Min(0.25f)] private float targetHeight = 1.55f;
        [SerializeField, Min(0f)] private float bobAmount = 0.035f;
        [SerializeField, Min(0f)] private float squashAmount = 0.10f;
        [SerializeField, Min(0f)] private float leanDegrees = 7.5f;

        private NetworkPlayerAvatar _avatar;
        private NetworkObject _networkObject;
        private PlayerColorProfile _colorProfile;
        private PlayerKillInputController _role;
        private PlayerLifeState _life;
        private NetworkPlayerAuthoritativeMover _mover;
        private SimpleCpuOpponentAgent _cpu;

        private Transform _rigRoot;
        private GameObject _currentModel;
        private string _currentModelName;
        private Renderer[] _modelRenderers;
        private MaterialPropertyBlock _block;
        private Light _revealLight;
        private Transform _eyeLeft;
        private Transform _eyeRight;
        private Vector3 _baseRigLocalPosition;
        private Vector3 _previousVelocity;
        private bool _legacyHidden;

        private void Awake()
        {
            _avatar = GetComponent<NetworkPlayerAvatar>();
            _networkObject = GetComponent<NetworkObject>();
            _colorProfile = GetComponent<PlayerColorProfile>();
            _role = GetComponent<PlayerKillInputController>();
            _life = GetComponent<PlayerLifeState>();
            _mover = GetComponent<NetworkPlayerAuthoritativeMover>();
            _cpu = GetComponent<SimpleCpuOpponentAgent>();
            _block = new MaterialPropertyBlock();
            EnsureRig();
        }

        private void LateUpdate()
        {
            EnsureRig();
            HideLegacyPresentation();
            string desiredModel = ResolveModelName();
            if (_currentModel == null || _currentModelName != desiredModel)
            {
                LoadModel(desiredModel);
            }

            AnimateRig(Time.deltaTime);
            ApplyMaterialState();
        }

        private void EnsureRig()
        {
            if (_rigRoot != null)
            {
                return;
            }

            Transform existing = transform.Find(RigRootName);
            if (existing != null)
            {
                _rigRoot = existing;
            }
            else
            {
                GameObject root = new GameObject(RigRootName);
                root.transform.SetParent(transform, false);
                _rigRoot = root.transform;
            }

            _baseRigLocalPosition = new Vector3(0f, 0.15f, 0f);
            _rigRoot.localPosition = _baseRigLocalPosition;
            _rigRoot.localRotation = Quaternion.identity;
            _rigRoot.localScale = Vector3.one;
        }

        private string ResolveModelName()
        {
            if (_role != null && _role.CurrentRole == PlayerRole.Bleach && _role.IsBleachExposed)
            {
                return RevealedPeroxideModelName;
            }

            ulong stableId;
            if (_cpu != null)
            {
                stableId = _networkObject != null && _networkObject.IsSpawned
                    ? _networkObject.NetworkObjectId
                    : (ulong)Mathf.Abs(GetInstanceID());
            }
            else
            {
                stableId = _avatar != null ? _avatar.OwnerClientId : 0UL;
            }

            int index = Mathf.Abs((int)(stableId % (ulong)OutfitModelNames.Length));
            if (_cpu != null && OutfitModelNames.Length > 1)
            {
                index = (index + 1) % OutfitModelNames.Length;
            }

            return OutfitModelNames[index];
        }

        private void LoadModel(string modelName)
        {
            if (_currentModel != null)
            {
                Destroy(_currentModel);
                _currentModel = null;
            }

            _currentModelName = modelName;
            GameObject prefab = Resources.Load<GameObject>(ModelResourcesFolder + modelName);
            if (prefab != null)
            {
                _currentModel = Instantiate(prefab, _rigRoot);
                _currentModel.name = "OutfitModel_" + modelName;
            }
            else
            {
                _currentModel = BuildFallbackModel(modelName);
            }

            _currentModel.transform.localPosition = Vector3.zero;
            _currentModel.transform.localRotation = Quaternion.identity;
            _currentModel.transform.localScale = Vector3.one;
            StripModelColliders(_currentModel);
            FitModelToTargetHeight(_currentModel);
            _modelRenderers = _currentModel.GetComponentsInChildren<Renderer>(true);
            EnsureRevealAccents();
        }

        private GameObject BuildFallbackModel(string modelName)
        {
            GameObject root = new GameObject("FallbackOutfit_" + modelName);
            root.transform.SetParent(_rigRoot, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "FallbackBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            body.transform.localScale = new Vector3(0.68f, 0.68f, 0.68f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "FallbackHead";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.38f, 0f);
            head.transform.localScale = new Vector3(0.46f, 0.32f, 0.46f);

            StripModelColliders(root);
            return root;
        }

        private void FitModelToTargetHeight(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            if (bounds.size.y > 0.001f)
            {
                float scale = Mathf.Clamp(targetHeight / bounds.size.y, 0.015f, 25f);
                model.transform.localScale *= scale;
            }

            renderers = model.GetComponentsInChildren<Renderer>(true);
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 localCenter = _rigRoot.InverseTransformPoint(bounds.center);
            float localBottom = _rigRoot.InverseTransformPoint(bounds.min).y;
            model.transform.localPosition += new Vector3(-localCenter.x, -localBottom, -localCenter.z);
        }

        private void StripModelColliders(GameObject model)
        {
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }
        }

        private void HideLegacyPresentation()
        {
            if (_legacyHidden && Time.frameCount % 60 != 0)
            {
                return;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rendererRef = renderers[i];
                if (rendererRef == null || rendererRef.transform.IsChildOf(_rigRoot))
                {
                    continue;
                }

                string lower = rendererRef.name.ToLowerInvariant();
                if (rendererRef.transform == transform ||
                    lower.Contains("bodyplaceholder") ||
                    lower.Contains("hatplaceholder") ||
                    lower.Contains("jellybody") ||
                    lower.Contains("jellyhighlight") ||
                    lower.Contains("squishshadow") ||
                    rendererRef.transform.root == transform)
                {
                    rendererRef.enabled = false;
                }
            }

            _legacyHidden = true;
        }

        private void AnimateRig(float deltaTime)
        {
            if (_rigRoot == null || _mover == null)
            {
                return;
            }

            Vector3 velocity = _mover.CurrentVelocity;
            Vector3 planarVelocity = velocity;
            planarVelocity.y = 0f;
            float speed01 = Mathf.Clamp01(planarVelocity.magnitude / 9f);
            float vertical01 = Mathf.Clamp01(Mathf.Abs(velocity.y) / 12f);
            float accel01 = deltaTime > 0.0001f ? Mathf.Clamp01((velocity - _previousVelocity).magnitude / (deltaTime * 18f)) : 0f;
            _previousVelocity = velocity;

            bool alive = _life == null || _life.IsAlive;
            bool exposed = _role != null && _role.CurrentRole == PlayerRole.Bleach && _role.IsBleachExposed;

            Vector3 targetScale = Vector3.one;
            targetScale.x += speed01 * 0.06f + accel01 * squashAmount * 0.35f;
            targetScale.z += speed01 * 0.06f + accel01 * squashAmount * 0.35f;
            targetScale.y -= accel01 * squashAmount;
            targetScale.y += vertical01 * 0.08f;

            if (!alive)
            {
                targetScale = new Vector3(1.45f, 0.18f, 1.45f);
            }
            else if (exposed)
            {
                targetScale += Vector3.one * (Mathf.Sin(Time.time * 12f) * 0.035f);
            }

            Vector3 bob = Vector3.up * (Mathf.Sin(Time.time * 5.4f) * bobAmount * (alive ? 1f : 0f));
            _rigRoot.localPosition = Vector3.Lerp(_rigRoot.localPosition, _baseRigLocalPosition + bob, 1f - Mathf.Exp(-12f * deltaTime));
            _rigRoot.localScale = Vector3.Lerp(_rigRoot.localScale, targetScale, 1f - Mathf.Exp(-14f * deltaTime));

            Quaternion targetRotation = Quaternion.identity;
            if (planarVelocity.sqrMagnitude > 0.05f)
            {
                Vector3 localVelocity = transform.InverseTransformDirection(planarVelocity.normalized);
                targetRotation = Quaternion.Euler(localVelocity.z * -leanDegrees * speed01, 0f, localVelocity.x * -leanDegrees * speed01);
            }

            _rigRoot.localRotation = Quaternion.Slerp(_rigRoot.localRotation, targetRotation, 1f - Mathf.Exp(-12f * deltaTime));
        }

        private void ApplyMaterialState()
        {
            if (_modelRenderers == null)
            {
                return;
            }

            bool alive = _life == null || _life.IsAlive;
            bool exposed = _role != null && _role.CurrentRole == PlayerRole.Bleach && _role.IsBleachExposed;
            Color playerColor = _colorProfile != null ? _colorProfile.PlayerColor : Color.white;
            Color targetColor = exposed
                ? Color.white
                : (alive ? playerColor : Color.Lerp(playerColor, Color.gray, 0.65f));

            for (int i = 0; i < _modelRenderers.Length; i++)
            {
                Renderer rendererRef = _modelRenderers[i];
                if (rendererRef == null)
                {
                    continue;
                }

                _block ??= new MaterialPropertyBlock();
                rendererRef.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, targetColor);
                _block.SetColor(ColorId, targetColor);
                _block.SetColor(EmissionColorId, exposed ? new Color(0.05f, 0.72f, 1f, 1f) * 1.35f : Color.black);
                rendererRef.SetPropertyBlock(_block);
            }

            if (_revealLight != null)
            {
                _revealLight.enabled = exposed;
                _revealLight.intensity = exposed ? 2.2f + Mathf.Sin(Time.time * 9f) * 0.55f : 0f;
            }

            SetEyeActive(_eyeLeft, exposed);
            SetEyeActive(_eyeRight, exposed);
        }

        private void EnsureRevealAccents()
        {
            if (_eyeLeft != null && _eyeRight != null && _revealLight != null)
            {
                return;
            }

            GameObject left = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            left.name = "RevealedBlueEye_L";
            left.transform.SetParent(_rigRoot, false);
            left.transform.localPosition = new Vector3(-0.16f, 1.23f, 0.42f);
            left.transform.localScale = Vector3.one * 0.075f;
            StripModelColliders(left);
            ApplyEyeMaterial(left);
            _eyeLeft = left.transform;

            GameObject right = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            right.name = "RevealedBlueEye_R";
            right.transform.SetParent(_rigRoot, false);
            right.transform.localPosition = new Vector3(0.16f, 1.23f, 0.42f);
            right.transform.localScale = Vector3.one * 0.075f;
            StripModelColliders(right);
            ApplyEyeMaterial(right);
            _eyeRight = right.transform;

            _revealLight = _rigRoot.gameObject.GetComponent<Light>();
            if (_revealLight == null)
            {
                _revealLight = _rigRoot.gameObject.AddComponent<Light>();
            }

            _revealLight.type = LightType.Point;
            _revealLight.range = 6.5f;
            _revealLight.color = new Color(0.05f, 0.82f, 1f, 1f);

            SetEyeActive(_eyeLeft, false);
            SetEyeActive(_eyeRight, false);
            _revealLight.enabled = false;
        }

        private static void ApplyEyeMaterial(GameObject eye)
        {
            Renderer rendererRef = eye.GetComponent<Renderer>();
            if (rendererRef == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = "PeroxideRevealEyeBlue";
            material.color = new Color(0.03f, 0.68f, 1f, 1f);
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, material.color);
            }
            if (material.HasProperty(EmissionColorId))
            {
                material.SetColor(EmissionColorId, new Color(0.03f, 0.68f, 1f, 1f) * 2.25f);
            }

            rendererRef.sharedMaterial = material;
        }

        private static void SetEyeActive(Transform eye, bool active)
        {
            if (eye != null && eye.gameObject.activeSelf != active)
            {
                eye.gameObject.SetActive(active);
            }
        }
    }
}
