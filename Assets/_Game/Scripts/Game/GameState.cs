using System;
using FriendSlop.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Shared, host-owned session state: the team's money and a feed of announcements
    /// ("SOLD rabbit +$14, Headshot ×1.5"). One per scene, placed in-scene.
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        public static GameState Instance { get; private set; }

        /// <summary>Team wallet. Everyone shares one pot, How-to-Fish style.</summary>
        public readonly NetworkVariable<int> Money = new NetworkVariable<int>(0);

        /// <summary>Total animals sold this session (for fun stats later).</summary>
        public readonly NetworkVariable<int> AnimalsSold = new NetworkVariable<int>(0);

        /// <summary>Raised locally on every peer when the host announces something.</summary>
        public static event Action<string> Announced;

        [SerializeField] WeaponCatalog catalog;
        public WeaponCatalog Catalog => catalog;

        public static string PlayerName(ulong clientId) =>
            clientId == NetworkManager.ServerClientId ? "Host" : $"Player {clientId}";

        /// <summary>Client: buy a weapon with the team's money; it goes into your own inventory.</summary>
        public void RequestBuyWeapon(int index)
        {
            PlayerWeapons.Local?.NotePendingPurchase(index);
            BuyWeaponRpc(index);
        }

        [Rpc(SendTo.Server)]
        void BuyWeaponRpc(int index, RpcParams rpcParams = default)
        {
            var def = catalog != null ? catalog.Get(index) : null;
            if (def == null || index >= WeaponCatalog.MaxWeapons) return;

            ulong buyer = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(buyer, out var client) || client.PlayerObject == null) return;
            if (!client.PlayerObject.TryGetComponent(out PlayerWeapons weapons) || weapons.Owns(index)) return;

            string who = PlayerName(buyer);
            if (Money.Value < def.price)
            {
                AnnounceRpc($"{who} can't afford the {def.displayName} (${def.price:N0}). Go hunt!");
                return;
            }

            if (!weapons.ServerGive(index)) return;
            Money.Value -= def.price;
            AnnounceRpc($"{who} bought the {def.displayName} for ${def.price:N0}!");
        }

        /// <summary>Server only: grant money without counting a sale (dev/testing).</summary>
        public void ServerGrantMoney(int amount)
        {
            if (IsServer) Money.Value += amount;
        }

        /// <summary>Editor-only cheat (F9): +$1,000. Ignored in builds, so friends can't use it.</summary>
        public void RequestDevMoney()
        {
            if (Application.isEditor) DevMoneyRpc();
        }

        [Rpc(SendTo.Server)]
        void DevMoneyRpc()
        {
            if (!Application.isEditor) return; // the host must also be the editor
            Money.Value += 1000;
            AnnounceRpc("[dev] +$1,000");
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameState] Duplicate GameState in scene; ignoring this one.");
                return;
            }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>Server only: pay the team and tell everyone why.</summary>
        public void ServerAddMoney(int amount, string announcement)
        {
            if (!IsServer) return;
            Money.Value += amount;
            AnimalsSold.Value++;
            AnnounceRpc(announcement);
        }

        /// <summary>Server only: broadcast a message to every player's HUD.</summary>
        public void ServerAnnounce(string announcement)
        {
            if (!IsServer) return;
            AnnounceRpc(announcement);
        }

        [Rpc(SendTo.Everyone)]
        void AnnounceRpc(string announcement)
        {
            Debug.Log($"[Game] {announcement}");
            Announced?.Invoke(announcement);
        }
    }
}
