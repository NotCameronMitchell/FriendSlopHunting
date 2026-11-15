using FriendSlop.Interaction;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Animals
{
    public enum Persona { Rabbit, Duck, Goose, Deer, Boar, Wolf, Moose, Bear, Boss, Raccoon, Opossum, Coyote, Fox, Skunk, Goat, Turkey }

    /// <summary>
    /// Gives a procedural animal its personality. Runs locally on every peer from state that is
    /// already networked (position, IsAngry, IsDead, carried), so it costs no bandwidth:
    /// walk/trot/gallop/waddle/hop gaits, a head that turns to look at you, grazing, blinking,
    /// expressive brows and pupils, ears that flop and pin back, tails that wag or flag, mouths
    /// that open when it makes a noise, and a cartoon death (X eyes, tongue out, stiff legs).
    /// Each <see cref="Persona"/> adds its own quirks (rabbit binkies, goose neck-stretch, bear rears up…).
    /// </summary>
    public class CritterRig : MonoBehaviour
    {
        [Header("Rig")]
        public Persona persona;
        public Transform posture;           // whole-body pivot (rear hips); pitched for rearing up
        public Transform body;              // breathing / waddle roll
        public Transform head;              // neck pivot: look, graze, bob
        public Transform jaw;               // opens on voice
        public Transform tail;
        public Transform nose;              // wiggles (rabbit) / snorts
        public Transform[] legs = System.Array.Empty<Transform>();
        public float[] legPhase = System.Array.Empty<float>();
        public bool[] rearLeg = System.Array.Empty<bool>();
        public Transform[] ears = System.Array.Empty<Transform>();
        public Transform[] wings = System.Array.Empty<Transform>();
        public Transform[] eyes = System.Array.Empty<Transform>();
        public Transform[] pupils = System.Array.Empty<Transform>();
        public Transform[] brows = System.Array.Empty<Transform>();
        public GameObject[] xEyes = System.Array.Empty<GameObject>();
        public GameObject tongue;

        [Header("Feel")]
        public float stride = 1f;           // gait cycles per meter
        public float legSwing = 32f;        // degrees at a walk
        public bool grazer;                 // drops its head to eat when nobody's around
        public bool hopper;                 // moves in hops (legs together)
        public bool biped;                  // waddles
        public bool floaty;                 // hovers and bobs (ghosts)

        [Header("Paint")]
        public Renderer[] paintRenderers = System.Array.Empty<Renderer>();
        public Color[] paintColors = System.Array.Empty<Color>();
        public Color[] paintEmission = System.Array.Empty<Color>();
        [Tooltip("Emissive material swapped onto the pupils of elite (Alpha/Golden) animals.")]
        public Material glowMaterial;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        bool IsCanine => persona == Persona.Wolf || persona == Persona.Coyote || persona == Persona.Fox;

        Animal _animal;
        Carryable _carry;
        Vector3 _lastPos;
        float _speed, _phase, _moveK;

        // rest poses
        Quaternion[] _legRest, _earRest, _wingRest, _browRest;
        Vector3[] _pupilRest, _browRestPos, _eyeRestScale, _pupilRestScale;
        Quaternion _headRest, _jawRest, _tailRest, _bodyRest, _postureRest;
        Vector3 _headRestPos, _noseRestScale, _postureRestPos;

        // timers / state
        float _blinkAt, _blinkT = -1f;
        float _sayUntil, _sayAmount;
        float _attackT = -1f;
        float _grazeUntil, _nextGraze;
        float _twitchAt; int _twitchEar = -1; float _twitchT = -1f;
        float _quirkAt, _quirkT = -1f;
        float _rearT = -1f;
        float _lookYaw, _lookPitch;
        bool _wasDead;

        void Awake()
        {
            _animal = GetComponentInParent<Animal>();
            _carry = GetComponentInParent<Carryable>();
            Paint();
            CaptureRest();
            _lastPos = transform.position;
            _blinkAt = Time.time + Random.Range(1f, 4f);
            _nextGraze = Time.time + Random.Range(2f, 8f);
            _twitchAt = Time.time + Random.Range(1f, 4f);
            _quirkAt = Time.time + Random.Range(4f, 12f);
        }

        void Paint()
        {
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < paintRenderers.Length && i < paintColors.Length; i++)
            {
                var r = paintRenderers[i];
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, paintColors[i]);
                block.SetColor(ColorId, paintColors[i]);
                if (i < paintEmission.Length) block.SetColor(EmissionId, paintEmission[i]);
                r.SetPropertyBlock(block);
            }
        }

        void CaptureRest()
        {
            _legRest = Rots(legs); _earRest = Rots(ears); _wingRest = Rots(wings); _browRest = Rots(brows);
            _pupilRest = new Vector3[pupils.Length]; _pupilRestScale = new Vector3[pupils.Length];
            for (int i = 0; i < pupils.Length; i++) if (pupils[i]) { _pupilRest[i] = pupils[i].localPosition; _pupilRestScale[i] = pupils[i].localScale; }
            _browRestPos = new Vector3[brows.Length];
            for (int i = 0; i < brows.Length; i++) if (brows[i]) _browRestPos[i] = brows[i].localPosition;
            _eyeRestScale = new Vector3[eyes.Length];
            for (int i = 0; i < eyes.Length; i++) if (eyes[i]) _eyeRestScale[i] = eyes[i].localScale;
            if (head) { _headRest = head.localRotation; _headRestPos = head.localPosition; }
            if (jaw) _jawRest = jaw.localRotation;
            if (tail) _tailRest = tail.localRotation;
            if (body) _bodyRest = body.localRotation;
            if (posture) { _postureRest = posture.localRotation; _postureRestPos = posture.localPosition; }
            if (nose) _noseRestScale = nose.localScale;
        }

        static Quaternion[] Rots(Transform[] ts)
        {
            var r = new Quaternion[ts.Length];
            for (int i = 0; i < ts.Length; i++) if (ts[i]) r[i] = ts[i].localRotation;
            return r;
        }

        // ------------------------------------------------------------------ hooks from Animal

        /// <summary>Elite look: burning golden eyes (the body is scaled up by <see cref="Animal"/>).</summary>
        public void SetElite()
        {
            var block = new MaterialPropertyBlock();
            foreach (var p in pupils)
            {
                if (!p) continue;
                var r = p.GetComponent<Renderer>();
                if (!r) continue;
                if (glowMaterial && r.sharedMaterial != glowMaterial && r.sharedMaterial.renderQueue < 3000) r.sharedMaterial = glowMaterial;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(1f, 0.8f, 0.2f));
                block.SetColor(ColorId, new Color(1f, 0.8f, 0.2f));
                block.SetColor(EmissionId, new Color(2.4f, 1.6f, 0.2f));
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Open the mouth for a bit (called when the animal makes a sound).</summary>
        public void Say(float seconds, float amount = 1f)
        {
            _sayUntil = Time.time + seconds;
            _sayAmount = amount;
            if (persona == Persona.Bear || persona == Persona.Boss) { if (_animal != null && _animal.IsAngry.Value) _rearT = 0f; }
        }

        /// <summary>Head snap for a bite / headbutt / peck.</summary>
        public void Attack()
        {
            _attackT = 0f;
            Say(0.35f, 1f);
            if (persona == Persona.Bear || persona == Persona.Boss) _rearT = 0f;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float t = Time.time;
            bool dead = _animal != null && _animal.IsSpawned && (_animal.IsDead.Value || _animal.PlayingDead.Value);
            bool angry = !dead && _animal != null && _animal.IsSpawned && _animal.IsAngry.Value;
            bool held = _carry != null && _carry.IsHeld;

            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos; delta.y = 0f;
            _lastPos = pos;
            float dist = dead ? 0f : delta.magnitude;
            _speed = Mathf.Lerp(_speed, dist / dt, 1f - Mathf.Exp(-10f * dt));
            float walk = _animal != null && _animal.Species != null ? Mathf.Max(0.5f, _animal.Species.wanderSpeed) : 1.5f;
            _moveK = Mathf.Clamp01(_speed / walk);
            bool fast = _speed > walk * 1.7f;
            bool scared = fast && !angry;
            _phase += dist * stride * Mathf.PI * 2f;

            if (dead != _wasDead) { _wasDead = dead; SetDeadFace(dead); }
            if (dead) { DeadPose(held, t); return; }

            Legs(fast, t);
            Body(t, fast, angry);
            Head(t, dt, angry, scared);
            Ears(t, angry, scared);
            Tail(t, angry, scared);
            Wings(t, angry, scared);
            Face(t, angry, scared);
            Quirks(t, dt, angry, scared);
        }

        void Legs(bool fast, float t)
        {
            float amp = legSwing * Mathf.Lerp(0f, fast ? 1.45f : 1f, _moveK);
            for (int i = 0; i < legs.Length; i++)
            {
                if (!legs[i]) continue;
                float ph = i < legPhase.Length ? legPhase[i] : 0f;
                if (fast && !biped && !hopper && legs.Length == 4) ph = (i < 2 ? 0f : Mathf.PI) + (i % 2) * 0.35f; // gallop
                float swing = hopper ? -Mathf.Abs(Mathf.Sin(_phase * 0.5f)) * amp : Mathf.Sin(_phase + ph) * amp;
                var r = _legRest[i] * Quaternion.Euler(swing, 0f, 0f);
                // Rear legs stay planted when the whole body rears up.
                if (posture && i < rearLeg.Length && rearLeg[i]) r = Quaternion.Euler(-PostureAngle(), 0f, 0f) * r;
                legs[i].localRotation = r;
            }
        }

        float PostureAngle()
        {
            if (_rearT < 0f) return 0f;
            const float dur = 1.3f;
            float k = _rearT / dur;
            return -Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * 55f;
        }

        void Body(float t, bool fast, bool angry)
        {
            if (posture)
            {
                if (_rearT >= 0f) { _rearT += Time.deltaTime; if (_rearT > 1.3f) _rearT = -1f; }
                posture.localRotation = _postureRest * Quaternion.Euler(PostureAngle() + (fast ? 5f : 0f), 0f, 0f);
                posture.localPosition = _postureRestPos + (floaty ? Vector3.up * (0.35f + Mathf.Sin(Time.time * 2.2f) * 0.1f) : Vector3.zero);
            }
            if (!body) return;
            float breathe = 1f + Mathf.Sin(t * (angry ? 6f : 2.4f)) * (angry ? 0.035f : 0.02f);
            float roll = biped ? Mathf.Sin(_phase) * 12f * Mathf.Max(0.25f, _moveK) : Mathf.Sin(_phase) * 2.5f * _moveK;
            float pitch = hopper ? -Mathf.Sin(_phase * 0.5f) * 14f * _moveK : 0f;
            body.localRotation = _bodyRest * Quaternion.Euler(pitch, 0f, roll);
            body.localScale = new Vector3(1f, breathe, 1f / Mathf.Sqrt(breathe));
        }

        void Head(float t, float dt, bool angry, bool scared)
        {
            if (!head) return;
            // Look at the local player when they're close (everyone sees animals eyeballing *them*).
            float wantYaw = 0f, wantPitch = 0f, weight = 0f;
            var cam = NetworkPlayer.LocalCamera;
            if (cam != null && !angry)
            {
                Vector3 to = cam.position - head.position;
                float d = to.magnitude;
                if (d < 16f && d > 0.3f)
                {
                    var local = (head.parent ? head.parent : transform).InverseTransformDirection(to / d);
                    wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
                    wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 30f);
                    weight = scared ? 0.25f : 1f;
                    _grazeUntil = 0f; // something's watching; stop eating
                }
            }
            _lookYaw = Mathf.Lerp(_lookYaw, wantYaw * weight, 1f - Mathf.Exp(-6f * dt));
            _lookPitch = Mathf.Lerp(_lookPitch, wantPitch * weight, 1f - Mathf.Exp(-6f * dt));

            // Grazing / pecking when idle and alone.
            float graze = 0f;
            if (grazer && _moveK < 0.15f && weight == 0f && !angry)
            {
                if (t > _nextGraze && _grazeUntil < t) { _grazeUntil = t + Random.Range(2f, 5f); _nextGraze = _grazeUntil + Random.Range(3f, 9f); }
                if (t < _grazeUntil)
                {
                    float nibble = persona == Persona.Duck || persona == Persona.Goose ? Mathf.Abs(Mathf.Sin(t * 9f)) * 15f : Mathf.Sin(t * 7f) * 4f;
                    graze = 50f + nibble;
                }
            }

            float bob = Mathf.Sin(_phase * 2f) * 5f * _moveK;
            float charge = angry ? 18f : 0f;                     // head down when charging
            float attack = 0f;
            if (_attackT >= 0f) { _attackT += Time.deltaTime; attack = Mathf.Sin(Mathf.Clamp01(_attackT / 0.3f) * Mathf.PI) * 30f; if (_attackT > 0.3f) _attackT = -1f; }
            float sayToss = t < _sayUntil ? -8f * _sayAmount : 0f;
            float idleSway = Mathf.Sin(t * 0.7f + transform.GetInstanceID()) * 6f * (1f - _moveK);
            if (persona == Persona.Moose || persona == Persona.Boss) idleSway *= 1.8f;

            head.localRotation = _headRest * Quaternion.Euler(_lookPitch + graze + bob + charge + attack + sayToss, _lookYaw + idleSway, 0f);

            // Goose: neck stretches out like a hissing snake when angry.
            Vector3 stretch = Vector3.zero;
            if (persona == Persona.Goose && angry) stretch = Vector3.forward * 0.12f + Vector3.down * 0.05f;
            head.localPosition = Vector3.Lerp(head.localPosition, _headRestPos + stretch, 1f - Mathf.Exp(-8f * dt));

            if (jaw)
            {
                float open = 0f;
                if (t < _sayUntil) open = 28f * _sayAmount * (0.7f + 0.3f * Mathf.Abs(Mathf.Sin(t * 30f)));
                else if (persona == Persona.Goose && angry) open = 20f;                         // hiss
                else if (IsCanine && angry) open = 12f;                          // snarl
                else if (IsCanine && _moveK > 0.2f) open = 14f + Mathf.Sin(t * 14f) * 4f; // pant
                else if (persona == Persona.Moose) open = Mathf.Max(0f, Mathf.Sin(t * 3f)) * 7f; // chew
                jaw.localRotation = _jawRest * Quaternion.Euler(open, 0f, 0f);
            }
            if (tongue) tongue.SetActive(IsCanine && !angry && _moveK > 0.2f);
        }

        void Ears(float t, bool angry, bool scared)
        {
            if (ears.Length == 0) return;
            if (_twitchT < 0f && t > _twitchAt) { _twitchT = 0f; _twitchEar = Random.Range(0, ears.Length); _twitchAt = t + Random.Range(1.5f, 5f); }
            if (_twitchT >= 0f) { _twitchT += Time.deltaTime; if (_twitchT > 0.18f) _twitchT = -1f; }
            float pinned = angry ? -55f : scared ? -40f : 0f;
            for (int i = 0; i < ears.Length; i++)
            {
                if (!ears[i]) continue;
                float side = (i % 2 == 0) ? -1f : 1f;
                float flop = Mathf.Sin(_phase * 2f + i) * 14f * _moveK;
                float twitch = (i == _twitchEar && _twitchT >= 0f) ? Mathf.Sin(_twitchT / 0.18f * Mathf.PI) * 30f : 0f;
                float droop = persona == Persona.Rabbit ? Mathf.Sin(t * 0.9f + i * 1.7f) * 6f : 0f;
                ears[i].localRotation = _earRest[i] * Quaternion.Euler(pinned + flop + droop, 0f, side * twitch);
            }
        }

        void Tail(float t, bool angry, bool scared)
        {
            if (!tail) return;
            Quaternion q = Quaternion.identity;
            switch (persona)
            {
                case Persona.Wolf:
                case Persona.Coyote:
                    q = angry ? Quaternion.Euler(-25f, 0f, 0f) : Quaternion.Euler(scared ? 35f : 0f, Mathf.Sin(t * 11f) * (scared ? 5f : 28f), 0f);
                    break;
                case Persona.Deer:
                    q = Quaternion.Euler(scared ? -75f : -10f, Mathf.Sin(t * 6f) * 8f, 0f); // white flag up when fleeing
                    break;
                case Persona.Boar:
                    q = Quaternion.Euler(0f, 0f, t * (180f + 900f * _moveK));                 // curly tail spins
                    break;
                case Persona.Duck:
                case Persona.Goose:
                    q = Quaternion.Euler(0f, Mathf.Sin(t * (_moveK > 0.1f ? 16f : 4f)) * 18f, 0f);
                    break;
                case Persona.Rabbit:
                    q = Quaternion.Euler(Mathf.Sin(t * 13f) * 6f, 0f, 0f);
                    break;
                default:
                    q = Quaternion.Euler(0f, Mathf.Sin(t * 3f) * 8f, 0f);
                    break;
            }
            tail.localRotation = _tailRest * q;
        }

        void Wings(float t, bool angry, bool scared)
        {
            for (int i = 0; i < wings.Length; i++)
            {
                if (!wings[i]) continue;
                float side = (i % 2 == 0) ? -1f : 1f;
                bool flap = scared || (persona == Persona.Goose && angry) || (_quirkT >= 0f && persona == Persona.Duck);
                float a = flap ? (Mathf.Sin(t * 22f) * 0.5f + 0.5f) * 70f : Mathf.Sin(t * 1.5f + i) * 2f;
                wings[i].localRotation = _wingRest[i] * Quaternion.Euler(0f, 0f, side * a);
            }
        }

        void Face(float t, bool angry, bool scared)
        {
            // Blink
            if (_blinkT < 0f && t > _blinkAt) { _blinkT = 0f; _blinkAt = t + Random.Range(1.8f, 5f); }
            float lid = 1f;
            if (_blinkT >= 0f) { _blinkT += Time.deltaTime; lid = 1f - Mathf.Sin(Mathf.Clamp01(_blinkT / 0.14f) * Mathf.PI) * 0.92f; if (_blinkT > 0.14f) _blinkT = -1f; }
            float openness = persona == Persona.Moose ? 0.55f : persona == Persona.Boss ? 0.7f : 1f; // dopey / grumpy squint
            if (scared) openness = 1.15f;
            if (angry) openness *= 0.8f;
            for (int i = 0; i < eyes.Length; i++)
                if (eyes[i]) { var s = _eyeRestScale[i]; eyes[i].localScale = new Vector3(s.x, s.y * openness * lid, s.z); }

            // Pupils: tiny when scared, follow the look direction a little.
            float size = scared ? 0.6f : angry ? 0.85f : 1f;
            var drift = new Vector3(Mathf.Clamp(_lookYaw / 75f, -1f, 1f) * 0.12f, Mathf.Clamp(-_lookPitch / 35f, -1f, 1f) * 0.08f, 0f);
            for (int i = 0; i < pupils.Length; i++)
                if (pupils[i]) { pupils[i].localPosition = _pupilRest[i] + drift; pupils[i].localScale = _pupilRestScale[i] * size; }

            // Brows: angry V, worried /\, or a permanent goose scowl.
            bool scowl = angry || persona == Persona.Goose || persona == Persona.Boss || persona == Persona.Boar;
            float tilt = scowl ? 24f : scared ? -18f : persona == Persona.Moose ? -8f : 0f;
            float lift = scared ? 0.35f : scowl ? -0.12f : 0f; // in brow-widths
            for (int i = 0; i < brows.Length; i++)
            {
                if (!brows[i]) continue;
                float side = (i % 2 == 0) ? -1f : 1f;
                brows[i].localRotation = _browRest[i] * Quaternion.Euler(0f, 0f, side * tilt);
                brows[i].localPosition = _browRestPos[i] + Vector3.up * lift * brows[i].localScale.x;
            }
        }

        void Quirks(float t, float dt, bool angry, bool scared)
        {
            if (nose)
            {
                float wiggle = persona == Persona.Rabbit ? 1f + Mathf.Abs(Mathf.Sin(t * 14f)) * 0.25f : 1f;
                if (persona == Persona.Boar && _quirkT >= 0f) wiggle = 1f + Mathf.Sin(_quirkT * 40f) * 0.2f; // snort
                nose.localScale = _noseRestScale * wiggle;
            }
            if (_quirkT < 0f && t > _quirkAt && _moveK < 0.2f && !angry && !scared)
            {
                _quirkT = 0f;
                _quirkAt = t + Random.Range(6f, 14f);
            }
            if (_quirkT < 0f) return;
            _quirkT += dt;
            float k = Mathf.Clamp01(_quirkT / 0.6f);
            switch (persona)
            {
                case Persona.Rabbit:
                    // Binky: a happy little twisting jump.
                    if (posture) { posture.localPosition = _postureRestPos + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f; posture.localRotation *= Quaternion.Euler(0f, Mathf.Sin(k * Mathf.PI * 2f) * 35f, Mathf.Sin(k * Mathf.PI) * 20f); }
                    break;
                case Persona.Boar:
                    if (head) head.localRotation *= Quaternion.Euler(-Mathf.Sin(k * Mathf.PI * 3f) * 12f, 0f, 0f); // snort toss
                    break;
                case Persona.Deer:
                    if (head) head.localRotation *= Quaternion.Euler(-Mathf.Sin(k * Mathf.PI) * 18f, 0f, 0f);     // alert head-up
                    break;
                case Persona.Wolf:
                case Persona.Coyote:
                    if (head) head.localRotation *= Quaternion.Euler(-Mathf.Sin(k * Mathf.PI) * 35f, 0f, 0f);     // little howl
                    if (jaw) jaw.localRotation = _jawRest * Quaternion.Euler(Mathf.Sin(k * Mathf.PI) * 25f, 0f, 0f);
                    break;
                case Persona.Bear:
                    if (body) body.localRotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 4f) * 8f); // back scratch wiggle
                    break;
            }
            if (_quirkT > 0.6f) _quirkT = -1f;
        }

        // ------------------------------------------------------------------ death

        void SetDeadFace(bool dead)
        {
            foreach (var e in eyes) if (e) e.gameObject.SetActive(!dead);
            foreach (var b in brows) if (b) b.gameObject.SetActive(!dead);
            foreach (var x in xEyes) if (x) x.SetActive(dead);
            if (tongue) tongue.SetActive(dead);
            if (jaw) jaw.localRotation = _jawRest * Quaternion.Euler(dead ? 25f : 0f, 0f, 0f);
            if (dead)
            {
                if (posture) { posture.localRotation = _postureRest; posture.localPosition = _postureRestPos; }
                if (body) { body.localRotation = _bodyRest; body.localScale = Vector3.one; }
                if (head) head.localPosition = _headRestPos;
                StartRagdoll();
            }
            else
            {
                _ragInit = false;
                if (body) body.localScale = Vector3.one;
            }
        }

        // ------------------------------------------------------------------ floppy ragdoll

        /// <summary>
        /// One limp limb: a damped pendulum whose tip direction lives in world space, so it lags behind
        /// when the carcass is thrown, spun or carried, sags with gravity and wobbles on landing.
        /// Purely local; it only reads the (already networked) carcass motion.
        /// </summary>
        struct Floppy
        {
            public Transform bone;
            public Quaternion rest;      // limp rest rotation (local)
            public Vector3 tipLocal;     // direction from the pivot toward the limb's mass, in bone space
            public Vector3 dir, vel;     // simulated world tip direction and its velocity
            public float stiffness, damping, maxAngle, gravity;
        }

        Floppy[] _floppy;
        Vector3 _ragPrevPos, _ragVel;
        float _squash, _squashVel;
        bool _ragInit;

        void BuildFloppies()
        {
            var list = new System.Collections.Generic.List<Floppy>();
            void Add(Transform b, Quaternion rest, Vector3 fallbackTip, float k, float maxDeg, float g)
            {
                if (!b || !b.parent) return;
                list.Add(new Floppy
                {
                    bone = b, rest = rest, tipLocal = TipOf(b, fallbackTip), stiffness = k * 1.8f,
                    damping = 1.75f * Mathf.Sqrt(k), // slightly under-damped: a little sway, settles fast
                    maxAngle = maxDeg * 0.7f * Mathf.Deg2Rad, gravity = g * 0.6f,
                });
            }
            // Parents before children (head before ears) so each reads its parent's fresh pose.
            for (int i = 0; i < legs.Length; i++)
                Add(legs[i], _legRest[i] * Quaternion.Euler(0f, 0f, ((i % 2 == 0) ? -1f : 1f) * 15f), Vector3.down, 14f, 80f, 1.6f);
            Add(head, _headRest * Quaternion.Euler(-10f, 0f, 12f), Vector3.forward, 10f, 70f, 2f);
            Add(tail, _tailRest, Vector3.back, 6f, 90f, 1.6f);
            for (int i = 0; i < ears.Length; i++) Add(ears[i], _earRest[i] * Quaternion.Euler(-35f, 0f, 0f), Vector3.up, 5f, 110f, 1.8f);
            for (int i = 0; i < wings.Length; i++)
                Add(wings[i], _wingRest[i] * Quaternion.Euler(0f, 0f, ((i % 2 == 0) ? -1f : 1f) * 35f), Vector3.down, 8f, 85f, 1.6f);
            _floppy = list.ToArray();
        }

        /// <summary>Where the limb's mass sits relative to its pivot (from its renderers), in bone space.</summary>
        static Vector3 TipOf(Transform bone, Vector3 fallback)
        {
            var rs = bone.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return fallback;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            Vector3 local = bone.InverseTransformPoint(b.center);
            return local.sqrMagnitude > 1e-6f ? local.normalized : fallback;
        }

        void StartRagdoll()
        {
            if (_floppy == null) BuildFloppies(); // limbs are at their rest pose the first time we die
            for (int i = 0; i < _floppy.Length; i++)
            {
                ref var f = ref _floppy[i];
                if (!f.bone) continue;
                f.dir = f.bone.parent.rotation * (f.bone.localRotation * f.tipLocal);
                f.vel = Vector3.zero;
            }
            _ragPrevPos = transform.position; _ragVel = Vector3.zero; _ragInit = true;
            _squash = 0f; _squashVel = 0f;
        }

        void DeadPose(bool held, float t)
        {
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 30f);
            if (!_ragInit) StartRagdoll();

            // Carcass acceleration → the gravity the limbs "feel" (they lag when it's thrown, spun or carried).
            Vector3 pos = transform.position;
            Vector3 v = (pos - _ragPrevPos) / dt;
            _ragPrevPos = pos;
            if (v.sqrMagnitude > 1600f) v = _ragVel; // teleport / pickup snap: ignore
            Vector3 accel = Vector3.ClampMagnitude((v - _ragVel) / dt, 90f);
            _ragVel = Vector3.Lerp(_ragVel, v, 1f - Mathf.Exp(-30f * dt));
            Vector3 felt = Physics.gravity - accel;

            for (int i = 0; i < _floppy.Length; i++)
            {
                ref var f = ref _floppy[i];
                if (!f.bone) continue;
                Quaternion parentRot = f.bone.parent.rotation;
                Vector3 restLocal = f.rest * f.tipLocal;
                Vector3 restDir = parentRot * restLocal;
                Vector3 force = (restDir - f.dir) * f.stiffness + Vector3.ProjectOnPlane(felt, f.dir) * f.gravity;
                f.vel += force * dt;
                f.vel *= Mathf.Exp(-f.damping * dt);
                f.dir = (f.dir + f.vel * dt).normalized;
                f.vel = Vector3.ProjectOnPlane(f.vel, f.dir);
                f.dir = Vector3.RotateTowards(restDir, f.dir, f.maxAngle, 0f); // joint limit
                f.bone.localRotation = Quaternion.FromToRotation(restLocal, Quaternion.Inverse(parentRot) * f.dir) * f.rest;
            }

            // Squash on hard landings, then jiggle back.
            float impact = Mathf.Max(0f, accel.y) * dt;
            if (impact > 0.18f) _squashVel -= impact * 2.5f;
            _squashVel += -_squash * 220f * dt;
            _squashVel *= Mathf.Exp(-8f * dt);
            _squash = Mathf.Clamp(_squash + _squashVel * dt, -0.15f, 0.15f);
            if (body) body.localScale = new Vector3(1f - _squash * 0.6f, 1f + _squash, 1f - _squash * 0.6f);
        }
    }
}
