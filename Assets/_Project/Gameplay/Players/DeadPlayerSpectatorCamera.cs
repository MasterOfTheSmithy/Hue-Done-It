// File: Assets/_Project/Gameplay/Players/DeadPlayerSpectatorCamera.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Elimination;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Players
{
    [DefaultExecutionOrder(750)]
    public sealed class DeadPlayerSpectatorCamera : MonoBehaviour
    {
        private const string GameplaySceneName = "Gameplay_Undertint";
        private const float FollowEyeHeight = 0.92f;

        [SerializeField, Min(0.1f)] private float freeMoveSpeed = 7f;
        [SerializeField, Min(0.1f)] private float fastFreeMoveSpeed = 14f;
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.11f;
        [SerializeField, Min(0.1f)] private float targetRefreshSeconds = 0.4f;
        [SerializeField, Min(0.1f)] private float followLerpSpeed = 18f;

        private readonly List<NetworkPlayerAvatar> _spectateTargets = new();
        private NetworkPlayerAvatar _localAvatar;
        private PlayerLifeState _localLifeState;
        private Camera _camera;
        private int _targetIndex;
        private bool _freeCamera;
        private bool _wasSpectating;
        private float _yaw;
        private float _pitch;
        private float _nextTargetRefreshTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRuntimeSpectator()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryAttach(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryAttach(scene);
        }

        private static void TryAttach(Scene scene)
        {
            if (!scene.IsValid() || scene.name != GameplaySceneName)
            {
                return;
            }

            if (FindFirstObjectByType<DeadPlayerSpectatorCamera>() != null)
            {
                return;
            }

            GameObject go = new GameObject("DeadPlayerSpectatorCamera");
            DontDestroyOnLoad(go);
            go.AddComponent<DeadPlayerSpectatorCamera>();
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                _wasSpectating = false;
                return;
            }

            BindLocalPlayerIfNeeded();
            bool shouldSpectate = _localAvatar != null && _localLifeState != null && !_localLifeState.IsAlive;
            if (!shouldSpectate)
            {
                _wasSpectating = false;
                return;
            }

            EnsureCamera();
            if (_camera == null)
            {
                return;
            }

            if (!_wasSpectating)
            {
                EnterSpectatorMode();
            }

            RefreshTargetsIfNeeded();
            HandleInput();

            if (_freeCamera || _spectateTargets.Count == 0)
            {
                UpdateFreeCamera();
            }
            else
            {
                UpdateFollowCamera();
            }
        }

        private void BindLocalPlayerIfNeeded()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            _localAvatar = null;
            _localLifeState = null;

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar == null || !avatar.IsSpawned || !avatar.IsOwner || !avatar.IsClient || avatar.TryGetComponent(out SimpleCpuOpponentAgent _))
                {
                    continue;
                }

                _localAvatar = avatar;
                _localLifeState = avatar.GetComponent<PlayerLifeState>();
                break;
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null)
            {
                _camera.enabled = true;
                AudioListener listener = _camera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = true;
                }
                return;
            }

            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject cameraObject = new GameObject("SpectatorMainCamera");
                _camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<AudioListener>();
            }

            _camera.enabled = true;
            AudioListener audioListener = _camera.GetComponent<AudioListener>();
            if (audioListener != null)
            {
                audioListener.enabled = true;
            }
        }

        private void EnterSpectatorMode()
        {
            _wasSpectating = true;
            _freeCamera = false;
            _targetIndex = 0;
            RefreshTargets(force: true);

            Transform cameraTransform = _camera.transform;
            cameraTransform.SetParent(null, true);

            if (_localAvatar != null)
            {
                Vector3 forward = _localAvatar.transform.forward;
                forward.y = 0f;
                _yaw = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward, Vector3.up).eulerAngles.y;
                _pitch = 8f;
                cameraTransform.position = _localAvatar.transform.position + Vector3.up * 1.6f - _localAvatar.transform.forward * 3f;
                cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (keyboard == null)
            {
                return;
            }

            if (keyboard.fKey.wasPressedThisFrame)
            {
                _freeCamera = !_freeCamera;
                if (_freeCamera && _camera != null)
                {
                    Vector3 euler = _camera.transform.eulerAngles;
                    _yaw = euler.y;
                    _pitch = NormalizePitch(euler.x);
                }
            }

            if (keyboard.tabKey.wasPressedThisFrame || keyboard.rightBracketKey.wasPressedThisFrame)
            {
                CycleTarget(1);
            }
            else if (keyboard.leftBracketKey.wasPressedThisFrame)
            {
                CycleTarget(-1);
            }
        }

        private void UpdateFreeCamera()
        {
            if (_camera == null)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
            }

            Transform cameraTransform = _camera.transform;
            cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed) input += Vector3.forward;
            if (keyboard.sKey.isPressed) input += Vector3.back;
            if (keyboard.aKey.isPressed) input += Vector3.left;
            if (keyboard.dKey.isPressed) input += Vector3.right;
            if (keyboard.spaceKey.isPressed) input += Vector3.up;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) input += Vector3.down;

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            float speed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? fastFreeMoveSpeed : freeMoveSpeed;
            Vector3 worldDelta = cameraTransform.TransformDirection(input) * speed * Time.deltaTime;
            cameraTransform.position += worldDelta;
        }

        private void UpdateFollowCamera()
        {
            if (_camera == null || _spectateTargets.Count == 0)
            {
                return;
            }

            _targetIndex = Mathf.Clamp(_targetIndex, 0, _spectateTargets.Count - 1);
            NetworkPlayerAvatar target = _spectateTargets[_targetIndex];
            if (target == null)
            {
                RefreshTargets(force: true);
                return;
            }

            Transform cameraTransform = _camera.transform;
            Vector3 targetPosition = target.transform.position + Vector3.up * FollowEyeHeight;
            Quaternion targetRotation = Quaternion.Euler(0f, target.transform.eulerAngles.y, 0f);

            cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetPosition, followLerpSpeed * Time.deltaTime);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, followLerpSpeed * Time.deltaTime);
            _yaw = cameraTransform.eulerAngles.y;
            _pitch = NormalizePitch(cameraTransform.eulerAngles.x);
        }

        private void CycleTarget(int direction)
        {
            RefreshTargets(force: true);
            if (_spectateTargets.Count == 0)
            {
                _freeCamera = true;
                return;
            }

            _freeCamera = false;
            _targetIndex = (_targetIndex + direction) % _spectateTargets.Count;
            if (_targetIndex < 0)
            {
                _targetIndex += _spectateTargets.Count;
            }
        }

        private void RefreshTargetsIfNeeded()
        {
            if (Time.time >= _nextTargetRefreshTime)
            {
                RefreshTargets(force: false);
            }
        }

        private void RefreshTargets(bool force)
        {
            if (!force && Time.time < _nextTargetRefreshTime)
            {
                return;
            }

            _nextTargetRefreshTime = Time.time + targetRefreshSeconds;
            _spectateTargets.Clear();

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            AddSpectateTargets(avatars, includeCpu: false);
            if (_spectateTargets.Count == 0)
            {
                AddSpectateTargets(avatars, includeCpu: true);
            }

            if (_targetIndex >= _spectateTargets.Count)
            {
                _targetIndex = 0;
            }
        }

        private void AddSpectateTargets(NetworkPlayerAvatar[] avatars, bool includeCpu)
        {
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar == null || !avatar.IsSpawned || avatar == _localAvatar)
                {
                    continue;
                }

                bool isCpu = avatar.TryGetComponent(out SimpleCpuOpponentAgent _);
                if (isCpu && !includeCpu)
                {
                    continue;
                }

                PlayerLifeState lifeState = avatar.GetComponent<PlayerLifeState>();
                if (lifeState == null || !lifeState.IsAlive)
                {
                    continue;
                }

                _spectateTargets.Add(avatar);
            }
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }

        private void OnGUI()
        {
            if (!_wasSpectating || SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                return;
            }

            string mode = _freeCamera || _spectateTargets.Count == 0
                ? "FREE CAMERA"
                : $"FOLLOWING {_targetIndex + 1}/{_spectateTargets.Count}";

            GUI.Box(new Rect(18f, 18f, 420f, 78f), $"SPECTATOR // {mode}\nTab/[ ] cycle players    F free camera    WASD/Space/Ctrl fly    Esc cursor");
        }
    }
}
