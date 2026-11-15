using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FriendSlop.Animals
{
    /// <summary>
    /// Host-side spawner that keeps a population of one animal type alive in a ring around a center
    /// point, away from camp. Dead animals (carcasses) don't count toward the population.
    /// </summary>
    public class AnimalSpawner : NetworkBehaviour
    {
        [SerializeField] GameObject animalPrefab;
        [SerializeField] int population = 8;
        [SerializeField] float minRadius = 22f;
        [SerializeField] float maxRadius = 70f;
        [SerializeField] float respawnDelay = 6f;

        readonly List<Animal> _spawned = new List<Animal>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (animalPrefab == null)
            {
                Debug.LogError("[AnimalSpawner] No prefab assigned.");
                return;
            }
            StartCoroutine(SpawnLoop());
        }

        IEnumerator SpawnLoop()
        {
            // Fill the woods right away, then trickle in replacements.
            for (int i = 0; i < population; i++) TrySpawnOne();

            var wait = new WaitForSeconds(respawnDelay);
            while (IsSpawned)
            {
                yield return wait;
                _spawned.RemoveAll(a => a == null || !a.IsSpawned);
                int alive = 0;
                foreach (var a in _spawned) if (!a.IsDead.Value) alive++;
                if (alive < population) TrySpawnOne();
            }
        }

        bool TrySpawnOne()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                float dist = Random.Range(minRadius, maxRadius);
                Vector3 candidate = transform.position + new Vector3(dir.x, 0f, dir.y) * dist;
                if (!NavMesh.SamplePosition(candidate, out var hit, 4f, NavMesh.AllAreas)) continue;

                var go = Instantiate(animalPrefab, hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                _spawned.Add(go.GetComponent<Animal>());
                return true;
            }
            Debug.LogWarning("[AnimalSpawner] Couldn't find a navmesh spot. Is the NavMesh baked?");
            return false;
        }
    }
}
