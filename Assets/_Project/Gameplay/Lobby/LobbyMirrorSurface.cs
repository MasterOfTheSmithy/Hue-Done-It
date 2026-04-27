// File: Assets/_Project/Gameplay/Lobby/LobbyMirrorSurface.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Lobby
{
    // This creates a simple client-side mirror in Lobby so players can inspect their chosen color and cosmetics.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class LobbyMirrorSurface : MonoBehaviour
    {
        [SerializeField] private Vector2Int textureSize = new Vector2Int(1024, 1024);
        [SerializeField] private float nearClipPlane = 0.03f;
        [SerializeField] private float farClipPlane = 200f;
        [SerializeField] private LayerMask mirrorCullingMask = ~0;

        private Camera _mirrorCamera;
        private RenderTexture _renderTexture;
        private Material _runtimeMaterial;
        private Renderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            EnsureMirrorResources();
        }

        private void LateUpdate()
        {
            if (SceneManager.GetActiveScene().name != "Lobby")
            {
                return;
            }

            Camera sourceCamera = Camera.main;
            if (sourceCamera == null)
            {
                return;
            }

            EnsureMirrorResources();
            SyncMirrorCamera(sourceCamera);
            _mirrorCamera.Render();
        }

        private void OnDisable()
        {
            if (_mirrorCamera != null)
            {
                Object.Destroy(_mirrorCamera.gameObject);
                _mirrorCamera = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Object.Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_runtimeMaterial != null)
            {
                Object.Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        private void EnsureMirrorResources()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<Renderer>();
            }

            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(textureSize.x, textureSize.y, 24)
                {
                    name = "LobbyMirrorRT",
                    antiAliasing = 2
                };
            }

            if (_mirrorCamera == null)
            {
                GameObject cameraObject = new GameObject("LobbyMirrorCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                _mirrorCamera = cameraObject.AddComponent<Camera>();
                _mirrorCamera.enabled = false;
                _mirrorCamera.nearClipPlane = nearClipPlane;
                _mirrorCamera.farClipPlane = farClipPlane;
                _mirrorCamera.targetTexture = _renderTexture;
                _mirrorCamera.allowHDR = false;
                _mirrorCamera.allowMSAA = true;
            }

            if (_runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }

                _runtimeMaterial = new Material(shader);
                _renderer.material = _runtimeMaterial;
            }

            _runtimeMaterial.mainTexture = _renderTexture;
        }

        private void SyncMirrorCamera(Camera sourceCamera)
        {
            _mirrorCamera.CopyFrom(sourceCamera);
            _mirrorCamera.enabled = false;
            _mirrorCamera.targetTexture = _renderTexture;
            _mirrorCamera.cullingMask = mirrorCullingMask;

            Vector3 mirrorNormal = transform.forward;
            Vector3 mirrorPosition = transform.position;
            Vector3 reflectedPosition = ReflectPoint(sourceCamera.transform.position, mirrorPosition, mirrorNormal);
            Vector3 reflectedForward = Vector3.Reflect(sourceCamera.transform.forward, mirrorNormal);
            Vector3 reflectedUp = Vector3.Reflect(sourceCamera.transform.up, mirrorNormal);

            _mirrorCamera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));
        }

        private static Vector3 ReflectPoint(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
        {
            Vector3 offset = point - planePoint;
            float distance = Vector3.Dot(offset, planeNormal);
            return point - (2f * distance * planeNormal);
        }
    }
}
