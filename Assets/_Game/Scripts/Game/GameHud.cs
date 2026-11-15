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
            GUI.skin = UiTheme.Skin;

            _money ??= new GUIStyle(UiTheme.HudBig) { fontSize = 30, alignment = TextAnchor.MiddleRight };
            _money.normal.textColor = UiTheme.Gold;
            _toast ??= new GUIStyle(UiTheme.Center) { fontSize = 16 };
            _hint ??= new GUIStyle(UiTheme.Center) { fontSize = 15 };

            _marker ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _gain ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };

            // Top-right panel: team money (with a punch when it goes up), animals sold, clock.
            var tr = new Rect(Screen.width - 250, 12, 236, 92);
            UiTheme.Panel(tr);
            float p = (Time.unscaledTime - _punchStart) / PunchSeconds;
            bool punching = p >= 0f && p < 1f;
            float scale = punching ? 1f + 0.35f * Mathf.Sin(p * Mathf.PI) : 1f;
            _money.fontSize = Mathf.RoundToInt(30 * scale);
            var prevColor = GUI.color;
            if (punching) GUI.color = Color.Lerp(new Color(0.5f, 1f, 0.5f), Color.white, p);
            UiTheme.Shadow(new Rect(tr.x, tr.y + 4, tr.width - 16, 38), $"${state.Money.Value:N0}", _money);
            GUI.color = prevColor;
            GUI.Label(new Rect(tr.x + 14, tr.y + 10, 80, 22), "TEAM", UiTheme.Small);
            var right = new GUIStyle(UiTheme.Small) { alignment = TextAnchor.MiddleRight };
            right.normal.textColor = UiTheme.Muted;
            GUI.Label(new Rect(tr.x, tr.y + 42, tr.width - 16, 20), $"{state.AnimalsSold.Value} sold", right);
            DrawClock(prevColor, new Rect(tr.x, tr.y + 62, tr.width - 16, 22));

            float g = (Time.unscaledTime - _punchStart) / 1.4f;
            if (g >= 0f && g < 1f)
            {
                GUI.color = new Color(0.45f, 1f, 0.45f, 1f - g);
                UiTheme.Shadow(new Rect(Screen.width - 330, 110 + g * 30f, 316, 30), $"+${_lastGain:N0}", _gain);
                GUI.color = prevColor;
            }

            float s = (Time.unscaledTime - _spendStart) / 1.4f;
            if (s >= 0f && s < 1f)
            {
                GUI.color = new Color(1f, 0.4f, 0.35f, 1f - s);
                UiTheme.Shadow(new Rect(Screen.width - 330, 110 + s * 30f, 316, 30), $"-${_lastSpend:N0}", _gain);
                GUI.color = prevColor;
            }

            DrawWeaponBar();
            DrawSurvival();
            DrawItems();
            DrawSleep();
            DrawTank();

            // Hit marker over the crosshair.
            if (Time.unscaledTime < _hitMarkerUntil)
            {
                var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                GUI.color = _hitMarkerHead ? new Color(1f, 0.25f, 0.2f) : Color.white;
                _marker.fontSize = _hitMarkerHead ? 34 : 24;
                GUI.Label(new Rect(c.x - 30, c.y - 30, 60, 60), "X", _marker);
                GUI.color = prevColor;
            }

            // Announcement feed: pills under the boss bar, newest at the bottom, fading out.
            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.Until < now);
            float ty = _bossBarShown ? 64f : 22f;
            for (int i = 0; i < _toasts.Count; i++)
            {
                float alpha = Mathf.Clamp01((_toasts[i].Until - now) / 1f);
                float tw = Mathf.Min(Screen.width - 560f, _toast.CalcSize(new GUIContent(_toasts[i].Text)).x + 36f);
                var r = new Rect(Screen.width / 2f - tw / 2f, ty + i * 32, tw, 28);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                UiTheme.Pill(r);
                UiTheme.Shadow(r, _toasts[i].Text, _toast);
                GUI.color = prev;
            }

            string hint = NetworkPlayer.LocalHint;
            if (!string.IsNullOrEmpty(hint))
            {
                float hw = _hint.CalcSize(new GUIContent(hint)).x + 40f;
                var r = new Rect(Screen.width / 2f - hw / 2f, Screen.height - 132, hw, 32);
                UiTheme.Pill(r);
                UiTheme.Shadow(r, hint, _hint);
            }
        }

        bool _bossBarShown;

        /// <summary>Snack pockets (with their keys) and the energy-drink timer, beside the status panel.</summary>
        void DrawItems()
        {
            var items = PlayerItems.Local;
            if (items == null) return;
            float x = 300f, y = Screen.height - 56f;
            for (int k = 0; k < PlayerItems.Items.Length; k++)
            {
                var kind = (ItemKind)k;
                int n = items.Count(kind);
                var act = kind == ItemKind.ProteinBar ? FriendSlop.Settings.GameAction.UseProteinBar : FriendSlop.Settings.GameAction.UseEnergyDrink;
                string label = $"{PlayerItems.Items[k].Name}  x{n}";
                float w = UiTheme.HudLabel.CalcSize(new GUIContent(label)).x + 52f;
                var r = new Rect(x, y, w, 30);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, n > 0 ? 1f : 0.5f);
                UiTheme.Pill(r);
                UiTheme.KeyCap(r.x + 7, r.y + 5, FriendSlop.Settings.Controls.Label(act));
                UiTheme.Shadow(new Rect(r.x + 36, r.y, w - 36, r.height), label, UiTheme.HudLabel);
                GUI.color = prev;
                x += w + 8f;
            }
            float left = items.BoostLeft;
            if (left > 0f)
                UiTheme.Bar(new Rect(300f, Screen.height - 82f, 200f, 18f), left / PlayerItems.BoostSeconds, new Color(0.35f, 0.85f, 1f), $"ENERGY  {left:0}s", UiTheme.Small);
        }

        GUIStyle _clock, _zzz;

        /// <summary>Day number + clock, a moon at night, and the goose countdown when the flock is close to haunting you.</summary>
        void DrawClock(Color prevColor, Rect at)
        {
            var cycle = FriendSlop.World.DayNightCycle.Instance;
            if (cycle == null || !cycle.IsSpawned) return;
            _clock ??= new GUIStyle(UiTheme.Small) { fontSize = 15, alignment = TextAnchor.MiddleRight };
            bool night = cycle.IsNight;
            GUI.color = night ? new Color(0.72f, 0.8f, 1f) : new Color(1f, 0.93f, 0.7f);
            UiTheme.Shadow(at, $"{(night ? "☾ Night" : "☀ Day")} {cycle.Day}  ·  {cycle.ClockText}", _clock);
            GUI.color = prevColor;

            var ev = NightEvents.Instance;
            if (ev == null || !ev.IsSpawned) return;
            var below = new Rect(Screen.width - 420, at.y + 34, 406, 22);
            if (ev.GhostNight.Value)
            {
                GUI.color = new Color(0.55f, 1f, 1f);
                UiTheme.Shadow(below, "GHOST GOOSE NIGHT", _clock);
            }
            else
            {
                int left = ev.GeesePerHaunting - ev.GeeseKilled.Value % ev.GeesePerHaunting;
                if (left <= 10 && ev.GeeseKilled.Value > 0)
                {
                    GUI.color = new Color(0.8f, 0.9f, 1f, 0.85f);
                    UiTheme.Shadow(below, $"The flock is watching... ({left} more geese)", _clock);
                }
            }
            GUI.color = prevColor;
        }

        /// <summary>Crew role and cannon reload bar for whoever is aiming the tank.</summary>
        void DrawTank()
        {
            var me = NetworkPlayer.Local;
            if (me == null || !me.InTank || me.CurrentTank == null) return;
            var tank = me.CurrentTank;
            _white ??= Texture2D.whiteTexture;
            int crew = 0; for (int i = 0; i < FriendSlop.World.Tank.Seats; i++) if (tank.Occupant(i) != FriendSlop.World.Tank.Empty) crew++;
            Shadowed(new Rect(Screen.width / 2f - 200, Screen.height - 170, 400, 24), $"TANK  ·  crew {crew}/{FriendSlop.World.Tank.Seats}", _hint);
            if (me.TankSeat != tank.AimerSeat) return;
            var bar = new Rect(Screen.width / 2f - 120, Screen.height - 145, 240, 14);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(bar, _white);
            float f = tank.ReloadFraction;
            GUI.color = f >= 1f ? new Color(1f, 0.6f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            GUI.DrawTexture(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * f, bar.height - 4), _white);
            GUI.color = prev;
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f - 40);
            GUI.Label(new Rect(c.x - 10, c.y - 12, 20, 24), "+");
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

                // Status panel (bottom left): level badge + XP, then the health bar.
                var panel = new Rect(14, Screen.height - 104, 272, 90);
                UiTheme.Panel(panel);
                float frac = hp.Health.Value / hp.MaxHp;
                var bar = new Rect(panel.x + 12, panel.y + 52, panel.width - 24, 26);
                var hpCol = Color.Lerp(UiTheme.Bad, UiTheme.Good, Mathf.InverseLerp(0.2f, 0.8f, frac));
                if (frac < 0.25f) hpCol = Color.Lerp(hpCol, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.time * 10f))); // low-health pulse
                UiTheme.Bar(bar, frac, hpCol, $"♥  {Mathf.CeilToInt(hp.Health.Value)} / {Mathf.CeilToInt(hp.MaxHp)}");

                var prof = FriendSlop.Profile.PlayerProfile.Current;
                int lv = prof.Level;
                int lo = FriendSlop.Profile.PlayerProfile.XpForLevel(lv), hi = FriendSlop.Profile.PlayerProfile.XpForLevel(lv + 1);
                float xpFrac = hi > lo ? Mathf.Clamp01((prof.xp - lo) / (float)(hi - lo)) : 1f;
                var badge = new Rect(panel.x + 12, panel.y + 10, 58, 32);
                UiTheme.Pill(badge);
                var lvStyle = new GUIStyle(UiTheme.Center) { fontSize = 15 };
                lvStyle.normal.textColor = UiTheme.Gold;
                GUI.Label(badge, $"Lv {lv}", lvStyle);
                UiTheme.Bar(new Rect(panel.x + 78, panel.y + 22, panel.width - 90, 10), xpFrac, UiTheme.Gold);
                var xpStyle = new GUIStyle(UiTheme.Small) { fontSize = 11 };
                xpStyle.normal.textColor = UiTheme.Muted;
                GUI.Label(new Rect(panel.x + 80, panel.y + 4, panel.width - 90, 18), $"{prof.xp - lo:N0} / {hi - lo:N0} XP", xpStyle);
                bar = new Rect(panel.x, panel.y - 4, panel.width, 26); // mic line sits just above the panel

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

            // Boss bars (top center) while bosses are alive (two different bosses can be out at once).
            _bossBarShown = false;
            int shown = 0;
            foreach (var a in FriendSlop.Animals.Animal.All)
            {
                if (a == null || !a.IsSpawned || a.Species == null || !a.Species.isBoss || a.IsDead.Value || shown >= 2) continue;
                var r = new Rect(Screen.width / 2f - 300, 18 + shown * 30, 600, 24);
                UiTheme.Bar(r, a.HealthFraction.Value, new Color(0.88f, 0.22f, 0.16f));
                UiTheme.Shadow(r, $"☠ {a.Species.bossTitle}{(a.IsAngry.Value ? "  (hunting)" : "")}", _toast);
                _bossBarShown = true;
                shown++;
            }
            GUI.color = prev;
        }

        void DrawWeaponBar()
        {
            var weapons = FriendSlop.Weapons.PlayerWeapons.Local;
            var state = GameState.Instance;
            if (weapons == null || state == null || weapons.Catalog == null) return;

            _weapon ??= new GUIStyle(UiTheme.HudLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            var catalog = weapons.Catalog;
            var slots = weapons.Slots();
            var prev = GUI.color;

            // Weapons panel (bottom right): a row per slot with its key; the equipped one is highlighted.
            const float rowH = 26f, pw = 250f;
            float ph = slots.Count * rowH + 40f;
            var panel = new Rect(Screen.width - pw - 14, Screen.height - ph - 14, pw, ph);
            UiTheme.Panel(panel);
            float y = panel.y + 10f;
            for (int s = 0; s < slots.Count; s++)
            {
                int i = slots[s];
                var def = catalog.Get(i);
                bool on = weapons.Equipped.Value == i;
                var row = new Rect(panel.x + 8, y, pw - 16, rowH - 2);
                if (on) { GUI.color = new Color(1f, 0.64f, 0.22f, 0.9f); UiTheme.Pill(row); GUI.color = prev; }
                UiTheme.KeyCap(row.x + 6, row.y + 2, FriendSlop.Weapons.WeaponCatalog.KeyLabel(s));
                string label = def.displayName;
                if (on && def.spinUpTime > 0f && weapons.SpinUp < 1f) label += $"  (spinning {weapons.SpinUp:P0})";
                _weapon.normal.textColor = on ? UiTheme.Gold : UiTheme.Text;
                _weapon.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                UiTheme.Shadow(new Rect(row.x + 34, row.y, row.width - 34, row.height), label, _weapon);
                y += rowH;
            }
            var help = new GUIStyle(UiTheme.Small) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            help.normal.textColor = UiTheme.Muted;
            GUI.Label(new Rect(panel.x, panel.yMax - 26, pw, 20),
                $"{FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Inventory)} inventory  ·  {FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.DropWeapon)} drop  ·  {FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Map)} map", help);
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
