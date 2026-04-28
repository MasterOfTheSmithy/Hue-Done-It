// File: Assets/_Project/Gameplay/Lobby/NetworkLobbyState.cs
using System.Collections.Generic;
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Gameplay.Beta;
using HueDoneIt.Gameplay.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Lobby
{
    // This is the authoritative lobby state machine used only in Lobby scene.
    // It owns map voting, CPU roster targets, and match start transitions.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkLobbyState : NetworkBehaviour
    {
        private const string LobbySceneName = "Lobby";
        private const string DefaultGameplayScene = BetaGameplaySceneCatalog.MainMap;
        private static readonly string[] SupportedMaps = BetaGameplaySceneCatalog.LobbySelectableMaps;

        private readonly NetworkVariable<FixedString64Bytes> _selectedMapScene =
            new NetworkVariable<FixedString64Bytes>(HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(DefaultGameplayScene), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _targetCpuCount =
            new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _undertintVotes =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _undertintAnnexVotes =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _undertintOverflowVotes =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _testFloodVotes =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _testTasksVotes =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly List<NetworkObject> _spawnedCpuObjects = new List<NetworkObject>();
        private readonly Dictionary<ulong, string> _playerVotes = new Dictionary<ulong, string>();

        public string SelectedMapScene => _selectedMapScene.Value.ToString();
        public int TargetCpuCount => _targetCpuCount.Value;
        public int UndertintVotes => _undertintVotes.Value;
        public int UndertintAnnexVotes => _undertintAnnexVotes.Value;
        public int UndertintOverflowVotes => _undertintOverflowVotes.Value;
        public int TestFloodVotes => _testFloodVotes.Value;
        public int TestTasksVotes => _testTasksVotes.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }

            string initialMap = string.IsNullOrWhiteSpace(BootSessionConfig.SelectedMapScene)
                ? DefaultGameplayScene
                : BootSessionConfig.SelectedMapScene;
            initialMap = ValidateMap(initialMap);
            _selectedMapScene.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(initialMap);
            _targetCpuCount.Value = Mathf.Clamp(BootSessionConfig.RequestedCpuCount, 0, 7);
            ReconcileVoteTallies();
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

        public void ServerVoteForMap(ulong clientId, string mapScene)
        {
            if (!IsServer)
            {
                return;
            }

            string validated = ValidateMap(mapScene);
            _playerVotes[clientId] = validated;
            ReconcileVoteTallies();
        }

        public void ServerSelectMap(string mapScene)
        {
            // Preserve older call sites by routing them into the vote system.
            if (!IsServer)
            {
                return;
            }

            ServerVoteForMap(NetworkManager != null ? NetworkManager.LocalClientId : 0UL, mapScene);
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

            string map = ValidateMap(SelectedMapScene);
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

        private void ReconcileVoteTallies()
        {
            int undertintVotes = 0;
            int undertintAnnexVotes = 0;
            int undertintOverflowVotes = 0;

            foreach (KeyValuePair<ulong, string> pair in _playerVotes)
            {
                string vote = ValidateMap(pair.Value);
                if (vote == BetaGameplaySceneCatalog.MainMap) undertintVotes++;
                else if (vote == BetaGameplaySceneCatalog.AnnexMap) undertintAnnexVotes++;
                else if (vote == BetaGameplaySceneCatalog.OverflowMap) undertintOverflowVotes++;
            }

            if (_playerVotes.Count == 0)
            {
                undertintVotes = 1;
            }

            _undertintVotes.Value = undertintVotes;
            _undertintAnnexVotes.Value = undertintAnnexVotes;
            _undertintOverflowVotes.Value = undertintOverflowVotes;
            _testFloodVotes.Value = 0;
            _testTasksVotes.Value = 0;

            string selected = BetaGameplaySceneCatalog.MainMap;
            int bestVotes = undertintVotes;
            if (undertintAnnexVotes > bestVotes)
            {
                selected = BetaGameplaySceneCatalog.AnnexMap;
                bestVotes = undertintAnnexVotes;
            }

            if (undertintOverflowVotes > bestVotes)
            {
                selected = BetaGameplaySceneCatalog.OverflowMap;
                bestVotes = undertintOverflowVotes;
            }

            _selectedMapScene.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(selected);
            BootSessionConfig.SelectedMapScene = selected;
            BootSessionConfig.Save();
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

                GameObject cpuObject = Object.Instantiate(playerPrefab, position, rotation);
                cpuObject.name = $"Lobby_CPU_{i + 1:00}";

                if (cpuObject.GetComponent<SimpleCpuOpponentAgent>() == null)
                {
                    cpuObject.AddComponent<SimpleCpuOpponentAgent>();
                }

                NetworkObject networkObject = cpuObject.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Object.Destroy(cpuObject);
                    continue;
                }

                if (!networkObject.IsSpawned)
                {
                    networkObject.Spawn(true);
                }

                _spawnedCpuObjects.Add(networkObject);
            }
        }

        private static string ValidateMap(string mapScene)
        {
            if (string.IsNullOrWhiteSpace(mapScene))
            {
                return DefaultGameplayScene;
            }

            string trimmed = mapScene.Trim();
            for (int i = 0; i < SupportedMaps.Length; i++)
            {
                if (trimmed == SupportedMaps[i])
                {
                    return trimmed;
                }
            }

            return DefaultGameplayScene;
        }
    }
}
