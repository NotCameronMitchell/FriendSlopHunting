using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// A supply crate out at a landmark: look at it and press E to pocket some cash for the team.
    /// Once per in-game day; it restocks every morning.
    /// </summary>
    public class LootCrate : NetworkBehaviour
    {
        public const float InteractRange = 3.5f;

        [SerializeField] int minCash = 60;
        [SerializeField] int maxCash = 220;
        [SerializeField] string label = "supply crate";
        [SerializeField] Transform lid;

        /// <summary>The day it was last emptied (-1 = never).</summary>
        public readonly NetworkVariable<int> OpenedDay = new NetworkVariable<int>(-1);

        static int Today => DayNightCycle.Instance != null && DayNightCycle.Instance.IsSpawned ? DayNightCycle.Instance.Day : 1;
        public bool Looted => OpenedDay.Value == Today;
        public string Label => label;

        public void RequestOpen() => OpenRpc();

        [Rpc(SendTo.Server)]
        void OpenRpc(RpcParams rpcParams = default)
        {
            if (Looted) return;
            OpenedDay.Value = Today;
            int cash = Random.Range(minCash, maxCash + 1);
            GameState.Instance?.ServerGrantMoney(cash);
            ulong who = rpcParams.Receive.SenderClientId;
            string bonus = "";
            // Often a snack too, straight into the opener's pockets.
            if (Random.value < 0.6f && NetworkManager.ConnectedClients.TryGetValue(who, out var client) && client.PlayerObject != null
                && client.PlayerObject.TryGetComponent(out FriendSlop.Player.PlayerItems items))
            {
                var kind = Random.value < 0.6f ? FriendSlop.Player.ItemKind.ProteinBar : FriendSlop.Player.ItemKind.EnergyDrink;
                if (items.ServerGive(kind)) bonus = $" and a {FriendSlop.Player.PlayerItems.Items[(int)kind].Name}";
            }
            GameState.Instance?.ServerAnnounce($"{GameState.PlayerName(who)} found ${cash}{bonus} in the {label}!");
        }

        void Update()
        {
            if (!lid) return;
            var want = Quaternion.Euler(IsSpawned && Looted ? -115f : 0f, 0f, 0f);
            lid.localRotation = Quaternion.Slerp(lid.localRotation, want, Time.deltaTime * 6f);
        }
    }
}
