// File: Assets/_Project/Gameplay/Lobby/NetworkLobbyState.cs
using System.Collections.Generic;
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Gameplay.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Lobby
{
    // This is the authoritative lobby state machine used only in Lobby scene.
    // It owns map selection, CPU roster targets, and match start transitions.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkLobbyState : NetworkBehaviour
    {
        private const string LobbySceneName = "Lobby";
        private const string DefaultGameplayScene = "Gameplay_Undertint";

        private readonly NetworkVariable<FixedString64Bytes> _selectedMapScene =
            new(new FixedString64Bytes(DefaultGameplayScene), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _targetCpuCount =
            new(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly List<NetworkObject> _spawnedCpuObjects = new();

        public string SelectedMapScene => _selectedMapScene.Value.ToString();
        public int TargetCpuCount => _targetCpuCount.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }

            _selectedMapScene.Value = new FixedString64Bytes(BootSessionConfig.SelectedMapScene);
            _targetCpuCount.Value = Mathf.Clamp(BootSessionConfig.RequestedCpuCount, 0, 7);
            ReconcileCpuRoster();
        }

        private void Update()
        {
            if (!IsServer || SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            ReconcileCpuRoster();
        }

        public void ServerSelectMap(string mapScene)
        {
            if (!IsServer)
            {
                return;
            }

            string validated = string.IsNullOrWhiteSpace(mapScene) ? DefaultGameplayScene : mapScene.Trim();
            _selectedMapScene.Value = new FixedString64Bytes(validated);
            BootSessionConfig.SelectedMapScene = validated;
            BootSessionConfig.Save();
        }

        public void ServerSetCpuCount(int target)
        {
            if (!IsServer)
            {
                return;
            }

            _targetCpuCount.Value = Mathf.Clamp(target, 0, 7);
            BootSessionConfig.RequestedCpuCount = _targetCpuCount.Value;
            BootSessionConfig.Save();
            ReconcileCpuRoster();
        }

        public void ServerStartMatch()
        {
            if (!IsServer)
            {
                return;
            }

            string map = SelectedMapScene;
            if (string.IsNullOrWhiteSpace(map))
            {
                map = DefaultGameplayScene;
            }

            if (!Application.CanStreamedLevelBeLoaded(map))
            {
                Debug.LogError($"NetworkLobbyState: cannot start match because scene '{map}' is missing from Build Settings.");
                return;
            }

            if (NetworkManager == null)
            {
                return;
            }

            if (!NetworkManager.NetworkConfig.EnableSceneManagement)
            {
                SceneManager.LoadScene(map, LoadSceneMode.Single);
                return;
            }

            NetworkManager.SceneManager.LoadScene(map, LoadSceneMode.Single);
        }

        private void ReconcileCpuRoster()
        {
            _spawnedCpuObjects.RemoveAll(cpu => cpu == null || !cpu.IsSpawned);

            int target = Mathf.Clamp(_targetCpuCount.Value, 0, 7);
            while (_spawnedCpuObjects.Count > target)
            {
                NetworkObject cpu = _spawnedCpuObjects[_spawnedCpuObjects.Count - 1];
                _spawnedCpuObjects.RemoveAt(_spawnedCpuObjects.Count - 1);
                if (cpu != null && cpu.IsSpawned)
                {
                    cpu.Despawn(true);
                }
            }

            if (_spawnedCpuObjects.Count >= target)
            {
                return;
            }

            GameObject playerPrefab = NetworkManager != null && NetworkManager.NetworkConfig != null
                ? NetworkManager.NetworkConfig.PlayerPrefab
                : null;

            if (playerPrefab == null)
            {
                Debug.LogWarning("NetworkLobbyState: cannot spawn CPU because NetworkConfig.PlayerPrefab is missing.");
                return;
            }

            List<Transform> spawnPoints = LobbySceneInstaller.CollectLobbySpawnPoints();
            for (int i = _spawnedCpuObjects.Count; i < target; i++)
            {
                Transform spawnPoint = spawnPoints.Count > 0 ? spawnPoints[i % spawnPoints.Count] : null;
                Vector3 position = spawnPoint != null ? spawnPoint.position : new Vector3(2f + i, 1f, -2f - i);
                Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

                GameObject cpuObject = Instantiate(playerPrefab, position, rotation);
                cpuObject.name = $"Lobby_CPU_{i + 1:00}";

                if (cpuObject.GetComponent<SimpleCpuOpponentAgent>() == null)
                {
                    cpuObject.AddComponent<SimpleCpuOpponentAgent>();
                }

                NetworkObject networkObject = cpuObject.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Destroy(cpuObject);
                    continue;
                }

                if (!networkObject.IsSpawned)
                {
                    networkObject.Spawn(true);
                }

                _spawnedCpuObjects.Add(networkObject);
            }
        }
    }
}
