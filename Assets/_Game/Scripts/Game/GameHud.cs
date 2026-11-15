using System.Collections.Generic;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Placeholder IMGUI HUD: team money (top right), announcement feed (top center),
    /// and the local player's interaction hint (bottom center). Replaced by real UI later.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        const float ToastSeconds = 5f;
        const int MaxToasts = 6;

        struct Toast
        {
            public string Text;
            public float Until;
        }

        readonly List<Toast> _toasts = new List<Toast>();
        GUIStyle _money, _toast, _hint, _marker, _gain;

        // Hit marker (set by the local rifle)
        static float _hitMarkerUntil;
        static bool _hitMarkerHead;

        // Money punch
        int _lastMoney = -1;
        float _punchStart = -10f;
        int _lastGain;
        float _spendStart = -10f;
        int _lastSpend;
        GUIStyle _weapon;

        const float HitMarkerSeconds = 0.18f;
        const float PunchSeconds = 0.6f;

        /// <summary>Flash an X on the crosshair. Red + bigger for headshots.</summary>
        public static void ShowHitMarker(bool headshot)
        {
            _hitMarkerUntil = Time.unscaledTime + HitMarkerSeconds;
            _hitMarkerHead = headshot;
        }

        void Update()
        {
            var state = GameState.Instance;
            if (state == null || !state.IsSpawned)
            {
                _lastMoney = -1;
                return;
            }

            int money = state.Money.Value;
            if (_lastMoney >= 0 && money > _lastMoney)
            {
                _lastGain = money - _lastMoney;
                FriendSlop.Profile.PlayerProfile.Current.RecordTeamEarnings(_lastGain); // bank career bucks
                _punchStart = Time.unscaledTime;
                FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.ChaChing, 0.7f);
            }
            else if (_lastMoney >= 0 && money < _lastMoney)
            {
                _lastSpend = _lastMoney - money;
                _spendStart = Time.unscaledTime;
                FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.ChaChing, 0.5f, 0.7f);
            }
            _lastMoney = money;
        }

        void OnEnable()
        {
            GameState.Announced += OnAnnounced;
            FriendSlop.Profile.PlayerProfile.AchievementUnlocked += OnAchievement;
            FriendSlop.Profile.PlayerProfile.LevelUp += OnLevelUp;
        }

        void OnLevelUp(int level)
        {
            OnAnnounced($"LEVEL UP! Hunter level {level}. Check the gun shop for new upgrades.");
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.HeadshotDing, 0.9f, 1.2f);
        }

        void OnAchievement(string id, string name)
        {
            var a = FriendSlop.Profile.Cosmetics.FindAchievement(id);
            var reward = a != null && !string.IsNullOrEmpty(a.RewardId) ? FriendSlop.Profile.Cosmetics.Find(a.RewardId) : null;
            OnAnnounced(reward != null ? $"ACHIEVEMENT: {name}! Unlocked {reward.Name}" : $"ACHIEVEMENT: {name}!");
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.HeadshotDing, 0.8f, 0.8f);
        }
        void OnDisable()
        {
            GameState.Announced -= OnAnnounced;
            FriendSlop.Profile.PlayerProfile.AchievementUnlocked -= OnAchievement;
            FriendSlop.Profile.PlayerProfile.LevelUp -= OnLevelUp;
        }

        void OnAnnounced(string text)
        {
            _toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + ToastSeconds });
            if (_toasts.Count > MaxToasts) _toasts.RemoveAt(0);
        }

        void OnGUI()
        {
            var state = GameState.Instance;
            if (state == null || !state.IsSpawned) return;

            _money ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            _toast ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            _hint ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.LowerCenter };

            _marker ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _gain ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };

            // Money with a punch when it goes up.
            float p = (Time.unscaledTime - _punchStart) / PunchSeconds;
            bool punching = p >= 0f && p < 1f;
            float scale = punching ? 1f + 0.45f * Mathf.Sin(p * Mathf.PI) : 1f;
            _money.fontSize = Mathf.RoundToInt(28 * scale);
            var prevColor = GUI.color;
            if (punching) GUI.color = Color.Lerp(new Color(0.4f, 1f, 0.4f), Color.white, p);
            Shadowed(new Rect(Screen.width - 420, 12, 400, 50), $"${state.Money.Value:N0}", _money);
            GUI.color = prevColor;
            Shadowed(new Rect(Screen.width - 320, 56, 300, 24), $"{state.AnimalsSold.Value} sold", _hint);
            DrawClock(prevColor);

            float g = (Time.unscaledTime - _punchStart) / 1.4f;
            if (g >= 0f && g < 1f)
            {
                GUI.color = new Color(0.45f, 1f, 0.45f, 1f - g);
                Shadowed(new Rect(Screen.width - 320, 80 + g * 30f, 300, 30), $"+${_lastGain:N0}", _gain);
                GUI.color = prevColor;
            }

            float s = (Time.unscaledTime - _spendStart) / 1.4f;
            if (s >= 0f && s < 1f)
            {
                GUI.color = new Color(1f, 0.4f, 0.35f, 1f - s);
                Shadowed(new Rect(Screen.width - 320, 80 + s * 30f, 300, 30), $"-${_lastSpend:N0}", _gain);
                GUI.color = prevColor;
            }

            DrawWeaponBar();
            DrawSurvival();
            DrawSleep();

            // Hit marker over the crosshair.
            if (Time.unscaledTime < _hitMarkerUntil)
            {
                var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                GUI.color = _hitMarkerHead ? new Color(1f, 0.25f, 0.2f) : Color.white;
                _marker.fontSize = _hitMarkerHead ? 34 : 24;
                GUI.Label(new Rect(c.x - 30, c.y - 30, 60, 60), "X", _marker);
                GUI.color = prevColor;
            }

            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.Until < now);
            for (int i = 0; i < _toasts.Count; i++)
            {
                float alpha = Mathf.Clamp01((_toasts[i].Until - now) / 1f);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Shadowed(new Rect(Screen.width / 2f - 400, 60 + i * 26, 800, 26), _toasts[i].Text, _toast);
                GUI.color = prev;
            }

            string hint = NetworkPlayer.LocalHint;
            if (!string.IsNullOrEmpty(hint))
                Shadowed(new Rect(Screen.width / 2f - 400, Screen.height - 120, 800, 30), hint, _hint);
        }

        GUIStyle _clock, _zzz;

        /// <summary>Day number + clock, a moon at night, and the goose countdown when the flock is close to haunting you.</summary>
        void DrawClock(Color prevColor)
        {
            var cycle = FriendSlop.World.DayNightCycle.Instance;
            if (cycle == null || !cycle.IsSpawned) return;
            _clock ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            bool night = cycle.IsNight;
            GUI.color = night ? new Color(0.7f, 0.78f, 1f) : new Color(1f, 0.95f, 0.75f);
            Shadowed(new Rect(Screen.width - 420, 82, 400, 24), $"{(night ? "Night" : "Day")} {cycle.Day}  ·  {cycle.ClockText}", _clock);
            GUI.color = prevColor;

            var ev = NightEvents.Instance;
            if (ev == null || !ev.IsSpawned) return;
            if (ev.GhostNight.Value)
            {
                GUI.color = new Color(0.55f, 1f, 1f);
                Shadowed(new Rect(Screen.width - 420, 104, 400, 24), "GHOST GOOSE NIGHT", _clock);
            }
            else
            {
                int left = ev.GeesePerHaunting - ev.GeeseKilled.Value % ev.GeesePerHaunting;
                if (left <= 10 && ev.GeeseKilled.Value > 0)
                {
                    GUI.color = new Color(0.8f, 0.9f, 1f, 0.85f);
                    Shadowed(new Rect(Screen.width - 420, 104, 400, 24), $"The flock is watching... ({left} more geese)", _clock);
                }
            }
            GUI.color = prevColor;
        }

        /// <summary>Eyes closed while sleeping, with how many hunters are in bed.</summary>
        void DrawSleep()
        {
            var me = NetworkPlayer.Local;
            if (me == null || !me.Sleeping.Value) return;
            int asleep = 0, total = 0;
            foreach (var p in NetworkPlayer.All) { if (p == null || !p.IsSpawned) continue; total++; if (p.Sleeping.Value) asleep++; }
            _white ??= Texture2D.whiteTexture;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0.02f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = prev;
            _zzz ??= new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Shadowed(new Rect(0, Screen.height / 2f - 60, Screen.width, 60), "Z z z . . .", _zzz);
            Shadowed(new Rect(0, Screen.height / 2f + 10, Screen.width, 30),
                asleep >= total ? "Everyone's asleep. Sleeping through the night..." : $"{asleep}/{total} hunters in bed. Everyone has to sleep to skip the night.", _hint);
        }

        GUIStyle _big, _small;
        static Texture2D _white;

        /// <summary>Health bar, hurt flash, downed overlay, boss bar.</summary>
        void DrawSurvival()
        {
            _white ??= Texture2D.whiteTexture;
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var prev = GUI.color;

            var hp = FriendSlop.Player.PlayerHealth.Local;
            if (hp != null && hp.IsSpawned)
            {
                // Red flash when hit.
                float flash = Mathf.Clamp01(1f - (Time.time - hp.LastHurtTime) / 0.5f);
                if (flash > 0f)
                {
                    GUI.color = new Color(0.8f, 0f, 0f, 0.35f * flash);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
                }

                // Health bar (bottom left).
                float frac = hp.Health.Value / hp.MaxHp;
                var bar = new Rect(24, Screen.height - 52, 260, 22);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(bar, _white);
                GUI.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.85f, 0.3f), frac);
                GUI.DrawTexture(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * frac, bar.height - 4), _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(bar.x + 8, bar.y, bar.width, bar.height), $"HP {Mathf.CeilToInt(hp.Health.Value)}", _small);

                // Hunter level + XP toward the next one, just above the health bar.
                var prof = FriendSlop.Profile.PlayerProfile.Current;
                int lv = prof.Level;
                int lo = FriendSlop.Profile.PlayerProfile.XpForLevel(lv), hi = FriendSlop.Profile.PlayerProfile.XpForLevel(lv + 1);
                float xpFrac = hi > lo ? Mathf.Clamp01((prof.xp - lo) / (float)(hi - lo)) : 1f;
                var xpBar = new Rect(24, Screen.height - 64, 260, 8);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(xpBar, _white);
                GUI.color = new Color(1f, 0.8f, 0.25f);
                GUI.DrawTexture(new Rect(xpBar.x + 1, xpBar.y + 1, (xpBar.width - 2) * xpFrac, xpBar.height - 2), _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(24, Screen.height - 88, 300, 22), $"Lv {lv}   {prof.xp - lo:N0} / {hi - lo:N0} XP", _small);

                // Mic status (voice chat itself arrives later; the controls already work).
                string mic = null;
                if (FriendSlop.Settings.GameSettings.MicMuted)
                {
                    mic = $"MIC MUTED ({FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.ToggleMute)})";
                    GUI.color = new Color(1f, 0.45f, 0.4f);
                }
                else if (FriendSlop.Settings.GameSettings.PushToTalk && FriendSlop.Settings.Controls.Held(FriendSlop.Settings.GameAction.PushToTalk))
                {
                    mic = "TALKING";
                    GUI.color = new Color(0.5f, 1f, 0.5f);
                }
                else if (!FriendSlop.Settings.GameSettings.PushToTalk)
                {
                    mic = "OPEN MIC";
                    GUI.color = new Color(0.85f, 0.85f, 0.85f, 0.7f);
                }
                if (mic != null) Shadowed(new Rect(bar.x, bar.y - 26, 300, 22), mic, _small);
                GUI.color = Color.white;

                if (hp.IsDown.Value)
                {
                    GUI.color = new Color(0.25f, 0f, 0f, 0.45f);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
                    GUI.color = Color.white;
                    int secs = Mathf.Max(0, Mathf.CeilToInt(hp.RespawnAt - Time.time));
                    Shadowed(new Rect(0, Screen.height / 2f - 40, Screen.width, 50), "YOU GOT WRECKED", _big);
                    Shadowed(new Rect(0, Screen.height / 2f + 10, Screen.width, 30), $"Back at camp in {secs}…", _toast);
                }
            }

            // Boss bar (top center) while a boss is alive.
            foreach (var a in FriendSlop.Animals.Animal.All)
            {
                if (a == null || !a.IsSpawned || a.Species == null || !a.Species.isBoss || a.IsDead.Value) continue;
                var r = new Rect(Screen.width / 2f - 300, 24, 600, 20);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(r, _white);
                GUI.color = new Color(0.85f, 0.2f, 0.15f);
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * a.HealthFraction.Value, r.height - 4), _white);
                GUI.color = Color.white;
                Shadowed(new Rect(r.x, r.y - 2, r.width, 24), $"{a.Species.bossTitle}{(a.IsAngry.Value ? " (hunting)" : "")}", _toast);
                break;
            }
            GUI.color = prev;
        }

        void DrawWeaponBar()
        {
            var weapons = FriendSlop.Weapons.PlayerWeapons.Local;
            var state = GameState.Instance;
            if (weapons == null || state == null || weapons.Catalog == null) return;

            _weapon ??= new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleRight };
            var catalog = weapons.Catalog;
            var slots = weapons.Slots();
            float y = Screen.height - 40f;
            var prev = GUI.color;

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Shadowed(new Rect(Screen.width - 420, y, 400, 22), "Tab inventory · G drop", _weapon);
            y -= 26f;

            for (int s = slots.Count - 1; s >= 0; s--)
            {
                int i = slots[s];
                var def = catalog.Get(i);
                bool on = weapons.Equipped.Value == i;
                string label = $"{def.displayName}  [{FriendSlop.Weapons.WeaponCatalog.KeyLabel(s)}]";
                if (on && def.spinUpTime > 0f && weapons.SpinUp < 1f) label = $"spinning {weapons.SpinUp:P0}  " + label;
                GUI.color = on ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, 0.6f);
                _weapon.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                Shadowed(new Rect(Screen.width - 420, y, 400, 22), label, _weapon);
                y -= 22f;
            }
            GUI.color = prev;
        }

        static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, prev.a * 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            GUI.color = prev;
            GUI.Label(r, text, style);
        }
    }
}
