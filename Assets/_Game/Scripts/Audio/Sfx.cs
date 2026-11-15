using System;
using UnityEngine;

namespace FriendSlop.Audio
{
    /// <summary>
    /// Every sound effect in the game, synthesized in code on first use. No audio files, no
    /// licenses, and every sound is tweakable here. Good enough for friendslop. Swap in recorded
    /// clips later by replacing what a property returns.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;

        static AudioClip _gunshot, _bolt, _hit, _headshot, _squeak, _boing, _chaChing, _thud, _whoosh;
        static AudioClip[] _footsteps;

        public static AudioClip Gunshot => _gunshot ??= MakeGunshot();
        public static AudioClip BoltCycle => _bolt ??= MakeBolt();
        public static AudioClip HitTick => _hit ??= MakeHitTick();
        public static AudioClip HeadshotDing => _headshot ??= MakeHeadshotDing();
        public static AudioClip Squeak => _squeak ??= MakeSqueak();
        public static AudioClip Boing => _boing ??= MakeBoing();
        public static AudioClip ChaChing => _chaChing ??= MakeChaChing();
        public static AudioClip Thud => _thud ??= MakeThud();
        public static AudioClip Whoosh => _whoosh ??= MakeWhoosh();

        public static AudioClip RandomFootstep()
        {
            _footsteps ??= new[] { MakeFootstep(11), MakeFootstep(22), MakeFootstep(33), MakeFootstep(44) };
            return _footsteps[UnityEngine.Random.Range(0, _footsteps.Length)];
        }

        // --------------------------------------------------------------------- recipes

        static AudioClip MakeGunshot()
        {
            var rng = new System.Random(1);
            return Build("Gunshot", 0.7f, (t, i, s) =>
            {
                double noise = rng.NextDouble() * 2 - 1;
                s.Low += (noise - s.Low) * 0.18;                         // boomy body
                double crack = t < 0.006 ? noise * 1.0 : 0;              // sharp transient
                double body = s.Low * Math.Exp(-t * 9) * 1.8;
                double thump = Math.Sin(2 * Math.PI * (55 + 40 * Math.Exp(-t * 30)) * t) * Math.Exp(-t * 18);
                double tail = noise * Math.Exp(-t * 5) * 0.08;           // forest echo-ish
                return crack + body + thump * 0.9 + tail;
            });
        }

        static AudioClip MakeBolt()
        {
            var rng = new System.Random(2);
            return Build("Bolt", 0.3f, (t, i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                double hp = n - s.Prev; s.Prev = n;                      // crude high-pass = metallic
                double c1 = Math.Exp(-t * 220);
                double c2 = t > 0.14 ? Math.Exp(-(t - 0.14) * 260) : 0;
                double ring = Math.Sin(2 * Math.PI * 3100 * t) * (c1 + c2) * 0.25;
                return hp * (c1 * 0.6 + c2 * 0.8) + ring;
            });
        }

        static AudioClip MakeHitTick() =>
            Build("HitTick", 0.07f, (t, i, s) => Math.Sin(2 * Math.PI * 2300 * t) * Math.Exp(-t * 70));

        static AudioClip MakeHeadshotDing() =>
            Build("HeadshotDing", 0.35f, (t, i, s) =>
            {
                double a = Math.Sin(2 * Math.PI * 1760 * t) * Math.Exp(-t * 14);
                double b = t > 0.06 ? Math.Sin(2 * Math.PI * 2640 * (t - 0.06)) * Math.Exp(-(t - 0.06) * 12) : 0;
                return (a + b) * 0.6;
            });

        static AudioClip MakeSqueak() =>
            Build("Squeak", 0.28f, (t, i, s) =>
            {
                // Chirp up then down, with a little vibrato: cartoon critter.
                double f = 1500 + 1300 * Math.Sin(Math.PI * Math.Min(1, t / 0.2)) + 90 * Math.Sin(2 * Math.PI * 28 * t);
                s.Phase += 2 * Math.PI * f / Rate;
                double env = Math.Min(1, t / 0.01) * Math.Exp(-t * 7);
                return (Math.Sin(s.Phase) + 0.3 * Math.Sin(2 * s.Phase)) * env * 0.7;
            });

        static AudioClip MakeBoing() =>
            Build("Boing", 0.45f, (t, i, s) =>
            {
                double f = 220 + 520 * (1 - Math.Exp(-t * 9)) + 35 * Math.Sin(2 * Math.PI * 16 * t);
                s.Phase += 2 * Math.PI * f / Rate;
                return Math.Sin(s.Phase) * Math.Exp(-t * 5.5) * 0.8;
            });

        static AudioClip MakeChaChing()
        {
            var rng = new System.Random(3);
            return Build("ChaChing", 0.9f, (t, i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                double hp = n - s.Prev; s.Prev = n;
                double cha = t < 0.06 ? hp * Math.Exp(-t * 60) * 0.5 : 0;
                double bell1 = t > 0.07 ? Bell(t - 0.07, 1318.5, 1975.5, 2637) : 0;
                double bell2 = t > 0.2 ? Bell(t - 0.2, 1568, 2349, 3136) * 1.1 : 0;
                return cha + bell1 * 0.45 + bell2 * 0.45;
            });
        }

        static double Bell(double t, double f1, double f2, double f3) =>
            (Math.Sin(2 * Math.PI * f1 * t) + 0.6 * Math.Sin(2 * Math.PI * f2 * t) + 0.35 * Math.Sin(2 * Math.PI * f3 * t))
            * Math.Exp(-t * 5);

        static AudioClip MakeThud()
        {
            var rng = new System.Random(4);
            return Build("Thud", 0.22f, (t, i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                s.Low += (n - s.Low) * 0.08;
                double tone = Math.Sin(2 * Math.PI * (60 + 50 * Math.Exp(-t * 30)) * t) * Math.Exp(-t * 22);
                return tone + s.Low * Math.Exp(-t * 40) * 2;
            });
        }

        static AudioClip MakeWhoosh()
        {
            var rng = new System.Random(5);
            return Build("Whoosh", 0.4f, (t, i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                double k = 0.05 + 0.25 * Math.Sin(Math.PI * t / 0.4);    // sweeping filter
                s.Low += (n - s.Low) * k;
                return s.Low * Math.Sin(Math.PI * t / 0.4) * 1.6;
            });
        }

        static AudioClip MakeFootstep(int seed)
        {
            var rng = new System.Random(seed);
            double tone = 95 + seed % 30;
            return Build($"Footstep{seed}", 0.14f, (t, i, s) =>
            {
                double n = rng.NextDouble() * 2 - 1;
                s.Low += (n - s.Low) * 0.25;                             // grass crunch
                return s.Low * Math.Exp(-t * 45) * 1.4 + Math.Sin(2 * Math.PI * tone * t) * Math.Exp(-t * 50) * 0.4;
            });
        }

        // --------------------------------------------------------------------- engine

        class State
        {
            public double Low, Prev, Phase;
        }

        static AudioClip Build(string name, float seconds, Func<double, int, State, double> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var state = new State();
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float v = (float)sample(i / (double)Rate, i, state);
                data[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }

            // Normalize, then 5 ms fade-out so nothing clicks at the end.
            float gain = 0.9f / peak;
            int fade = Mathf.Min(n, Rate / 200);
            for (int i = 0; i < n; i++)
            {
                float f = i >= n - fade ? (n - i) / (float)fade : 1f;
                data[i] *= gain * f;
            }

            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
