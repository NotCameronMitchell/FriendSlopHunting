using System;
using FriendSlop.World;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Points of interest out in the wilds, each on a flat pad carved by <see cref="TerrainBuilder"/>
    /// with a worn trail from camp: the Hog Wallow (Hamzilla's totem), the Ranger Tower (climbable
    /// lookout) and the Abandoned Camp. Towers and camps hold supply crates that restock daily.
    /// </summary>
    public static partial class HuntSceneBuilder
    {
        static GameObject _hogTotemPrefab, _lootCratePrefab;

        static bool NearLandmark(Vector3 p, float margin)
        {
            foreach (var lm in TerrainBuilder.Landmarks)
                if (new Vector2(p.x - lm.x, p.z - lm.z).magnitude < lm.y + margin) return true;
            return false;
        }

        static void BuildLandmarks(Transform world, Scene scene, Func<Vector3, float> groundY)
        {
            var root = new GameObject("Landmarks").transform;
            root.SetParent(world, false);
            // Invisible walls around the walkable valley (tall enough to cover the peaks they run through).
            var edge = new GameObject("MapEdge").transform;
            edge.SetParent(world, false);
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"Edge{i}");
                wall.transform.SetParent(edge, false);
                bool alongX = i < 2;
                float s = (i % 2 == 0 ? 1f : -1f) * (MapHalfSize - 2f);
                wall.transform.localPosition = alongX ? new Vector3(0f, 90f, s) : new Vector3(s, 90f, 0f);
                var bc = wall.AddComponent<BoxCollider>();
                bc.size = alongX ? new Vector3(MapHalfSize * 2f, 280f, 2f) : new Vector3(2f, 280f, MapHalfSize * 2f);
            }

            BuildHogWallow(root, scene, groundY);
            BuildRangerTower(root, scene, groundY);
            BuildOldCamp(root, scene, groundY);
        }

        static Transform LandmarkRoot(Transform parent, string name, Vector3 lm, Func<Vector3, float> groundY)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            var c = new Vector3(lm.x, 0f, lm.z);
            t.position = new Vector3(lm.x, groundY(c), lm.z);
            t.rotation = Quaternion.LookRotation(-c.normalized); // faces back toward camp (where the trail comes from)
            return t;
        }

        /// <summary>A primitive that keeps its collider (walkable / solid scenery).</summary>
        static GameObject Solid(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, string mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            if (mat != null && Mats.TryGetValue(mat, out var m)) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static void Sign(Transform parent, Vector3 localPos, string title, string sub)
        {
            Solid(PrimitiveType.Cube, "SignPost", parent, localPos + new Vector3(0f, 0.9f, 0f), new Vector3(0.14f, 1.8f, 0.14f), "Bark");
            var board = Visual(PrimitiveType.Cube, "SignBoard", parent, localPos + new Vector3(0f, 1.75f, 0.09f), new Vector3(1.9f, 0.6f, 0.06f), "Plank");
            board.transform.localRotation = Quaternion.Euler(0f, 0f, 2f);
            var text = new GameObject("SignText").transform;
            text.SetParent(parent, false);
            text.localPosition = localPos;
            text.localRotation = Quaternion.Euler(0f, 180f, 0f); // readable when walking in
            Text3D(text, title, new Vector3(0f, 1.82f, -0.14f), 0.055f, new Color(1f, 0.9f, 0.6f));
            Text3D(text, sub, new Vector3(0f, 1.6f, -0.14f), 0.026f, new Color(1f, 0.96f, 0.9f));
        }

        static Light Lantern(Transform parent, Vector3 localPos, float range = 9f)
        {
            Visual(PrimitiveType.Cube, "Lantern", parent, localPos, new Vector3(0.2f, 0.28f, 0.2f), "Glow");
            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.7f, 0.4f); light.range = range; light.intensity = 1.6f; light.shadows = LightShadows.None;
            _nightLights.Add(light);
            return light;
        }

        static void PlaceNetworked(GameObject prefab, Scene scene, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(pos, rot);
        }

        // =========================================================== Hog Wallow

        static void BuildHogWallow(Transform parent, Scene scene, Func<Vector3, float> groundY)
        {
            var t = LandmarkRoot(parent, "HogWallow", TerrainBuilder.HogWallow, groundY);
            var rng = new System.Random(21);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // The wallow: a wide mud pool with darker wet puddles and lazy bubbles.
            Visual(PrimitiveType.Cylinder, "Mud", t, new Vector3(0f, 0.03f, 1f), new Vector3(11f, 0.03f, 8.5f), "Mud");
            for (int i = 0; i < 5; i++)
                Visual(PrimitiveType.Cylinder, "Puddle", t, new Vector3(R(-3.5f, 3.5f), 0.05f, 1f + R(-2.5f, 2.5f)), new Vector3(R(1.2f, 2.6f), 0.02f, R(1f, 2.2f)), "MudWet");
            for (int i = 0; i < 9; i++)
                Visual(PrimitiveType.Sphere, "Bubble", t, new Vector3(R(-4f, 4f), 0.06f, 1f + R(-3f, 3f)), Vector3.one * R(0.12f, 0.3f), "MudWet");

            // Fallen logs and a broken fence around it.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + R(-0.3f, 0.3f);
                var pos = new Vector3(Mathf.Cos(a) * 7.2f, 0.3f, 1f + Mathf.Sin(a) * 6.2f);
                Solid(PrimitiveType.Cylinder, "Log", t, pos, new Vector3(0.6f, R(1.4f, 2.2f), 0.6f), "Bark", Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 90f));
            }
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-2.4f, 0.8f, i / 6f);
                var post = Solid(PrimitiveType.Cube, "FencePost", t, new Vector3(Mathf.Cos(a) * 9.5f, 0.55f, 1f + Mathf.Sin(a) * 8.5f), new Vector3(0.16f, 1.1f, 0.16f), "Plank");
                post.transform.localRotation = Quaternion.Euler(R(-12f, 12f), 0f, R(-12f, 12f));
                if (i < 6 && i != 3) // one rail missing
                {
                    float a2 = Mathf.Lerp(-2.4f, 0.8f, (i + 1) / 6f);
                    var p1 = new Vector3(Mathf.Cos(a) * 9.5f, 0.8f, 1f + Mathf.Sin(a) * 8.5f);
                    var p2 = new Vector3(Mathf.Cos(a2) * 9.5f, 0.8f, 1f + Mathf.Sin(a2) * 8.5f);
                    var rail = Visual(PrimitiveType.Cube, "Rail", t, (p1 + p2) * 0.5f, new Vector3(0.08f, 0.12f, (p2 - p1).magnitude), "Plank");
                    rail.transform.localRotation = Quaternion.LookRotation(p2 - p1) * Quaternion.Euler(0f, 0f, R(-6f, 6f));
                }
            }
            Sign(t, new Vector3(3.2f, 0f, 9.6f), "HOG WALLOW", "something BIG wallows here…");

            // Hamzilla's totem at the head of the wallow.
            var totemLocal = new Vector3(0f, 0f, -7.4f);
            PlaceNetworked(_hogTotemPrefab, scene, t.TransformPoint(totemLocal), t.rotation);
        }

        static void HogTotemVisuals(Transform root)
        {
            Visual(PrimitiveType.Cylinder, "MudBase", root, new Vector3(0f, 0.05f, 0f), new Vector3(2.6f, 0.05f, 2.6f), "Mud");
            Visual(PrimitiveType.Cylinder, "Stump", root, new Vector3(0f, 1.2f, 0f), new Vector3(1.3f, 1.2f, 1.3f), "Bark");
            Visual(PrimitiveType.Cylinder, "StumpTop", root, new Vector3(0f, 2.41f, 0f), new Vector3(1.22f, 0.02f, 1.22f), "Plank");
            var pink = "HogPink";
            Visual(PrimitiveType.Sphere, "Skull", root, new Vector3(0f, 3.05f, 0f), new Vector3(1.25f, 1.05f, 1.15f), pink);
            Visual(PrimitiveType.Cylinder, "Snout", root, new Vector3(0f, 2.9f, 0.6f), new Vector3(0.62f, 0.14f, 0.5f), pink).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int e = -1; e <= 1; e += 2)
            {
                Visual(PrimitiveType.Sphere, "Nostril", root, new Vector3(e * 0.13f, 2.9f, 0.75f), new Vector3(0.12f, 0.16f, 0.05f), "Ash");
                Visual(PrimitiveType.Sphere, "Eye", root, new Vector3(e * 0.3f, 3.25f, 0.47f), Vector3.one * 0.2f, "Glow");
                Visual(PrimitiveType.Capsule, "Tusk", root, new Vector3(e * 0.36f, 2.72f, 0.62f), new Vector3(0.1f, 0.24f, 0.1f), "Tail").transform.localRotation = Quaternion.Euler(-30f, 0f, e * 20f);
                Visual(PrimitiveType.Sphere, "Ear", root, new Vector3(e * 0.5f, 3.62f, -0.05f), new Vector3(0.32f, 0.42f, 0.12f), pink).transform.localRotation = Quaternion.Euler(0f, 0f, -e * 30f);
            }
            // A crude crown, like Big Doug's.
            Visual(PrimitiveType.Cylinder, "Crown", root, new Vector3(0f, 3.62f, 0f), new Vector3(0.6f, 0.08f, 0.6f), "Coal");
        }

        // =========================================================== Ranger Tower

        static void BuildRangerTower(Transform parent, Scene scene, Func<Vector3, float> groundY)
        {
            var t = LandmarkRoot(parent, "RangerTower", TerrainBuilder.RangerTower, groundY);
            const float deckY = 6f, half = 2.1f;

            // Legs and cross-bracing.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Solid(PrimitiveType.Cube, "Leg", t, new Vector3(x * 1.8f, deckY * 0.5f + 0.5f, z * 1.8f), new Vector3(0.3f, deckY + 1f, 0.3f), "Bark");
            for (int s = 0; s < 4; s++)
            {
                var brace = Visual(PrimitiveType.Cube, "Brace", t, Vector3.zero, new Vector3(0.12f, 0.12f, 4.6f), "Plank").transform;
                float yaw = s * 90f;
                brace.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-48f, 0f, 0f);
                brace.localPosition = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 2.6f, 1.82f);
            }

            // Deck, railings (gap where the stairs arrive) and a little roof.
            Solid(PrimitiveType.Cube, "Deck", t, new Vector3(0f, deckY, 0f), new Vector3(half * 2f, 0.2f, half * 2f), "Plank");
            Solid(PrimitiveType.Cube, "RailN", t, new Vector3(0f, deckY + 0.6f, -half), new Vector3(half * 2f, 0.9f, 0.08f), "Plank");
            Solid(PrimitiveType.Cube, "RailW", t, new Vector3(-half, deckY + 0.6f, 0f), new Vector3(0.08f, 0.9f, half * 2f), "Plank");
            Solid(PrimitiveType.Cube, "RailE", t, new Vector3(half, deckY + 0.6f, 0f), new Vector3(0.08f, 0.9f, half * 2f), "Plank");
            Solid(PrimitiveType.Cube, "RailS", t, new Vector3(0.7f, deckY + 0.6f, half), new Vector3(2.8f, 0.9f, 0.08f), "Plank");
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Visual(PrimitiveType.Cube, "RoofPost", t, new Vector3(x * 1.95f, deckY + 1.4f, z * 1.95f), new Vector3(0.14f, 2.6f, 0.14f), "Bark");
            for (int e = -1; e <= 1; e += 2)
                Visual(PrimitiveType.Cube, "Roof", t, new Vector3(e * 1.25f, deckY + 3.05f, 0f), new Vector3(2.9f, 0.14f, 5f), "Roof")
                    .transform.localRotation = Quaternion.Euler(0f, 0f, -e * 28f);

            // Switchback ramps: up the east side, then along the south side to the deck's south-west corner.
            void Ramp(Vector3 from, Vector3 to, float width)
            {
                var d = to - from;
                var go = Solid(PrimitiveType.Cube, "Ramp", t, (from + to) * 0.5f, new Vector3(width, 0.18f, d.magnitude + 0.2f), "Plank", Quaternion.LookRotation(d));
                for (int i = 1; i < 6; i++) // cleats so it reads as stairs
                    Visual(PrimitiveType.Cube, "Cleat", go.transform, new Vector3(0f, 0.6f, -0.5f + i / 6f), new Vector3(0.98f, 0.5f, 0.012f), "Bark");
            }
            float x0 = half + 0.9f;
            Ramp(new Vector3(x0, 0f, -4.6f), new Vector3(x0, deckY * 0.5f, 0.6f), 1.4f);
            Solid(PrimitiveType.Cube, "Landing", t, new Vector3(x0, deckY * 0.5f, 1.7f), new Vector3(1.6f, 0.2f, 2.4f), "Plank");
            Ramp(new Vector3(x0 - 0.2f, deckY * 0.5f, half + 0.9f), new Vector3(-0.9f, deckY, half + 0.9f), 1.4f);
            Solid(PrimitiveType.Cube, "TopLanding", t, new Vector3(-1.35f, deckY, half + 0.55f), new Vector3(1.5f, 0.2f, 1.3f), "Plank");
            for (int i = 0; i < 3; i++) // ramp supports
            {
                Visual(PrimitiveType.Cube, "Support", t, new Vector3(x0, deckY * 0.25f * (i * 0.5f + 0.5f) * 0.9f, -3.2f + i * 1.6f), new Vector3(0.14f, deckY * 0.5f * (i * 0.5f + 0.5f) * 0.9f, 0.14f), "Bark");
            }

            Lantern(t, new Vector3(1.8f, deckY + 2.2f, -1.8f), 12f);
            PlaceNetworked(_lootCratePrefab, scene, t.TransformPoint(new Vector3(-1f, deckY + 0.1f, -1f)), t.rotation);
            Sign(t, new Vector3(x0 + 1.6f, 0f, -3.4f), "RANGER TOWER", "climb up for a look around");
        }

        // =========================================================== Abandoned Camp

        static void BuildOldCamp(Transform parent, Scene scene, Func<Vector3, float> groundY)
        {
            var t = LandmarkRoot(parent, "AbandonedCamp", TerrainBuilder.OldCamp, groundY);
            var rng = new System.Random(5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // A tent that's seen better days: one side up, the other slumped.
            var tent = new GameObject("Tent").transform;
            tent.SetParent(t, false);
            tent.localPosition = new Vector3(-3f, 0f, 2.5f);
            tent.localRotation = Quaternion.Euler(0f, 25f, 0f);
            Solid(PrimitiveType.Cube, "CanvasUp", tent, new Vector3(-0.62f, 0.85f, 0f), new Vector3(0.06f, 2.1f, 2.8f), "Canvas", Quaternion.Euler(0f, 0f, -36f));
            Solid(PrimitiveType.Cube, "CanvasDown", tent, new Vector3(0.95f, 0.25f, 0.1f), new Vector3(0.06f, 2.0f, 2.6f), "Canvas", Quaternion.Euler(4f, 0f, 74f));
            Visual(PrimitiveType.Cylinder, "Pole", tent, new Vector3(0f, 0.85f, 1.35f), new Vector3(0.06f, 0.85f, 0.06f), "Bark");
            Visual(PrimitiveType.Cylinder, "Pole", tent, new Vector3(0.6f, 0.5f, -1.4f), new Vector3(0.06f, 0.85f, 0.06f), "Bark").transform.localRotation = Quaternion.Euler(0f, 0f, 50f);
            Visual(PrimitiveType.Capsule, "Bedroll", t, new Vector3(-0.8f, 0.14f, 4.6f), new Vector3(0.45f, 0.95f, 0.3f), "BedrollRed").transform.localRotation = Quaternion.Euler(90f, 70f, 0f);

            // Cold fire pit.
            var pit = new GameObject("FirePit").transform;
            pit.SetParent(t, false);
            pit.localPosition = new Vector3(1.2f, 0f, -0.5f);
            Visual(PrimitiveType.Cylinder, "Ash", pit, new Vector3(0f, 0.02f, 0f), new Vector3(1.1f, 0.02f, 1.1f), "Ash");
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                Visual(PrimitiveType.Sphere, "Stone", pit, new Vector3(Mathf.Cos(a) * 0.72f, 0.08f, Mathf.Sin(a) * 0.72f), new Vector3(R(0.24f, 0.34f), R(0.16f, 0.24f), R(0.24f, 0.32f)), "Stone");
            }
            for (int i = 0; i < 3; i++)
                Visual(PrimitiveType.Cylinder, "BurntLog", pit, new Vector3(R(-0.2f, 0.2f), 0.08f, R(-0.2f, 0.2f)), new Vector3(0.11f, 0.35f, 0.11f), "Ash").transform.localRotation = Quaternion.Euler(90f, i * 60f + R(-15f, 15f), 0f);
            Solid(PrimitiveType.Cylinder, "Stump", t, new Vector3(2.9f, 0.25f, 0.6f), new Vector3(0.6f, 0.25f, 0.6f), "Bark");
            Solid(PrimitiveType.Cylinder, "Stump", t, new Vector3(-0.4f, 0.25f, -2.3f), new Vector3(0.55f, 0.25f, 0.55f), "Bark");

            // Scattered gear.
            VillageArt.Piece("Prop_Crate", t, new Vector3(3.6f, 0f, 3.2f), 20f);
            VillageArt.Piece("Prop_Crate", t, new Vector3(4.4f, 0f, 2.1f), -35f, scale: 0.8f);
            VillageArt.Piece("Prop_Crate", t, new Vector3(3.8f, 1.05f, 3.0f), 60f, scale: 0.6f);
            Visual(PrimitiveType.Cylinder, "TippedBucket", t, new Vector3(2.2f, 0.18f, -2.4f), new Vector3(0.36f, 0.22f, 0.36f), "Gun").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Solid(PrimitiveType.Cube, "LanternPole", t, new Vector3(-4.5f, 1.1f, -1.5f), new Vector3(0.1f, 2.2f, 0.1f), "Bark", Quaternion.Euler(0f, 0f, 9f));
            Lantern(t, new Vector3(-4.3f, 2.1f, -1.5f), 8f);
            PlaceNetworked(_lootCratePrefab, scene, t.TransformPoint(new Vector3(4.6f, 0f, 0.4f)), t.rotation * Quaternion.Euler(0f, -70f, 0f));
            Sign(t, new Vector3(-1.6f, 0f, 7.6f), "ABANDONED CAMP", "whoever lived here left in a hurry");
        }

        // =========================================================== Loot crate

        static GameObject BuildLootCratePrefab()
        {
            var root = new GameObject("LootCrate");
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(1.0f, 0.75f, 0.68f);
            col.center = new Vector3(0f, 0.37f, 0f);
            root.AddComponent<NetworkObject>();
            var crate = root.AddComponent<LootCrate>();

            Visual(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.96f, 0.6f, 0.64f), "Plank");
            for (int e = -1; e <= 1; e += 2)
                Visual(PrimitiveType.Cube, "Band", root.transform, new Vector3(e * 0.3f, 0.3f, 0f), new Vector3(0.08f, 0.62f, 0.66f), "Gun");
            Visual(PrimitiveType.Cube, "Stripe", root.transform, new Vector3(0f, 0.3f, 0.325f), new Vector3(0.5f, 0.12f, 0.01f), "Glow");
            var lid = new GameObject("Lid").transform;
            lid.SetParent(root.transform, false);
            lid.localPosition = new Vector3(0f, 0.6f, -0.32f);
            Visual(PrimitiveType.Cube, "LidBoard", lid, new Vector3(0f, 0.05f, 0.32f), new Vector3(0.98f, 0.1f, 0.66f), "Plank");
            Visual(PrimitiveType.Cube, "LidBand", lid, new Vector3(0f, 0.05f, 0.32f), new Vector3(0.1f, 0.12f, 0.68f), "Gun");

            var so = new SerializedObject(crate);
            so.FindProperty("lid").objectReferenceValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root, $"{PrefabDir}/LootCrate.prefab");
        }
    }
}
