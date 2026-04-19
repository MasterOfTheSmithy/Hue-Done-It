// File: Assets/_Project/Core/Bootstrap/AppBootstrap.cs
using HueDoneIt.Core.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Core.Bootstrap
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("Startup")]
        [SerializeField] private bool loadInitialSceneOnStart;
        [SerializeField] private string initialSceneName = "Gameplay_Undertint";

        private ISceneLoader _sceneLoader;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _sceneLoader = new UnitySceneLoader();
        }

        private void Start()
        {
            if (!loadInitialSceneOnStart)
            {
                Debug.Log("AppBootstrap initialized in Boot scene.");
                return;
            }

            LoadInitialScene();
        }

        public void LoadScene(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
        {
            _sceneLoader.LoadSceneAsync(sceneName, loadSceneMode);
        }

        private void LoadInitialScene()
        {
            if (string.IsNullOrWhiteSpace(initialSceneName))
            {
                Debug.LogWarning("Initial scene name is empty. Staying in Boot scene.");
                return;
            }

            _sceneLoader.LoadSceneAsync(initialSceneName, LoadSceneMode.Single);
        }
    }
}
