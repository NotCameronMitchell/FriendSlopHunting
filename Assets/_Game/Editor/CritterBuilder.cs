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
        static Material _mat, _plain;

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
            "Fox" => Persona.Fox,
            "Skunk" => Persona.Skunk,
            "MountainGoat" => Persona.Goat,
            "Turkey" => Persona.Turkey,
            "Hamzilla" => Persona.Boar,
            _ => Persona.Boss,
        };

        public static Result Build(AnimalSpecies s, string file, Transform visual, Material mat)
        {
            _rends = new List<Renderer>();
            _cols = new List<Color>();
            _emis = new List<Color>();
            _mat = mat;
            FurUp(mat);
            _plain = PlainMaterial(mat);
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
            if (rig.persona == Persona.Boss || s.isBoss)
                for (int i = 0; i < _rends.Count; i++) if (_rends[i].name == "Pupil") _emis[i] = new Color(1.8f, 0.25f, 0.1f);
            // Glowing parts use a material with emission switched on (property blocks set each glow color).
            for (int i = 0; i < _rends.Count; i++)
                if (_emis[i].maxColorComponent > 0f && (_rends[i].sharedMaterial == _mat || _rends[i].sharedMaterial == _plain)) _rends[i].sharedMaterial = GlowMaterial();
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
            Color belly = p == Persona.Fox ? acc : p == Persona.Skunk ? Color.Lerp(fur, Color.white, 0.12f) : Color.Lerp(fur, Color.white, p == Persona.Bear ? 0.25f : 0.45f);
            Color dark = Color.Lerp(fur, Ink, 0.7f);
            float bodyY = leg + H * 0.5f;
            bool chunky = p == Persona.Bear || p == Persona.Boar || p == Persona.Boss || p == Persona.Raccoon || p == Persona.Skunk;
            bool canine = p == Persona.Wolf || p == Persona.Coyote || p == Persona.Fox;

            var posture = Node("Posture", visual, new Vector3(0f, leg, -L * 0.32f));
            var body = Node("Body", posture, new Vector3(0f, bodyY, 0f));

            // Torso: chest + waist + rump masses give each species its own silhouette
            // (deep-chested tucked wolves, wedge-shaped boars, pear-shaped raccoons, humped bears and moose).
            bool hoofed = p == Persona.Deer || p == Persona.Moose || p == Persona.Boss || p == Persona.Boar || p == Persona.Goat;
            bool hump = p == Persona.Bear || p == Persona.Moose || p == Persona.Boss;
            float chestW = 1f, chestH = 1f, rumpW = 1f, waist = 1f, chestDrop = 0f;
            switch (p)
            {
                case Persona.Wolf: case Persona.Coyote: case Persona.Fox: chestH = 1.12f; rumpW = 0.86f; waist = 0.78f; chestDrop = 0.08f; break;
                case Persona.Boar: chestW = 1.18f; chestH = 1.12f; rumpW = 0.88f; waist = 0.95f; break;
                case Persona.Bear: chestW = 1.12f; chestH = 1.05f; rumpW = 1.08f; break;
                case Persona.Moose: case Persona.Boss: chestW = 1.1f; chestH = 1.12f; rumpW = 0.94f; waist = 0.9f; chestDrop = 0.04f; break;
                case Persona.Deer: chestH = 1.06f; rumpW = 0.95f; waist = 0.86f; break;
                case Persona.Goat: chestW = 1.05f; chestH = 1.1f; rumpW = 0.95f; waist = 0.9f; break;
                case Persona.Raccoon: case Persona.Opossum: case Persona.Skunk: chestW = 0.92f; chestH = 0.95f; rumpW = 1.12f; break;
            }
            float girth = chunky ? 1.12f : 1f;
            float chestZ = L * 0.27f, rumpZ = -L * 0.28f;
            Mesh(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, bodyY, 0f), new Vector3(H * girth * waist, L * 0.5f, H * waist * (chunky ? 1.05f : 0.95f)), fur, Quaternion.Euler(90f, 0f, 0f));
            Mesh(PrimitiveType.Sphere, "Chest", body, new Vector3(0f, bodyY - H * chestDrop, chestZ), new Vector3(H * girth * chestW, H * chestH, L * 0.56f), fur);
            Mesh(PrimitiveType.Sphere, "Rump", body, new Vector3(0f, bodyY + H * 0.03f, rumpZ), new Vector3(H * girth * rumpW, H * 0.98f, L * 0.5f), fur);
            Mesh(PrimitiveType.Sphere, "Belly", body, new Vector3(0f, bodyY - H * (canine ? 0.14f : 0.2f), 0.02f), new Vector3(H * 0.8f * waist * girth, H * 0.6f, L * 0.72f), belly);
            if (hump) Mesh(PrimitiveType.Sphere, "Hump", body, new Vector3(0f, bodyY + H * 0.4f, chestZ - L * 0.02f), new Vector3(H * 0.8f, H * 0.62f, L * 0.42f), fur);

            // Markings
            if (p == Persona.Deer)
            {
                for (int i = 0; i < 6; i++)
                    Mesh(PrimitiveType.Sphere, "Spot", body, new Vector3((i % 2 == 0 ? -1f : 1f) * H * 0.2f, bodyY + H * 0.42f, -L * 0.25f + i * L * 0.09f), new Vector3(H * 0.12f, H * 0.08f, H * 0.12f), acc);
                Mesh(PrimitiveType.Sphere, "RumpPatch", body, new Vector3(0f, bodyY + H * 0.08f, rumpZ - L * 0.2f), new Vector3(H * 0.6f, H * 0.62f, L * 0.14f), Color.Lerp(Color.white, fur, 0.08f));
            }
            if (canine) // pale chest bib
                Mesh(PrimitiveType.Sphere, "Bib", body, new Vector3(0f, bodyY - H * 0.1f, chestZ + L * 0.22f), new Vector3(H * 0.55f, H * 0.8f, H * 0.42f), belly);
            if (p == Persona.Boar)
                for (int i = 0; i < 8; i++)
                    Mesh(PrimitiveType.Cube, "Bristle", body, new Vector3(0f, bodyY + H * (0.58f - i * 0.012f), L * 0.42f - i * L * 0.1f), new Vector3(H * 0.07f, H * (0.26f - i * 0.015f), H * 0.09f), dark, Quaternion.Euler(-25f, 0f, 0f));
            if (p == Persona.Skunk) // the white racing stripe
                Mesh(PrimitiveType.Sphere, "Stripe", body, new Vector3(0f, bodyY + H * 0.4f, -L * 0.02f), new Vector3(H * 0.34f, H * 0.24f, L * 1.05f), acc);
            if (p == Persona.Goat) // shaggy shoulders
                Mesh(PrimitiveType.Sphere, "Shag", body, new Vector3(0f, bodyY + H * 0.12f, chestZ + L * 0.04f), new Vector3(H * 1.12f, H * 1.08f, L * 0.42f), Color.Lerp(fur, Color.white, 0.3f));

            // Legs (pivots at the hips): shoulder/haunch mass, jointed segments, hooves or toed paws.
            float t = Mathf.Max(0.05f, H * (chunky ? 0.34f : 0.26f));
            float hipY = leg + H * 0.18f;
            Color lowerCol = p == Persona.Moose ? Color.Lerp(fur, new Color(0.78f, 0.7f, 0.56f), 0.65f)
                           : p == Persona.Opossum || p == Persona.Raccoon ? Color.Lerp(fur, Ink, 0.6f) : p == Persona.Fox ? Color.Lerp(fur, Ink, 0.75f) : fur;
            Color footCol = p == Persona.Opossum ? Pink : hoofed ? Color.Lerp(dark, Ink, 0.5f) : Color.Lerp(fur, Ink, 0.45f);
            var legs = new List<Transform>(); var rear = new List<bool>();
            for (int i = 0; i < 4; i++)
            {
                bool back = i >= 2;
                float x = (i % 2 == 0 ? -1f : 1f) * H * 0.32f;
                float z = (back ? -1f : 1f) * L * 0.3f;
                var hip = Node($"Leg{i}", posture, new Vector3(x, hipY, z));
                Mesh(PrimitiveType.Sphere, back ? "Haunch" : "Shoulder", hip, new Vector3(x * 0.9f, hipY + H * 0.06f, z + (back ? -t * 0.2f : t * 0.1f)),
                     new Vector3(t * (back ? 1.75f : 1.5f), H * (back ? 0.74f : 0.62f), back ? L * 0.3f : L * 0.24f), fur);
                float upper = t * (hoofed ? 0.88f : 1f), mid = t * (hoofed ? 0.58f : 0.76f), low = t * (hoofed ? 0.44f : 0.64f);
                Vector3 a = new Vector3(x, hipY - H * 0.05f, z), b, c, d;
                if (back) { b = new Vector3(x, hipY * 0.62f, z + t * 0.45f); c = new Vector3(x, hipY * 0.3f, z - t * 0.55f); d = new Vector3(x, t * 0.3f, z - t * 0.35f); } // stifle forward, hock back
                else { b = new Vector3(x, hipY * 0.58f, z - t * 0.18f); c = new Vector3(x, hipY * 0.28f, z + t * 0.05f); d = new Vector3(x, t * 0.3f, z + t * 0.08f); }   // elbow back, knee straight
                Limb("Thigh", hip, a, b, upper, fur);
                Limb("Shin", hip, b, c, mid, Color.Lerp(fur, lowerCol, 0.5f));
                Limb("Cannon", hip, c, d, low, lowerCol);
                if (hoofed)
                {
                    var hoofAt = new Vector3(d.x, t * 0.2f, d.z + t * 0.06f);
                    Mesh(PrimitiveType.Cylinder, "Hoof", hip, hoofAt, new Vector3(low * 1.4f, t * 0.2f, low * 1.55f), footCol);
                    Mesh(PrimitiveType.Cube, "Cleft", hip, hoofAt + new Vector3(0f, 0f, low * 0.72f), new Vector3(low * 0.14f, t * 0.38f, low * 0.2f), Ink);
                }
                else
                {
                    var paw = new Vector3(d.x, t * 0.24f, d.z + t * 0.18f);
                    Mesh(PrimitiveType.Sphere, "Paw", hip, paw, new Vector3(t * 1.15f, t * 0.52f, t * 1.35f), footCol);
                    for (int toe = -1; toe <= 1; toe++)
                    {
                        var toeAt = paw + new Vector3(toe * t * 0.32f, -t * 0.05f, t * 0.55f);
                        Mesh(PrimitiveType.Sphere, "Toe", hip, toeAt, Vector3.one * t * 0.4f, footCol);
                        if (p == Persona.Bear || p == Persona.Boss)
                            Mesh(PrimitiveType.Capsule, "Claw", hip, toeAt + new Vector3(0f, -t * 0.02f, t * 0.22f), new Vector3(t * 0.1f, t * 0.16f, t * 0.1f), new Color(0.92f, 0.88f, 0.78f), Quaternion.Euler(70f, 0f, 0f));
                    }
                }
                legs.Add(hip); rear.Add(back);
            }
            rig.legs = legs.ToArray();
            rig.rearLeg = rear.ToArray();
            rig.legPhase = new[] { 0f, Mathf.PI, Mathf.PI, 0f };

            // Head on a neck pivot
            var neckBase = new Vector3(0f, bodyY + H * 0.25f, L * 0.42f);
            var headPos = new Vector3(0f, bodyY + H * 0.75f, L * 0.5f + head * 0.45f);
            if (p == Persona.Boar || p == Persona.Bear) headPos = new Vector3(0f, bodyY + H * 0.3f, L * 0.5f + head * 0.4f); // low-slung heads
            var headPivot = Node("Head", posture, neckBase);
            bool longNeck = p == Persona.Deer || p == Persona.Moose || p == Persona.Boss;
            if (longNeck) // tapering two-part neck
            {
                var midNeck = Vector3.Lerp(neckBase, headPos, 0.5f) + new Vector3(0f, 0f, -head * 0.05f);
                Limb("Neck", headPivot, neckBase, midNeck, H * 0.56f, fur);
                Limb("Neck2", headPivot, midNeck, headPos, H * 0.4f, fur);
                if (p == Persona.Deer)
                    Mesh(PrimitiveType.Sphere, "Throat", headPivot, Vector3.Lerp(neckBase, headPos, 0.78f) + new Vector3(0f, -H * 0.1f, head * 0.16f), new Vector3(H * 0.26f, H * 0.2f, H * 0.2f), Color.Lerp(Color.white, fur, 0.08f));
            }
            else Limb("Neck", headPivot, neckBase, headPos, H * (chunky ? 0.66f : 0.5f), fur);
            if (canine) // fluffy neck ruff
            {
                Vector3 axis = (headPos - neckBase).normalized, side = Vector3.right, up = Vector3.Cross(axis, side).normalized;
                var ring = Vector3.Lerp(neckBase, headPos, 0.25f);
                Color ruff = Color.Lerp(fur, belly, 0.45f);
                for (int i = 0; i < 7; i++)
                {
                    float ang = i / 7f * Mathf.PI * 2f;
                    Mesh(PrimitiveType.Sphere, "Ruff", headPivot, ring + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * H * 0.22f, Vector3.one * H * 0.42f, ruff);
                }
            }
            Color skullCol = p == Persona.Opossum ? new Color(0.95f, 0.93f, 0.9f) : fur;
            Mesh(PrimitiveType.Sphere, "Skull", headPivot, headPos, new Vector3(head * 1.0f, head * 0.9f, head * 1.1f), skullCol);

            float snoutLen = canine ? 0.85f : p == Persona.Opossum ? 1.0f : p == Persona.Moose ? 0.9f : p == Persona.Boar || p == Persona.Raccoon ? 0.6f : 0.65f;
            var snout = headPos + new Vector3(0f, -head * 0.14f, head * 0.5f);
            Color snoutCol = p == Persona.Boar ? Color.Lerp(fur, Pink, 0.35f)
                           : p == Persona.Opossum ? new Color(0.97f, 0.95f, 0.92f)
                           : p == Persona.Bear ? Color.Lerp(fur, new Color(0.82f, 0.66f, 0.45f), 0.6f)
                           : p == Persona.Moose ? Color.Lerp(fur, Ink, 0.25f)
                           : p == Persona.Skunk ? Color.Lerp(fur, Color.white, 0.2f) : belly;
            Mesh(PrimitiveType.Sphere, "Snout", headPivot, snout, new Vector3(head * 0.6f, head * 0.5f, head * snoutLen), snoutCol);
            bool taper = canine || longNeck || p == Persona.Opossum || p == Persona.Goat;
            float noseZ = 0.45f;
            if (taper) // narrower muzzle tip so snouts read as tapered, not as balls
            {
                Mesh(PrimitiveType.Sphere, "Muzzle", headPivot, snout + new Vector3(0f, -head * 0.02f, head * snoutLen * 0.3f), new Vector3(head * 0.44f, head * 0.38f, head * snoutLen * 0.62f), snoutCol);
                noseZ = 0.58f;
            }
            if (p == Persona.Moose) // big droopy moose lip
                Mesh(PrimitiveType.Sphere, "Lip", headPivot, snout + new Vector3(0f, -head * 0.12f, head * snoutLen * 0.5f), new Vector3(head * 0.55f, head * 0.45f, head * 0.45f), snoutCol);
            var noseAt = snout + new Vector3(0f, head * 0.08f, head * snoutLen * noseZ);
            if (p == Persona.Boar)
            {
                rig.nose = Mesh(PrimitiveType.Cylinder, "Nose", headPivot, noseAt, new Vector3(head * 0.38f, head * 0.05f, head * 0.32f), Color.Lerp(Pink, Ink, 0.35f), Quaternion.Euler(90f, 0f, 0f)).transform;
                for (int e = -1; e <= 1; e += 2)
                    Mesh(PrimitiveType.Sphere, "Nostril", headPivot, noseAt + new Vector3(e * head * 0.08f, 0f, head * 0.05f), new Vector3(head * 0.08f, head * 0.1f, head * 0.03f), Ink);
            }
            else
            {
                Vector3 noseSize = canine ? new Vector3(head * 0.26f, head * 0.18f, head * 0.17f) : Vector3.one * head * (p == Persona.Moose ? 0.32f : 0.2f);
                rig.nose = Mesh(PrimitiveType.Sphere, "Nose", headPivot, noseAt, noseSize, p == Persona.Opossum ? Pink : Ink).transform;
            }
            if (canine || p == Persona.Raccoon) // cheek fluff
                for (int e = -1; e <= 1; e += 2)
                    Mesh(PrimitiveType.Sphere, "Cheek", headPivot, headPos + new Vector3(e * head * 0.4f, -head * 0.14f, -head * 0.02f), new Vector3(head * 0.3f, head * 0.36f, head * 0.42f),
                         p == Persona.Raccoon ? new Color(0.93f, 0.92f, 0.9f) : Color.Lerp(fur, belly, 0.6f), Quaternion.Euler(0f, 0f, e * 20f));

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

            Face(rig, headPivot, headPos, head, 0.27f, 0.14f, 0.38f, 0.34f, p == Persona.Boss || s.isBoss ? new Color(1f, 0.25f, 0.1f) : Pupil);
            if (p == Persona.Raccoon) // bandit mask with white brows
            {
                Mesh(PrimitiveType.Cube, "Mask", headPivot, headPos + new Vector3(0f, head * 0.13f, head * 0.33f), new Vector3(head * 1.0f, head * 0.24f, head * 0.22f), Ink);
                for (int e = -1; e <= 1; e += 2)
                    Mesh(PrimitiveType.Sphere, "BrowPatch", headPivot, headPos + new Vector3(e * head * 0.25f, head * 0.33f, head * 0.34f), new Vector3(head * 0.26f, head * 0.09f, head * 0.1f), new Color(0.95f, 0.94f, 0.92f));
            }
            if (p == Persona.Skunk) // white blaze up the forehead
                Mesh(PrimitiveType.Sphere, "Blaze", headPivot, headPos + new Vector3(0f, head * 0.36f, head * 0.12f), new Vector3(head * 0.16f, head * 0.14f, head * 0.85f), acc);
            if (p == Persona.Goat) // swept-back horns and a beard
            {
                Color horn = new Color(0.2f, 0.18f, 0.17f);
                for (int e = -1; e <= 1; e += 2)
                {
                    var hb = headPos + new Vector3(e * head * 0.2f, head * 0.4f, -head * 0.02f);
                    var hm = hb + new Vector3(e * head * 0.05f, head * 0.38f, -head * 0.22f);
                    var ht = hm + new Vector3(e * head * 0.06f, head * 0.08f, -head * 0.36f);
                    Limb("Horn", headPivot, hb, hm, head * 0.15f, horn);
                    Limb("Horn", headPivot, hm, ht, head * 0.1f, horn);
                }
                Mesh(PrimitiveType.Sphere, "Beard", jawPivot, headPos + new Vector3(0f, -head * 0.55f, head * 0.38f), new Vector3(head * 0.2f, head * 0.42f, head * 0.18f), Color.Lerp(fur, Color.white, 0.5f));
            }

            // Ears
            float earLen = head * (s.bigEars ? 0.45f : 0.28f);
            bool round = p == Persona.Bear || p == Persona.Raccoon || p == Persona.Opossum;
            Color earCol = p == Persona.Opossum ? Ink : fur;
            Color innerCol = p == Persona.Raccoon || p == Persona.Fox ? Color.white : Pink;
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
            if (p == Persona.Boss || s.isBoss)
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
                case Persona.Skunk:
                {
                    // Big plume held up over the back, striped white.
                    var tail = Node("Plume", tailPivot, tailPivot.position, Quaternion.Euler(-55f, 0f, 0f));
                    LocalMesh(PrimitiveType.Sphere, "Plume", tail, new Vector3(0f, 0f, -H * 0.42f), new Vector3(H * 0.62f, H * 0.55f, H * 1.0f), fur);
                    LocalMesh(PrimitiveType.Sphere, "Plume", tail, new Vector3(0f, H * 0.08f, -H * 0.85f), new Vector3(H * 0.66f, H * 0.6f, H * 0.7f), fur);
                    LocalMesh(PrimitiveType.Sphere, "Stripe", tail, new Vector3(0f, H * 0.2f, -H * 0.6f), new Vector3(H * 0.26f, H * 0.5f, H * 1.25f), acc);
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
                case Persona.Fox:
                {
                    var tail = Node("Brush", tailPivot, tailPivot.position, Quaternion.Euler(35f, 0f, 0f));
                    float len = L * 0.5f;
                    LocalMesh(PrimitiveType.Capsule, "Fluff", tail, new Vector3(0f, 0f, -len * 0.5f), new Vector3(H * 0.3f, len * 0.5f, H * 0.3f), fur, Quaternion.Euler(90f, 0f, 0f));
                    LocalMesh(PrimitiveType.Sphere, "Puff", tail, new Vector3(0f, 0f, -len * 0.62f), new Vector3(H * 0.36f, H * 0.36f, len * 0.5f), Color.Lerp(fur, Ink, 0.2f));
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

        // ================================================================== materials

        /// <summary>Parts that stay smooth and a little glossy (eyes, noses, hooves, beaks…), not furry.</summary>
        static readonly HashSet<string> Glossy = new HashSet<string>
        {
            "White", "Pupil", "Shine", "Stroke1", "Stroke2", "Brow", "Nose", "Nostril", "Teeth", "Tongue",
            "Hoof", "Cleft", "Claw", "Beak", "LowerBeak", "Knob", "Tusk", "Crown", "Point",
            "Antler", "Tine", "Tine2", "Beam", "Palm", "Prong", "Horn", "Wattle",
        };

        const string FurTexPath = "Assets/_Game/Materials/AnimalFur.png";

        /// <summary>Soft streaky fur on the shared animal material (tinted per part by property blocks), matte.</summary>
        static void FurUp(Material m)
        {
            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(FurTexPath);
            if (tex == null)
            {
                const int N = 256;
                var t = new Texture2D(N, N, TextureFormat.RGB24, true);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (float)x / N, v = (float)y / N;
                        // Strands: noise stretched along v, plus soft clumps and fine speckle.
                        float strands = PNoise(u, v, 64, 32, 1) * 0.5f + PNoise(u, v, 128, 64, 2) * 0.5f;
                        float clumps = PNoise(u, v, 8, 8, 3);
                        float g = 0.86f + strands * 0.08f + clumps * 0.06f;
                        t.SetPixel(x, y, new Color(g, g, g));
                    }
                t.Apply();
                System.IO.File.WriteAllBytes(FurTexPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                UnityEditor.AssetDatabase.ImportAsset(FurTexPath);
                var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(FurTexPath);
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.anisoLevel = 4;
                ti.SaveAndReimport();
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(FurTexPath);
            }
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            m.SetFloat("_Smoothness", 0.12f);
            UnityEditor.EditorUtility.SetDirty(m);
        }

        static Material PlainMaterial(Material fur)
        {
            const string path = "Assets/_Game/Materials/AnimalPlain.mat";
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(fur.shader); UnityEditor.AssetDatabase.CreateAsset(m, path); }
            m.shader = fur.shader;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.55f);
            UnityEditor.EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Tileable value noise with separate x/y cell counts (0..1).</summary>
        static float PNoise(float u, float v, int fx, int fy, int seed)
        {
            float x = u * fx, y = v * fy;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float H(int ix, int iy)
            {
                ix = ((ix % fx) + fx) % fx; iy = ((iy % fy) + fy) % fy;
                uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
            float a = Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx);
            float b = Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx);
            return Mathf.Lerp(a, b, ty);
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
            bool goose = p == Persona.Goose, turkey = p == Persona.Turkey;
            Color fur = s.furColor, acc = s.accentColor;
            Color belly = turkey ? Color.Lerp(fur, Ink, 0.15f) : goose ? Color.Lerp(fur, Color.white, 0.6f) : new Color(0.62f, 0.55f, 0.45f);
            Color bodyCol = goose || turkey ? fur : new Color(0.55f, 0.5f, 0.42f); // mallard: grey-brown body, green head
            Color wingCol = Color.Lerp(bodyCol, Ink, 0.18f);
            float bodyY = leg + H * 0.5f;

            var posture = Node("Posture", visual, new Vector3(0f, leg, 0f));
            var body = Node("Body", posture, new Vector3(0f, bodyY, 0f));
            Mesh(PrimitiveType.Sphere, "Torso", body, new Vector3(0f, bodyY, 0f), new Vector3(H * 0.95f, H * 0.9f, L), bodyCol);
            Mesh(PrimitiveType.Sphere, "Chest", body, new Vector3(0f, bodyY - H * 0.08f, L * 0.18f), new Vector3(H * 0.82f, H * 0.78f, L * 0.68f), belly);

            var tailPivot = Node("Tail", body, new Vector3(0f, bodyY + H * 0.12f, -L * 0.45f));
            if (turkey) // the big strutting fan: banded feathers in a half circle
            {
                Color band = new Color(0.72f, 0.58f, 0.38f);
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.Lerp(-80f, 80f, i / 8f);
                    var dir = Quaternion.Euler(0f, 0f, -a) * Vector3.up;
                    LocalMesh(PrimitiveType.Sphere, "Fan", tailPivot, dir * H * 0.62f + new Vector3(0f, 0f, -H * 0.1f), new Vector3(H * 0.26f, H * 0.95f, H * 0.06f), fur, Quaternion.Euler(0f, 0f, -a));
                    LocalMesh(PrimitiveType.Sphere, "FanTip", tailPivot, dir * H * 1.02f + new Vector3(0f, 0f, -H * 0.12f), new Vector3(H * 0.22f, H * 0.18f, H * 0.05f), band, Quaternion.Euler(0f, 0f, -a));
                }
            }
            else LocalMesh(PrimitiveType.Sphere, "Feathers", tailPivot, new Vector3(0f, H * 0.06f, -H * 0.12f), new Vector3(H * 0.45f, H * 0.14f, H * 0.4f), wingCol, Quaternion.Euler(-30f, 0f, 0f));
            if (!goose && !turkey) LocalMesh(PrimitiveType.Sphere, "Curl", tailPivot, new Vector3(0f, H * 0.2f, -H * 0.18f), Vector3.one * H * 0.09f, Ink);
            rig.tail = tailPivot;

            var wings = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var w = Node("Wing", body, new Vector3(e * H * 0.42f, bodyY + H * 0.22f, 0f));
                LocalMesh(PrimitiveType.Sphere, "Feathers", w, new Vector3(e * H * 0.05f, -H * 0.18f, -L * 0.05f), new Vector3(H * 0.15f, H * 0.55f, L * 0.68f), wingCol);
                if (turkey) LocalMesh(PrimitiveType.Sphere, "Bar", w, new Vector3(e * H * 0.12f, -H * 0.3f, -L * 0.1f), new Vector3(H * 0.04f, H * 0.2f, L * 0.4f), new Color(0.85f, 0.82f, 0.75f));
                else if (!goose) LocalMesh(PrimitiveType.Cube, "Speculum", w, new Vector3(e * H * 0.12f, -H * 0.2f, -L * 0.12f), new Vector3(H * 0.02f, H * 0.12f, L * 0.2f), new Color(0.2f, 0.35f, 0.85f));
                wings.Add(w);
            }
            rig.wings = wings.ToArray();

            // Neck + head
            float neckLen = goose ? H * 1.35f : turkey ? H * 0.75f : H * 0.45f;
            var neckBase = new Vector3(0f, bodyY + H * 0.3f, L * 0.3f);
            var headPos = neckBase + new Vector3(0f, neckLen, head * 0.2f);
            var headPivot = Node("Head", posture, neckBase);
            Limb("Neck", headPivot, neckBase, headPos, H * (goose ? 0.28f : 0.34f), goose ? fur : fur);
            if (!goose && !turkey) Mesh(PrimitiveType.Cylinder, "Collar", headPivot, Vector3.Lerp(neckBase, headPos, 0.35f), new Vector3(H * 0.38f, H * 0.03f, H * 0.38f), Color.white);
            Color beakCol = turkey ? new Color(0.86f, 0.76f, 0.56f) : acc;
            Mesh(PrimitiveType.Sphere, "Skull", headPivot, headPos, Vector3.one * head, turkey ? new Color(0.62f, 0.74f, 0.88f) : fur);
            Mesh(PrimitiveType.Cube, "Beak", headPivot, headPos + new Vector3(0f, -head * 0.06f, head * (turkey ? 0.5f : 0.6f)), new Vector3(head * (turkey ? 0.32f : 0.5f), head * 0.16f, head * (turkey ? 0.38f : 0.6f)), beakCol);
            if (turkey) // red snood over the beak and a wobbly wattle under it
            {
                Mesh(PrimitiveType.Capsule, "Wattle", headPivot, headPos + new Vector3(head * 0.05f, -head * 0.12f, head * 0.62f), new Vector3(head * 0.1f, head * 0.32f, head * 0.1f), acc, Quaternion.Euler(-12f, 0f, 8f));
                Mesh(PrimitiveType.Sphere, "Wattle", headPivot, headPos + new Vector3(0f, -head * 0.62f, head * 0.15f), new Vector3(head * 0.32f, head * 0.5f, head * 0.3f), acc);
            }
            if (goose) Mesh(PrimitiveType.Sphere, "Knob", headPivot, headPos + new Vector3(0f, head * 0.05f, head * 0.42f), Vector3.one * head * 0.22f, acc);
            var jawPivot = Node("Jaw", headPivot, headPos + new Vector3(0f, -head * 0.16f, head * 0.32f));
            Mesh(PrimitiveType.Cube, "LowerBeak", jawPivot, headPos + new Vector3(0f, -head * 0.19f, head * 0.6f), new Vector3(head * (turkey ? 0.28f : 0.44f), head * 0.1f, head * (turkey ? 0.32f : 0.52f)), Color.Lerp(beakCol, Ink, 0.15f));
            rig.jaw = jawPivot;

            Face(rig, headPivot, headPos, head, 0.3f, 0.12f, 0.3f, 0.36f, Pupil);

            // Legs + webbed feet
            var legs = new List<Transform>();
            for (int e = -1; e <= 1; e += 2)
            {
                var hip = Node("Leg", posture, new Vector3(e * H * 0.22f, leg + H * 0.05f, 0f));
                Mesh(PrimitiveType.Cylinder, "Shin", hip, new Vector3(e * H * 0.22f, (leg + H * 0.05f) * 0.5f, 0f), new Vector3(0.035f * k, (leg + H * 0.05f) * 0.5f, 0.035f * k), turkey ? new Color(0.72f, 0.58f, 0.55f) : acc);
                Mesh(PrimitiveType.Cube, "Foot", hip, new Vector3(e * H * 0.22f, 0.012f, H * 0.12f), new Vector3(H * 0.34f, 0.024f, H * 0.4f), turkey ? new Color(0.72f, 0.58f, 0.55f) : acc);
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
            r.sharedMaterial = Glossy.Contains(go.name) ? _plain : _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _rends.Add(r);
            _cols.Add(c);
            _emis.Add(Color.black);
            return r;
        }
    }
}
