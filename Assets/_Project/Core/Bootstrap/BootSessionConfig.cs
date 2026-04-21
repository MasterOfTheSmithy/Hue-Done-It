// File: Assets/_Project/Core/Bootstrap/BootSessionConfig.cs
using UnityEngine;

namespace HueDoneIt.Core.Bootstrap
{
    public static class BootSessionConfig
    {
        private const string CpuCountKey = "HueDoneIt.Lobby.CpuCount";
        private const string HatKey = "HueDoneIt.Cosmetic.Hat";
        private const string OutfitKey = "HueDoneIt.Cosmetic.Outfit";
        private const string BodyColorKey = "HueDoneIt.Cosmetic.BodyColor";

        public static int RequestedCpuCount
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(CpuCountKey, 1), 0, 7);
            set => PlayerPrefs.SetInt(CpuCountKey, Mathf.Clamp(value, 0, 7));
        }

        public static int SelectedHat
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(HatKey, 0), 0, 5);
            set => PlayerPrefs.SetInt(HatKey, Mathf.Clamp(value, 0, 5));
        }

        public static int SelectedOutfit
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(OutfitKey, 0), 0, 5);
            set => PlayerPrefs.SetInt(OutfitKey, Mathf.Clamp(value, 0, 5));
        }

        public static Color SelectedBodyColor
        {
            get
            {
                string html = PlayerPrefs.GetString(BodyColorKey, "#FF6A8C");
                return ColorUtility.TryParseHtmlString(html, out Color parsed) ? parsed : new Color(1f, 0.42f, 0.55f);
            }
            set
            {
                string html = $"#{ColorUtility.ToHtmlStringRGB(value)}";
                PlayerPrefs.SetString(BodyColorKey, html);
            }
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
