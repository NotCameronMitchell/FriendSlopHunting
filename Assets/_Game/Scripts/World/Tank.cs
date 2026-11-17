using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.Player;
using FriendSlop.Settings;
using FriendSlop.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// The team tank. Four seats:
    ///   0 Driver: WASD drives (and aims/fires the cannon when the gunner seat is empty).
    ///   1 Gunner: mouse swings the turret, click fires explosive shells.
    ///   2–3 Riders: sit on the back deck, free look, shoot their own guns.
    /// The driver owns the tank (they simulate its physics); the host assigns seats and validates
    /// shells. Running animals over at speed flattens them.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Tank : NetworkBehaviour
    {
        public const ulong Empty = ulong.MaxValue;
        public const int Seats = 4;
        public static readonly List<Tank> All = new();

        [SerializeField] Transform turret;
        [SerializeField] Transform barrel;
        [SerializeField] Transform muzzle;
        [SerializeField] Transform[] seatPoints = new Transform[Seats];
        [SerializeField] float maxSpeed = 9f;
        [SerializeField] float turnRate = 55f;
        [SerializeField] float reload = 2.2f;
        public const float InteractRange = 5f;

        // Seats: one field each (Netcode only discovers NetworkVariables declared as fields).
        readonly NetworkVariable<ulong> _seat0 = new(Empty), _seat1 = new(Empty), _seat2 = new(Empty), _seat3 = new(Empty);
        /// <summary>Turret yaw (relative to hull) and barrel pitch, written by the host from the aimer's input.</summary>
        readonly NetworkVariable<Vector2> _aim = new(Vector2.zero);

        Rigidbody _rb;
        WeaponDefinition _shell;
        float _nextFire, _nextAimSend;
        Vector2 _localAim;          // the aimer's own (unsent) aim, so it feels instant
        float _throttle, _steer;
        float _debugDriveUntil, _debugThrottle, _debugSteer;

        /// <summary>Dev: drive without input for a few seconds (bridge self-tests).</summary>
        public void DebugDrive(float throttle, float steer, float seconds) { _debugThrottle = throttle; _debugSteer = steer; _debugDriveUntil = Time.time + seconds; }
        /// <summary>Dev: fire the cannon now (if this client is aiming).</summary>
        public void DebugFire() { if (SeatOf(NetworkManager.LocalClientId) == AimerSeat) FireLocal(); }
        /// <summary>Dev: set the turret aim (if this client is aiming).</summary>
        public void DebugAim(float yaw, float pitch) { _localAim = new Vector2(yaw, pitch); AimRpc(_localAim); }
        readonly Dictionary<Animal, float> _roadkilled = new();

        NetworkVariable<ulong> Seat(int i) => i switch { 0 => _seat0, 1 => _seat1, 2 => _seat2, _ => _seat3 };
        public ulong Occupant(int i) => Seat(i).Value;
        public bool Full { get { for (int i = 0; i < Seats; i++) if (Occupant(i) == Empty) return false; return true; } }
        public float ReloadFraction => Mathf.Clamp01(1f - (_nextFire - Time.time) / reload);
        public int AimerSeat => Occupant(1) != Empty ? 1 : 0;

        public int SeatOf(ulong clientId)
        {
            for (int i = 0; i < Seats; i++) if (Occupant(i) == clientId) return i;
            return -1;
        }

        public Transform SeatPoint(int i) => seatPoints != null && i < seatPoints.Length && seatPoints[i] ? seatPoints[i] : transform;
        public Transform Turret => turret;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass = new Vector3(0f, 0.3f, 0f); // low and wide: hard to tip over
            _shell = ScriptableObject.CreateInstance<WeaponDefinition>();
            _shell.displayName = "Tank Shell";
            _shell.kind = WeaponKind.Projectile;
            _shell.projectileSpeed = 75f;
            _shell.projectileGravity = 3f;
            _shell.explosionRadius = 6.5f;
            _shell.explosionDamage = 260f;
            _shell.explosionForce = 26f;
            _shell.sound = ShotSound.Rocket;
            _shell.soundPitch = 0.6f;
            _shell.soundVolume = 1f;
            _shell.model = WeaponModel.Bazooka;
            _shell.tracer = false;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnDisconnect;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnDisconnect;
        }

        void OnDisconnect(ulong id) { int s = SeatOf(id); if (s >= 0) ServerVacate(s); }

        // ------------------------------------------------------------------ seats

        public void RequestEnter() => EnterRpc();
        public void RequestExit() => ExitRpc();

        [Rpc(SendTo.Server)]
        void EnterRpc(RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            if (SeatOf(who) >= 0) return;
            foreach (var t in All) if (t != this && t.SeatOf(who) >= 0) return; // already in another tank
            for (int i = 0; i < Seats; i++)
            {
                if (Occupant(i) != Empty) continue;
                Seat(i).Value = who;
                if (i == 0) NetworkObject.ChangeOwnership(who); // the driver simulates the tank
                return;
            }
        }

        [Rpc(SendTo.Server)]
        void ExitRpc(RpcParams rpcParams = default)
        {
            int s = SeatOf(rpcParams.Receive.SenderClientId);
            if (s >= 0) ServerVacate(s);
        }

        /// <summary>Host: empty a seat (exit, downed, disconnected).</summary>
        public void ServerVacate(int seat)
        {
            if (!IsServer) return;
            Seat(seat).Value = Empty;
            if (seat == 0)
            {
                NetworkObject.RemoveOwnership();
                _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;

            // Host: kick out anyone who got downed.
            if (IsServer)
                for (int i = 0; i < Seats; i++)
                {
                    ulong id = Occupant(i);
                    if (id == Empty) continue;
                    var obj = NetworkManager.SpawnManager.GetPlayerNetworkObject(id);
                    if (obj == null || (obj.TryGetComponent(out PlayerHealth hp) && hp.IsDown.Value)) ServerVacate(i);
                }

            ulong me = NetworkManager.LocalClientId;
            int seat = SeatOf(me);
            if (seat < 0 || FriendSlop.Game.GameMenus.AnyOpen) { _throttle = _steer = 0f; return; }

            if (seat == 0)
            {
                _throttle = (Controls.Held(GameAction.MoveForward) ? 1f : 0f) - (Controls.Held(GameAction.MoveBack) ? 1f : 0f);
                _steer = (Controls.Held(GameAction.MoveRight) ? 1f : 0f) - (Controls.Held(GameAction.MoveLeft) ? 1f : 0f);
                if (Time.time < _debugDriveUntil) { _throttle = _debugThrottle; _steer = _debugSteer; }
            }

            if (seat == AimerSeat && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.delta.ReadValue() : Vector2.zero;
                d *= FriendSlop.Settings.GameSettings.MouseSensitivity;
                if (FriendSlop.Settings.GameSettings.InvertY) d.y = -d.y;
                _localAim.x = Mathf.Repeat(_localAim.x + d.x + 180f, 360f) - 180f;
                _localAim.y = Mathf.Clamp(_localAim.y - d.y, -20f, 12f);
                if (Time.time > _nextAimSend) { _nextAimSend = Time.time + 0.05f; AimRpc(_localAim); }
                if (Controls.Pressed(GameAction.Fire) && Time.time >= _nextFire) FireLocal();
            }
        }

        void FixedUpdate()
        {
            if (!IsSpawned) return;
            // Only the owner (driver, or the host when empty) simulates; everyone else follows the network transform.
            if (_rb.isKinematic == IsOwner) _rb.isKinematic = !IsOwner;
            bool simulating = IsOwner && (Occupant(0) == NetworkManager.LocalClientId || (IsServer && Occupant(0) == Empty));
            if (!simulating) return;

            // Tank steering: velocity along the hull, yaw on the spot; gravity handles hills.
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var v = _rb.linearVelocity;
            var target = fwd * (_throttle * maxSpeed);
            var horiz = Vector3.MoveTowards(new Vector3(v.x, 0f, v.z), target, (Mathf.Abs(_throttle) > 0.01f ? 9f : 14f) * Time.fixedDeltaTime);
            // Apply as velocity-change forces (assigning velocity directly gets overwritten by the network rigidbody sync).
            _rb.AddForce(new Vector3(horiz.x - v.x, 0f, horiz.z - v.z), ForceMode.VelocityChange);
            float turn = _steer * turnRate * (_throttle < -0.01f ? -1f : 1f) * Mathf.Deg2Rad;
            var w = _rb.angularVelocity;
            _rb.AddTorque(new Vector3(-w.x * 0.1f, turn - w.y, -w.z * 0.1f), ForceMode.VelocityChange);

            // Keep it upright-ish (no flipping on hills) but allow tilting with the slope.
            var up = transform.up;
            if (Vector3.Angle(up, Vector3.up) > 40f)
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(fwd, Vector3.up), 0.1f));
            if (transform.position.y < -30f) { _rb.position = new Vector3(0f, 5f, -10f); _rb.linearVelocity = Vector3.zero; }
        }

        void LateUpdate()
        {
            if (!IsSpawned || turret == null) return;
            bool iAim = SeatOf(NetworkManager.LocalClientId) == AimerSeat;
            Vector2 aim = iAim ? _localAim : _aim.Value;
            turret.localRotation = Quaternion.Euler(0f, aim.x, 0f);
            if (barrel) barrel.localRotation = Quaternion.Euler(aim.y, 0f, 0f);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void AimRpc(Vector2 aim, RpcParams rpcParams = default)
        {
            if (SeatOf(rpcParams.Receive.SenderClientId) != AimerSeat) return;
            _aim.Value = new Vector2(Mathf.Repeat(aim.x + 180f, 360f) - 180f, Mathf.Clamp(aim.y, -20f, 12f));
        }

        // ------------------------------------------------------------------ cannon

        void FireLocal()
        {
            _nextFire = Time.time + reload;
            var origin = muzzle ? muzzle.position : transform.position + transform.forward * 3f;
            var dir = muzzle ? muzzle.forward : transform.forward;
            Projectile.Launch(_shell, origin, dir, transform, authority: true, onAnimalHit: null,
                onExplode: point => ShellExplodeRpc(point), onFinished: null);
            SfxPlayer.Play2D(Sfx.RocketLaunch, 1f, 0.55f);
            SfxPlayer.Play2D(Sfx.Explosion, 0.35f, 1.6f);
            FriendSlop.Player.NetworkPlayer.Shake(0.35f);
            if (_rb) _rb.AddForce(-dir * 6f, ForceMode.VelocityChange); // recoil kick (if we simulate)
            FireFxRpc(origin, dir);
        }

        [Rpc(SendTo.NotMe)]
        void FireFxRpc(Vector3 origin, Vector3 dir)
        {
            Projectile.Launch(_shell, origin, dir, transform, authority: false, onAnimalHit: null, onExplode: null, onFinished: null);
            SfxPlayer.PlayAt(Sfx.RocketLaunch, origin, 1f, 0.55f, 8f, 300f);
        }

        [Rpc(SendTo.Server)]
        void ShellExplodeRpc(Vector3 point, RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            if (SeatOf(who) != AimerSeat || (point - transform.position).sqrMagnitude > 350f * 350f) return;
            Explosions.ServerExplode(point, _shell, who);
            foreach (var a in Animal.All) a.ServerHearShot(point, point);
            ExplodeFxRpc(point);
        }

        [Rpc(SendTo.Everyone)]
        void ExplodeFxRpc(Vector3 point) => Explosions.PlayFx(point, _shell.explosionRadius);

        // ------------------------------------------------------------------ roadkill

        void OnCollisionEnter(Collision c)
        {
            if (!IsOwner || c.relativeVelocity.magnitude < 3.5f) return; // impact speed (the hull has already slowed by now)
            var animal = c.collider.GetComponentInParent<Animal>();
            if (animal == null || !animal.IsSpawned || animal.IsDead.Value) return;
            if (_roadkilled.TryGetValue(animal, out float t) && Time.time - t < 1f) return;
            _roadkilled[animal] = Time.time;
            RoadkillRpc(animal.NetworkObject, -c.relativeVelocity);
        }

        [Rpc(SendTo.Server)]
        void RoadkillRpc(NetworkObjectReference target, Vector3 velocity, RpcParams rpcParams = default)
        {
            ulong driver = rpcParams.Receive.SenderClientId;
            if (SeatOf(driver) != 0 || !target.TryGet(out NetworkObject obj) || !obj.TryGetComponent(out Animal animal)) return;
            if ((obj.transform.position - transform.position).sqrMagnitude > 8f * 8f) return;
            animal.ServerApplyHit(Mathf.Clamp(velocity.magnitude * 60f, 250f, 800f), (velocity.normalized + Vector3.up).normalized, ShotStyle.None, driver, "Tank");
            SquishRpc(obj.transform.position);
        }

        [Rpc(SendTo.Everyone)]
        void SquishRpc(Vector3 at) => SfxPlayer.PlayAt(Sfx.Thud, at, 1f, 0.6f, 4f, 60f);
    }
}
