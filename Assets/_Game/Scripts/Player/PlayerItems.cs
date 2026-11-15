using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Settings;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    public enum ItemKind { ProteinBar, EnergyDrink }

    /// <summary>
    /// One-time-use snacks in YOUR pockets: bought at the gun shop (team pot) or found in supply crates.
    /// Protein bar (Q): heals. Energy drink (X): a burst of speed for a while. Host-authoritative counts.
    /// </summary>
    public class PlayerItems : NetworkBehaviour
    {
        public struct Info { public string Name, Blurb; public int Price, Max; }

        public static readonly Info[] Items =
        {
            new Info { Name = "Protein Bar", Blurb = "Chomp! Heals 50 HP.", Price = 40, Max = 5 },
            new Info { Name = "Energy Drink", Blurb = "Fizzy rocket fuel: +45% move speed for 30 seconds.", Price = 60, Max = 3 },
        };

        public const float HealAmount = 50f, BoostSeconds = 30f, BoostSpeed = 1.45f;

        public static PlayerItems Local { get; private set; }

        readonly NetworkVariable<int> _bars = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _drinks = new NetworkVariable<int>(0);
        float _boostUntil;

        public int Count(ItemKind k) => k == ItemKind.ProteinBar ? _bars.Value : _drinks.Value;
        NetworkVariable<int> Var(ItemKind k) => k == ItemKind.ProteinBar ? _bars : _drinks;

        /// <summary>Owner-side speed multiplier from an energy drink.</summary>
        public float SpeedMult => Time.time < _boostUntil ? BoostSpeed : 1f;
        public float BoostLeft => Mathf.Max(0f, _boostUntil - Time.time);

        public override void OnNetworkSpawn() { if (IsOwner) Local = this; }
        public override void OnNetworkDespawn() { if (Local == this) Local = null; }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            if (FriendSlop.Game.GameMenus.Open != FriendSlop.Game.GameMenus.Menu.None) return;
            if (TryGetComponent(out PlayerHealth hp) && hp.IsDown.Value) return;
            if (Controls.Pressed(GameAction.UseProteinBar)) UseRpc((int)ItemKind.ProteinBar);
            if (Controls.Pressed(GameAction.UseEnergyDrink)) UseRpc((int)ItemKind.EnergyDrink);
        }

        public void RequestBuy(ItemKind k) => BuyRpc((int)k);
        public void RequestUse(ItemKind k) => UseRpc((int)k);

        [Rpc(SendTo.Server)]
        void BuyRpc(int kind, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || kind < 0 || kind >= Items.Length) return;
            var state = GameState.Instance;
            var k = (ItemKind)kind;
            var info = Items[kind];
            string who = GameState.PlayerName(OwnerClientId);
            if (state == null) return;
            if (Var(k).Value >= info.Max) { state.ServerAnnounce($"{who}'s pockets are full of {info.Name}s."); return; }
            if (!state.ServerTrySpend(info.Price)) { state.ServerAnnounce($"{who} can't afford a {info.Name} (${info.Price})."); return; }
            Var(k).Value++;
        }

        /// <summary>Host: drop a free snack into this player's pockets (supply crates). False if full.</summary>
        public bool ServerGive(ItemKind k)
        {
            if (!IsServer || Var(k).Value >= Items[(int)k].Max) return false;
            Var(k).Value++;
            return true;
        }

        [Rpc(SendTo.Server)]
        void UseRpc(int kind, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || kind < 0 || kind >= Items.Length) return;
            var k = (ItemKind)kind;
            if (Var(k).Value <= 0) return;
            if (!TryGetComponent(out PlayerHealth hp) || hp.IsDown.Value) return;
            if (k == ItemKind.ProteinBar && hp.Health.Value >= hp.MaxHp - 0.5f) return; // don't waste it at full health
            Var(k).Value--;
            if (k == ItemKind.ProteinBar) hp.ServerHeal(HealAmount);
            UsedRpc(kind);
        }

        [Rpc(SendTo.Everyone)]
        void UsedRpc(int kind)
        {
            var k = (ItemKind)kind;
            SfxPlayer.PlayAt(k == ItemKind.ProteinBar ? Sfx.Squeak : Sfx.Boing, transform.position + Vector3.up, 0.6f, k == ItemKind.ProteinBar ? 0.7f : 1.6f);
            if (IsOwner && k == ItemKind.EnergyDrink) _boostUntil = Mathf.Max(_boostUntil, Time.time) + BoostSeconds;
        }
    }
}
