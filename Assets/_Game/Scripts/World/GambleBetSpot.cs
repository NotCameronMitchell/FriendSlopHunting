using FriendSlop.Game;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Something in the Lucky Lodge you look at to bet team money: the slot machine, a roulette colour
    /// on the table, the scratch-card booth. Scroll to change the bet, press E to play.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GambleBetSpot : MonoBehaviour
    {
        public const float InteractRange = 4f;
        public static readonly int[] BetSizes = { 10, 25, 50, 100, 250, 500, 1000 };

        public GambleStation station;
        [Tooltip("Which bet this spot places (roulette: 0 red, 1 black, 2 green).")]
        public int option;
        public string label = "SLOTS";
        public string odds = "";

        /// <summary>Set each frame while the local player is looking at a bet spot (the scroll wheel picks the bet instead of the weapon).</summary>
        public static bool Focused;
        static int _betIndex = 1;
        public static int CurrentBet => BetSizes[Mathf.Clamp(_betIndex, 0, BetSizes.Length - 1)];

        public static void ChangeBet(int direction) => _betIndex = Mathf.Clamp(_betIndex + direction, 0, BetSizes.Length - 1);

        public string Hint()
        {
            var state = GameState.Instance;
            int money = state != null ? state.Money.Value : 0;
            int lv = FriendSlop.Profile.PlayerProfile.Current.Level;
            if (lv < GambleStation.LodgeLevel)
                return $"LOCKED: the Lucky Lodge opens at hunter level {GambleStation.LodgeLevel} (you're level {lv}). Go hunt!";
            if (station != null && station.Busy)
                return station.game == GambleGame.Scratchers ? "E scratch!" : $"{label}: rolling…";
            string jackpot = station != null && station.game == GambleGame.Slots && state != null ? $"   ·   JACKPOT ${state.LodgeJackpot.Value:N0}" : "";
            string afford = money >= CurrentBet ? "" : "  (team can't afford it)";
            return $"E bet ${CurrentBet:N0} on {label}{(odds.Length > 0 ? $" ({odds})" : "")}   ·   scroll: bet size{jackpot}{afford}";
        }

        public void Use()
        {
            var state = GameState.Instance;
            if (state == null || station == null) return;
            if (FriendSlop.Profile.PlayerProfile.Current.Level < GambleStation.LodgeLevel) return;
            if (station.Busy)
            {
                if (station.game == GambleGame.Scratchers) state.RequestGambleAction(station.id, 1);
                return;
            }
            state.RequestGambleBet(station.id, option, CurrentBet);
        }
    }

}
