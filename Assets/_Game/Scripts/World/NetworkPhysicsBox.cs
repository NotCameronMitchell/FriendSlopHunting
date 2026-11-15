using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// A server-simulated rigidbody that any player can shove. This is the M0 stand-in for
    /// carcasses: the host runs physics, clients see an interpolated NetworkTransform, and
    /// clients ask the host to apply forces through an RPC (validated and clamped server-side).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkPhysicsBox : NetworkBehaviour
    {
        [SerializeField] Color color = new Color(1f, 0.55f, 0.1f);
        [SerializeField] float maxImpulse = 20f;
        [SerializeField] float maxReach = 6f;
        [SerializeField] float pushCooldown = 0.1f;
        [SerializeField] float killY = -30f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Rigidbody _rb;
        Vector3 _startPosition;
        Quaternion _startRotation;
        float _lastPushTime = -1f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _startPosition = transform.position;
            _startRotation = transform.rotation;

            if (TryGetComponent(out Renderer r))
            {
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Called by the local player when they click the box.</summary>
        public void RequestImpulse(Vector3 impulse, Vector3 worldPoint) => ApplyImpulseRpc(impulse, worldPoint);

        /// <summary>Called every frame a player walks into the box; throttled locally.</summary>
        public void RequestPush(Vector3 impulse, Vector3 worldPoint)
        {
            if (Time.time - _lastPushTime < pushCooldown) return;
            _lastPushTime = Time.time;
            ApplyImpulseRpc(impulse, worldPoint);
        }

        [Rpc(SendTo.Server)]
        void ApplyImpulseRpc(Vector3 impulse, Vector3 worldPoint, RpcParams rpcParams = default)
        {
            // Basic sanity checks so a bad client can't launch the box into orbit.
            if (!float.IsFinite(impulse.x) || !float.IsFinite(impulse.y) || !float.IsFinite(impulse.z)) return;
            if ((worldPoint - _rb.worldCenterOfMass).sqrMagnitude > maxReach * maxReach) return;

            _rb.AddForceAtPosition(Vector3.ClampMagnitude(impulse, maxImpulse), worldPoint, ForceMode.Impulse);
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            if (transform.position.y < killY)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.position = _startPosition;
                _rb.rotation = _startRotation;
            }
        }
    }
}
