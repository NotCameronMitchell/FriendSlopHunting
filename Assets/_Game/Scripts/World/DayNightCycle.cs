using System;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// The shared clock. Time of day is derived from the network's server time and one synced
    /// epoch, so every peer agrees without streaming updates. 0 = midnight, 0.25 = sunrise,
    /// 0.5 = noon, 0.75 = sunset. The host fires dusk/dawn events (night mobs, ghost geese…)
    /// and can skip to morning when everyone is asleep.
    /// </summary>
    public class DayNightCycle : NetworkBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [Tooltip("Seconds for a full day (day + night).")]
        [SerializeField] float dayLength = 720f;
        [Tooltip("Time of day when a hunt starts (0.3 = early morning).")]
        [SerializeField] float startTime = 0.3f;

        public const float Dusk = 0.77f, Dawn = 0.23f;

        /// <summary>Server time at which "day 1, midnight" happened.</summary>
        public readonly NetworkVariable<double> Epoch = new NetworkVariable<double>(0);

        /// <summary>Host only: night started / ended. Arg = day number.</summary>
        public static event Action<int> ServerDusk, ServerDawn;

        bool _wasNight;
        float _allAsleepSince = -1f;

        public float DayLength => dayLength;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        double Days => IsSpawned ? (Now - Epoch.Value) / dayLength : startTime;

        /// <summary>0..1 through the current day.</summary>
        public float TimeOfDay => (float)(Days - Math.Floor(Days));
        public int Day => (int)Math.Floor(Days) + 1;
        public bool IsNight => TimeOfDay > Dusk || TimeOfDay < Dawn;

        /// <summary>Night right now (false outside a hunt).</summary>
        public static bool NightNow => Instance != null && Instance.IsSpawned && Instance.IsNight;

        /// <summary>"7:30 PM" style clock.</summary>
        public string ClockText
        {
            get
            {
                float hours = TimeOfDay * 24f;
                int h = Mathf.FloorToInt(hours), m = Mathf.FloorToInt((hours - h) * 60f / 10f) * 10;
                int h12 = h % 12 == 0 ? 12 : h % 12;
                return $"{h12}:{m:00} {(h < 12 ? "AM" : "PM")}";
            }
        }

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) Epoch.Value = Now - startTime * dayLength;
            _wasNight = IsNight;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            CheckSleepers();
            bool night = IsNight;
            if (night == _wasNight) return;
            _wasNight = night;
            if (night) ServerDusk?.Invoke(Day);
            else ServerDawn?.Invoke(Day);
        }

        /// <summary>Everyone in bed at night for a few seconds: skip to morning and wake them up.</summary>
        void CheckSleepers()
        {
            int total = 0, asleep = 0;
            foreach (var p in FriendSlop.Player.NetworkPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                total++;
                if (p.Sleeping.Value) asleep++;
            }
            bool all = total > 0 && asleep == total && IsNight;
            if (!all) { _allAsleepSince = -1f; return; }
            if (_allAsleepSince < 0f) { _allAsleepSince = Time.time; return; }
            if (Time.time - _allAsleepSince < 3f) return;
            _allAsleepSince = -1f;
            ServerSkipToMorning();
            foreach (var p in FriendSlop.Player.NetworkPlayer.All) if (p != null && p.IsSpawned) p.ServerWake();
        }

        /// <summary>Host: jump to just after sunrise of the next morning.</summary>
        public void ServerSkipToMorning()
        {
            if (!IsServer) return;
            double days = Days;
            double target = Math.Floor(days) + Dawn + 0.02;
            if (target <= days) target += 1.0;
            Epoch.Value = Now - target * dayLength;
        }

        /// <summary>Host (dev): jump to a time of day, today or tomorrow.</summary>
        public void ServerSetTime(float timeOfDay)
        {
            if (!IsServer) return;
            double days = Days;
            double target = Math.Floor(days) + timeOfDay;
            if (target < days) target += 1.0;
            Epoch.Value = Now - target * dayLength;
        }
    }
}
