using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Rules the host picks before starting a hunt (remembered between sessions). Copied into
    /// <see cref="GameState"/> when the hunt starts, so every player sees the same rules.
    /// </summary>
    public static class LobbySettings
    {
        const string FriendlyFireKey = "fsh.lobby.friendlyFire";

        /// <summary>Can players hurt each other? (Explosions always shove everyone; this adds damage.)</summary>
        public static bool FriendlyFire
        {
            get => PlayerPrefs.GetInt(FriendlyFireKey, 0) == 1;
            set { PlayerPrefs.SetInt(FriendlyFireKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Share of a weapon's animal damage dealt to players (friendly fire stings, but it isn't a one-shot).</summary>
        public const float PlayerDamageScale = 0.45f;
    }
}
