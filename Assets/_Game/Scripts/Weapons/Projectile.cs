using System;
using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.World;
using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// A locally simulated shot: arrow, slingshot rock, rocket, or boomerang. Every peer simulates
    /// its own copy from the same starting values (cheap, and smooth for everyone). Only the
    /// shooter's copy is "authoritative": it reports hits and explosions to the host. The others
    /// are pure visuals.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        WeaponDefinition _def;
        Vector3 _velocity;
        bool _authority;
        Transform _owner;
        Action<Animal, Vector3, bool> _onAnimalHit;
        Action<Vector3> _onExplode;
        Action<FriendSlop.Player.PlayerHealth, Vector3> _onPlayerHit;
        Action _onFinished;
        float _age;
        bool _returning;
        Vector3 _start;
        Transform _visual;
        readonly HashSet<Animal> _alreadyHit = new HashSet<Animal>();

        const float MaxLifetime = 6f;
        const float StepRadius = 0.08f;

        public static Projectile Launch(WeaponDefinition def, Vector3 origin, Vector3 direction, Transform owner,
                                        bool authority, Action<Animal, Vector3, bool> onAnimalHit,
                                        Action<Vector3> onExplode, Action onFinished,
                                        Action<FriendSlop.Player.PlayerHealth, Vector3> onPlayerHit = null)
        {
            var go = new GameObject($"Projectile_{def.displayName}");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(direction);
            var p = go.AddComponent<Projectile>();
            p._def = def;
            p._velocity = direction.normalized * def.projectileSpeed;
            p._authority = authority;
            p._owner = owner;
            p._onAnimalHit = onAnimalHit;
            p._onExplode = onExplode;
            p._onPlayerHit = onPlayerHit;
            p._onFinished = onFinished;
            p._start = origin;
            p._visual = BuildVisual(def, go.transform);
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > MaxLifetime) { Finish(); return; }

            Vector3 from = transform.position;
            if (_def.kind == WeaponKind.Boomerang) StepBoomerang(dt);
            else _velocity += Vector3.down * _def.projectileGravity * dt;

            Vector3 to = from + _velocity * dt;
            Vector3 seg = to - from;
            float len = seg.magnitude;

            if (len > 0.0001f && TryFindHit(from, seg / len, len, out var hit))
            {
                if (HandleHit(hit)) return;
            }

            transform.position = to;
            if (_velocity.sqrMagnitude > 0.01f && _def.kind != WeaponKind.Boomerang)
                transform.rotation = Quaternion.LookRotation(_velocity);
            if (_def.kind == WeaponKind.Boomerang && _visual)
                _visual.Rotate(0f, 1440f * dt, 0f, Space.Self);
        }

        void StepBoomerang(float dt)
        {
            float speed = _def.projectileSpeed;
            if (!_returning && Vector3.Distance(transform.position, _start) >= _def.boomerangRange)
                _returning = true;

            if (_returning)
            {
                if (_owner == null) { Finish(); return; }
                Vector3 target = _owner.position + Vector3.up * 1.3f;
                Vector3 toOwner = target - transform.position;
                if (toOwner.magnitude < 1.2f) { Finish(); return; }
                // Steer back with a curve instead of snapping around.
                _velocity = Vector3.RotateTowards(_velocity, toOwner.normalized * speed, 5f * dt, speed * dt * 4f);
                _velocity = _velocity.normalized * speed;
            }
        }

        /// <summary>Dev: log what projectiles hit (set by the editor bridge).</summary>
        public static bool DebugLog;

        /// <returns>True if the projectile is finished.</returns>
        bool HandleHit(RaycastHit hit)
        {
            if (DebugLog) Debug.Log($"[Projectile] {_def.displayName} hit {hit.collider.name} rb={(hit.rigidbody ? hit.rigidbody.name : "none")} authority={_authority} at {hit.point}");
            var animal = hit.collider.GetComponentInParent<Animal>();

            if (_def.IsExplosive)
            {
                if (_authority) _onExplode?.Invoke(hit.point);
                Finish();
                return true;
            }

            if (animal != null && animal.IsSpawned)
            {
                if (_alreadyHit.Add(animal) && _authority)
                {
                    bool head = hit.collider.TryGetComponent(out Hitbox hb) && hb.IsHead;
                    _onAnimalHit?.Invoke(animal, hit.point, head);
                }
                if (_def.kind == WeaponKind.Boomerang) return false; // keeps flying through
                Finish();
                return true;
            }

            if (hit.collider.TryGetComponent(out FriendSlop.Player.PlayerHealth player))
            {
                if (_authority) _onPlayerHit?.Invoke(player, hit.point);
                if (_def.kind == WeaponKind.Boomerang) { _returning = true; return false; }
                Finish();
                return true;
            }

            if (_authority && hit.rigidbody != null && hit.rigidbody.TryGetComponent(out NetworkPhysicsBox box))
            {
                // Same small upward lift as bullets, so things hop instead of being driven into the dirt.
                box.RequestImpulse((_velocity.normalized + Vector3.up * 0.35f).normalized * _def.impulse, hit.point);
                SfxPlayer.PlayAt(Sfx.HitTick, hit.point, 0.6f, 0.55f, 2f, 40f);
            }

            if (_def.kind == WeaponKind.Boomerang)
            {
                // Bounce off the world and head home.
                _returning = true;
                _velocity = Vector3.Reflect(_velocity, hit.normal);
                return false;
            }

            Finish();
            return true;
        }

        /// <summary>
        /// Forgiving against targets, exact against the world: a fat sphere cast so arrows and rocks
        /// connect with animals, dropped guns and boxes, but only the exact center line counts for
        /// static ground and walls, so a shot skimming low over the grass at a gun lying in it
        /// isn't stopped short by the sphere grazing the ground.
        /// </summary>
        bool TryFindHit(Vector3 from, Vector3 dir, float len, out RaycastHit result)
        {
            bool fat = Physics.SphereCast(from, StepRadius, dir, out var sphereHit, len, ~0, QueryTriggerInteraction.Ignore)
                       && !IsOwnerCollider(sphereHit.collider);
            bool thin = Physics.Raycast(from, dir, out var rayHit, len, ~0, QueryTriggerInteraction.Ignore)
                        && !IsOwnerCollider(rayHit.collider);

            if (fat && IsTarget(sphereHit.collider) && (!thin || sphereHit.distance <= rayHit.distance + StepRadius))
            {
                result = sphereHit;
                return true;
            }
            if (thin)
            {
                result = rayHit;
                return true;
            }
            result = default;
            return false;
        }

        static bool IsTarget(Collider c) => c.attachedRigidbody != null || c.GetComponentInParent<Animal>() != null || c is CharacterController;

        bool IsOwnerCollider(Collider c) => _owner != null && c.transform.IsChildOf(_owner);

        void Finish()
        {
            _onFinished?.Invoke();
            _onFinished = null;
            Destroy(gameObject);
        }

        static Transform BuildVisual(WeaponDefinition def, Transform parent)
        {
            PrimitiveType type = PrimitiveType.Sphere;
            Vector3 scale = Vector3.one * 0.08f;
            Color color = def.tint;
            switch (def.model)
            {
                case WeaponModel.Bow: type = PrimitiveType.Cube; scale = new Vector3(0.02f, 0.02f, 0.55f); color = new Color(0.5f, 0.35f, 0.2f); break;
                case WeaponModel.Slingshot: scale = Vector3.one * 0.07f; color = new Color(0.5f, 0.5f, 0.52f); break;
                case WeaponModel.Bazooka: type = PrimitiveType.Capsule; scale = new Vector3(0.09f, 0.18f, 0.09f); break;
                case WeaponModel.Boomerang: type = PrimitiveType.Cube; scale = new Vector3(0.38f, 0.02f, 0.06f); break;
            }
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            if (def.model == WeaponModel.Bazooka) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var r = go.GetComponent<Renderer>();
            var mat = new Material(r.sharedMaterial) { color = color };
            mat.SetColor("_BaseColor", color);
            r.sharedMaterial = mat;
            return go.transform;
        }
    }
}
