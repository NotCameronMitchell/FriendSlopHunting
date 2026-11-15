using System.Collections;
using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Audio;
using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>Explosion damage (host only) and explosion visuals (everyone).</summary>
    public static class Explosions
    {
        static readonly Collider[] Hits = new Collider[64];

        /// <summary>Host: damage animals and shove rigidbodies inside the radius.</summary>
        public static void ServerExplode(Vector3 center, WeaponDefinition def, ulong shooterId)
        {
            int n = Physics.OverlapSphereNonAlloc(center, def.explosionRadius, Hits, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new HashSet<Animal>();
            var pushed = new HashSet<Rigidbody>();
            var shoved = new HashSet<FriendSlop.Player.PlayerHealth>();
            var up = FriendSlop.Player.PlayerUpgrades.Of(shooterId);
            float dmgMult = up != null ? up.DamageMult : 1f;

            for (int i = 0; i < n; i++)
            {
                var col = Hits[i];
                var animal = col.GetComponentInParent<Animal>();
                if (animal != null && animal.IsSpawned && damaged.Add(animal))
                {
                    float dist = Vector3.Distance(center, animal.transform.position);
                    float falloff = Mathf.Clamp01(1f - dist / def.explosionRadius);
                    Vector3 dir = (animal.transform.position - center + Vector3.up * 0.5f).normalized;
                    animal.ServerApplyHit(def.explosionDamage * dmgMult * (0.35f + 0.65f * falloff), dir, ShotStyle.None, shooterId);
                    continue;
                }

                if (col.TryGetComponent(out FriendSlop.Player.PlayerHealth player) && shoved.Add(player))
                {
                    // Everyone gets blasted (rocket jumps!); with friendly fire on, others also get hurt.
                    float dist = Vector3.Distance(center, player.transform.position + Vector3.up);
                    float falloff = Mathf.Clamp01(1f - dist / def.explosionRadius);
                    Vector3 dir = (player.transform.position - center).normalized;
                    Vector3 blast = (dir + Vector3.up * 0.8f).normalized * Mathf.Lerp(4f, 14f, falloff);
                    bool self = player.OwnerClientId == shooterId;
                    if (!self && FriendSlop.Game.GameState.FriendlyFireOn)
                        player.ServerDamage(def.explosionDamage * FriendSlop.Game.LobbySettings.PlayerDamageScale * (0.3f + 0.7f * falloff), blast,
                            $"{def.displayName} (thanks, {FriendSlop.Game.GameState.PlayerName(shooterId)})");
                    else player.ServerShove(blast);
                    continue;
                }

                var rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic && pushed.Add(rb))
                    rb.AddExplosionForce(def.explosionForce * rb.mass, center, def.explosionRadius, 1.2f, ForceMode.Impulse);
            }
        }

        /// <summary>Everyone: flash, fireball, boom.</summary>
        public static void PlayFx(Vector3 center, float radius)
        {
            SfxPlayer.PlayAt(Sfx.Explosion, center, 1f, 1f, minDistance: 8f, maxDistance: 300f);
            FriendSlop.Player.NetworkPlayer.ShakeAt(center, 0.75f, Mathf.Max(18f, radius * 4f));

            var go = new GameObject("Explosion");
            go.transform.position = center;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = radius * 4f;
            light.intensity = 8f;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            var r = ball.GetComponent<Renderer>();
            var mat = new Material(r.sharedMaterial) { color = new Color(1f, 0.55f, 0.15f) };
            mat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f));
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            go.AddComponent<FxRunner>().Run(Grow(go.transform, ball.transform, light, radius));
        }

        static IEnumerator Grow(Transform root, Transform ball, Light light, float radius)
        {
            const float duration = 0.35f;
            float t = 0f;
            while (t < duration)
            {
                float k = t / duration;
                ball.localScale = Vector3.one * Mathf.Lerp(0.3f, radius * 1.6f, Mathf.Sqrt(k));
                light.intensity = Mathf.Lerp(8f, 0f, k);
                t += Time.deltaTime;
                yield return null;
            }
            Object.Destroy(root.gameObject);
        }

        /// <summary>Tiny host for coroutines on throwaway FX objects.</summary>
        class FxRunner : MonoBehaviour
        {
            public void Run(IEnumerator routine) => StartCoroutine(routine);
        }
    }
}
