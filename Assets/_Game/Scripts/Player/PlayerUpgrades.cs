using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    public enum UpgradeTrack { Sprinter, ThickSkin, StrongBack, SpringBoots, HollowPoints, HairTrigger, SteadyHands }

    /// <summary>
    /// Per-player upgrades for this hunt, bought at the gun shop with team money. Each tier needs a
    /// minimum hunter level (levels come from your saved profile). The host owns the tiers; every
    /// peer reads them so effects (speed, spread, health) match everywhere.
    /// </summary>
    public class PlayerUpgrades : NetworkBehaviour
    {
        public struct Info
        {
            public string Name, Description;
            public int[] Levels, Costs;
            public int MaxTier => Costs.Length;
        }

        public static readonly Info[] Tracks =
        {
            new Info { Name = "Sprinter", Description = "+8% move speed per tier.", Levels = new[] { 2, 5, 9 }, Costs = new[] { 150, 400, 900 } },
            new Info { Name = "Thick Skin", Description = "+25 max health per tier.", Levels = new[] { 3, 6, 10 }, Costs = new[] { 200, 500, 1100 } },
            new Info { Name = "Strong Back", Description = "Carry heavy carcasses faster and throw them farther.", Levels = new[] { 4, 8 }, Costs = new[] { 250, 700 } },
            new Info { Name = "Spring Boots", Description = "+25% jump height per tier.", Levels = new[] { 2, 7 }, Costs = new[] { 120, 500 } },
            new Info { Name = "Hollow Points", Description = "+15% weapon damage per tier.", Levels = new[] { 3, 6, 10 }, Costs = new[] { 250, 650, 1400 } },
            new Info { Name = "Hair Trigger", Description = "+12% fire rate per tier.", Levels = new[] { 5, 9 }, Costs = new[] { 400, 1000 } },
            new Info { Name = "Steady Hands", Description = "40% tighter spread.", Levels = new[] { 4 }, Costs = new[] { 350 } },
        };

        // One field per track: Netcode only discovers NetworkVariables declared as fields (not inside arrays).
        readonly NetworkVariable<int> _sprinter = new(0), _thickSkin = new(0), _strongBack = new(0), _springBoots = new(0),
            _hollowPoints = new(0), _hairTrigger = new(0), _steadyHands = new(0);

        NetworkVariable<int> Var(int t) => (UpgradeTrack)t switch
        {
            UpgradeTrack.Sprinter => _sprinter, UpgradeTrack.ThickSkin => _thickSkin, UpgradeTrack.StrongBack => _strongBack,
            UpgradeTrack.SpringBoots => _springBoots, UpgradeTrack.HollowPoints => _hollowPoints, UpgradeTrack.HairTrigger => _hairTrigger,
            _ => _steadyHands,
        };

        public static PlayerUpgrades Local { get; private set; }

        public int Tier(UpgradeTrack t) => Var((int)t).Value;

        public float SpeedMult => 1f + 0.08f * Tier(UpgradeTrack.Sprinter);
        public float HealthBonus => 25f * Tier(UpgradeTrack.ThickSkin);
        public int CarryTier => Tier(UpgradeTrack.StrongBack);
        public float JumpMult => 1f + 0.25f * Tier(UpgradeTrack.SpringBoots);
        public float DamageMult => 1f + 0.15f * Tier(UpgradeTrack.HollowPoints);
        public float FireIntervalMult => 1f / (1f + 0.12f * Tier(UpgradeTrack.HairTrigger));
        public float SpreadMult => Tier(UpgradeTrack.SteadyHands) > 0 ? 0.6f : 1f;

        public override void OnNetworkSpawn() { if (IsOwner) Local = this; }
        public override void OnNetworkDespawn() { if (Local == this) Local = null; }

        public static PlayerUpgrades Of(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            var obj = nm != null && nm.SpawnManager != null ? nm.SpawnManager.GetPlayerNetworkObject(clientId) : null;
            return obj != null ? obj.GetComponent<PlayerUpgrades>() : null;
        }

        /// <summary>Owner: try to buy the next tier.</summary>
        public void RequestBuy(UpgradeTrack track) => BuyRpc((int)track);

        [Rpc(SendTo.Server)]
        void BuyRpc(int track, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || track < 0 || track >= Tracks.Length) return;
            var state = GameState.Instance;
            if (state == null) return;
            var info = Tracks[track];
            int tier = Var(track).Value;
            if (tier >= info.MaxTier) return;
            string who = GameState.PlayerName(OwnerClientId);
            int level = TryGetComponent(out PlayerProgress progress) ? progress.Level.Value : 1;
            if (level < info.Levels[tier]) { state.ServerAnnounce($"{who} needs hunter level {info.Levels[tier]} for {info.Name} {Roman(tier + 1)}."); return; }
            if (!state.ServerTrySpend(info.Costs[tier])) { state.ServerAnnounce($"{who} can't afford {info.Name} {Roman(tier + 1)} (${info.Costs[tier]:N0})."); return; }
            Var(track).Value = tier + 1;
            if ((UpgradeTrack)track == UpgradeTrack.ThickSkin && TryGetComponent(out PlayerHealth hp)) hp.ServerHeal(25f);
            state.ServerAnnounce($"{who} upgraded {info.Name} {Roman(tier + 1)}!");
        }

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => n.ToString() };
    }
}
