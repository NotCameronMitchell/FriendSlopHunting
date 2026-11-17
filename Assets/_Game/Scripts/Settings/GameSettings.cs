using System;
using UnityEngine;

namespace FriendSlop.Settings
{
    /// <summary>
    /// Per-player preferences, saved on this PC (PlayerPrefs). Not networked: everyone has
    /// their own sensitivity, volume and voice setup.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "fsh.settings.";

        public static event Action Changed;

        // Audio
        public static float MasterVolume { get => GetF("master", 0.8f); set => SetF("master", Mathf.Clamp01(value)); }
        public static float SfxVolume { get => GetF("sfx", 1f); set => SetF("sfx", Mathf.Clamp01(value)); }
        public static float VoiceVolume { get => GetF("voice", 1f); set => SetF("voice", Mathf.Clamp01(value)); }

        // Mouse & view
        public static float MouseSensitivity { get => GetF("sens", 0.12f); set => SetF("sens", Mathf.Clamp(value, 0.02f, 0.6f)); }
        public static bool InvertY { get => GetB("invertY", false); set => SetB("invertY", value); }
        public static float FieldOfView { get => GetF("fov", 75f); set => SetF("fov", Mathf.Clamp(value, 60f, 100f)); }
        public static float ScreenShake { get => GetF("shake", 1f); set => SetF("shake", Mathf.Clamp01(value)); }
        public static bool HeadBob { get => GetB("headBob", true); set => SetB("headBob", value); }

        // Voice (used by proximity voice chat once it lands; the toggles already work today)
        public static bool PushToTalk { get => GetB("ptt", true); set => SetB("ptt", value); }
        public static bool MicMuted { get => GetB("micMuted", false); set => SetB("micMuted", value); }

        /// <summary>Push saved values into the engine (volume). Call on startup and after changes.</summary>
        public static void Apply() => AudioListener.volume = MasterVolume;

        public static void ResetToDefaults()
        {
            foreach (var key in new[] { "master", "sfx", "voice", "sens", "invertY", "fov", "shake", "headBob", "ptt", "micMuted" })
                PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnStartup() => Apply();

        static float GetF(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
        static bool GetB(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;

        static void SetF(string key, float value)
        {
            if (Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key, float.NaN), value)) return;
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        static void SetB(string key, bool value)
        {
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }
    }
}
