using System.Collections.Generic;
using FriendSlop.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Fishing
{
    /// <summary>
    /// A caught fish lying on the bank: carry it to the trader like a carcass (or toss it on a Lucky Lodge pad).
    /// It flops around for a bit after landing. The model is built from primitives on every peer from the
    /// synced species + weight, so one prefab covers every fish.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class FishCatch : NetworkBehaviour
    {
        public static readonly List<FishCatch> All = new List<FishCatch>();

        public readonly NetworkVariable<int> Kind = new NetworkVariable<int>(0);
        public readonly NetworkVariable<float> Kg = new NetworkVariable<float>(0.5f);

        Carryable _carry;
        Rigidbody _rb;
        BoxCollider _box;
        Transform _visual;
        float _spawnTime, _nextFlop;
        static Material _mat;

        public Carryable Carry => _carry;
        public FishSpecies Species => FishSpecies.Get(Kind.Value);
        public int SaleValue => FishSpecies.ValueOf(Kind.Value, Kg.Value);
        public string DisplayName => Species.Name;

        void Awake()
        {
            _carry = GetComponent<Carryable>();
            _rb = GetComponent<Rigidbody>();
            _box = GetComponent<BoxCollider>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _spawnTime = Time.time;
            _nextFlop = Time.time + Random.Range(0.6f, 1.4f);
            BuildVisual();
            Kind.OnValueChanged += (_, _) => BuildVisual();
            Kg.OnValueChanged += (_, _) => BuildVisual();
            if (IsServer) _carry.ServerSetCarryable(true);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        /// <summary>Host: set what this fish is right after spawning.</summary>
        public void ServerInit(int kind, float kg) { Kind.Value = kind; Kg.Value = kg; }

        void FixedUpdate()
        {
            if (!IsSpawned || _rb == null) return;
            bool simulate = IsOwner && !_carry.IsHeld;
            if (_rb.isKinematic == simulate) _rb.isKinematic = !simulate;
            if (!simulate || Species.Junk) return;
            // Flop! (less and less as it tires out)
            float age = Time.time - _spawnTime;
            if (age < 25f && Time.time > _nextFlop && _rb.linearVelocity.sqrMagnitude < 0.5f)
            {
                _nextFlop = Time.time + Random.Range(0.7f, 1.6f) + age * 0.08f;
                float k = Mathf.Lerp(1f, 0.3f, age / 25f);
                _rb.AddForce((Vector3.up * 2.6f + Random.insideUnitSphere * 0.8f) * k, ForceMode.VelocityChange);
                _rb.AddTorque(Random.onUnitSphere * 6f * k, ForceMode.VelocityChange);
            }
        }

        void BuildVisual()
        {
            if (_visual != null) Destroy(_visual.gameObject);
            var s = Species;
            float scale = Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(s.MinKg, s.MaxKg, Kg.Value));
            float L = s.Length * scale;
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            _mat ??= new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FishRuntime" };
            if (s.Junk)
            {
                // An old boot: sole, shaft, a sad flap.
                Part(PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), new Vector3(L * 0.36f, 0.06f, L), s.Belly);
                Part(PrimitiveType.Cube, new Vector3(0f, 0.16f, -L * 0.28f), new Vector3(L * 0.34f, 0.26f, L * 0.4f), s.Body);
                Part(PrimitiveType.Cube, new Vector3(0f, 0.1f, L * 0.18f), new Vector3(L * 0.33f, 0.12f, L * 0.55f), s.Body);
            }
            else
            {
                float h = L * 0.32f, w = L * 0.2f;
                Part(PrimitiveType.Sphere, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, L), s.Body);                         // body
                Part(PrimitiveType.Sphere, new Vector3(0f, h * 0.38f, L * 0.05f), new Vector3(w * 0.92f, h * 0.7f, L * 0.8f), s.Belly); // belly
                var tail = Part(PrimitiveType.Cube, new Vector3(0f, h * 0.5f, -L * 0.55f), new Vector3(w * 0.15f, h * 1.1f, L * 0.22f), s.Body);
                tail.localRotation = Quaternion.Euler(0f, 0f, 0f);
                Part(PrimitiveType.Cube, new Vector3(0f, h * 1.05f, -L * 0.05f), new Vector3(w * 0.1f, h * 0.45f, L * 0.35f), s.Body); // dorsal fin
                for (int e = -1; e <= 1; e += 2)
                {
                    Part(PrimitiveType.Sphere, new Vector3(e * w * 0.42f, h * 0.62f, L * 0.36f), Vector3.one * h * 0.32f, Color.white);   // googly eye
                    Part(PrimitiveType.Sphere, new Vector3(e * w * 0.52f, h * 0.62f, L * 0.38f), Vector3.one * h * 0.16f, Color.black);
                }
            }
            if (_box != null)
            {
                _box.size = s.Junk ? new Vector3(L * 0.36f, 0.3f, L) : new Vector3(L * 0.22f, L * 0.34f, L * 1.1f);
                _box.center = new Vector3(0f, _box.size.y * 0.5f, 0f);
            }
            if (_rb != null) _rb.mass = Mathf.Max(0.3f, Kg.Value);
        }

        Transform Part(PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_visual, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            r.SetPropertyBlock(block);
            return go.transform;
        }
    }
}
