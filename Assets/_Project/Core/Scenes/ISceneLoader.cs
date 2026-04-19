// File: Assets/_Project/Core/Scenes/ISceneLoader.cs
using UnityEngine.SceneManagement;

namespace HueDoneIt.Core.Scenes
{
    public interface ISceneLoader
    {
        AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single);
    }
}
