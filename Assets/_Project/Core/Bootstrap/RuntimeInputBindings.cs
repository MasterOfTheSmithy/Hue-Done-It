// File: Assets/_Project/Core/Bootstrap/RuntimeInputBindings.cs
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Core.Bootstrap
{
    public static class RuntimeInputBindings
    {
        private const string ForwardKey = "HueDoneIt.Input.Forward";
        private const string BackKey = "HueDoneIt.Input.Back";
        private const string LeftKey = "HueDoneIt.Input.Left";
        private const string RightKey = "HueDoneIt.Input.Right";
        private const string JumpKey = "HueDoneIt.Input.Jump";
        private const string BurstKey = "HueDoneIt.Input.Burst";

        public static Key Forward => (Key)PlayerPrefs.GetInt(ForwardKey, (int)Key.W);
        public static Key Back => (Key)PlayerPrefs.GetInt(BackKey, (int)Key.S);
        public static Key Left => (Key)PlayerPrefs.GetInt(LeftKey, (int)Key.A);
        public static Key Right => (Key)PlayerPrefs.GetInt(RightKey, (int)Key.D);
        public static Key Jump => (Key)PlayerPrefs.GetInt(JumpKey, (int)Key.Space);
        public static Key Burst => (Key)PlayerPrefs.GetInt(BurstKey, (int)Key.LeftShift);

        public static void SetDefaults()
        {
            PlayerPrefs.SetInt(ForwardKey, (int)Key.W);
            PlayerPrefs.SetInt(BackKey, (int)Key.S);
            PlayerPrefs.SetInt(LeftKey, (int)Key.A);
            PlayerPrefs.SetInt(RightKey, (int)Key.D);
            PlayerPrefs.SetInt(JumpKey, (int)Key.Space);
            PlayerPrefs.SetInt(BurstKey, (int)Key.LeftShift);
            PlayerPrefs.Save();
        }

        public static bool IsPressed(this Keyboard keyboard, Key key)
        {
            return keyboard != null && keyboard[key].isPressed;
        }

        public static bool WasPressedThisFrame(this Keyboard keyboard, Key key)
        {
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }
    }
}
