using System;
using System.Collections.Generic;

namespace FriendSlop.Animals
{
    /// <summary>Trick-shot flags recorded on the killing shot. They multiply the sale price.</summary>
    [Flags]
    public enum ShotStyle
    {
        None = 0,
        Headshot = 1 << 0,
        LongShot = 1 << 1,
        JumpShot = 1 << 2,
    }

    /// <summary>
    /// Pricing rules for style. Kept in one place so the HUD can explain exactly why a sale paid
    /// what it did (How to Fish's trick shots were unclear; ours shouldn't be).
    /// </summary>
    public static class StylePricing
    {
        public const float HeadshotMult = 1.5f;
        public const float LongShotMult = 1.25f;
        public const float JumpShotMult = 1.25f;
        public const float PerJuggle = 0.5f;   // each mid-air shot on a carcass adds +0.5×
        public const int MaxJuggles = 6;

        public static float Multiplier(ShotStyle style, int juggles)
        {
            float m = 1f;
            if ((style & ShotStyle.Headshot) != 0) m *= HeadshotMult;
            if ((style & ShotStyle.LongShot) != 0) m *= LongShotMult;
            if ((style & ShotStyle.JumpShot) != 0) m *= JumpShotMult;
            m *= 1f + PerJuggle * Math.Clamp(juggles, 0, MaxJuggles);
            return m;
        }

        /// <summary>e.g. "Headshot ×1.5 · Juggle ×2 (+1.0)". Empty when there's no bonus.</summary>
        public static string Describe(ShotStyle style, int juggles)
        {
            var parts = new List<string>(4);
            if ((style & ShotStyle.Headshot) != 0) parts.Add($"Headshot ×{HeadshotMult:0.##}");
            if ((style & ShotStyle.LongShot) != 0) parts.Add($"Long shot ×{LongShotMult:0.##}");
            if ((style & ShotStyle.JumpShot) != 0) parts.Add($"Jump shot ×{JumpShotMult:0.##}");
            if (juggles > 0) parts.Add($"Juggle ×{juggles} (+{PerJuggle * Math.Clamp(juggles, 0, MaxJuggles):0.0})");
            return string.Join(" · ", parts);
        }
    }
}
