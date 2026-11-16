using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Interaction
{
    /// <summary>
    /// Something a player can pick up, carry and throw (carcasses now; props later).
    ///
    /// Flow: client asks the host to pick up → host checks distance and that it's free, records the
    /// holder and hands network ownership to them → the holder moves it locally every frame (no lag
    /// on their screen) and an Owner-mode NetworkTransform replicates it → on drop/throw the host
    /// takes ownership back and simulates physics again.
    /// Requires an Owner-authority NetworkTransform on the same object.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Carryable : NetworkBehaviour
    {
        public const ulong NoHolder = ulong.MaxValue;
        const float MaxPickupDistance = 4.5f;
        const float MaxThrowSpeed = 22f;

        public readonly NetworkVariable<ulong> HolderId = new NetworkVariable<ulong>(NoHolder);
        public readonly NetworkVariable<bool> CanCarry = new NetworkVariable<bool>(false);

        Rigidbody _rb;
        Collider[] _colliders;

        public Rigidbody Body => _rb;
        public bool IsHeld => HolderId.Value != NoHolder;
        public bool IsHeldByLocalPlayer => IsSpawned && IsHeld && HolderId.Value == NetworkManager.LocalClientId;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            if (TryGetComponent(out NetworkObject no)) no.DontDestroyWithOwner = true;
        }

        public override void OnNetworkSpawn()
        {
            HolderId.OnValueChanged += OnHolderChanged;
            ApplyHeldState(IsHeld);
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            HolderId.OnValueChanged -= OnHolderChanged;
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void OnHolderChanged(ulong previous, ulong current) => ApplyHeldState(current != NoHolder);

        void ApplyHeldState(bool held)
        {
            // Held objects shouldn't shove their carrier or trigger the sell zone mid-carry.
            foreach (var c in _colliders) if (c) c.enabled = !held;
        }

        // ------------------------- Server API -------------------------

        public void ServerSetCarryable(bool value)
        {
            if (IsServer) CanCarry.Value = value;
        }

        // ------------------------- Client requests -------------------------

        public void RequestPickup() => PickupRpc();

        public void RequestDrop(Vector3 velocity) => DropRpc(transform.position, transform.rotation, velocity);

        /// <summary>Holder only: snap to the hold point. Called from the player's LateUpdate.</summary>
        public void FollowHoldPoint(Transform holdPoint)
        {
            if (!IsHeldByLocalPlayer || !IsOwner) return; // wait until ownership has arrived
            transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
        }

        [Rpc(SendTo.Server)]
        void PickupRpc(RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            if (!CanCarry.Value || IsHeld) return;
            if (!NetworkManager.ConnectedClients.TryGetValue(who, out var client) || client.PlayerObject == null) return;
            if ((client.PlayerObject.transform.position - transform.position).sqrMagnitude > MaxPickupDistance * MaxPickupDistance) return;

            HolderId.Value = who;
            NetworkObject.ChangeOwnership(who);
        }

        [Rpc(SendTo.Server)]
        void DropRpc(Vector3 position, Quaternion rotation, Vector3 velocity, RpcParams rpcParams = default)
        {
            if (HolderId.Value != rpcParams.Receive.SenderClientId) return;
            ServerRelease(position, rotation, Vector3.ClampMagnitude(velocity, MaxThrowSpeed));
        }

        /// <summary>Host only: who last carried/threw this (NoHolder if nobody ever did).</summary>
        public ulong LastHolder { get; private set; } = NoHolder;

        void ServerRelease(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            LastHolder = HolderId.Value;
            HolderId.Value = NoHolder;
            NetworkObject.RemoveOwnership();

            transform.SetPositionAndRotation(position, rotation);
            _rb.position = position;
            _rb.rotation = rotation;
            _rb.isKinematic = false;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Random.insideUnitSphere * 4f;
        }

        void OnClientDisconnected(ulong clientId)
        {
            // Holder rage-quit mid-carry: drop it where it is.
            if (IsServer && HolderId.Value == clientId)
                ServerRelease(transform.position, transform.rotation, Vector3.zero);
        }
    }
}
