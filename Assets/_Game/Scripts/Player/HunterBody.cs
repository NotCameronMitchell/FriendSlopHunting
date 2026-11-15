using System.Collections.Generic;
using FriendSlop.Profile;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Player
{
    /// <summary>
    /// The chunky KayKit hunter (CC0): one skeleton carrying every outfit/head/hat part from the
    /// pack, with the right ones switched on for a player's look. Drives the animator from how the
    /// body actually moves (so it works the same for the owner and for remote copies), flops over
    /// when downed, and swings the right arm up to hold the gun. Used in game and in the menu preview.
    /// </summary>
    public class HunterBody : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] SkinnedMeshRenderer[] parts = System.Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] Transform hatAnchor;
        [SerializeField] Transform faceAnchor;
        [SerializeField] Transform neckAnchor;
        [SerializeField] Transform upperArmR, lowerArmR, handR;
        [SerializeField] Transform aimTarget;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int DownId = Animator.StringToHash("Down");
        static readonly int WaveId = Animator.StringToHash("Wave");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly Dictionary<string, SkinnedMeshRenderer> _byName = new();
        readonly List<Renderer> _active = new();
        readonly List<GameObject> _built = new();
        Vector3 _lastPos;
        float _speed;
        bool _shadowOnly;

        /// <summary>Where the gun sits (remote view); the right arm reaches for it. Optional.</summary>
        public Transform AimTarget { get => aimTarget; set => aimTarget = value; }

        public Transform HatAnchor => hatAnchor;

        /// <summary>Riding in a vehicle: idle pose instead of running with the vehicle speed.</summary>
        public bool Seated { get; set; }

        void Awake()
        {
            if (animator && animator.runtimeAnimatorController == null)
                animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Hunter");
            foreach (var p in parts) if (p) _byName[p.name] = p;
            _lastPos = transform.position;
        }

        /// <summary>Show this look. Ids come from <see cref="Cosmetics"/>; null/unknown = defaults.</summary>
        public void Apply(string outfit, string head, string hat, string accessory, Color color)
        {
            if (_byName.Count == 0) Awake();
            foreach (var p in parts) if (p) p.enabled = false;
            _active.Clear();
            foreach (var b in _built) if (b) Destroy(b);
            _built.Clear();

            var outfitItem = Cosmetics.Find(outfit) ?? Cosmetics.Find(Cosmetics.DefaultOutfit);
            var headItem = Cosmetics.Find(head) ?? Cosmetics.Find(Cosmetics.DefaultHead);
            var block = new MaterialPropertyBlock();
            var tint = Color.Lerp(Color.white, color, 0.6f);
            foreach (var n in outfitItem.Parts) Enable(n, block, tint);
            foreach (var n in headItem.Parts) Enable(n, null, Color.white);

            var hatItem = Cosmetics.Find(hat);
            var accItem = Cosmetics.Find(accessory);
            if (hatItem?.Parts != null) foreach (var n in hatItem.Parts) Enable(n, null, Color.white);
            if (accItem?.Parts != null) foreach (var n in accItem.Parts) Enable(n, block, tint);

            if (hatItem != null && hatItem.Parts == null) Follow(Cosmetics.BuildHat(hatItem.Id, hatAnchor));
            if (accItem != null && accItem.Parts == null) Follow(Cosmetics.BuildAccessory(accItem.Id, faceAnchor, neckAnchor));
            ApplyShadowMode();
        }

        void Follow(Transform t)
        {
            if (t == null) return;
            // Mark primitive cosmetics so they can be hidden/shadow-only along with the body.
            _built.Add(t.gameObject);
            foreach (var r in t.GetComponentsInChildren<Renderer>()) _active.Add(r);
        }

        void Enable(string name, MaterialPropertyBlock block, Color tint)
        {
            if (!_byName.TryGetValue(name, out var r)) { Debug.LogWarning($"[Hunter] no part {name}"); return; }
            r.enabled = true;
            if (block != null)
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                r.SetPropertyBlock(block);
            }
            else r.SetPropertyBlock(null);
            _active.Add(r);
        }

        /// <summary>Owner: invisible to your own camera, but you still cast a shadow.</summary>
        public void SetShadowOnly(bool shadowOnly)
        {
            _shadowOnly = shadowOnly;
            ApplyShadowMode();
        }

        void ApplyShadowMode()
        {
            var mode = _shadowOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in _active) if (r) r.shadowCastingMode = mode;
        }

        public void SetDown(bool down)
        {
            if (animator) animator.SetBool(DownId, down);
        }

        /// <summary>Little wave (menu preview idle flourish).</summary>
        public void Wave()
        {
            if (animator) animator.SetTrigger(WaveId);
        }

        void Update()
        {
            if (!animator) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var pos = transform.position;
            var v = (pos - _lastPos) / dt;
            _lastPos = pos;
            v.y = 0f;
            _speed = Mathf.Lerp(_speed, Seated ? 0f : v.magnitude, 1f - Mathf.Exp(-12f * dt));
            animator.SetFloat(SpeedId, _speed);
            bool grounded = Seated || Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, 0.55f, ~0, QueryTriggerInteraction.Ignore);
            animator.SetBool(GroundedId, grounded);
        }

        void LateUpdate()
        {
            // Hold the gun: point the right arm at where the weapon floats in front of the chest.
            if (AimTarget == null || upperArmR == null || lowerArmR == null || handR == null) return;
            if (animator && animator.GetBool(DownId)) return;
            Vector3 target = AimTarget.position;
            Vector3 cur = handR.position - upperArmR.position;
            Vector3 want = target - upperArmR.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            upperArmR.rotation = Quaternion.FromToRotation(cur, want) * upperArmR.rotation;
        }
    }
}
