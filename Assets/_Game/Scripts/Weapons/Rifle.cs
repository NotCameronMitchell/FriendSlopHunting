using System.Collections;
using FriendSlop.Animals;
using FriendSlop.Player;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// Bolt-action hitscan rifle. The shooter raycasts locally (instant feedback, aims at what they
    /// actually see), then tells the host what they hit. The host sanity-checks the claim (shooter
    /// position, range, target near the hit point) instead of re-raycasting, because animals the
    /// client sees are ~100 ms behind the host's: a host re-raycast would miss fair shots.
    /// That's the right trade for co-op with friends; anti-cheat isn't a goal.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class Rifle : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] Camera playerCamera;
        [SerializeField] Transform muzzle;
        [SerializeField] Transform gunModel;
        [SerializeField] Material tracerMaterial;

        [Header("Tuning")]
        [SerializeField] float fireCooldown = 0.7f;
        [SerializeField] float range = 250f;
        [SerializeField] float bodyDamage = 60f;
        [SerializeField] float headDamage = 500f;
        [SerializeField] float longShotDistance = 35f;
        [SerializeField] float boxImpulse = 16f;
        [SerializeField] float hipFov = 75f;
        [SerializeField] float aimFov = 35f;

        NetworkPlayer _player;
        float _nextFireTime;
        Vector3 _gunRestPos;

        void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            if (gunModel) _gunRestPos = gunModel.localPosition;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool controlling = Cursor.lockState == CursorLockMode.Locked;
            bool aiming = controlling && mouse.rightButton.isPressed && !_player.IsCarrying;
            if (playerCamera)
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, aiming ? aimFov : hipFov, Time.deltaTime * 12f);

            if (gunModel)
                gunModel.localPosition = Vector3.Lerp(gunModel.localPosition, _gunRestPos, Time.deltaTime * 10f);

            if (controlling && !_player.IsCarrying && mouse.leftButton.wasPressedThisFrame && Time.time >= _nextFireTime)
            {
                _nextFireTime = Time.time + fireCooldown;
                Fire();
            }
        }

        void Fire()
        {
            Transform eye = playerCamera ? playerCamera.transform : transform;
            var ray = new Ray(eye.position, eye.forward);
            Vector3 end = ray.origin + ray.direction * range;

            var targetRef = default(NetworkObjectReference);
            bool hasTarget = false;
            bool headshot = false;

            if (Physics.Raycast(ray, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var animal = hit.collider.GetComponentInParent<Animal>();
                if (animal != null && animal.IsSpawned)
                {
                    targetRef = animal.NetworkObject;
                    hasTarget = true;
                    headshot = hit.collider.TryGetComponent(out Hitbox hb) && hb.IsHead;
                }
                else if (hit.rigidbody != null && hit.rigidbody.TryGetComponent(out NetworkPhysicsBox box))
                {
                    box.RequestImpulse(ray.direction * boxImpulse, hit.point);
                }
            }

            if (gunModel) gunModel.localPosition = _gunRestPos + new Vector3(0f, 0.02f, -0.12f); // kick
            SpawnTracer(muzzle ? muzzle.position : ray.origin, end);

            FireRpc(ray.origin, end, targetRef, hasTarget, headshot, !_player.IsGrounded);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(Vector3 origin, Vector3 end, NetworkObjectReference target, bool hasTarget,
                     bool headshot, bool shooterAirborne, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if ((origin - transform.position).sqrMagnitude > 5f * 5f) return;   // shooter is where they say
            float distance = Vector3.Distance(origin, end);
            if (distance > range + 5f) return;

            ShotFxRpc(origin, end);

            // Gunshots spook everything nearby.
            foreach (var a in Animal.All) a.ServerHearShot(end, origin);

            if (!hasTarget || !target.TryGet(out NetworkObject targetObject)) return;
            if (!targetObject.TryGetComponent(out Animal animal)) return;
            if ((targetObject.transform.position - end).sqrMagnitude > 3f * 3f) return; // lag tolerance

            var style = ShotStyle.None;
            if (headshot) style |= ShotStyle.Headshot;
            if (distance >= longShotDistance) style |= ShotStyle.LongShot;
            if (shooterAirborne) style |= ShotStyle.JumpShot;

            float damage = headshot ? headDamage : bodyDamage;
            animal.ServerApplyHit(damage, (end - origin).normalized, style, OwnerClientId);
        }

        [Rpc(SendTo.NotOwner)]
        void ShotFxRpc(Vector3 origin, Vector3 end)
        {
            // Other players see the tracer start a little below the shooter's eye, roughly at the gun.
            SpawnTracer(origin + Vector3.down * 0.2f, end);
        }

        void SpawnTracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.03f;
            line.endWidth = 0.015f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (tracerMaterial) line.sharedMaterial = tracerMaterial;
            StartCoroutine(FadeAndDestroy(line, 0.12f));
            Destroy(go, 1f); // safety net if this player despawns mid-fade
        }

        static IEnumerator FadeAndDestroy(LineRenderer line, float seconds)
        {
            float t = 0f;
            while (t < seconds && line)
            {
                float k = 1f - t / seconds;
                line.startWidth = 0.03f * k;
                line.endWidth = 0.015f * k;
                t += Time.deltaTime;
                yield return null;
            }
            if (line) Destroy(line.gameObject);
        }
    }
}
