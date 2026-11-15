using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Interaction;
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
        [SerializeField] float lookSensitivity = 0.12f;

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

        /// <summary>True while holding something, and on the frame you let go (so a throw click doesn't also fire).</summary>
        public bool IsCarrying => _held != null || _releasedFrame == Time.frameCount;
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        void Awake() => _cc = GetComponent<CharacterController>();

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

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (GameMenus.AnyOpen)
            {
                bool close = kb.escapeKey.wasPressedThisFrame
                             || (ShopMenu.IsOpen && kb.eKey.wasPressedThisFrame)
                             || (InventoryMenu.IsOpen && kb.tabKey.wasPressedThisFrame);
                if (close) GameMenus.Close();
                LocalHint = string.Empty;
                Move(kb, controlling: false); // keep gravity going
                return;
            }

            if (kb.tabKey.wasPressedThisFrame && !IsCarrying)
            {
                InventoryMenu.Toggle();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

            if (Application.isEditor && kb.f9Key.wasPressedThisFrame)
                GameState.Instance?.RequestDevMoney();

            bool controlling = Cursor.lockState == CursorLockMode.Locked;

            if (controlling) Look(mouse.delta.ReadValue());
            _recoil = Mathf.Lerp(_recoil, 0f, Time.deltaTime * 9f);
            if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(_pitch - _recoil, 0f, 0f);
            Move(kb, controlling);
            UpdateCarry(kb, mouse, controlling);
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
            delta *= lookSensitivity;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
        }

        void Move(Keyboard kb, bool controlling)
        {
            Vector2 input = Vector2.zero;
            if (controlling)
            {
                input.x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                input.y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            float speed = controlling && kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
            if (IsCarrying) speed *= carrySpeedMultiplier;
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            if (controlling && _cc.isGrounded && kb.spaceKey.wasPressedThisFrame)
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

        void Teleport(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _verticalVelocity = 0f;
        }

        // ------------------------------ Carrying ------------------------------

        void UpdateCarry(Keyboard kb, Mouse mouse, bool controlling)
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
                if (kb.eKey.wasPressedThisFrame) Release(_cc.velocity);
                else if (mouse.leftButton.wasPressedThisFrame) Release(_cc.velocity + LookDirection() * throwSpeed);
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
                    if (!have && controlling && kb.eKey.wasPressedThisFrame) pickup.RequestPickup();
                }
                else if (LookAt<GunShop>(GunShop.InteractRange) != null)
                {
                    LocalHint = "E browse the gun shop";
                    if (controlling && kb.eKey.wasPressedThisFrame) ShopMenu.Open();
                }
                else LocalHint = string.Empty;
                return;
            }

            LocalHint = target.TryGetComponent(out Animal animal)
                ? $"E pick up {animal.Species.displayName} ({animal.WeightKg.Value:0.0} kg, ~${animal.SaleValue})"
                : "E pick up";

            if (controlling && kb.eKey.wasPressedThisFrame && _pendingPickup == null)
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
