using FriendSlop.Animals;
using FriendSlop.Profile;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    /// <summary>
    /// Routes kill credit from the host to the player who made the kill, so their saved profile
    /// (stats, achievements, cosmetic unlocks) updates on their own machine.
    /// </summary>
    public class PlayerProgress : NetworkBehaviour
    {
        /// <summary>Host: tell the killer about their kill.</summary>
        public void ServerCreditKill(AnimalSpecies species, ShotStyle style)
        {
            if (!IsServer || species == null) return;
            KillCreditRpc(new FixedString32Bytes(species.displayName), (int)style, species.isBoss);
        }

        [Rpc(SendTo.Owner)]
        void KillCreditRpc(FixedString32Bytes species, int style, bool boss)
        {
            var s = (ShotStyle)style;
            PlayerProfile.Current.RecordKill(species.ToString(),
                headshot: (s & ShotStyle.Headshot) != 0,
                jumpShot: (s & ShotStyle.JumpShot) != 0,
                longShot: (s & ShotStyle.LongShot) != 0,
                boss: boss);
        }

        /// <summary>Host helper: find a client's progress component.</summary>
        public static PlayerProgress Of(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            var obj = nm != null && nm.SpawnManager != null ? nm.SpawnManager.GetPlayerNetworkObject(clientId) : null;
            return obj != null ? obj.GetComponent<PlayerProgress>() : null;
        }
    }
}
