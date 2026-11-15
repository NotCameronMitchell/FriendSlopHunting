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
            ApplyPlayerColor();
            _lastStepPos = transform.position;

            bool mine = IsOwner;
            if (playerCamera) playerCamera.enabled = mine;
            if (audioListener) audioListener.enabled = mine;

            if (!mine) return;

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
            if (!IsOwner) return;
            if (_menuCamera) _menuCamera.SetActive(true);
            LocalHint = string.Empty;
            SetCursorLocked(false);
        }

        /// <summary>Kick the camera up; it settles back on its own. Owner only.</summary>
        public void AddRecoil(float degrees) => _recoil += degrees;

        void Update()
        {
            if (!IsSpawned) return;
            UpdateFootsteps();
            if (!IsOwner) return;

            if (Keyboard.current == null || Mouse.current == null) return;

            if (IsDown)
            {
                LocalHint = string.Empty;
                Move(controlling: false); // gravity + knockback only
                return;
            }

            if (Controls.Pressed(GameAction.ToggleMute)) GameSettings.MicMuted = !GameSettings.MicMuted;

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
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(_pitch - _recoil, 0f, 0f);
            Move(controlling);
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
            Vector2 input = Vector2.zero;
            if (controlling)
            {
                input.x = (Controls.Held(GameAction.MoveRight) ? 1f : 0f) - (Controls.Held(GameAction.MoveLeft) ? 1f : 0f);
                input.y = (Controls.Held(GameAction.MoveForward) ? 1f : 0f) - (Controls.Held(GameAction.MoveBack) ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            float speed = controlling && Controls.Held(GameAction.Sprint) ? sprintSpeed : walkSpeed;
            if (IsCarrying) speed *= CarrySpeedFactor();
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed + _knockback;
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 22f * Time.deltaTime);

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            if (controlling && _cc.isGrounded && Controls.Pressed(GameAction.Jump))
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _verticalVelocity += gravity * Time.deltaTime;

            _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);

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
                return Mathf.Clamp(carrySpeedMultiplier - a.WeightKg.Value / 900f, 0.3f, carrySpeedMultiplier);
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
                else if (Controls.Pressed(GameAction.Fire)) Release(_cc.velocity + LookDirection() * throwSpeed);
                return;
            }

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
                else if (LookAt<GunShop>(GunShop.InteractRange) != null)
                {
                    LocalHint = "E browse the gun shop";
                    if (controlling && Controls.Pressed(GameAction.Interact)) ShopMenu.Open();
                }
                else if (LookAt<BossTotem>(BossTotem.InteractRange) is BossTotem totem && totem.BossSpecies != null)
                {
                    LocalHint = BossTotem.BossAlive
                        ? $"{totem.BossSpecies.bossTitle} is already loose…"
                        : $"E summon {totem.BossSpecies.bossTitle} (${totem.SummonCost:N0}, bounty ${totem.BossSpecies.bounty:N0})";
                    if (!BossTotem.BossAlive && controlling && Controls.Pressed(GameAction.Interact)) totem.RequestSummon();
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
