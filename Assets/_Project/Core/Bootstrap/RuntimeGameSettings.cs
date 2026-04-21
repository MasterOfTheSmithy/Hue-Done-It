// File: Assets/_Project/Core/Bootstrap/RuntimeGameSettings.cs
using UnityEngine;

namespace HueDoneIt.Core.Bootstrap
{
    public static class RuntimeGameSettings
    {
        private const string MasterVolumeKey = "HueDoneIt.Settings.MasterVolume";
        private const string MusicVolumeKey = "HueDoneIt.Settings.MusicVolume";
        private const string SfxVolumeKey = "HueDoneIt.Settings.SfxVolume";
        private const string SensitivityKey = "HueDoneIt.Settings.LookSensitivity";
        private const string WindowModeKey = "HueDoneIt.Settings.WindowMode";

        public static float MasterVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 0.9f));
            set => PlayerPrefs.SetFloat(MasterVolumeKey, Mathf.Clamp01(value));
        }

        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 0.75f));
            set => PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
        }

        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 0.8f));
            set => PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
        }

        public static float LookSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 0.12f), 0.02f, 1.2f);
            set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.02f, 1.2f));
        }

        public static FullScreenMode WindowMode
        {
            get => (FullScreenMode)Mathf.Clamp(PlayerPrefs.GetInt(WindowModeKey, (int)FullScreenMode.FullScreenWindow), 0, 3);
            set => PlayerPrefs.SetInt(WindowModeKey, (int)value);
        }

        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
            Screen.fullScreenMode = WindowMode;
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
