using FriendSlop.Audio;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// A weapon lying in the world (dropped with G, or left behind by a player who quit).
    /// Look at it and press E to put it in your inventory. That's how friends hand guns over.
    /// It's a real physics object: it lies on the ground with a hitbox fitted to the model, and
    /// can be kicked, shot or blown around (via NetworkPhysicsBox). The host simulates physics;
    /// everyone builds the matching model and collider locally.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WeaponPickup : NetworkBehaviour
    {
        [SerializeField] WeaponCatalog catalog;
        [SerializeField] Transform modelRoot;
        [SerializeField] float despawnAfterSeconds = 900f;
        [Tooltip("Dropped guns are drawn a bit bigger than in-hand so they're easy to spot.")]
        [SerializeField] float modelScale = 1.4f;

        public readonly NetworkVariable<int> WeaponIndex = new NetworkVariable<int>(-1);

        float _spawnTime;
        Transform _model;

        public WeaponDefinition Definition => catalog != null ? catalog.Get(WeaponIndex.Value) : null;

        public override void OnNetworkSpawn()
        {
            _spawnTime = Time.time;
            WeaponIndex.OnValueChanged += OnIndexChanged;
            BuildModel();
            if (!IsServer && TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        }

        public override void OnNetworkDespawn() => WeaponIndex.OnValueChanged -= OnIndexChanged;

        void OnIndexChanged(int previous, int current) => BuildModel();

        void BuildModel()
        {
            if (_model) Destroy(_model.gameObject);
            var def = Definition;
            if (def == null || modelRoot == null) return;

            // Lay the gun on its side, like it was dropped on the ground.
            modelRoot.localPosition = Vector3.zero;
            modelRoot.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var muzzle = WeaponModelFactory.Build(def, modelRoot);
            _model = muzzle.parent;
            _model.localScale = Vector3.one * modelScale;
            FitColliderToModel();
        }

        /// <summary>
        /// Size the physics box to the actual model, so each gun rests and collides with its own
        /// shape (a pistol is small, a minigun is long) instead of a generic crate.
        /// </summary>
        void FitColliderToModel()
        {
            if (!TryGetComponent(out BoxCollider box) || _model == null) return;

            bool any = false;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var mf in _model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            if (!any) return;

            var size = max - min;
            const float minThickness = 0.08f; // keep thin parts (bow string) clickable and stable
            box.size = new Vector3(Mathf.Max(size.x, minThickness), Mathf.Max(size.y, minThickness), Mathf.Max(size.z, minThickness));
            box.center = (min + max) * 0.5f;
        }

        void Update()
        {
            if (IsServer && IsSpawned && (Time.time - _spawnTime > despawnAfterSeconds || transform.position.y < -30f))
                NetworkObject.Despawn(true);
        }

        /// <summary>Client: ask the host to pick this up.</summary>
        public void RequestPickup() => PickupRpc();

        [Rpc(SendTo.Server)]
        void PickupRpc(RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            if (!IsSpawned || !NetworkManager.ConnectedClients.TryGetValue(who, out var client) || client.PlayerObject == null) return;
            if ((client.PlayerObject.transform.position - transform.position).sqrMagnitude > 4.5f * 4.5f) return;
            if (!client.PlayerObject.TryGetComponent(out PlayerWeapons weapons)) return;
            if (!weapons.ServerGive(WeaponIndex.Value)) return; // already has one

            PickedUpRpc(transform.position);
            NetworkObject.Despawn(true);
        }

        [Rpc(SendTo.Everyone)]
        void PickedUpRpc(Vector3 position) => SfxPlayer.PlayAt(Sfx.Thud, position, 0.6f, 1.5f, 2f, 20f);
    }
}
