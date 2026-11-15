using System;
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
