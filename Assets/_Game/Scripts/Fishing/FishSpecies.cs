using UnityEngine;

namespace FriendSlop.Fishing
{
    public enum FishWater { Any, Lake, Stream, FishermansLake }

    /// <summary>
    /// What you can pull out of the water. Kept as code data (like the weapon recipes) so balancing is one file.
    /// Research notes (GMTK-style fishing breakdowns): the minigame is a small step between "go to a spot" and
    /// "roll the catch"; depth comes from where/when you fish changing the pool, rare fish to brag about,
    /// size measurements, and a junk catch for comedy. Fishing pays a bit less than hunting on purpose.
    /// </summary>
    public sealed class FishSpecies
    {
        public string Name;
        public Color Body, Belly;
        public int Price;            // sale price at the average weight
        public float MinKg, MaxKg;
        public float Weight;         // how common, relative
        public FishWater Water;
        public float NightBonus;     // multiplier at night
        public float TwilightBonus;  // multiplier at dawn/dusk
        public float Fight;          // 0..1: how hard it pulls in the reel minigame
        public float Length;         // visual body length (m) at average weight
        public bool Junk;

        public float AverageKg => (MinKg + MaxKg) * 0.5f;

        public static readonly FishSpecies[] All =
        {
            new FishSpecies { Name = "Minnow", Body = C(0.62f, 0.66f, 0.6f), Belly = C(0.9f, 0.9f, 0.85f), Price = 5, MinKg = 0.05f, MaxKg = 0.2f, Weight = 30f, Water = FishWater.Any, Fight = 0.1f, Length = 0.14f },
            new FishSpecies { Name = "Perch", Body = C(0.55f, 0.62f, 0.25f), Belly = C(0.95f, 0.75f, 0.35f), Price = 12, MinKg = 0.2f, MaxKg = 0.9f, Weight = 24f, Water = FishWater.Any, Fight = 0.3f, Length = 0.25f },
            new FishSpecies { Name = "Trout", Body = C(0.45f, 0.55f, 0.45f), Belly = C(0.95f, 0.6f, 0.62f), Price = 24, MinKg = 0.5f, MaxKg = 3f, Weight = 16f, Water = FishWater.Stream, TwilightBonus = 1.6f, Fight = 0.5f, Length = 0.42f },
            new FishSpecies { Name = "Bass", Body = C(0.3f, 0.45f, 0.25f), Belly = C(0.85f, 0.88f, 0.7f), Price = 30, MinKg = 1f, MaxKg = 4f, Weight = 14f, Water = FishWater.Lake, Fight = 0.55f, Length = 0.45f },
            new FishSpecies { Name = "Catfish", Body = C(0.32f, 0.3f, 0.28f), Belly = C(0.75f, 0.72f, 0.65f), Price = 50, MinKg = 3f, MaxKg = 12f, Weight = 6f, Water = FishWater.Lake, NightBonus = 3f, Fight = 0.75f, Length = 0.75f },
            new FishSpecies { Name = "Pike", Body = C(0.35f, 0.5f, 0.3f), Belly = C(0.9f, 0.9f, 0.7f), Price = 45, MinKg = 2f, MaxKg = 8f, Weight = 5f, Water = FishWater.FishermansLake, Fight = 0.8f, Length = 0.8f },
            new FishSpecies { Name = "Golden Carp", Body = C(1f, 0.72f, 0.15f), Belly = C(1f, 0.92f, 0.5f), Price = 160, MinKg = 2f, MaxKg = 6f, Weight = 0.8f, Water = FishWater.FishermansLake, TwilightBonus = 3f, Fight = 0.9f, Length = 0.55f },
            new FishSpecies { Name = "Old Boot", Body = C(0.35f, 0.24f, 0.15f), Belly = C(0.25f, 0.18f, 0.12f), Price = 1, MinKg = 0.6f, MaxKg = 1.2f, Weight = 4f, Water = FishWater.Any, Fight = 0.05f, Length = 0.3f, Junk = true },
        };

        static Color C(float r, float g, float b) => new Color(r, g, b);

        /// <summary>Host: roll a catch for this spot and time of day. Returns (species index, kg).</summary>
        public static (int kind, float kg) Roll(FishWater water, bool night, bool twilight, System.Random rng = null)
        {
            float total = 0f;
            var w = new float[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                var s = All[i];
                bool ok = s.Water == FishWater.Any || s.Water == water || (s.Water == FishWater.Lake && water == FishWater.FishermansLake);
                if (!ok) continue;
                float x = s.Weight;
                if (night && s.NightBonus > 0f) x *= s.NightBonus;
                if (twilight && s.TwilightBonus > 0f) x *= s.TwilightBonus;
                if (water == FishWater.FishermansLake && !s.Junk && s.Price >= 30) x *= 1.4f; // the good lake is worth the walk
                w[i] = x; total += x;
            }
            float r = (rng != null ? (float)rng.NextDouble() : Random.value) * total;
            int kind = 0;
            for (int i = 0; i < All.Length; i++) { r -= w[i]; if (w[i] > 0f && r <= 0f) { kind = i; break; } }
            var sp = All[kind];
            float t = Mathf.Pow(Random.value, 1.8f); // most fish are small; the occasional whopper
            return (kind, Mathf.Round(Mathf.Lerp(sp.MinKg, sp.MaxKg, t) * 100f) / 100f);
        }

        public static FishSpecies Get(int kind) => All[Mathf.Clamp(kind, 0, All.Length - 1)];

        public static int ValueOf(int kind, float kg)
        {
            var s = Get(kind);
            return Mathf.Max(1, Mathf.RoundToInt(s.Price * Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(s.MinKg, s.MaxKg, kg))));
        }
    }
}
