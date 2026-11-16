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
        /// <summary>The owner's hunter level (from their saved profile), for nametags and upgrade checks.</summary>
        public readonly NetworkVariable<int> Level = new NetworkVariable<int>(1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            Level.Value = PlayerProfile.Current.Level;
            PlayerProfile.LevelUp += OnLevelUp;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) PlayerProfile.LevelUp -= OnLevelUp;
        }

        void OnLevelUp(int level) { if (IsSpawned && IsOwner) Level.Value = level; }

        /// <summary>Host: tell the killer about their kill (stats, achievements, XP).</summary>
        public void ServerCreditKill(AnimalSpecies species, ShotStyle style, bool elite = false)
        {
            if (!IsServer || species == null) return;
            // Economy v3: about half the old XP, so levels (and the upgrades they gate) take a few sessions.
            // Skill still pays: style bonuses are a bigger share of the total than before.
            int xp = Mathf.RoundToInt(5 + species.basePrice * 0.28f);
            if ((style & ShotStyle.Headshot) != 0) xp += 6;
            if ((style & ShotStyle.LongShot) != 0) xp += 6;
            if ((style & ShotStyle.JumpShot) != 0) xp += 9;
            if (elite) xp *= 2;
            if (species.isBoss) xp += 250;
            if (species.shadowy) xp += 150;
            KillCreditRpc(new FixedString32Bytes(species.displayName), (int)style, species.isBoss, xp);
        }

        /// <summary>Host: credit a fish catch (XP + the catch log).</summary>
        public void ServerCreditCatch(int kind, float kg)
        {
            if (!IsServer) return;
            var sp = FriendSlop.Fishing.FishSpecies.Get(kind);
            int xp = sp.Junk ? 1 : Mathf.RoundToInt(3 + sp.Price * 0.25f);
            CatchCreditRpc(kind, kg, xp);
        }

        [Rpc(SendTo.Owner)]
        void CatchCreditRpc(int kind, float kg, int xp)
        {
            PlayerProfile.Current.AddXp(xp);
            PlayerProfile.Current.RecordFish(FriendSlop.Fishing.FishSpecies.Get(kind).Name, kg);
        }

        [Rpc(SendTo.Owner)]
        void KillCreditRpc(FixedString32Bytes species, int style, bool boss, int xp)
        {
            var s = (ShotStyle)style;
            PlayerProfile.Current.AddXp(xp);
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
