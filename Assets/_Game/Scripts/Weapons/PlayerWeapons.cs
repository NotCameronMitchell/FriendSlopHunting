using System.Collections;
using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Player;
using FriendSlop.Settings;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// A player's personal inventory of weapons. Ammo is unlimited. Weapons are bought at the gun
    /// shop (with the team's money) into the buyer's inventory, and can be dropped (G) as world
    /// pickups that anyone can grab (E): that's how you give a gun to a friend. The pistol is
    /// permanent. Number keys pick inventory slots in order; scroll cycles.
    ///
    /// Shots: the shooter simulates locally for instant feedback and reports hits; the host
    /// sanity-checks every claim (position, range, target near the hit) and applies damage.
    /// Other players replay each shot from (weapon, origin, direction, random seed), so shotgun
    /// spreads and projectile arcs look the same on every screen without sending every pellet.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerWeapons : NetworkBehaviour
    {
        /// <summary>The local player's weapons (for the HUD and shop).</summary>
        public static PlayerWeapons Local { get; private set; }

        [SerializeField] WeaponCatalog catalog;
        [SerializeField] Camera playerCamera;
        [SerializeField] Transform gunHolder;
        [SerializeField] Material tracerMaterial;
        [SerializeField] GameObject pickupPrefab;

        const float LongShotDistance = 35f;
        const float MaxPickupDistance = 4.5f;

        public readonly NetworkVariable<int> Equipped = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Weapons this player carries, as a bitmask over the catalog. Host-written. Bit 0 (pistol) is permanent.</summary>
        public readonly NetworkVariable<int> Owned = new NetworkVariable<int>(1);

        NetworkPlayer _player;

        FriendSlop.Player.PlayerHealth _health;
        Transform _model;
        Transform _muzzle;
        Vector3 _holderRest;
        float _nextFire;
        float _spin;
        bool _boomerangOut;
        float _boomerangTimeout;
        int _pendingBuy = -1;
        int _lastUnlockMask = 1;

        public WeaponCatalog Catalog => catalog;
        public WeaponDefinition Current => catalog != null ? catalog.Get(Equipped.Value) : null;
        public float SpinUp => _spin;

        void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _health = GetComponent<FriendSlop.Player.PlayerHealth>();
            if (gunHolder) _holderRest = gunHolder.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            Equipped.OnValueChanged += OnEquippedChanged;
            // Others see the gun held at chest height in the hunter's hand, not floating at eye level.
            if (!IsOwner && gunHolder) { gunHolder.localPosition = new Vector3(0.26f, -0.55f, 0.5f); _holderRest = gunHolder.localPosition; }
            RebuildModel(Equipped.Value);
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            Equipped.OnValueChanged -= OnEquippedChanged;
            if (Local == this) Local = null;

            // A player leaving drops their guns where they stood, so nothing is lost.
            if (IsServer && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            {
                var slots = Slots();
                for (int s = 0; s < slots.Count; s++)
                    if (slots[s] != 0)
                        ServerSpawnPickup(slots[s], transform.position + Vector3.up * (1f + 0.3f * s), Random.insideUnitSphere * 2f);
            }
        }

        void OnEquippedChanged(int previous, int current) => RebuildModel(current);

        // ============================================================ Owner input

        void Update()
        {
            if (!IsSpawned || !IsOwner || catalog == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            WatchUnlocks();

            bool controlling = Cursor.lockState == CursorLockMode.Locked && !GameMenus.AnyOpen && !_player.IsDown;
            var def = Current;
            if (def == null) return;

            if (controlling) HandleSwitching(kb, mouse);
            if (controlling && Controls.Pressed(GameAction.DropWeapon)) RequestDropEquipped();

            bool canShoot = controlling && !_player.IsCarrying;
            bool aiming = canShoot && Controls.Held(GameAction.Aim);

            if (playerCamera)
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, aiming ? def.aimFov * GameSettings.FieldOfView / 75f : GameSettings.FieldOfView, Time.deltaTime * 12f);
            if (gunHolder)
                gunHolder.localPosition = Vector3.Lerp(gunHolder.localPosition, _holderRest, Time.deltaTime * 12f);

            // Minigun-style spin-up.
            bool trigger = canShoot && (def.IsAuto ? Controls.Held(GameAction.Fire) : Controls.Pressed(GameAction.Fire));
            if (def.spinUpTime > 0f)
            {
                bool holding = canShoot && Controls.Held(GameAction.Fire);
                _spin = Mathf.MoveTowards(_spin, holding ? 1f : 0f, Time.deltaTime / def.spinUpTime);
                if (_model) _model.Rotate(0f, 0f, 1500f * _spin * Time.deltaTime, Space.Self);
                if (_spin < 1f) trigger = false;
            }
            else _spin = 0f;

            if (_boomerangOut && Time.time > _boomerangTimeout) _boomerangOut = false;
            if (def.kind == WeaponKind.Boomerang && _boomerangOut) trigger = false;

            if (trigger && Time.time >= _nextFire)
            {
                _nextFire = Time.time + def.fireInterval;
                Fire(def, aiming);
            }
        }

        void HandleSwitching(Keyboard kb, Mouse mouse)
        {
            var slots = Slots();
            for (int i = 0; i < 10 && i < slots.Count; i++)
            {
                var key = i == 9 ? kb.digit0Key : kb[(Key)((int)Key.Digit1 + i)];
                if (key.wasPressedThisFrame) { RequestEquip(slots[i]); return; }
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) Cycle(scroll > 0f ? -1 : 1);
        }

        void Cycle(int direction)
        {
            var slots = Slots();
            if (slots.Count < 2) return;
            int current = Mathf.Max(0, slots.IndexOf(Equipped.Value));
            int next = ((current + direction) % slots.Count + slots.Count) % slots.Count;
            RequestEquip(slots[next]);
        }

        /// <summary>The catalog indices this player owns, in catalog order (= inventory slots).</summary>
        public List<int> Slots()
        {
            var list = new List<int>();
            int mask = OwnedMask();
            int n = catalog != null ? catalog.Count : 1;
            for (int i = 0; i < n; i++) if (WeaponCatalog.IsOwned(mask, i)) list.Add(i);
            return list;
        }

        public bool Owns(int index) => WeaponCatalog.IsOwned(OwnedMask(), index);

        // ============================================================ Drop / pick up / give

        /// <summary>Owner: drop the equipped weapon in front of you (not the pistol).</summary>
        public void RequestDropEquipped() => RequestDrop(Equipped.Value);

        /// <summary>Owner: drop a weapon from your inventory.</summary>
        public void RequestDrop(int index)
        {
            if (!IsOwner || index == 0 || !Owns(index)) return;
            Transform eye = playerCamera ? playerCamera.transform : transform;
            if (index == Equipped.Value) RequestEquip(0);
            DropWeaponRpc(index, eye.position + eye.forward * 0.9f, eye.forward * 4f + Vector3.up * 1.5f);
            SfxPlayer.Play2D(Sfx.Whoosh, 0.4f, 1.3f);
        }

        [Rpc(SendTo.Server)]
        void DropWeaponRpc(int index, Vector3 position, Vector3 velocity, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || index == 0 || !Owns(index)) return;
            if ((position - transform.position).sqrMagnitude > 9f) position = transform.position + Vector3.up * 1.2f;
            Owned.Value &= ~(1 << index);
            ServerSpawnPickup(index, position, Vector3.ClampMagnitude(velocity, 10f));
        }

        /// <summary>Host: put a weapon into this player's inventory. False if they already have one.</summary>
        public bool ServerGive(int index)
        {
            if (!IsServer || catalog == null || catalog.Get(index) == null || Owns(index)) return false;
            Owned.Value |= 1 << index;
            return true;
        }

        /// <summary>Host: spawn a weapon lying in the world.</summary>
        public void ServerSpawnPickup(int index, Vector3 position, Vector3 velocity)
        {
            if (!IsServer || pickupPrefab == null) return;
            var go = Instantiate(pickupPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var pickup = go.GetComponent<WeaponPickup>();
            pickup.WeaponIndex.Value = index;
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            if (go.TryGetComponent(out Rigidbody rb)) rb.linearVelocity = velocity;
        }

        /// <summary>Equip a weapon from your inventory.</summary>
        public void RequestEquip(int index)
        {
            if (!IsOwner || catalog.Get(index) == null || index == Equipped.Value) return;
            if (!WeaponCatalog.IsOwned(OwnedMask(), index)) return;
            Equipped.Value = index;
            _spin = 0f;
            _nextFire = Time.time + 0.2f;
            SfxPlayer.Play2D(Sfx.HitTick, 0.35f, 0.45f);
        }

        /// <summary>Remember a purchase so we can auto-equip it once the host confirms.</summary>
        public void NotePendingPurchase(int index) => _pendingBuy = index;

        void WatchUnlocks()
        {
            int mask = OwnedMask();
            if (mask == _lastUnlockMask) return;
            int gained = mask & ~_lastUnlockMask;
            _lastUnlockMask = mask;

            // Lost the equipped weapon (dropped): fall back to the pistol.
            if (!WeaponCatalog.IsOwned(mask, Equipped.Value)) RequestEquip(0);

            // Bought or picked something up: equip it.
            if (_pendingBuy >= 0 && WeaponCatalog.IsOwned(mask, _pendingBuy))
            {
                RequestEquip(_pendingBuy);
                _pendingBuy = -1;
            }
            else if (gained != 0)
            {
                for (int i = 1; i < 31; i++) if ((gained & (1 << i)) != 0) { RequestEquip(i); break; }
            }
        }

        public int OwnedMask() => Owned.Value | 1;

        // ============================================================ Firing (owner)

        void Fire(WeaponDefinition def, bool aiming)
        {
            int index = Equipped.Value;
            int seed = Random.Range(int.MinValue, int.MaxValue);
            Transform eye = playerCamera ? playerCamera.transform : transform;
            Vector3 origin = eye.position;
            Vector3 dir = eye.forward;
            Vector3 muzzlePos = _muzzle ? _muzzle.position : origin;
            bool airborne = !_player.IsGrounded;

            switch (def.kind)
            {
                case WeaponKind.Hitscan:
                    foreach (var pelletDir in PelletDirections(def, dir, aiming, seed))
                    {
                        Vector3 end = origin + pelletDir * def.range;
                        if (Physics.Raycast(origin, pelletDir, out var hit, def.range, ~0, QueryTriggerInteraction.Ignore))
                        {
                            end = hit.point;
                            var animal = hit.collider.GetComponentInParent<Animal>();
                            if (animal != null && animal.IsSpawned)
                            {
                                bool head = hit.collider.TryGetComponent(out Hitbox hb) && hb.IsHead;
                                ReportHit(index, animal, origin, hit.point, head, airborne);
                            }
                            else if (hit.collider.TryGetComponent(out FriendSlop.Player.PlayerHealth victim) && victim != _health)
                            {
                                ReportPlayerHit(index, victim, origin, hit.point);
                            }
                            else if (hit.rigidbody != null && hit.rigidbody.TryGetComponent(out NetworkPhysicsBox box))
                            {
                                // Shoving dropped guns, boxes, etc. Push a little upward too so
                                // things hop instead of just grinding along the ground.
                                box.RequestImpulse((pelletDir + Vector3.up * 0.35f).normalized * def.impulse, hit.point);
                                SfxPlayer.PlayAt(Sfx.HitTick, hit.point, 0.6f, 0.55f, 2f, 40f); // metallic tink
                            }
                        }
                        if (def.tracer) SpawnTracer(muzzlePos, end);
                    }
                    break;

                case WeaponKind.Projectile:
                case WeaponKind.Boomerang:
                    Vector3 spawn = origin + dir * 0.6f;
                    Vector3 pdir = PelletDirections(def, dir, aiming, seed)[0];
                    if (def.kind == WeaponKind.Boomerang)
                    {
                        _boomerangOut = true;
                        _boomerangTimeout = Time.time + 5f;
                        if (_model) _model.gameObject.SetActive(false);
                    }
                    Projectile.Launch(def, spawn, pdir, transform, authority: true,
                        onAnimalHit: (a, point, head) => ReportHit(index, a, origin, point, head, airborne),
                        onPlayerHit: (victim, point) => { if (victim != _health) ReportPlayerHit(index, victim, origin, point); },
                        onExplode: point => ExplodeRpc(index, point),
                        onFinished: def.kind == WeaponKind.Boomerang ? OnBoomerangBack : null);
                    break;
            }

            // Shooter feedback
            PlayShotSound(def, muzzlePos, positional: false);
            if (UsesMuzzleFlash(def)) MuzzleFlash(muzzlePos);
            _player.AddRecoil(def.recoil * (aiming ? 0.6f : 1f));
            if (gunHolder) gunHolder.localPosition = _holderRest + new Vector3(0f, 0.015f, -0.08f);

            FireRpc(index, origin, dir, seed, aiming);
        }

        /// <summary>Dev self-test: fire the equipped weapon straight ahead, ignoring input and cooldowns.</summary>
        public void TestFire()
        {
            var def = Current;
            if (!IsOwner || def == null) return;
            _boomerangOut = false;
            Fire(def, aiming: false);
        }

        /// <summary>Dev self-test: point the camera at a world position, then fire (aimed, so minimal spread).</summary>
        public void TestFireAt(Vector3 target)
        {
            var def = Current;
            if (!IsOwner || def == null || playerCamera == null) return;
            // The camera sits on the pitch pivot itself; aim it directly (NetworkPlayer restores its
            // own pitch next frame).
            var cam = playerCamera.transform;
            cam.rotation = Quaternion.LookRotation(target - cam.position);
            _boomerangOut = false;
            Fire(def, aiming: true);
        }

        void OnBoomerangBack()
        {
            _boomerangOut = false;
            if (_model) _model.gameObject.SetActive(true);
            SfxPlayer.Play2D(Sfx.Thud, 0.3f, 1.6f);
        }

        void ReportHit(int index, Animal animal, Vector3 origin, Vector3 point, bool head, bool airborne)
        {
            GameHud.ShowHitMarker(head);
            SfxPlayer.Play2D(head ? Sfx.HeadshotDing : Sfx.HitTick, head ? 0.7f : 0.5f);
            HitRpc(index, animal.NetworkObject, origin, point, head, airborne);
        }

        void ReportPlayerHit(int index, FriendSlop.Player.PlayerHealth victim, Vector3 origin, Vector3 point)
        {
            if (!FriendSlop.Game.GameState.FriendlyFireOn || victim.IsDown.Value) return;
            GameHud.ShowHitMarker(false);
            SfxPlayer.Play2D(Sfx.HitTick, 0.5f, 0.8f);
            PlayerHitRpc(index, victim.NetworkObject, origin, point);
        }

        // ============================================================ Host

        [Rpc(SendTo.Server)]
        void FireRpc(int index, Vector3 origin, Vector3 dir, int seed, bool aiming, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            var def = catalog.Get(index);
            if (def == null || (origin - transform.position).sqrMagnitude > 25f) return;

            if (def.noiseRadius > 0f)
            {
                foreach (var a in Animal.All)
                    if ((a.transform.position - origin).sqrMagnitude < def.noiseRadius * def.noiseRadius)
                        a.ServerHearShot(origin + dir * 10f, origin);
            }
            ShotFxRpc(index, origin, dir, seed, aiming);
        }

        [Rpc(SendTo.Server)]
        void HitRpc(int index, NetworkObjectReference target, Vector3 origin, Vector3 point, bool head, bool airborne,
                    RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            var def = catalog.Get(index);
            if (def == null || !WeaponCatalog.IsOwned(OwnedMask(), index)) return;
            if ((origin - transform.position).sqrMagnitude > 25f) return;

            float distance = Vector3.Distance(origin, point);
            float maxRange = def.kind == WeaponKind.Hitscan ? def.range + 5f : 300f;
            if (distance > maxRange) return;
            if (!target.TryGet(out NetworkObject obj) || !obj.TryGetComponent(out Animal animal)) return;
            float tolerance = def.kind == WeaponKind.Hitscan ? 3f : 4.5f;
            if ((obj.transform.position - point).sqrMagnitude > tolerance * tolerance) return;

            var style = ShotStyle.None;
            if (head) style |= ShotStyle.Headshot;
            if (distance >= LongShotDistance) style |= ShotStyle.LongShot;
            if (airborne) style |= ShotStyle.JumpShot;

            float damage = head ? def.damage * def.headshotMultiplier : def.damage;
            animal.ServerApplyHit(damage, (point - origin).normalized, style, OwnerClientId);
        }

        /// <summary>Host: friendly fire. Only when the lobby allows it; same sanity checks as animal hits.</summary>
        [Rpc(SendTo.Server)]
        void PlayerHitRpc(int index, NetworkObjectReference target, Vector3 origin, Vector3 point, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !FriendSlop.Game.GameState.FriendlyFireOn) return;
            var def = catalog.Get(index);
            if (def == null || !WeaponCatalog.IsOwned(OwnedMask(), index)) return;
            if ((origin - transform.position).sqrMagnitude > 25f) return;
            if (!target.TryGet(out NetworkObject obj) || !obj.TryGetComponent(out FriendSlop.Player.PlayerHealth victim) || obj.OwnerClientId == OwnerClientId) return;
            if ((obj.transform.position + Vector3.up - point).sqrMagnitude > 3f * 3f) return;
            bool head = point.y - obj.transform.position.y > 1.45f;
            float damage = def.damage * FriendSlop.Game.LobbySettings.PlayerDamageScale * (head ? 1.5f : 1f);
            victim.ServerDamage(damage, (point - origin).normalized * 3f, $"{def.displayName} (thanks, {FriendSlop.Game.GameState.PlayerName(OwnerClientId)})");
        }

        [Rpc(SendTo.Server)]
        void ExplodeRpc(int index, Vector3 point, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            var def = catalog.Get(index);
            if (def == null || !def.IsExplosive || !WeaponCatalog.IsOwned(OwnedMask(), index)) return;
            if ((point - transform.position).sqrMagnitude > 300f * 300f) return;

            Explosions.ServerExplode(point, def, OwnerClientId);
            foreach (var a in Animal.All) a.ServerHearShot(point, point);
            ExplosionFxRpc(point, def.explosionRadius);
        }

        // ============================================================ Everyone else

        [Rpc(SendTo.NotOwner)]
        void ShotFxRpc(int index, Vector3 origin, Vector3 dir, int seed, bool aiming)
        {
            var def = catalog.Get(index);
            if (def == null) return;
            Vector3 gunPos = origin + Vector3.down * 0.2f;

            switch (def.kind)
            {
                case WeaponKind.Hitscan:
                    if (!def.tracer) break;
                    foreach (var pelletDir in PelletDirections(def, dir, aiming, seed))
                    {
                        Vector3 end = Physics.Raycast(origin, pelletDir, out var hit, def.range, ~0, QueryTriggerInteraction.Ignore)
                            ? hit.point : origin + pelletDir * def.range;
                        SpawnTracer(gunPos, end);
                    }
                    break;
                default:
                    Projectile.Launch(def, origin + dir * 0.6f, PelletDirections(def, dir, aiming, seed)[0], transform,
                        authority: false, onAnimalHit: null, onExplode: null, onFinished: null);
                    break;
            }

            PlayShotSound(def, gunPos, positional: true);
            if (UsesMuzzleFlash(def)) MuzzleFlash(gunPos);
        }

        [Rpc(SendTo.Everyone)]
        void ExplosionFxRpc(Vector3 point, float radius) => Explosions.PlayFx(point, radius);

        // ============================================================ Helpers

        static List<Vector3> PelletDirections(WeaponDefinition def, Vector3 forward, bool aiming, int seed)
        {
            var rng = new System.Random(seed);
            float cone = aiming ? def.aimSpread : def.spread;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);

            int count = Mathf.Max(1, def.pellets);
            var dirs = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                float angle = cone * Mathf.Sqrt((float)rng.NextDouble()) * Mathf.Deg2Rad;
                float phi = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 offset = (right * Mathf.Cos(phi) + up * Mathf.Sin(phi)) * Mathf.Tan(angle);
                dirs.Add((forward + offset).normalized);
            }
            return dirs;
        }

        /// <summary>Rebuild the held model (e.g. after the outfit color changed).</summary>
        public void RefreshModel() { if (IsSpawned) RebuildModel(Equipped.Value); }

        void RebuildModel(int index)
        {
            if (gunHolder == null || catalog == null) return;
            if (_model) Destroy(_model.gameObject);
            var def = catalog.Get(index);
            if (def == null) return;
            _muzzle = WeaponModelFactory.Build(def, gunHolder);
            _model = _muzzle.parent;
            if (IsOwner)
            {
                // Your own chunky mitts on the gun, in your outfit color.
                var look = GetComponent<FriendSlop.Player.PlayerAppearance>();
                var color = FriendSlop.Profile.Cosmetics.BodyColor(look != null && look.IsSpawned ? look.ColorIndex.Value : -1, OwnerClientId);
                WeaponModelFactory.AddHands(_model, Color.Lerp(color, Color.white, 0.15f));
            }
            _boomerangOut = false;
        }

        static bool UsesMuzzleFlash(WeaponDefinition def) =>
            def.sound == ShotSound.Gunshot || def.sound == ShotSound.HeavyGunshot ||
            def.sound == ShotSound.LightGunshot || def.sound == ShotSound.Rocket;

        static void PlayShotSound(WeaponDefinition def, Vector3 pos, bool positional)
        {
            AudioClip clip;
            float maxDist = 260f;
            switch (def.sound)
            {
                case ShotSound.Bow: clip = Sfx.BowTwang; maxDist = 35f; break;
                case ShotSound.Slingshot: clip = Sfx.SlingThwip; maxDist = 30f; break;
                case ShotSound.Rocket: clip = Sfx.RocketLaunch; break;
                case ShotSound.Throw: clip = Sfx.Whoosh; maxDist = 30f; break;
                default: clip = Sfx.Gunshot; break;
            }
            if (positional) SfxPlayer.PlayAt(clip, pos, def.soundVolume * 1.2f, def.soundPitch, 6f, maxDist);
            else SfxPlayer.Play2D(clip, def.soundVolume, def.soundPitch);
        }

        static void MuzzleFlash(Vector3 position)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 6f;
            light.color = new Color(1f, 0.82f, 0.5f);
            light.shadows = LightShadows.None;
            Destroy(go, 0.05f);
        }

        void SpawnTracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.025f;
            line.endWidth = 0.012f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (tracerMaterial) line.sharedMaterial = tracerMaterial;
            StartCoroutine(FadeAndDestroy(line, 0.1f));
            Destroy(go, 1f);
        }

        static IEnumerator FadeAndDestroy(LineRenderer line, float seconds)
        {
            float t = 0f;
            while (t < seconds && line)
            {
                float k = 1f - t / seconds;
                line.startWidth = 0.025f * k;
                line.endWidth = 0.012f * k;
                t += Time.deltaTime;
                yield return null;
            }
            if (line) Destroy(line.gameObject);
        }
    }
}
