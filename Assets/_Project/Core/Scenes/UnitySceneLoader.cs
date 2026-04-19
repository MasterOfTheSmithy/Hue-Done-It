// File: Assets/_Project/Core/Scenes/UnitySceneLoader.cs
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Core.Scenes
{
    public sealed class UnitySceneLoader : ISceneLoader
    {
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be null or empty.", nameof(sceneName));
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                throw new InvalidOperationException($"Scene '{sceneName}' is not available. Add it to Build Settings.");
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
            if (operation == null)
            {
                throw new InvalidOperationException($"Failed to start loading scene '{sceneName}'.");
            }

            return operation;
        }
    }
}
