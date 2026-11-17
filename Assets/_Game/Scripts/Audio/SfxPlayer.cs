using UnityEngine;

namespace FriendSlop.Audio
{
    /// <summary>
    /// Fire-and-forget sound playback from a small pool of AudioSources, so one-shots don't need
    /// GameObjects of their own. 3D sounds fall off linearly so distance reads clearly in co-op.
    /// </summary>
    public static class SfxPlayer
    {
        const int PoolSize = 32;
        static AudioSource[] _pool;
        static int _next;

        /// <summary>Non-positional (UI, your own gun, your own footsteps).</summary>
        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            var src = Next();
            if (src == null || clip == null) return;
            src.transform.position = Vector3.zero;
            src.spatialBlend = 0f;
            Fire(src, clip, volume, pitch);
        }

        /// <summary>Positional, heard by whoever is close enough.</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f,
                                  float minDistance = 2f, float maxDistance = 40f)
        {
            var src = Next();
            if (src == null || clip == null) return;
            src.transform.position = position;
            src.spatialBlend = 1f;
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            Fire(src, clip, volume, pitch);
        }

        static void Fire(AudioSource src, AudioClip clip, float volume, float pitch)
        {
            src.Stop();
            src.clip = clip;
            src.volume = volume * FriendSlop.Settings.GameSettings.SfxVolume;
            src.pitch = pitch * Random.Range(0.96f, 1.04f); // tiny variation so repeats don't feel robotic
            src.Play();
        }

        static AudioSource Next()
        {
            if (!Application.isPlaying) return null;
            if (_pool == null || _pool[0] == null) CreatePool();
            var src = _pool[_next];
            _next = (_next + 1) % PoolSize;
            return src;
        }

        static void CreatePool()
        {
            var root = new GameObject("SfxPool");
            Object.DontDestroyOnLoad(root);
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"Sfx{i}");
                go.transform.SetParent(root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.dopplerLevel = 0f;
                _pool[i] = src;
            }
            _next = 0;
        }
    }
}
