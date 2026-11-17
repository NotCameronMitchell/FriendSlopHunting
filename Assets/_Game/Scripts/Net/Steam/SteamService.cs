using System;
using Steamworks;
using UnityEngine;

namespace FriendSlop.Net.Steam
{
    /// <summary>
    /// Owns the Steam API lifetime for the whole game: init on startup, pump callbacks every frame
    /// on the main thread, shut down on quit. Uses the public test AppID 480 ("Spacewar") so we
    /// never need a store page. If Steam isn't running, the game still works over Direct.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SteamService : MonoBehaviour
    {
        /// <summary>Valve's public test app. Swap for a real AppID if we ever publish.</summary>
        public const uint AppId = 480;

        public static bool IsReady { get; private set; }
        public static string Error { get; private set; } = string.Empty;
        public static string PersonaName => IsReady ? SteamClient.Name : "(no Steam)";
        public static ulong LocalSteamId => IsReady ? SteamClient.SteamId.Value : 0UL;

        static SteamService _instance;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
            TryInit();
        }

        static void TryInit()
        {
            if (IsReady) return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nosteam") >= 0)
            {
                Error = "Steam disabled (-nosteam).";
                return;
            }
            try
            {
                // asyncCallbacks: false → we pump callbacks ourselves in Update, so every Steam
                // event (lobby joins, invites, connection changes) lands on Unity's main thread.
                SteamClient.Init(AppId, asyncCallbacks: false);
                SteamNetworkingUtils.InitRelayNetworkAccess();
                IsReady = true;
                Error = string.Empty;
                Debug.Log($"[Steam] Ready as {SteamClient.Name} ({SteamClient.SteamId.Value})");
            }
            catch (Exception e)
            {
                IsReady = false;
                Error = "Steam isn't running or couldn't start. Open Steam and restart the game.";
                Debug.LogWarning($"[Steam] Init failed: {e.Message}");
            }
        }

        void Update()
        {
            if (IsReady) SteamClient.RunCallbacks();
        }

        void OnApplicationQuit() => ShutdownSteam();

        void OnDestroy()
        {
            if (_instance == this) ShutdownSteam();
        }

        void ShutdownSteam()
        {
            if (!IsReady) return;
            IsReady = false;
            try { SteamClient.Shutdown(); }
            catch (Exception e) { Debug.LogWarning($"[Steam] Shutdown: {e.Message}"); }
            if (_instance == this) _instance = null;
        }
    }
}
