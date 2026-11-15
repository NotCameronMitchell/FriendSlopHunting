using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Interaction;
using FriendSlop.Settings;
using FriendSlop.Weapons;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    /// <summary>
    /// First-person co-op player. Movement is owner-authoritative: the owning client moves its own
    /// CharacterController and an Owner-mode NetworkTransform replicates it to everyone else.
    /// Shared objects (boxes, carcasses) are changed through server RPCs on those objects.
    /// Shooting lives in PlayerWeapons.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>The local player's current prompt for the HUD (empty when nothing to show).</summary>
        public static string LocalHint { get; private set; } = string.Empty;

        /// <summary>The local player's camera (name tags face it). Null outside a hunt.</summary>
        public static Transform LocalCamera { get; private set; }

        /// <summary>Every spawned player (all peers), and the local one.</summary>
        public static readonly System.Collections.Generic.List<NetworkPlayer> All = new();
        public static NetworkPlayer Local { get; private set; }

        /// <summary>In a cabin bed. When every hunter is asleep at night, the host skips to morning.</summary>
        public readonly NetworkVariable<bool> Sleeping = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        FriendSlop.World.Bed _bed;

        // Tank seat (every peer tracks it so the body can sit; the owner also drives the camera/position).
        Tank _tank;
        int _tankSeat = -1;
        public int TankSeat => _tankSeat;
        public Tank CurrentTank => _tank;
        public bool InTank => _tankSeat >= 0;
        /// <summary>Driver or gunner: the cannon replaces personal guns.</summary>
        public bool InTankControl => _tankSeat == 0 || _tankSeat == 1;

        /// <summary>Tint the body (from the player's chosen color).</summary>
        public void SetBodyColor(Color color)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in bodyRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        [Header("References")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] Camera playerCamera;
        [SerializeField] AudioListener audioListener;
        [SerializeField] Transform holdPoint;
        [SerializeField] Renderer[] bodyRenderers = System.Array.Empty<Renderer>();

        [Header("Movement")]
        [SerializeField] float walkSpeed = 5f;
        [SerializeField] float sprintSpeed = 8.5f;
        [SerializeField] float carrySpeedMultiplier = 0.8f;
        [SerializeField] float jumpHeight = 1.2f;
        [SerializeField] float gravity = -20f;

        [Header("Interaction")]
        [SerializeField] float interactRange = 3.5f;
        [SerializeField] float throwSpeed = 10f;
        [SerializeField] float walkPushImpulse = 1.5f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        CharacterController _cc;
        float _pitch;
        float _verticalVelocity;
        GameObject _menuCamera;
        Carryable _held;          // confirmed: the host made us the holder
        Carryable _pendingPickup; // requested, waiting for the host
        float _pendingUntil;

        int _releasedFrame = -1;
        float _recoil;
        Vector3 _lastStepPos;
        float _stepDistance;
        const float stepLength = 2.1f;

        Vector3 _knockback;
        PlayerHealth _health;
        PlayerUpgrades _upgrades;
        PlayerItems _items;
        Vector3[] _bodyRestPos;
        Quaternion[] _bodyRestRot;
        Vector3 _pivotRestPos;

        /// <summary>True while holding something, and on the frame you let go (so a throw click doesn't also fire).</summary>
        public bool IsCarrying => _held != null || _releasedFrame == Time.frameCount;
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public bool IsDown => _health != null && _health.IsSpawned && _health.IsDown.Value;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            _upgrades = GetComponent<PlayerUpgrades>();
            _items = GetComponent<PlayerItems>();
            if (cameraPivot) _pivotRestPos = cameraPivot.localPosition;
        }

        // ------------------------------ Health hooks ------------------------------

        /// <summary>Owner: get shoved (animal hits). Decays quickly.</summary>
        public void AddKnockback(Vector3 velocity)
        {
            _knockback += new Vector3(velocity.x, 0f, velocity.z);
            if (velocity.y > 0f) _verticalVelocity = Mathf.Max(_verticalVelocity, velocity.y);
        }

        /// <summary>Owner: just went down. Drop whatever we're holding.</summary>
        public void OnDowned()
        {
            if (_held != null) Release(Vector3.zero);
            if (GameMenus.AnyOpen) GameMenus.Close();
        }

        /// <summary>Everyone: lie the body down (or stand it back up).</summary>
        public void SetDownPose(bool down)
        {
            var hunter = GetComponentInChildren<HunterBody>();
            if (hunter != null) hunter.SetDown(down); // animated flop; the renderer shuffle below is for primitive bodies
            if (_bodyRestPos == null)
            {
                _bodyRestPos = new Vector3[bodyRenderers.Length];
                _bodyRestRot = new Quaternion[bodyRenderers.Length];
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    if (!bodyRenderers[i]) continue;
                    _bodyRestPos[i] = bodyRenderers[i].transform.localPosition;
                    _bodyRestRot[i] = bodyRenderers[i].transform.localRotation;
                }
            }

            var fall = Quaternion.Euler(-90f, 0f, 0f); // flat on your back
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var t = bodyRenderers[i] ? bodyRenderers[i].transform : null;
                if (t == null) continue;
                t.localPosition = down ? fall * _bodyRestPos[i] + Vector3.up * 0.35f : _bodyRestPos[i];
                t.localRotation = down ? fall * _bodyRestRot[i] : _bodyRestRot[i];
            }
            if (IsOwner && cameraPivot)
                cameraPivot.localPosition = down ? new Vector3(0f, 0.35f, 0f) : _pivotRestPos;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Sleeping.OnValueChanged += OnSleepingChanged;
            ApplyPlayerColor();
            _lastStepPos = transform.position;

            bool mine = IsOwner;
            if (playerCamera) playerCamera.enabled = mine;
            if (audioListener) audioListener.enabled = mine;

            if (!mine) return;
            LocalCamera = playerCamera ? playerCamera.transform : transform;
            Local = this;

            // Hide our own body from our camera but keep its shadow.
            foreach (var r in bodyRenderers)
                if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            // Swap the scene's menu camera out for ours.
            var main = Camera.main;
            if (main && main != playerCamera)
            {
                _menuCamera = main.gameObject;
                _menuCamera.SetActive(false);
            }

            // The server picked our spawn position; make the CharacterController pick it up
            // before our first Move() instead of snapping back to the prefab origin.
            _cc.enabled = false;
            _cc.enabled = true;

            SetCursorLocked(true);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Sleeping.OnValueChanged -= OnSleepingChanged;
            if (Local == this) Local = null;
            if (!IsOwner) return;
            if (_menuCamera) _menuCamera.SetActive(true);
            LocalCamera = null;
            LocalHint = string.Empty;
            SetCursorLocked(false);
        }

        // ------------------------------ Tank ------------------------------

        void TrackTankSeat()
        {
            Tank tank = null; int seat = -1;
            foreach (var t in Tank.All) { int s = t.SeatOf(OwnerClientId); if (s >= 0) { tank = t; seat = s; break; } }
            if (seat == _tankSeat && tank == _tank) return;
            var left = _tank;
            _tank = tank; _tankSeat = seat;
            var hunter = GetComponentInChildren<HunterBody>();
            if (hunter) hunter.Seated = seat >= 0;
            if (!IsOwner) return;
            if (seat >= 0)
            {
                if (_held != null) Release(Vector3.zero);
                _cc.enabled = false;
                // Driver/gunner watch from a chase camera, so show your own hunter.
                if (hunter) hunter.SetShadowOnly(seat >= 2);
            }
            else
            {
                // Hop out beside the tank.
                Vector3 at = left ? left.transform.position + left.transform.right * 3.2f : transform.position;
                at.y = Ground.HeightAt(at) + 0.2f;
                transform.position = at;
                _cc.enabled = true;
                _verticalVelocity = 0f;
                if (cameraPivot) { cameraPivot.localPosition = _pivotRestPos; cameraPivot.localRotation = Quaternion.identity; }
                _pitch = 0f;
                if (hunter) hunter.SetShadowOnly(true);
            }
        }

        /// <summary>Owner, after the tank moved: sit in the seat; driver/gunner get a chase camera behind the turret.</summary>
        void RideTank()
        {
            var seatPoint = _tank.SeatPoint(_tankSeat);
            transform.position = seatPoint.position;
            if (_tankSeat < 2)
            {
                transform.rotation = Quaternion.Euler(0f, seatPoint.eulerAngles.y, 0f);
                var aim = _tank.Turret ? _tank.Turret : _tank.transform;
                Vector3 fwd = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
                Vector3 pivot = aim.position + Vector3.up * 1.2f;
                Vector3 cam = aim.position - fwd * 9f + Vector3.up * 4f;
                // Pull the camera in if a roof, tree or wall is in the way (ignore the tank itself).
                var dir = cam - pivot;
                foreach (var hit in Physics.SphereCastAll(pivot, 0.35f, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(_tank.transform) || hit.distance <= 0f) continue;
                    if (hit.distance < (cam - pivot).magnitude) cam = pivot + dir.normalized * Mathf.Max(1.5f, hit.distance - 0.2f);
                }
                if (cameraPivot) cameraPivot.SetPositionAndRotation(cam, Quaternion.LookRotation(aim.position + fwd * 14f + Vector3.up * 0.5f - cam));
            }
        }

        // ------------------------------ Sleeping ------------------------------

        void GoToSleep(FriendSlop.World.Bed bed)
        {
            if (_held != null) Release(Vector3.zero);
            _bed = bed;
            _cc.enabled = false;
            transform.SetPositionAndRotation(bed.SleepPosition, bed.SleepRotation);
            _pitch = -70f; // staring at the rafters
            if (cameraPivot) { cameraPivot.localPosition = new Vector3(0f, 0.35f, 0.3f); cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f); }
            Sleeping.Value = true;
        }

        void WakeUp()
        {
            if (!Sleeping.Value) return;
            Sleeping.Value = false;
            transform.position = _bed ? FreeSpotNear(_bed.transform) : transform.position + Vector3.up * 0.1f;
            _pitch = 0f;
            if (cameraPivot) cameraPivot.localPosition = _pivotRestPos;
            _cc.enabled = true;
            _bed = null;
        }

        /// <summary>
        /// A spot beside the bed where a hunter fits (no walls, bunks or sleeping friends), on the floor.
        /// Tries the sides first, then the foot and head, then further out.
        /// </summary>
        Vector3 FreeSpotNear(Transform bed)
        {
            float radius = _cc ? _cc.radius + 0.05f : 0.4f, height = _cc ? _cc.height : 1.8f;
            Vector3 c = bed.position;
            Vector3[] dirs = { bed.right, -bed.right, bed.forward, -bed.forward, (bed.right + bed.forward).normalized,
                               (-bed.right + bed.forward).normalized, (bed.right - bed.forward).normalized, (-bed.right - bed.forward).normalized };
            var hits = new Collider[8];
            foreach (float dist in new[] { 1.1f, 1.6f, 2.2f, 3f })
                foreach (var d in dirs)
                {
                    Vector3 p = c + d * dist;
                    // Find the floor under the spot.
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) p.y = floor.point.y;
                    Vector3 bottom = p + Vector3.up * (radius + 0.08f), top = p + Vector3.up * (height - radius);
                    int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, hits, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int i = 0; i < n && !blocked; i++)
                        if (!hits[i].transform.IsChildOf(transform)) blocked = true;
                    if (!blocked) return p + Vector3.up * 0.05f;
                }
            return c + bed.right * 1.1f + Vector3.up * 0.6f; // last resort: as before, a bit higher
        }

        void OnSleepingChanged(bool previous, bool sleeping)
        {
            var hunter = GetComponentInChildren<HunterBody>();
            if (hunter != null) hunter.SetDown(sleeping || IsDown); // lie flat in bed
        }

        /// <summary>Host: morning came; get this player out of bed.</summary>
        public void ServerWake() { if (IsServer) WakeUpRpc(); }

        [Rpc(SendTo.Owner)]
        void WakeUpRpc() => WakeUp();

        /// <summary>Kick the camera up; it settles back on its own. Owner only.</summary>
        public void AddRecoil(float degrees) => _recoil += degrees;

        // ------------------------------------------------------------------ camera feel
        // Trauma-based shake (Perlin noise, so it rolls instead of jitters), head bob while walking,
        // a dip when you land, and a small FOV push while sprinting. All scaled by the player's
        // Screen shake / Head bob settings, since shake is the effect people most often turn off.

        float _trauma, _bobPhase, _bobBlend, _landDip, _landVel, _airMinVy, _sprintBlend;
        bool _wasGrounded = true;

        /// <summary>0..1 how much the local player is walking (for the gun bob).</summary>
        public float BobBlend => _bobBlend;
        /// <summary>Walk cycle phase in radians (two steps per cycle).</summary>
        public float BobPhase => _bobPhase;
        /// <summary>Extra field of view while sprinting (degrees, added by the weapon FOV logic).</summary>
        public float FovBonus => _sprintBlend * 6f * (GameSettings.HeadBob ? 1f : 0.5f);

        /// <summary>Shake the local player's camera. trauma 0..1 (0.15 a gunshot, 0.5 an explosion nearby, 1 a tank shell to the face).</summary>
        public static void Shake(float trauma)
        {
            if (Local == null) return;
            Local._trauma = Mathf.Clamp01(Local._trauma + trauma);
        }

        /// <summary>Shake scaled by distance from the local player (explosions, boss slams).</summary>
        public static void ShakeAt(Vector3 position, float trauma, float radius)
        {
            if (Local == null) return;
            float d = Vector3.Distance(Local.transform.position, position);
            if (d < radius) Shake(trauma * (1f - d / radius));
        }

        void TrackLanding(bool grounded, float sprinting, float moveAmount)
        {
            if (!grounded) _airMinVy = Mathf.Min(_airMinVy, _verticalVelocity);
            else if (!_wasGrounded)
            {
                float impact = Mathf.InverseLerp(4f, 18f, -_airMinVy);
                if (impact > 0f)
                {
                    _landVel -= 1.4f + impact * 2.2f; // spring kick downward
                    if (impact > 0.4f) Shake(impact * 0.35f);
                    SfxPlayer.Play2D(Sfx.Thud, 0.15f + impact * 0.35f, 1.15f - impact * 0.3f);
                }
                _airMinVy = 0f;
            }
            _wasGrounded = grounded;
            float targetBob = grounded ? moveAmount : 0f;
            _bobBlend = Mathf.MoveTowards(_bobBlend, targetBob, Time.deltaTime * 6f);
            _sprintBlend = Mathf.MoveTowards(_sprintBlend, grounded ? sprinting * moveAmount : _sprintBlend, Time.deltaTime * 4f);
        }

        void ApplyCameraFeel()
        {
            if (!cameraPivot) return;
            float dt = Time.deltaTime;
            // Landing spring (critically-ish damped).
            _landVel += (-_landDip * 90f - _landVel * 14f) * dt;
            _landDip += _landVel * dt;

            float shakeAmt = _trauma * _trauma * GameSettings.ScreenShake;
            _trauma = Mathf.Max(0f, _trauma - dt * 1.4f);
            float t = Time.time * 22f;
            float sx = (Mathf.PerlinNoise(t, 0.1f) * 2f - 1f) * 4f * shakeAmt;
            float sy = (Mathf.PerlinNoise(0.3f, t) * 2f - 1f) * 4f * shakeAmt;
            float sz = (Mathf.PerlinNoise(t, 7.7f) * 2f - 1f) * 6f * shakeAmt;

            bool bob = GameSettings.HeadBob;
            float amp = bob ? Mathf.Lerp(0.03f, 0.055f, _sprintBlend) * _bobBlend : 0f;
            Vector3 offset = new Vector3(Mathf.Sin(_bobPhase) * amp * 0.6f, Mathf.Abs(Mathf.Cos(_bobPhase)) * amp - amp * 0.5f, 0f);
            offset.y += _landDip * 0.06f;
            float roll = bob ? Mathf.Sin(_bobPhase) * 0.5f * _bobBlend : 0f;
            cameraPivot.localPosition = _pivotRestPos + offset;
            cameraPivot.localRotation = Quaternion.Euler(_pitch - _recoil + sx - _landDip * 1.2f, sy, sz + roll);
        }

        void Update()
        {
            if (!IsSpawned) return;
            UpdateFootsteps();
            TrackTankSeat();
            if (!IsOwner) return;

            if (Keyboard.current == null || Mouse.current == null) return;

            if (IsDown)
            {
                LocalHint = string.Empty;
                Move(controlling: false); // gravity + knockback only
                return;
            }

            if (Controls.Pressed(GameAction.ToggleMute)) GameSettings.MicMuted = !GameSettings.MicMuted;

            if (_tankSeat >= 0 && !GameMenus.AnyOpen)
            {
                if (Controls.Pressed(GameAction.Pause)) { GameMenus.Show(GameMenus.Menu.Pause); return; }
                if (Controls.Pressed(GameAction.Interact)) { _tank.RequestExit(); return; }
                if (Cursor.lockState != CursorLockMode.Locked && Mouse.current.leftButton.wasPressedThisFrame) SetCursorLocked(true);
                string[] roles = { "DRIVER: WASD drive", "GUNNER: mouse aim, click fire", "RIDER: shoot from the deck", "RIDER: shoot from the deck" };
                LocalHint = roles[_tankSeat] + (_tankSeat == 0 && _tank.Occupant(1) == Tank.Empty ? " · mouse aim, click fire" : "") + " · E hop out";
                if (_tankSeat >= 2 && Cursor.lockState == CursorLockMode.Locked)
                {
                    Look(Mouse.current.delta.ReadValue());
                    if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
                return;
            }

            if (Sleeping.Value)
            {
                LocalHint = "Sleeping. Press E, Space or move to get up.";
                if (Controls.Pressed(GameAction.Interact) || Controls.Pressed(GameAction.Jump) || Controls.Pressed(GameAction.MoveForward)
                    || Controls.Pressed(GameAction.MoveBack) || Controls.Pressed(GameAction.MoveLeft) || Controls.Pressed(GameAction.MoveRight))
                    WakeUp();
                return;
            }

            if (GameMenus.AnyOpen)
            {
                // Options backs out to the pause menu; everything else closes.
                if (OptionsMenu.IsCapturingKey) { /* rebinding: swallow input */ }
                else if (Controls.Pressed(GameAction.Pause))
                {
                    if (GameMenus.Open == GameMenus.Menu.Options) GameMenus.Show(GameMenus.Menu.Pause);
                    else GameMenus.Close();
                }
                else if ((ShopMenu.IsOpen && Controls.Pressed(GameAction.Interact))
                         || (InventoryMenu.IsOpen && Controls.Pressed(GameAction.Inventory)))
                    GameMenus.Close();
                LocalHint = string.Empty;
                Move(controlling: false); // keep gravity going
                return;
            }

            if (Controls.Pressed(GameAction.Inventory) && !IsCarrying)
            {
                InventoryMenu.Toggle();
                return;
            }

            if (Controls.Pressed(GameAction.Pause))
            {
                GameMenus.Show(GameMenus.Menu.Pause);
                return;
            }

            // Clicked back into the game window after alt-tabbing: grab the mouse again.
            if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);

            if (Application.isEditor && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
                GameState.Instance?.RequestDevMoney();

            bool controlling = Cursor.lockState == CursorLockMode.Locked;

            if (controlling) Look(Mouse.current.delta.ReadValue());
            _recoil = Mathf.Lerp(_recoil, 0f, Time.deltaTime * 9f);
            Move(controlling);
            ApplyCameraFeel();
            UpdateCarry(controlling);
        }

        void UpdateFootsteps()
        {
            Vector3 p = transform.position;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 d = p - _lastStepPos;
            float verticalSpeed = Mathf.Abs(d.y) / dt;
            d.y = 0f;
            float moved = d.magnitude;
            _lastStepPos = p;

            bool grounded = IsOwner ? _cc.isGrounded : verticalSpeed < 1.5f;
            if (!grounded || moved > 3f) return; // airborne or teleported

            _stepDistance += moved;
            if (_stepDistance < stepLength) return;
            _stepDistance = 0f;

            if (IsOwner) SfxPlayer.Play2D(Sfx.RandomFootstep(), 0.22f);
            else SfxPlayer.PlayAt(Sfx.RandomFootstep(), p, 0.7f, 1f, minDistance: 1.5f, maxDistance: 25f);
        }

        void LateUpdate()
        {
            if (IsOwner && _tankSeat >= 0 && _tank) { RideTank(); return; }
            if (IsOwner && _held != null && holdPoint)
                _held.FollowHoldPoint(holdPoint);
        }

        // ------------------------------ Movement ------------------------------

        void Look(Vector2 delta)
        {
            delta *= GameSettings.MouseSensitivity;
            if (GameSettings.InvertY) delta.y = -delta.y;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
        }

        void Move(bool controlling)
        {
            if (!_cc.enabled) return; // seated or asleep
            Vector2 input = Vector2.zero;
            if (controlling)
            {
                input.x = (Controls.Held(GameAction.MoveRight) ? 1f : 0f) - (Controls.Held(GameAction.MoveLeft) ? 1f : 0f);
                input.y = (Controls.Held(GameAction.MoveForward) ? 1f : 0f) - (Controls.Held(GameAction.MoveBack) ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            float speed = controlling && Controls.Held(GameAction.Sprint) ? sprintSpeed : walkSpeed;
            float wade = FriendSlop.World.WaterVolume.DepthAt(transform.position);
            if (wade > 0.2f) speed *= Mathf.Lerp(0.75f, 0.5f, Mathf.InverseLerp(0.2f, 1.2f, wade)); // sloshing through water
            if (_upgrades) speed *= _upgrades.SpeedMult;
            if (_items) speed *= _items.SpeedMult; // energy drink
            if (IsCarrying) speed *= CarrySpeedFactor();
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed + _knockback;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 22f * Time.deltaTime);

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            if (controlling && _cc.isGrounded && Controls.Pressed(GameAction.Jump))
                _verticalVelocity = Mathf.Sqrt(jumpHeight * (_upgrades ? _upgrades.JumpMult : 1f) * -2f * gravity);
            _verticalVelocity += gravity * Time.deltaTime;

            _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
            if (controlling || _bobBlend > 0f)
            {
                bool sprinting = controlling && Controls.Held(GameAction.Sprint);
                float moveAmount = input.magnitude;
                _bobPhase += Time.deltaTime * moveAmount * speed * 1.35f; // ~two steps per cycle at walking pace
                TrackLanding(_cc.isGrounded, sprinting ? 1f : 0f, moveAmount);
            }

            // Safety net while testing: fell off the world.
            if (transform.position.y < -30f) Teleport(Vector3.up * 2f);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!IsOwner || hit.rigidbody == null) return;
            if (hit.moveDirection.y < -0.3f) return; // standing on it, not walking into it
            if (!hit.rigidbody.TryGetComponent(out NetworkPhysicsBox box)) return;

            var dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z).normalized;
            box.RequestPush(dir * walkPushImpulse, hit.point);
        }

        /// <summary>Heavier carcasses slow you down more: a rabbit barely matters, a moose is a slog.</summary>
        float CarrySpeedFactor()
        {
            if (_held != null && _held.TryGetComponent(out Animal a))
            {
                float f = Mathf.Clamp(carrySpeedMultiplier - a.WeightKg.Value / 900f, 0.3f, carrySpeedMultiplier);
                return Mathf.Lerp(f, 1f, 0.4f * (_upgrades ? _upgrades.CarryTier : 0)); // Strong Back
            }
            return carrySpeedMultiplier;
        }

        public void Teleport(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _verticalVelocity = 0f;
        }

        // ------------------------------ Carrying ------------------------------

        void UpdateCarry(bool controlling)
        {
            // Resolve a pending pickup once the host confirms (or give up after a moment).
            if (_pendingPickup != null)
            {
                if (_pendingPickup.IsHeldByLocalPlayer)
                {
                    _held = _pendingPickup;
                    _pendingPickup = null;
                    SfxPlayer.Play2D(Sfx.Thud, 0.45f, 1.3f);
                }
                else if (Time.time > _pendingUntil || _pendingPickup.IsHeld) _pendingPickup = null;
            }
            // Lost it (host released it, object despawned/sold).
            if (_held != null && (!_held.IsSpawned || !_held.IsHeldByLocalPlayer)) _held = null;

            if (_held != null)
            {
                LocalHint = "E drop · Left click throw · Take it to the trader to sell";
                if (!controlling) return;
                if (Controls.Pressed(GameAction.Interact)) Release(_cc.velocity);
                else if (Controls.Pressed(GameAction.Fire)) Release(_cc.velocity + LookDirection() * throwSpeed * (1f + 0.3f * (_upgrades ? _upgrades.CarryTier : 0)));
                return;
            }

            GambleBetSpot.Focused = false;
            var target = LookAtCarryable();
            if (target == null)
            {
                var pickup = LookAt<WeaponPickup>(interactRange + 1f);
                if (pickup != null && pickup.IsSpawned && pickup.Definition != null)
                {
                    bool have = TryGetComponent(out PlayerWeapons pw) && pw.Owns(pickup.WeaponIndex.Value);
                    LocalHint = have ? $"You already have a {pickup.Definition.displayName}" : $"E pick up {pickup.Definition.displayName}";
                    if (!have && controlling && Controls.Pressed(GameAction.Interact)) pickup.RequestPickup();
                }
                else if (LookAt<Tank>(Tank.InteractRange) is Tank tank)
                {
                    LocalHint = tank.Full ? "The tank is full" : "E climb into the tank";
                    if (!tank.Full && controlling && Controls.Pressed(GameAction.Interact)) tank.RequestEnter();
                }
                else if (LookAt<FriendSlop.World.Bed>(FriendSlop.World.Bed.InteractRange) is FriendSlop.World.Bed bed)
                {
                    bool night = FriendSlop.World.DayNightCycle.NightNow;
                    LocalHint = night ? "E sleep (everyone sleeping skips the night)" : "Beds are for nights. Come back after dark.";
                    if (night && controlling && Controls.Pressed(GameAction.Interact)) GoToSleep(bed);
                }
                else if (LookAt<GambleBetSpot>(GambleBetSpot.InteractRange) is GambleBetSpot betSpot)
                {
                    GambleBetSpot.Focused = true;
                    if (controlling && UnityEngine.InputSystem.Mouse.current != null)
                    {
                        float scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y;
                        if (Mathf.Abs(scroll) > 0.01f) GambleBetSpot.ChangeBet(scroll > 0f ? 1 : -1);
                    }
                    LocalHint = betSpot.Hint();
                    if (controlling && Controls.Pressed(GameAction.Interact)) betSpot.Use();
                }
                else if (LookAt<LootCrate>(LootCrate.InteractRange) is LootCrate crate)
                {
                    LocalHint = crate.Looted ? $"The {crate.Label} is empty. It restocks tomorrow." : $"E open the {crate.Label}";
                    if (!crate.Looted && controlling && Controls.Pressed(GameAction.Interact)) crate.RequestOpen();
                }
                else if (LookAt<GunShop>(GunShop.InteractRange) != null)
                {
                    LocalHint = "E browse the gun shop";
                    if (controlling && Controls.Pressed(GameAction.Interact)) ShopMenu.Open();
                }
                else if (LookAt<BossTotem>(BossTotem.InteractRange) is BossTotem totem && totem.BossSpecies != null)
                {
                    LocalHint = totem.MyBossAlive
                        ? $"{totem.BossSpecies.bossTitle} is already loose…"
                        : $"E summon {totem.BossSpecies.bossTitle} (${totem.SummonCost:N0}, bounty ${totem.BossSpecies.bounty:N0})";
                    if (!totem.MyBossAlive && controlling && Controls.Pressed(GameAction.Interact)) totem.RequestSummon();
                }
                else LocalHint = string.Empty;
                return;
            }

            LocalHint = target.TryGetComponent(out Animal animal)
                ? $"E pick up {animal.Species.displayName} ({animal.WeightKg.Value:0.0} kg, ~${animal.SaleValue})"
                : "E pick up";

            if (controlling && Controls.Pressed(GameAction.Interact) && _pendingPickup == null)
            {
                _pendingPickup = target;
                _pendingUntil = Time.time + 1.5f;
                target.RequestPickup();
            }
        }

        void Release(Vector3 velocity)
        {
            var c = _held;
            _held = null;
            _releasedFrame = Time.frameCount;
            c.RequestDrop(velocity);
            if (velocity.magnitude > 6f) SfxPlayer.Play2D(Sfx.Whoosh, 0.55f);
            else SfxPlayer.Play2D(Sfx.Thud, 0.35f);
        }

        Carryable LookAtCarryable()
        {
            Transform eye = playerCamera ? playerCamera.transform : transform;
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, interactRange, ~0, QueryTriggerInteraction.Ignore))
                return null;
            var c = hit.collider.GetComponentInParent<Carryable>();
            return c != null && c.IsSpawned && c.CanCarry.Value && !c.IsHeld ? c : null;
        }

        T LookAt<T>(float range) where T : Component
        {
            Transform eye = playerCamera ? playerCamera.transform : transform;
            return Physics.Raycast(eye.position, eye.forward, out var hit, range, ~0, QueryTriggerInteraction.Ignore)
                ? hit.collider.GetComponentInParent<T>()
                : null;
        }

        Vector3 LookDirection() => playerCamera ? playerCamera.transform.forward : transform.forward;

        // ------------------------------ Cosmetics ------------------------------

        void ApplyPlayerColor()
        {
            // Stable, distinct-ish color per client id so friends can tell each other apart.
            float hue = (OwnerClientId * 0.618034f) % 1f;
            var color = Color.HSVToRGB(hue, 0.65f, 0.95f);
            var block = new MaterialPropertyBlock();
            foreach (var r in bodyRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
