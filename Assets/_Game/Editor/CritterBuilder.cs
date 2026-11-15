using System.Collections.Generic;
using FriendSlop.Animals;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Builds the chunky, cartoon animal bodies (PEAK-style personality): a jointed rig of rounded
    /// primitives with big eyes, brows, mouths, ears and tails, wired into a <see cref="CritterRig"/>
    /// that animates them. Everything is generated from the species' body measurements, so hitboxes
    /// line up with the visuals.
    /// </summary>
    public static class CritterBuilder
    {
        public struct Result
        {
            public Vector3 bodyCenter;
            public float bodyRadius;
            public float bodyLength;
            public Vector3 headPos;
            public float headRadius;
        }

        static readonly Color EyeWhite = new Color(0.98f, 0.98f, 0.96f);
        static readonly Color Pupil = new Color(0.04f, 0.04f, 0.05f);
        static readonly Color Pink = new Color(0.95f, 0.55f, 0.6f);
        static readonly Color Ink = new Color(0.12f, 0.09f, 0.08f);

        // Paint list for the current build.
        static List<Renderer> _rends;
        static List<Color> _cols;
        static List<Color> _emis;
        static Material _mat;

        public static Persona PersonaFor(string file) => file switch
        {
            "Rabbit" => Persona.Rabbit,
            "Duck" => Persona.Duck,
            "Goose" => Persona.Goose,
            "Deer" => Persona.Deer,
            "Boar" => Persona.Boar,
            "Wolf" => Persona.Wolf,
            "Moose" => Persona.Moose,
            "Bear" => Persona.Bear,
            "Raccoon" => Persona.Raccoon,
            "Opossum" => Persona.Opossum,
            "Coyote" => Persona.Coyote,
            "GhostGoose" => Persona.Goose,
            "ShadowStag" => Persona.Deer,
            _ => Persona.Boss,
        };

        public static Result Build(AnimalSpecies s, string file, Transform visual, Material mat)
        {
            _rends = new List<Renderer>();
            _cols = new List<Color>();
            _emis = new List<Color>();
            _mat = mat;
            // Glowing eyes (night creatures, Big Doug) use emission; black for everyone else.
            var rig = visual.gameObject.AddComponent<CritterRig>();
            rig.persona = PersonaFor(file);

            Result r = s.body switch
            {
                BodyStyle.Bird => Bird(s, visual, rig),
                BodyStyle.Hopper => Hopper(s, visual, rig),
                _ => Quadruped(s, visual, rig),
            };

            if (s.ghostly || s.shadowy) Spookify(rig, s);
            if (rig.persona == Persona.Boss)
                for (int i = 0; i < _rends.Count; i++) if (_rends[i].name == "Pupil") _emis[i] = new Color(1.8f, 0.25f, 0.1f);
            // Glowing parts use a material with emission switched on (property blocks set each glow color).
            for (int i = 0; i < _rends.Count; i++)
                if (_emis[i].maxColorComponent > 0f && _rends[i].sharedMaterial == _mat) _rends[i].sharedMaterial = GlowMaterial();
            rig.paintRenderers = _rends.ToArray();
            rig.paintColors = _cols.ToArray();
            rig.paintEmission = _emis.ToArray();
            rig.glowMaterial = GlowMaterial();
            return r;
        }

        // ================================================================== quadruped

        static Result Quadruped(AnimalSpecies s, Transform visual, CritterRig rig)
        {
            float k = s.scale;
            float L = s.bodyLength * k, H = s.bodyHeight * k, leg = s.legLength * k, head = s.headSize * k * 1.15f;
            var p = rig.persona;
            Color fur = s.furColor, acc = s.accentColor;
            Color belly = Color.Lerp(fur, Color.white, p == Persona.Bear ? 0.25f : 0.45f);
            Color dark = Color.Lerp(fur, Ink, 0.7f);
            float bodyY = leg + H * 0.5f;
            bool chunky = p == Persona.Bear || p == Persona.Boar || p == Persona.Boss || p == Persona.Raccoon;
            bool canine = p == Persona.Wolf || p == Persona.Coyote;

            var posture = Node("Posture", visual, new Vector3(0f, leg, -L * 0.32f));
            var body = Node("Body", posture, new Vector3(0f, bodyY, 0f));
            Mesh(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, bodyY, 0f), new Vector3(H * (chunky ? 1.22f : 1.08f), L * 0.52f, H * (chunky ? 1.12f : 1f)), fur, Quaternion.Euler(90f, 0f, 0f));
            Mesh(PrimitiveType.Sphere, "Belly", body, new Vector3(0f, bodyY - H * 0.2f, 0.02f), new Vector3(H * 0.85f, H * 0.62f, L * 0.78f), belly);
            if (p == Persona.Deer)
                for (int i = 0; i < 6; i++)
                    Mesh(PrimitiveType.Sphere, "Spot", body, new Vector3((i % 2 == 0 ? -1f : 1f) * H * 0.22f, bodyY + H * 0.45f, -L * 0.25f + i * L * 0.09f), new Vector3(H * 0.12f, H * 0.05f, H * 0.12f), acc);
            if (p == Persona.Boar)
                for (int i = 0; i < 5; i++)
                    Mesh(PrimitiveType.Cube, "Bristle", body, new Vector3(0f, bodyY + H * 0.55f, L * 0.25f - i * L * 0.12f), new Vector3(H * 0.06f, H * 0.22f, H * 0.08f), dark, Quaternion.Euler(-25f, 0f, 0f));

            // Legs (pivots at the hips)
            float t = Mathf.Max(0.05f, H * (chunky ? 0.36f : 0.28f));
            float hipY = leg + H * 0.18f;
            var legs = new List<Transform>(); var rear = new List<bool>();
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * H * 0.32f;
                float z = (i < 2 ? 1f : -1f) * L * 0.3f;
                var hip = Node($"Leg{i}", posture, new Vector3(x, hipY, z));
                Mesh(PrimitiveType.Capsule, "Leg", hip, new Vector3(x, hipY * 0.5f, z), new Vector3(t, hipY * 0.5f + t * 0.2f, t), fur);
                Mesh(p == Persona.Bear || p == Persona.Wolf ? PrimitiveType.Sphere : PrimitiveType.Cylinder, "Foot", hip,
                     new Vector3(x, t * 0.3f, z + t * 0.12f), new Vector3(t * 1.25f, t * 0.35f, t * 1.35f), dark);
                legs.Add(hip); rear.Add(i >= 2);
            }
            rig.legs = legs.ToArray();
            rig.rearLeg = rear.ToArray();
            rig.legPhase = new[] { 0f, Mathf.PI, Mathf.PI, 0f };

            // Head on a neck pivot
            var neckBase = new Vector3(0f, bodyY + H * 0.25f, L * 0.42f);
            var headPos = new Vector3(0f, bodyY + H * 0.75f, L * 0.5f + head * 0.45f);
            if (p == Persona.Boar || p == Persona.Bear) headPos = new Vector3(0f, bodyY + H * 0.3f, L * 0.5f + head * 0.4f); // low-slung heads
            var headPivot = Node("Head", posture, neckBase);
            Limb("Neck", headPivot, neckBase, headPos, H * (chunky ? 0.6f : 0.42f), fur);
            Mesh(PrimitiveType.Sphere, "Skull", headPivot, headPos, new Vector3(head * 1.0f, head * 0.9f, head * 1.1f), fur);

            float snoutLen = canine ? 0.85f : p == Persona.Opossum ? 1.0f : p == Persona.Moose ? 0.9f : p == Persona.Boar || p == Persona.Raccoon ? 0.6f : 0.65f;
            var snout = headPos + new Vector3(0f, -head * 0.14f, head * 0.5f);
            Mesh(PrimitiveType.Sphere, "Snout", headPivot, snout, new Vector3(head * 0.6f, head * 0.5f, head * snoutLen), p == Persona.Boar ? Color.Lerp(fur, Pink, 0.35f) : p == Persona.Opossum ? new Color(0.97f, 0.95f, 0.92f) : belly);
            var noseAt = snout + new Vector3(0f, head * 0.08f, head * snoutLen * 0.45f);
            rig.nose = p == Persona.Boar
                ? Mesh(PrimitiveType.Cylinder, "Nose", headPivot, noseAt, new Vector3(head * 0.38f, head * 0.05f, head * 0.32f), Color.Lerp(Pink, Ink, 0.35f), Quaternion.Euler(90f, 0f, 0f)).transform
                : Mesh(PrimitiveType.Sphere, "Nose", headPivot, noseAt, Vector3.one * head * (p == Persona.Moose ? 0.32f : 0.2f), p == Persona.Opossum ? Pink : Ink).transform;

            // Jaw + tongue
            var jawPivot = Node("Jaw", headPivot, headPos + new Vector3(0f, -head * 0.3f, head * 0.15f));
            Mesh(PrimitiveType.Cube, "Chin", jawPivot, headPos + new Vector3(0f, -head * 0.34f, head * 0.42f), new Vector3(head * 0.42f, head * 0.1f, head * 0.5f), belly);
            var tongue = Mesh(PrimitiveType.Cube, "Tongue", jawPivot, headPos + new Vector3(0f, -head * 0.36f, head * 0.78f), new Vector3(head * 0.2f, head * 0.05f, head * 0.35f), Pink, Quaternion.Euler(35f, 0f, 0f)).gameObject;
            tongue.SetActive(false);
            rig.jaw = jawPivot; rig.tongue = tongue;
            if (p == Persona.Moose)
                Mesh(PrimitiveType.Sphere, "Dewlap", jawPivot, headPos + new Vector3(0f, -head * 0.62f, head * 0.15f), new Vector3(head * 0.18f, head * 0.4f, head * 0.18f), dark);
            if (p == Persona.Boar)
                for (int e = -1; e <= 1; e += 2)
                    Mesh(PrimitiveType.Capsule, "Tusk", jawPivot, headPos + new Vector3(e * head * 0.26f, -head * 0.2f, head * 0.62f), new Vector3(head * 0.08f, head * 0.18f, head * 0.08f), acc, Quaternion.Euler(-30f, 0f, e * 15f));

            Face(rig, headPivot, headPos, head, 0.27f, 0.14f, 0.38f, 0.34f, p == Persona.Boss ? new Color(1f, 0.25f, 0.1f) : Pupil);
            if (p == Persona.Raccoon) // bandit mask
                Mesh(PrimitiveType.Cube, "Mask", headPivot, headPos + new Vector3(0f, head * 0.13f, head * 0.33f), new Vector3(head * 1.0f, head * 0.24f, head * 0.22f), Ink);

            // Ears
            float earLen = head * (s.bigEars ? 0.45f : 0.28f);
            bool round = p == Persona.Bear || p == Persona.Raccoon || p == Persona.Opossum;
            Color earCol = p == Persona.Opossum ? Ink : fur;
            Color innerCol = p == Persona.Raccoon ? Color.white : Pink;
            var ears = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var ear = Node("Ear", headPivot, headPos + new Vector3(e * head * 0.32f, head * 0.36f, -head * 0.12f), Quaternion.Euler(0f, 0f, -e * (round ? 10f : 28f)));
                LocalMesh(PrimitiveType.Sphere, "Ear", ear, new Vector3(0f, earLen * 0.42f, 0f), round ? new Vector3(head * 0.26f, head * 0.24f, head * 0.12f) : new Vector3(head * 0.17f, earLen, head * 0.07f), earCol);
                LocalMesh(PrimitiveType.Sphere, "Inner", ear, new Vector3(0f, earLen * 0.42f, head * 0.03f), round ? new Vector3(head * 0.15f, head * 0.14f, head * 0.06f) : new Vector3(head * 0.09f, earLen * 0.7f, head * 0.03f), innerCol);
                ears.Add(ear);
            }
            rig.ears = ears.ToArray();

            if (s.antlers) Antlers(headPivot, headPos, head, acc, p == Persona.Moose || p == Persona.Boss);
            if (p == Persona.Boss)
            {
                var gold = new Color(1f, 0.82f, 0.2f);
                Mesh(PrimitiveType.Cylinder, "Crown", headPivot, headPos + new Vector3(0f, head * 0.52f, -head * 0.05f), new Vector3(head * 0.42f, head * 0.07f, head * 0.42f), gold);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2f / 5f;
                    Mesh(PrimitiveType.Cube, "Point", headPivot, headPos + new Vector3(Mathf.Cos(a) * head * 0.17f, head * 0.66f, -head * 0.05f + Mathf.Sin(a) * head * 0.17f), new Vector3(head * 0.06f, head * 0.14f, head * 0.06f), gold);
                }
            }

            // Tail
            var tailPivot = Node("Tail", body, new Vector3(0f, bodyY + H * 0.25f, -L * 0.5f));
            switch (p)
            {
                case Persona.Raccoon:
                {
                    var tail = Node("Rings", tailPivot, tailPivot.position, Quaternion.Euler(20f, 0f, 0f));
                    for (int i = 0; i < 5; i++)
                        LocalMesh(PrimitiveType.Capsule, "Ring", tail, new Vector3(0f, 0f, -H * (0.12f + i * 0.13f)), new Vector3(H * 0.3f, H * 0.09f, H * 0.3f), i % 2 == 0 ? fur : Ink, Quaternion.Euler(90f, 0f, 0f));
                    break;
                }
                case Persona.Opossum:
                {
                    var tail = Node("Bare", tailPivot, tailPivot.position, Quaternion.Euler(15f, 0f, 0f));
                    LocalMesh(PrimitiveType.Capsule, "Tail", tail, new Vector3(0f, 0f, -L * 0.3f), new Vector3(H * 0.09f, L * 0.3f, H * 0.09f), acc, Quaternion.Euler(90f, 0f, 0f));
                    LocalMesh(PrimitiveType.Sphere, "Curl", tail, new Vector3(0f, -H * 0.05f, -L * 0.6f), Vector3.one * H * 0.12f, acc);
                    break;
                }
                case Persona.Wolf:
                case Persona.Coyote:
                {
                    var tail = Node("Brush", tailPivot, tailPivot.position, Quaternion.Euler(35f, 0f, 0f));
                    float len = L * 0.5f;
                    LocalMesh(PrimitiveType.Capsule, "Fluff", tail, new Vector3(0f, 0f, -len * 0.5f), new Vector3(H * 0.3f, len * 0.5f, H * 0.3f), fur, Quaternion.Euler(90f, 0f, 0f));
                    LocalMesh(PrimitiveType.Sphere, "Tip", tail, new Vector3(0f, 0f, -len * 0.95f), Vector3.one * H * 0.27f, acc);
                    break;
                }
                case Persona.Deer:
                    LocalMesh(PrimitiveType.Sphere, "Flag", tailPivot, new Vector3(0f, -H * 0.05f, -H * 0.12f), new Vector3(H * 0.25f, H * 0.38f, H * 0.1f), fur);
                    LocalMesh(PrimitiveType.Sphere, "Under", tailPivot, new Vector3(0f, -H * 0.05f, -H * 0.16f), new Vector3(H * 0.2f, H * 0.32f, H * 0.06f), acc);
                    break;
                case Persona.Boar:
                    LocalMesh(PrimitiveType.Capsule, "Curl", tailPivot, new Vector3(0f, 0f, -H * 0.09f), new Vector3(H * 0.06f, H * 0.09f, H * 0.06f), dark, Quaternion.Euler(90f, 0f, 0f));
                    LocalMesh(PrimitiveType.Sphere, "Curl2", tailPivot, new Vector3(H * 0.07f, H * 0.05f, -H * 0.17f), Vector3.one * H * 0.08f, dark);
                    break;
                default:
                    LocalMesh(PrimitiveType.Sphere, "Nub", tailPivot, new Vector3(0f, 0f, -H * 0.08f), Vector3.one * H * 0.24f, fur);
                    break;
            }
            rig.tail = tailPivot;
            rig.posture = posture; rig.body = body; rig.head = headPivot;
            rig.stride = 1f / (leg * 2.4f + 0.25f);
            rig.legSwing = p == Persona.Bear || p == Persona.Moose ? 26f : 34f;
            rig.grazer = p == Persona.Deer || p == Persona.Moose || p == Persona.Boar || p == Persona.Raccoon || p == Persona.Opossum;

            return new Result { bodyCenter = new Vector3(0f, bodyY, 0f), bodyRadius = H * 0.5f, bodyLength = L, headPos = headPos, headRadius = head * 0.6f };
        }

        static void Antlers(Transform headPivot, Vector3 headPos, float head, Color c, bool palmate)
        {
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 b = headPos + new Vector3(e * head * 0.25f, head * 0.42f, -head * 0.2f);
                float a = 1.6f;
                if (palmate)
                {
                    Mesh(PrimitiveType.Capsule, "Beam", headPivot, b + new Vector3(e * head * 0.2f, head * 0.02f, 0f), new Vector3(head * 0.13f, head * 0.22f, head * 0.13f), c, Quaternion.Euler(0f, 0f, -e * 70f));
                    Mesh(PrimitiveType.Sphere, "Palm", headPivot, b + new Vector3(e * head * 0.62f, head * 0.2f, 0f), new Vector3(head * 0.7f, head * 0.45f, head * 0.12f), c, Quaternion.Euler(0f, e * 10f, -e * 20f));
                    for (int i = 0; i < 3; i++)
                        Mesh(PrimitiveType.Capsule, "Prong", headPivot, b + new Vector3(e * head * (0.42f + i * 0.2f), head * (0.45f + i * 0.04f), 0f), new Vector3(head * 0.08f, head * 0.14f, head * 0.08f), c, Quaternion.Euler(0f, 0f, -e * (10f + i * 14f)));
                }
                else
                {
                    Mesh(PrimitiveType.Capsule, "Antler", headPivot, b + new Vector3(e * head * 0.35f * a, head * 0.45f * a, 0f), new Vector3(head * 0.1f, head * 0.5f * a, head * 0.1f), c, Quaternion.Euler(0f, 0f, -e * 35f));
                    Mesh(PrimitiveType.Capsule, "Tine", headPivot, b + new Vector3(e * head * 0.75f * a, head * 0.85f * a, head * 0.1f), new Vector3(head * 0.09f, head * 0.25f * a, head * 0.09f), c, Quaternion.Euler(-25f, 0f, -e * 60f));
                    Mesh(PrimitiveType.Capsule, "Tine2", headPivot, b + new Vector3(e * head * 0.45f * a, head * 0.95f * a, -head * 0.1f), new Vector3(head * 0.08f, head * 0.22f * a, head * 0.08f), c, Quaternion.Euler(20f, 0f, -e * 10f));
                }
            }
        }

        // ================================================================== spooky variants

        /// <summary>
        /// Ghosts: see-through pale bodies with hollow glowing eyes, hovering.
        /// Shadow creatures: pitch-dark with burning eyes and faintly glowing antlers.
        /// </summary>
        static void Spookify(CritterRig rig, AnimalSpecies s)
        {
            Material ghost = s.ghostly ? GhostMaterial() : null;
            for (int i = 0; i < _rends.Count; i++)
            {
                var r = _rends[i];
                string n = r.name;
                if (s.ghostly)
                {
                    r.sharedMaterial = ghost;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (n == "White") { _cols[i] = new Color(0.6f, 0.9f, 1f, 0.6f); _emis[i] = new Color(0.15f, 0.4f, 0.5f); }
                    else if (n == "Pupil") { _cols[i] = new Color(0.5f, 1f, 1f, 1f); _emis[i] = new Color(0.6f, 2.2f, 2.6f); } // hollow glowing eyes
                    else if (n == "Shine") { _cols[i] = new Color(1f, 1f, 1f, 1f); _emis[i] = new Color(2f, 2f, 2f); }
                    else if (n.StartsWith("Stroke")) { _cols[i] = new Color(0.5f, 1f, 1f, 0.9f); _emis[i] = new Color(0.3f, 1.2f, 1.4f); }
                    else { var c = Color.Lerp(_cols[i], new Color(0.85f, 0.95f, 1f), 0.65f); c.a = 0.45f; _cols[i] = c; _emis[i] = new Color(0.08f, 0.14f, 0.18f); }
                }
                else
                {
                    if (n == "White") _cols[i] = new Color(0.08f, 0.02f, 0.1f);
                    else if (n == "Pupil") { _cols[i] = new Color(1f, 0.25f, 0.9f); _emis[i] = new Color(2.2f, 0.3f, 1.8f); }
                    else if (n.StartsWith("Antler") || n.StartsWith("Tine")) _emis[i] = new Color(0.35f, 0.06f, 0.45f);
                }
            }
            rig.floaty = s.ghostly;
        }

        static Material GlowMaterial()
        {
            const string path = "Assets/_Game/Materials/AnimalGlow.mat";
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(m, path); }
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            UnityEditor.EditorUtility.SetDirty(m);
            return m;
        }

        static Material GhostMaterial()
        {
            const string path = "Assets/_Game/Materials/Ghost.mat";
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white); // real glow comes from each part's property block
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetFloat("_Smoothness", 0.6f);
            UnityEditor.EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================== bird

        static Result Bird(AnimalSpecies s, Transform visual, CritterRig rig)
        {
            float k = s.scale;
            float L = s.bodyLength * k, H = s.bodyHeight * k, leg = s.legLength * k, head = s.headSize * k * 1.25f;
            var p = rig.persona;
            bool goose = p == Persona.Goose;
            Color fur = s.furColor, acc = s.accentColor;
            Color belly = goose ? Color.Lerp(fur, Color.white, 0.6f) : new Color(0.62f, 0.55f, 0.45f);
            Color bodyCol = goose ? fur : new Color(0.55f, 0.5f, 0.42f); // mallard: grey-brown body, green head
            Color wingCol = Color.Lerp(bodyCol, Ink, 0.18f);
            float bodyY = leg + H * 0.5f;

            var posture = Node("Posture", visual, new Vector3(0f, leg, 0f));
            var body = Node("Body", posture, new Vector3(0f, bodyY, 0f));
            Mesh(PrimitiveType.Sphere, "Torso", body, new Vector3(0f, bodyY, 0f), new Vector3(H * 0.95f, H * 0.9f, L), bodyCol);
            Mesh(PrimitiveType.Sphere, "Chest", body, new Vector3(0f, bodyY - H * 0.08f, L * 0.18f), new Vector3(H * 0.82f, H * 0.78f, L * 0.68f), belly);

            var tailPivot = Node("Tail", body, new Vector3(0f, bodyY + H * 0.12f, -L * 0.45f));
            LocalMesh(PrimitiveType.Sphere, "Feathers", tailPivot, new Vector3(0f, H * 0.06f, -H * 0.12f), new Vector3(H * 0.45f, H * 0.14f, H * 0.4f), wingCol, Quaternion.Euler(-30f, 0f, 0f));
            if (!goose) LocalMesh(PrimitiveType.Sphere, "Curl", tailPivot, new Vector3(0f, H * 0.2f, -H * 0.18f), Vector3.one * H * 0.09f, Ink);
            rig.tail = tailPivot;

            var wings = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var w = Node("Wing", body, new Vector3(e * H * 0.42f, bodyY + H * 0.22f, 0f));
                LocalMesh(PrimitiveType.Sphere, "Feathers", w, new Vector3(e * H * 0.05f, -H * 0.18f, -L * 0.05f), new Vector3(H * 0.15f, H * 0.55f, L * 0.68f), wingCol);
                if (!goose) LocalMesh(PrimitiveType.Cube, "Speculum", w, new Vector3(e * H * 0.12f, -H * 0.2f, -L * 0.12f), new Vector3(H * 0.02f, H * 0.12f, L * 0.2f), new Color(0.2f, 0.35f, 0.85f));
                wings.Add(w);
            }
            rig.wings = wings.ToArray();

            // Neck + head
            float neckLen = goose ? H * 1.35f : H * 0.45f;
            var neckBase = new Vector3(0f, bodyY + H * 0.3f, L * 0.3f);
            var headPos = neckBase + new Vector3(0f, neckLen, head * 0.2f);
            var headPivot = Node("Head", posture, neckBase);
            Limb("Neck", headPivot, neckBase, headPos, H * (goose ? 0.28f : 0.34f), goose ? fur : fur);
            if (!goose) Mesh(PrimitiveType.Cylinder, "Collar", headPivot, Vector3.Lerp(neckBase, headPos, 0.35f), new Vector3(H * 0.38f, H * 0.03f, H * 0.38f), Color.white);
            Mesh(PrimitiveType.Sphere, "Skull", headPivot, headPos, Vector3.one * head, fur);
            Mesh(PrimitiveType.Cube, "Beak", headPivot, headPos + new Vector3(0f, -head * 0.06f, head * 0.6f), new Vector3(head * 0.5f, head * 0.16f, head * 0.6f), acc);
            if (goose) Mesh(PrimitiveType.Sphere, "Knob", headPivot, headPos + new Vector3(0f, head * 0.05f, head * 0.42f), Vector3.one * head * 0.22f, acc);
            var jawPivot = Node("Jaw", headPivot, headPos + new Vector3(0f, -head * 0.16f, head * 0.32f));
            Mesh(PrimitiveType.Cube, "LowerBeak", jawPivot, headPos + new Vector3(0f, -head * 0.19f, head * 0.6f), new Vector3(head * 0.44f, head * 0.1f, head * 0.52f), Color.Lerp(acc, Ink, 0.15f));
            rig.jaw = jawPivot;

            Face(rig, headPivot, headPos, head, 0.3f, 0.12f, 0.3f, 0.36f, Pupil);

            // Legs + webbed feet
            var legs = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var hip = Node("Leg", posture, new Vector3(e * H * 0.22f, leg + H * 0.05f, 0f));
                Mesh(PrimitiveType.Cylinder, "Shin", hip, new Vector3(e * H * 0.22f, (leg + H * 0.05f) * 0.5f, 0f), new Vector3(0.035f * k, (leg + H * 0.05f) * 0.5f, 0.035f * k), acc);
                Mesh(PrimitiveType.Cube, "Foot", hip, new Vector3(e * H * 0.22f, 0.012f, H * 0.12f), new Vector3(H * 0.34f, 0.024f, H * 0.4f), acc);
                legs.Add(hip);
            }
            rig.legs = legs.ToArray();
            rig.legPhase = new[] { 0f, Mathf.PI };
            rig.rearLeg = new[] { false, false };
            rig.posture = posture; rig.body = body; rig.head = headPivot;
            rig.biped = true; rig.grazer = true;
            rig.stride = 1f / (leg * 3f + 0.08f);
            rig.legSwing = 30f;

            return new Result { bodyCenter = new Vector3(0f, bodyY, 0f), bodyRadius = H * 0.5f, bodyLength = L, headPos = headPos, headRadius = head * 0.6f };
        }

        // ================================================================== hopper

        static Result Hopper(AnimalSpecies s, Transform visual, CritterRig rig)
        {
            float k = s.scale;
            float L = s.bodyLength * k, H = s.bodyHeight * k, leg = s.legLength * k, head = s.headSize * k * 1.2f;
            Color fur = s.furColor, acc = s.accentColor;
            Color belly = Color.Lerp(fur, Color.white, 0.55f);
            float bodyY = leg + H * 0.48f;

            var posture = Node("Posture", visual, new Vector3(0f, leg * 0.5f, -L * 0.2f));
            var body = Node("Body", posture, new Vector3(0f, bodyY, 0f));
            Mesh(PrimitiveType.Sphere, "Torso", body, new Vector3(0f, bodyY, -L * 0.04f), new Vector3(H * 1.3f, H * 1.2f, L * 1.0f), fur);
            Mesh(PrimitiveType.Sphere, "Belly", body, new Vector3(0f, bodyY - H * 0.15f, L * 0.12f), new Vector3(H * 0.9f, H * 0.8f, L * 0.6f), belly);

            var tailPivot = Node("Tail", body, new Vector3(0f, bodyY + H * 0.12f, -L * 0.52f));
            LocalMesh(PrimitiveType.Sphere, "Cotton", tailPivot, new Vector3(0f, 0f, -H * 0.06f), Vector3.one * H * 0.5f, acc);
            rig.tail = tailPivot;

            var legs = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var hind = Node("Hind", posture, new Vector3(e * H * 0.4f, bodyY - H * 0.2f, -L * 0.18f));
                Mesh(PrimitiveType.Sphere, "Haunch", hind, new Vector3(e * H * 0.42f, bodyY - H * 0.15f, -L * 0.18f), new Vector3(H * 0.4f, H * 0.65f, L * 0.45f), fur);
                Mesh(PrimitiveType.Capsule, "Foot", hind, new Vector3(e * H * 0.38f, H * 0.08f, -L * 0.04f), new Vector3(H * 0.26f, L * 0.24f, H * 0.2f), belly, Quaternion.Euler(90f, 0f, 0f));
                legs.Add(hind);
            }
            for (int e = -1; e <= 1; e += 2)
            {
                var front = Node("Paw", posture, new Vector3(e * H * 0.22f, bodyY - H * 0.2f, L * 0.3f));
                Mesh(PrimitiveType.Capsule, "Arm", front, new Vector3(e * H * 0.22f, (bodyY - H * 0.2f) * 0.5f, L * 0.33f), new Vector3(H * 0.18f, (bodyY - H * 0.2f) * 0.5f, H * 0.18f), fur);
                Mesh(PrimitiveType.Sphere, "Mitt", front, new Vector3(e * H * 0.22f, H * 0.07f, L * 0.36f), Vector3.one * H * 0.22f, belly);
                legs.Add(front);
            }
            rig.legs = legs.ToArray();
            rig.legPhase = new float[4];
            rig.rearLeg = new[] { true, true, false, false };

            var neckBase = new Vector3(0f, bodyY + H * 0.3f, L * 0.3f);
            var headPos = new Vector3(0f, bodyY + H * 0.6f, L * 0.48f);
            var headPivot = Node("Head", posture, neckBase);
            Mesh(PrimitiveType.Sphere, "Skull", headPivot, headPos, new Vector3(head * 1.05f, head * 0.95f, head * 1.0f), fur);
            for (int e = -1; e <= 1; e += 2)
                Mesh(PrimitiveType.Sphere, "Cheek", headPivot, headPos + new Vector3(e * head * 0.2f, -head * 0.2f, head * 0.26f), Vector3.one * head * 0.42f, belly);
            rig.nose = Mesh(PrimitiveType.Sphere, "Nose", headPivot, headPos + new Vector3(0f, -head * 0.04f, head * 0.5f), Vector3.one * head * 0.15f, Pink).transform;
            Mesh(PrimitiveType.Cube, "Teeth", headPivot, headPos + new Vector3(0f, -head * 0.3f, head * 0.43f), new Vector3(head * 0.17f, head * 0.15f, head * 0.04f), Color.white);

            Face(rig, headPivot, headPos, head, 0.27f, 0.1f, 0.33f, 0.37f, Pupil);

            float earLen = head * 0.95f;
            var ears = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var ear = Node("Ear", headPivot, headPos + new Vector3(e * head * 0.16f, head * 0.38f, -head * 0.1f), Quaternion.Euler(-12f, 0f, -e * 12f));
                LocalMesh(PrimitiveType.Capsule, "Ear", ear, new Vector3(0f, earLen * 0.5f, 0f), new Vector3(head * 0.24f, earLen * 0.5f, head * 0.11f), fur);
                LocalMesh(PrimitiveType.Capsule, "Inner", ear, new Vector3(0f, earLen * 0.5f, head * 0.04f), new Vector3(head * 0.13f, earLen * 0.4f, head * 0.04f), Pink);
                ears.Add(ear);
            }
            rig.ears = ears.ToArray();

            rig.posture = posture; rig.body = body; rig.head = headPivot;
            rig.hopper = true; rig.grazer = true;
            rig.stride = Mathf.Max(0.5f, s.hopsPerMeter);
            rig.legSwing = 40f;
            return new Result { bodyCenter = new Vector3(0f, bodyY, 0f), bodyRadius = H * 0.64f, bodyLength = L, headPos = headPos, headRadius = head * 0.5f };
        }

        // ================================================================== face

        /// <summary>Big cartoon eyes (white, pupil, shine), brows, and hidden X-eyes for when it dies.</summary>
        static void Face(CritterRig rig, Transform headPivot, Vector3 headPos, float head, float ex, float ey, float ez, float size, Color pupilColor)
        {
            var eyes = new List<Transform>(); var pupils = new List<Transform>(); var brows = new List<Transform>(); var xs = new List<GameObject>();
            for (int e = -1; e <= 1; e += 2)
            {
                var at = headPos + new Vector3(e * head * ex, head * ey, head * ez);
                var eye = Node("Eye", headPivot, at, Quaternion.Euler(0f, e * 14f, 0f));
                eye.localScale = Vector3.one * head * size;
                LocalMesh(PrimitiveType.Sphere, "White", eye, Vector3.zero, Vector3.one, EyeWhite);
                var pupil = LocalMesh(PrimitiveType.Sphere, "Pupil", eye, new Vector3(0f, 0f, 0.36f), new Vector3(0.56f, 0.62f, 0.4f), pupilColor).transform;
                LocalMesh(PrimitiveType.Sphere, "Shine", pupil, new Vector3(0.28f, 0.32f, 0.42f), Vector3.one * 0.32f, Color.white);
                eyes.Add(eye); pupils.Add(pupil);

                var brow = Mesh(PrimitiveType.Cube, "Brow", headPivot, at + new Vector3(0f, head * size * 0.62f, head * 0.04f), new Vector3(head * size * 0.95f, head * 0.07f, head * 0.07f), Ink, Quaternion.Euler(0f, e * 14f, 0f)).transform;
                brows.Add(brow);

                var x = Node("XEye", headPivot, at + new Vector3(0f, 0f, head * size * 0.4f), Quaternion.Euler(0f, e * 14f, 0f));
                LocalMesh(PrimitiveType.Cube, "Stroke1", x, Vector3.zero, new Vector3(head * size * 1.1f, head * 0.07f, head * 0.05f), Ink, Quaternion.Euler(0f, 0f, 45f));
                LocalMesh(PrimitiveType.Cube, "Stroke2", x, Vector3.zero, new Vector3(head * size * 1.1f, head * 0.07f, head * 0.05f), Ink, Quaternion.Euler(0f, 0f, -45f));
                x.gameObject.SetActive(false);
                xs.Add(x.gameObject);
            }
            rig.eyes = eyes.ToArray(); rig.pupils = pupils.ToArray(); rig.brows = brows.ToArray(); rig.xEyes = xs.ToArray();
        }

        // ================================================================== helpers

        static Transform Node(string name, Transform parent, Vector3 worldPos, Quaternion? rot = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = worldPos;
            t.rotation = rot ?? Quaternion.identity;
            return t;
        }

        /// <summary>A painted primitive placed in build (world) space under <paramref name="parent"/>.</summary>
        static Renderer Mesh(PrimitiveType type, string name, Transform parent, Vector3 worldPos, Vector3 scale, Color color, Quaternion? rot = null)
        {
            var go = Prim(type, name, parent);
            go.transform.position = worldPos;
            go.transform.rotation = rot ?? Quaternion.identity;
            SetWorldScale(go.transform, scale);
            return Paint(go, color);
        }

        /// <summary>A painted primitive placed in the parent's local space (for rotated/scaled pivots).</summary>
        static Renderer LocalMesh(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Color color, Quaternion? localRot = null)
        {
            var go = Prim(type, name, parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            return Paint(go, color);
        }

        /// <summary>A capsule stretched between two build-space points (necks).</summary>
        static void Limb(string name, Transform parent, Vector3 from, Vector3 to, float thickness, Color color)
        {
            var d = to - from;
            Mesh(PrimitiveType.Capsule, name, parent, (from + to) * 0.5f, new Vector3(thickness, d.magnitude * 0.5f + thickness * 0.25f, thickness), color, Quaternion.FromToRotation(Vector3.up, d.normalized));
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            return go;
        }

        static void SetWorldScale(Transform t, Vector3 scale)
        {
            var p = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.x / p.x, scale.y / p.y, scale.z / p.z);
        }

        static Renderer Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _rends.Add(r);
            _cols.Add(c);
            _emis.Add(Color.black);
            return r;
        }
    }
}
