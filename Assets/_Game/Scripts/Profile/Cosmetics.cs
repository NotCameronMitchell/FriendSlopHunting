using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Profile
{
    public enum CosmeticSlot { Outfit, Head, Hat, Accessory, Color }
    public enum UnlockKind { Free, Bucks, Achievement }

    public class CosmeticItem
    {
        public string Id;
        public string Name;
        public CosmeticSlot Slot;
        public UnlockKind Unlock;
        public int Cost;              // bucks
        public string AchievementId;  // when Unlock == Achievement
        public Color Color;           // for color items
        public string[] Parts;        // KayKit hunter mesh parts to switch on (null = primitive model)
    }

    public class Achievement
    {
        public string Id;
        public string Name;
        public string Description;
        public Func<PlayerProfile, bool> IsMet;
        public Func<PlayerProfile, string> Progress;
        public string RewardId;
    }

    /// <summary>
    /// Every unlockable look, the achievements that award some of them, and the primitive-built
    /// models (placeholders until the art pass). Shared by the menu preview and in-game players.
    /// </summary>
    public static class Cosmetics
    {
        // Must be declared before Items: static fields initialize in file order.
        public static readonly Color[] Palette =
        {
            new Color(1f, 0.5f, 0.1f), new Color(0.25f, 0.55f, 0.25f), new Color(0.4f, 0.7f, 1f), new Color(1f, 0.85f, 0.25f),
            new Color(0.55f, 0.3f, 0.75f), new Color(0.15f, 0.65f, 0.6f), new Color(0.7f, 0.22f, 0.18f), new Color(0.85f, 0.75f, 0.55f),
            new Color(1f, 0.5f, 0.75f), new Color(0.12f, 0.12f, 0.2f), new Color(0.4f, 0.45f, 0.2f), new Color(1f, 0.8f, 0.2f),
        };

        public const string DefaultOutfit = "outfit_rogue";
        public const string DefaultHead = "head_ranger";

        public static readonly List<CosmeticItem> Items = new List<CosmeticItem>
        {
            // Outfits (body + arms + legs from the KayKit Adventurers pack, tinted by your color)
            Outfit("outfit_rogue", "Woodsman", "Rogue", UnlockKind.Free),
            Outfit("outfit_ranger", "Ranger", "Ranger", UnlockKind.Free),
            Outfit("outfit_barbarian", "Shirtless Legend", "Barbarian", UnlockKind.Bucks, 250),
            Outfit("outfit_mage", "Robe Guy", "Mage", UnlockKind.Bucks, 350),
            Outfit("outfit_knight", "Tin Can", "Knight", UnlockKind.Achievement, ach: "legend"),

            // Heads (face + hair)
            Head("head_ranger", "Mustache", "Ranger_Head", UnlockKind.Free),
            Head("head_rogue", "Ponytail", "Rogue_Head", UnlockKind.Free),
            Head("head_knight", "Square Jaw", "Knight_Head", UnlockKind.Free),
            Head("head_mage", "Long Hair", "Mage_Head", UnlockKind.Free),
            Head("head_barbarian", "Big Beard", "Barbarian_Head", UnlockKind.Bucks, 150),
            Head("head_hooded", "Hooded", "RogueHooded_Head", UnlockKind.Bucks, 200),

            // Hats
            Hat("none", "Bare head", UnlockKind.Free),
            Hat("cap", "Hunting Cap", UnlockKind.Free),
            Hat("beanie", "Beanie", UnlockKind.Achievement, ach: "first_sale"),
            Hat("cowboy", "Cowboy Hat", UnlockKind.Achievement, ach: "headhunter"),
            Hat("party", "Party Hat", UnlockKind.Bucks, 120),
            Hat("pot", "Cooking Pot", UnlockKind.Bucks, 200),
            Hat("tophat", "Top Hat", UnlockKind.Bucks, 400),
            Hat("cone", "Traffic Cone", UnlockKind.Achievement, ach: "moose_hunter"),
            Hat("wolfhood", "Wolf Hood", UnlockKind.Bucks, 300),
            Hat("antlers", "Antler Helmet", UnlockKind.Bucks, 1500),
            Hat("crown", "Crown", UnlockKind.Bucks, 2500),
            Hat("helmet", "Knight Helmet", UnlockKind.Bucks, 700, parts: new[] { "Knight_Helmet", "Knight_HelmetVisor" }),
            Hat("wizard", "Wizard Hat", UnlockKind.Bucks, 350, parts: new[] { "Mage_Hat" }),
            Hat("bearhat", "Bear Hat", UnlockKind.Achievement, ach: "pack_leader", parts: new[] { "Barbarian_BearHat" }),

            // Accessories
            Acc("none", "Nothing", UnlockKind.Free),
            Acc("scarf", "Scarf", UnlockKind.Free),
            Acc("backpack", "Backpack", UnlockKind.Bucks, 150),
            Acc("shades", "Sunglasses", UnlockKind.Bucks, 180),
            Acc("monocle", "Monocle", UnlockKind.Achievement, ach: "sniper"),
            Acc("cape", "Royal Cape", UnlockKind.Bucks, 600, parts: new[] { "Knight_Cape" }),
            Acc("cloak", "Ranger Cloak", UnlockKind.Bucks, 300, parts: new[] { "Ranger_Cape" }),
            Acc("wizcape", "Wizard Cape", UnlockKind.Bucks, 400, parts: new[] { "Mage_Cape" }),
            Acc("quiver", "Quiver", UnlockKind.Bucks, 120, parts: new[] { "Ranger_Quiver" }),
            Acc("mask", "Bandit Mask", UnlockKind.Achievement, ach: "trickshot", parts: new[] { "RogueHooded_Mask" }),

            // Colors (index = position in Palette)
            Col(0, "Hunter Orange", UnlockKind.Free), Col(1, "Forest Green", UnlockKind.Free),
            Col(2, "Sky Blue", UnlockKind.Free), Col(3, "Sunny Yellow", UnlockKind.Free),
            Col(4, "Grape", UnlockKind.Free), Col(5, "Teal", UnlockKind.Free),
            Col(6, "Brick Red", UnlockKind.Free), Col(7, "Sand", UnlockKind.Free),
            Col(8, "Bubblegum", UnlockKind.Achievement, ach: "zoologist"),
            Col(9, "Midnight", UnlockKind.Bucks, 150),
            Col(10, "Camo Olive", UnlockKind.Bucks, 100),
            Col(11, "Solid Gold", UnlockKind.Bucks, 800),
        };



        public static readonly List<Achievement> Achievements = new List<Achievement>
        {
            new Achievement { Id = "first_blood", Name = "First Blood", Description = "Bag your first animal.",
                IsMet = p => p.kills >= 1, Progress = p => $"{Mathf.Min(p.kills, 1)}/1" },
            new Achievement { Id = "first_sale", Name = "Open for Business", Description = "Your team sells its first carcass.",
                IsMet = p => p.moneyEarnedWithTeam > 0, Progress = p => p.moneyEarnedWithTeam > 0 ? "done" : "0/1", RewardId = "beanie" },
            new Achievement { Id = "headhunter", Name = "Headhunter", Description = "Get 10 headshot kills.",
                IsMet = p => p.headshots >= 10, Progress = p => $"{Mathf.Min(p.headshots, 10)}/10", RewardId = "cowboy" },
            new Achievement { Id = "trickshot", Name = "Trick Shot", Description = "Kill something while you're in the air.",
                IsMet = p => p.jumpShotKills >= 1, Progress = p => $"{Mathf.Min(p.jumpShotKills, 1)}/1", RewardId = "mask" },
            new Achievement { Id = "sniper", Name = "Sniper", Description = "Get 5 long-shot kills (35 m+).",
                IsMet = p => p.longShotKills >= 5, Progress = p => $"{Mathf.Min(p.longShotKills, 5)}/5", RewardId = "monocle" },
            new Achievement { Id = "moose_hunter", Name = "Moose Hunter", Description = "Bring down a moose.",
                IsMet = p => p.KillsOf("Moose") >= 1, Progress = p => $"{Mathf.Min(p.KillsOf("Moose"), 1)}/1", RewardId = "cone" },
            new Achievement { Id = "pack_leader", Name = "Pack Leader", Description = "Kill 10 wolves.",
                IsMet = p => p.KillsOf("Wolf") >= 10, Progress = p => $"{Mathf.Min(p.KillsOf("Wolf"), 10)}/10", RewardId = "bearhat" },
            new Achievement { Id = "legend", Name = "Legend", Description = "Land the killing blow on a legendary boss.",
                IsMet = p => p.bossKills >= 1, Progress = p => $"{Mathf.Min(p.bossKills, 1)}/1", RewardId = "outfit_knight" },
            new Achievement { Id = "zoologist", Name = "Zoologist", Description = "Kill one of every regular species (8).",
                IsMet = p => CountRegularSpecies(p) >= 8, Progress = p => $"{Mathf.Min(CountRegularSpecies(p), 8)}/8", RewardId = "color_8" },
        };

        static int CountRegularSpecies(PlayerProfile p)
        {
            int n = 0;
            foreach (var s in p.speciesKilled) if (s != "Elk") n++;
            return n;
        }

        static CosmeticItem Hat(string id, string name, UnlockKind kind, int cost = 0, string ach = null, string[] parts = null) =>
            new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Hat, Unlock = kind, Cost = cost, AchievementId = ach, Parts = parts };

        static CosmeticItem Outfit(string id, string name, string set, UnlockKind kind, int cost = 0, string ach = null) =>
            new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Outfit, Unlock = kind, Cost = cost, AchievementId = ach,
                Parts = new[] { $"{set}_Body", $"{set}_ArmLeft", $"{set}_ArmRight", $"{set}_LegLeft", $"{set}_LegRight" } };

        static CosmeticItem Head(string id, string name, string part, UnlockKind kind, int cost = 0, string ach = null) =>
            new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Head, Unlock = kind, Cost = cost, AchievementId = ach, Parts = new[] { part } };

        static CosmeticItem Acc(string id, string name, UnlockKind kind, int cost = 0, string ach = null, string[] parts = null) =>
            new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Accessory, Unlock = kind, Cost = cost, AchievementId = ach, Parts = parts };

        static CosmeticItem Col(int index, string name, UnlockKind kind, int cost = 0, string ach = null) =>
            new CosmeticItem { Id = $"color_{index}", Name = name, Slot = CosmeticSlot.Color, Unlock = kind, Cost = cost, AchievementId = ach, Color = Palette[index] };

        public static CosmeticItem Find(string id) => Items.Find(i => i.Id == id);
        public static Achievement FindAchievement(string id) => Achievements.Find(a => a.Id == id);

        public static bool IsUnlocked(CosmeticItem item, PlayerProfile p) =>
            item.Unlock == UnlockKind.Free || p.Owns(item.Id);

        public static string HowToUnlock(CosmeticItem item)
        {
            switch (item.Unlock)
            {
                case UnlockKind.Bucks: return $"{item.Cost:N0} bucks";
                case UnlockKind.Achievement:
                    var a = FindAchievement(item.AchievementId);
                    return a != null ? $"Achievement: {a.Name}" : "Achievement";
                default: return "Free";
            }
        }

        /// <summary>Body color for a player: their pick, or a stable color from their id.</summary>
        public static Color BodyColor(int colorIndex, ulong clientId)
        {
            if (colorIndex >= 0 && colorIndex < Palette.Length) return Palette[colorIndex];
            float hue = (clientId * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.65f, 0.95f);
        }

        // ================================================================== models

        static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>();

        /// <summary>Builds a primitive hat on a head anchor (origin = top of the head, sized for a 0.7 m head). Returns its root.</summary>
        public static Transform BuildHat(string id, Transform hatAnchor)
        {
            if (hatAnchor == null) return null;
            var root = new GameObject($"Hat_{id}").transform;
            root.SetParent(hatAnchor, false);
            var orange = new Color(1f, 0.5f, 0.1f);
            switch (id)
            {
                case "cap":
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.04f, 0f), new Vector3(0.62f, 0.3f, 0.62f), orange);
                    P(PrimitiveType.Cube, root, new Vector3(0f, 0.0f, 0.36f), new Vector3(0.36f, 0.03f, 0.26f), orange);
                    break;
                case "beanie":
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.05f, 0f), new Vector3(0.66f, 0.42f, 0.66f), new Color(0.2f, 0.35f, 0.7f));
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.27f, 0f), Vector3.one * 0.14f, Color.white);
                    break;
                case "cowboy":
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.02f, 0f), new Vector3(1.0f, 0.015f, 1.0f), new Color(0.45f, 0.3f, 0.15f));
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.14f, 0f), new Vector3(0.5f, 0.12f, 0.5f), new Color(0.45f, 0.3f, 0.15f));
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.06f, 0f), new Vector3(0.52f, 0.025f, 0.52f), new Color(0.15f, 0.1f, 0.05f));
                    break;
                case "party":
                    for (int i = 0; i < 5; i++)
                    {
                        float s = 0.42f - i * 0.08f;
                        P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.06f + i * 0.09f, 0f), new Vector3(s, 0.045f, s), i % 2 == 0 ? new Color(1f, 0.3f, 0.6f) : new Color(0.3f, 0.8f, 1f));
                    }
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.5f, 0f), Vector3.one * 0.1f, new Color(1f, 0.9f, 0.2f));
                    break;
                case "pot":
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.12f, 0f), new Vector3(0.62f, 0.13f, 0.62f), new Color(0.35f, 0.36f, 0.38f));
                    P(PrimitiveType.Cube, root, new Vector3(0.42f, 0.18f, 0f), new Vector3(0.3f, 0.04f, 0.06f), new Color(0.15f, 0.12f, 0.1f));
                    break;
                case "tophat":
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.02f, 0f), new Vector3(0.75f, 0.015f, 0.75f), new Color(0.08f, 0.08f, 0.09f));
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.25f, 0f), new Vector3(0.48f, 0.24f, 0.48f), new Color(0.08f, 0.08f, 0.09f));
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.08f, 0f), new Vector3(0.5f, 0.035f, 0.5f), new Color(0.6f, 0.1f, 0.1f));
                    break;
                case "cone":
                    for (int i = 0; i < 6; i++)
                    {
                        float s = 0.5f - i * 0.07f;
                        P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.05f + i * 0.1f, 0f), new Vector3(s, 0.05f, s), i == 2 ? Color.white : orange);
                    }
                    P(PrimitiveType.Cube, root, new Vector3(0f, 0.0f, 0f), new Vector3(0.62f, 0.03f, 0.62f), orange);
                    break;
                case "wolfhood":
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.02f, -0.04f), new Vector3(0.74f, 0.5f, 0.78f), new Color(0.45f, 0.45f, 0.48f));
                    for (int e = -1; e <= 1; e += 2)
                        P(PrimitiveType.Cube, root, new Vector3(e * 0.2f, 0.28f, -0.02f), new Vector3(0.1f, 0.2f, 0.06f), new Color(0.4f, 0.4f, 0.43f), Quaternion.Euler(0f, 0f, -e * 12f));
                    P(PrimitiveType.Cube, root, new Vector3(0f, 0.06f, 0.36f), new Vector3(0.22f, 0.12f, 0.2f), new Color(0.45f, 0.45f, 0.48f));
                    break;
                case "antlers":
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.02f, 0f), new Vector3(0.62f, 0.05f, 0.62f), new Color(0.4f, 0.25f, 0.12f));
                    for (int e = -1; e <= 1; e += 2)
                    {
                        var c = new Color(0.92f, 0.86f, 0.7f);
                        P(PrimitiveType.Cube, root, new Vector3(e * 0.32f, 0.3f, 0f), new Vector3(0.06f, 0.55f, 0.06f), c, Quaternion.Euler(0f, 0f, -e * 35f));
                        P(PrimitiveType.Cube, root, new Vector3(e * 0.5f, 0.52f, 0.06f), new Vector3(0.05f, 0.28f, 0.05f), c, Quaternion.Euler(-20f, 0f, -e * 70f));
                        P(PrimitiveType.Cube, root, new Vector3(e * 0.38f, 0.58f, -0.04f), new Vector3(0.05f, 0.25f, 0.05f), c, Quaternion.Euler(15f, 0f, -e * 10f));
                    }
                    break;
                case "crown":
                    var gold = new Color(1f, 0.82f, 0.2f);
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.08f, 0f), new Vector3(0.56f, 0.08f, 0.56f), gold);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        P(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * 0.24f, 0.22f, Mathf.Sin(a) * 0.24f), new Vector3(0.07f, 0.14f, 0.07f), gold, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
                    }
                    P(PrimitiveType.Sphere, root, new Vector3(0f, 0.1f, 0.28f), Vector3.one * 0.08f, new Color(0.8f, 0.1f, 0.2f));
                    break;
                default:
                    UnityEngine.Object.Destroy(root.gameObject);
                    return null;
            }
            return root;
        }

        /// <summary>
        /// Builds a primitive accessory. Face items sit on <paramref name="faceAnchor"/> (front of the face at eye
        /// height); the rest on <paramref name="neckAnchor"/> (base of the neck). Both are sized for a 0.7 m body.
        /// </summary>
        public static Transform BuildAccessory(string id, Transform faceAnchor, Transform neckAnchor)
        {
            bool face = id == "shades" || id == "monocle";
            var parent = face ? faceAnchor : neckAnchor;
            if (parent == null) return null;
            var root = new GameObject($"Acc_{id}").transform;
            root.SetParent(parent, false);
            switch (id)
            {
                case "scarf":
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0f), new Vector3(0.74f, 0.06f, 0.74f), new Color(0.8f, 0.15f, 0.15f));
                    P(PrimitiveType.Cube, root, new Vector3(0.14f, -0.2f, 0.33f), new Vector3(0.12f, 0.36f, 0.04f), new Color(0.8f, 0.15f, 0.15f), Quaternion.Euler(0f, 0f, 8f));
                    break;
                case "backpack":
                    P(PrimitiveType.Cube, root, new Vector3(0f, -0.3f, -0.42f), new Vector3(0.5f, 0.6f, 0.25f), new Color(0.35f, 0.3f, 0.2f));
                    P(PrimitiveType.Cube, root, new Vector3(0f, -0.5f, -0.56f), new Vector3(0.4f, 0.22f, 0.08f), new Color(0.28f, 0.24f, 0.16f));
                    P(PrimitiveType.Cylinder, root, new Vector3(0f, 0.05f, -0.42f), new Vector3(0.42f, 0.06f, 0.14f), new Color(0.2f, 0.4f, 0.25f), Quaternion.Euler(0f, 0f, 90f));
                    break;
                case "shades":
                    for (int e = -1; e <= 1; e += 2)
                        P(PrimitiveType.Cube, root, new Vector3(e * 0.12f, 0f, 0.01f), new Vector3(0.17f, 0.09f, 0.03f), new Color(0.05f, 0.05f, 0.06f));
                    P(PrimitiveType.Cube, root, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.4f, 0.02f, 0.02f), new Color(0.05f, 0.05f, 0.06f));
                    break;
                case "monocle":
                    P(PrimitiveType.Cylinder, root, new Vector3(0.12f, 0f, 0.02f), new Vector3(0.13f, 0.01f, 0.13f), new Color(1f, 0.82f, 0.2f), Quaternion.Euler(90f, 0f, 0f));
                    P(PrimitiveType.Cube, root, new Vector3(0.17f, -0.16f, 0.01f), new Vector3(0.01f, 0.28f, 0.01f), new Color(1f, 0.82f, 0.2f));
                    break;
                default:
                    UnityEngine.Object.Destroy(root.gameObject);
                    return null;
            }
            return root;
        }

        static void P(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (!Mats.TryGetValue(color, out var m) || m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader) { color = color };
                m.SetColor("_BaseColor", color);
                Mats[color] = m;
            }
            r.sharedMaterial = m;
        }
    }
}
