using FriendSlop.Animals;
using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.World
{
    /// <summary>
    /// The legendary totem: pay from the team pot to summon a boss nearby. One boss at a time.
    /// Look at it and press E.
    /// </summary>
    public class BossTotem : NetworkBehaviour
    {
        public const float InteractRange = 4.5f;

        [SerializeField] GameObject bossPrefab;
        [SerializeField] int summonCost = 250;
        [SerializeField] float spawnDistance = 18f;

        public int SummonCost => summonCost;
        public AnimalSpecies BossSpecies => bossPrefab != null && bossPrefab.TryGetComponent(out Animal a) ? a.Species : null;

        public static bool BossAlive
        {
            get
            {
                foreach (var a in Animal.All)
                    if (a != null && a.IsSpawned && a.Species != null && a.Species.isBoss && !a.IsDead.Value) return true;
                return false;
            }
        }

        public void RequestSummon() => SummonRpc();

        [Rpc(SendTo.Server)]
        void SummonRpc(RpcParams rpcParams = default)
        {
            var state = GameState.Instance;
            var species = BossSpecies;
            if (state == null || species == null) return;
            string who = GameState.PlayerName(rpcParams.Receive.SenderClientId);

            if (BossAlive) { state.ServerAnnounce($"{species.bossTitle} is already out there!"); return; }
            if (state.Money.Value < summonCost) { state.ServerAnnounce($"Summoning {species.bossTitle} costs ${summonCost:N0}. Go hunt!"); return; }

            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector3 p = transform.position + new Vector3(dir.x, 0f, dir.y) * spawnDistance;
            if (!NavMesh.SamplePosition(p, out var hit, 10f, NavMesh.AllAreas)) { state.ServerAnnounce("The totem fizzles… (no room)"); return; }

            state.ServerGrantMoney(-summonCost);
            var go = Instantiate(bossPrefab, hit.position, Quaternion.LookRotation(transform.position - hit.position));
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            state.ServerAnnounce($"{who} summoned {species.bossTitle}! (-${summonCost:N0}) Bounty: ${species.bounty:N0}");
        }
    }
}
