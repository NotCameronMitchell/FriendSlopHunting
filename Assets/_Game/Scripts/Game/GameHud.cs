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
            public float Born;
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

        /// <summary>Flash the hit marker on the crosshair. Gold + bigger for headshots.</summary>
        public static void ShowHitMarker(bool headshot)
        {
            _hitMarkerUntil = Time.unscaledTime + HitMarkerSeconds;
            _hitMarkerStart = Time.unscaledTime;
            _hitMarkerHead = headshot;
        }

        // ------------------------------------------------------------------ juice state
        // Research notes (GMTK "Secrets of Game Feel" and "The Power of Video Game HUDs", a Valheim
        // combat-feel breakdown, Lexispell's UI tween tips): every hit gets layered feedback (marker,
        // number, sound, flash); anything that changes on the HUD animates (pop, count up, slide);
        // the most important info gets the "first read" (centre, big, bright); motion stays snappy.

        static float _hitMarkerStart = -10f, _killStart = -10f;
        static string _killName, _killTags = "";
        static int _killValue;
        struct PopupText { public Vector3 World; public string Text; public Color Color; public float Size, Born; public Vector2 Drift; }
        static readonly List<PopupText> _popups = new List<PopupText>();
        struct Banner { public string Title, Sub; public Color Color; public float Born; }
        static readonly Queue<Banner> _banners = new Queue<Banner>();
        static Banner? _banner;
        float _shownMoney = -1f, _hpTrail = 1f, _hpTrailHold, _lastHp = -1f;
        string _lastHint = "";
        float _hintBorn;
        int _lastEquipped = -1;
        float _equipBorn = -10f;
        static Texture2D _vignette;

        /// <summary>Floating text at a world position (damage numbers), popping in, rising and fading.</summary>
        public static void Popup(Vector3 world, string text, Color color, float size = 1f)
        {
            _popups.Add(new PopupText { World = world, Text = text, Color = color, Size = size, Born = Time.unscaledTime,
                Drift = new Vector2(Random.Range(-28f, 28f), 0f) });
            if (_popups.Count > 24) _popups.RemoveAt(0);
        }

        /// <summary>Local player's kill: big confirm under the crosshair with the style tags and value.</summary>
        public static void ShowKill(string name, FriendSlop.Animals.ShotStyle style, int value, int bounty)
        {
            _killStart = Time.unscaledTime;
            _killName = $"{name.ToUpperInvariant()} DOWN";
            var tags = new List<string>();
            if ((style & FriendSlop.Animals.ShotStyle.Headshot) != 0) tags.Add("HEADSHOT");
            if ((style & FriendSlop.Animals.ShotStyle.LongShot) != 0) tags.Add("LONG SHOT");
            if ((style & FriendSlop.Animals.ShotStyle.JumpShot) != 0) tags.Add("JUMP SHOT");
            _killTags = string.Join("  ·  ", tags);
            _killValue = bounty > 0 ? bounty : value;
            _hitMarkerStart = Time.unscaledTime; _hitMarkerUntil = Time.unscaledTime + 0.4f; _hitMarkerHead = true;
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.KillConfirm, 0.8f, tags.Count > 0 ? 1.12f : 1f);
        }

        /// <summary>A big centre banner (level up, achievements). Queued so they never overlap.</summary>
        public static void ShowBanner(string title, string sub, Color color)
        {
            _banners.Enqueue(new Banner { Title = title, Sub = sub, Color = color });
        }

        static float EaseOutBack(float x) { const float c1 = 1.70158f, c3 = c1 + 1f; x = Mathf.Clamp01(x); return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }
        static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); return 1f - Mathf.Pow(1f - x, 3f); }

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
            ShowBanner("LEVEL UP!", $"Hunter level {level}  ·  new upgrades at the gun shop", UiTheme.Gold);
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.Fanfare, 0.8f);
        }

        void OnAchievement(string id, string name)
        {
            var a = FriendSlop.Profile.Cosmetics.FindAchievement(id);
            var reward = a != null && !string.IsNullOrEmpty(a.RewardId) ? FriendSlop.Profile.Cosmetics.Find(a.RewardId) : null;
            OnAnnounced(reward != null ? $"ACHIEVEMENT: {name}! Unlocked {reward.Name}" : $"ACHIEVEMENT: {name}!");
            ShowBanner("ACHIEVEMENT", reward != null ? $"{name}  ·  unlocked {reward.Name}" : name, new Color(0.55f, 0.9f, 1f));
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.Fanfare, 0.6f, 1.25f);
        }
        void OnDisable()
        {
            GameState.Announced -= OnAnnounced;
            FriendSlop.Profile.PlayerProfile.AchievementUnlocked -= OnAchievement;
            FriendSlop.Profile.PlayerProfile.LevelUp -= OnLevelUp;
        }

        void OnAnnounced(string text)
        {
            _toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + ToastSeconds, Born = Time.unscaledTime });
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
            int money = state.Money.Value;
            if (_shownMoney < 0f) _shownMoney = money;
            float diff = money - _shownMoney;
            _shownMoney = Mathf.Abs(diff) < 1f ? money : Mathf.MoveTowards(_shownMoney, money, Mathf.Max(60f, Mathf.Abs(diff) * 5f) * Time.unscaledDeltaTime);
            UiTheme.Shadow(new Rect(tr.x, tr.y + 4, tr.width - 16, 38), $"${Mathf.RoundToInt(_shownMoney):N0}", _money);
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

            DrawPopups();
            DrawCrosshair();
            DrawKill();
            DrawBanner();

            // Announcement feed: pills under the boss bar, newest at the bottom, fading out.
            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => t.Until < now);
            float ty = _bossBarShown ? 64f : 22f;
            for (int i = 0; i < _toasts.Count; i++)
            {
                float alpha = Mathf.Clamp01((_toasts[i].Until - now) / 1f);
                float tin = (now - _toasts[i].Born) / 0.28f;
                alpha *= Mathf.Clamp01(tin * 2f);
                float pop = Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(tin));
                float tw = Mathf.Min(Screen.width - 560f, _toast.CalcSize(new GUIContent(_toasts[i].Text)).x + 36f);
                var r = new Rect(Screen.width / 2f - tw * pop / 2f, ty + i * 32 - (1f - EaseOutCubic(tin)) * 18f, tw * pop, 28);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                UiTheme.Pill(r);
                UiTheme.Shadow(r, _toasts[i].Text, _toast);
                GUI.color = prev;
            }

            string hint = NetworkPlayer.LocalHint ?? "";
            if (hint != _lastHint) { if (_lastHint.Length == 0) _hintBorn = now; _lastHint = hint; }
            if (hint.Length > 0)
            {
                float k = EaseOutBack((now - _hintBorn) / 0.2f);
                float hw = _hint.CalcSize(new GUIContent(hint)).x + 40f;
                var r = new Rect(Screen.width / 2f - hw / 2f, Screen.height - 132 + (1f - k) * 10f, hw, 32);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((now - _hintBorn) / 0.12f));
                UiTheme.Pill(r);
                UiTheme.Shadow(r, hint, _hint);
                GUI.color = prev;
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
                // Red vignette when hit (edges only, so you can still see what bit you), pulsing at low health.
                float flash = Mathf.Clamp01(1f - (Time.time - hp.LastHurtTime) / 0.6f) * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(5f, 40f, hp.LastHurtAmount));
                float hpNow = hp.Health.Value / hp.MaxHp;
                float lowHp = hpNow < 0.3f && !hp.IsDown.Value ? (0.35f + 0.2f * Mathf.Sin(Time.time * 4f)) * (1f - hpNow / 0.3f) : 0f;
                float vig = Mathf.Max(flash * 0.9f, lowHp);
                if (vig > 0.01f && Event.current.type == EventType.Repaint)
                {
                    GUI.color = new Color(0.8f, 0f, 0f, Mathf.Clamp01(vig * 1.5f));
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Vignette, ScaleMode.StretchToFill);
                    GUI.color = prev;
                }

                // Status panel (bottom left): level badge + XP, then the health bar.
                var panel = new Rect(14, Screen.height - 104, 272, 90);
                UiTheme.Panel(panel);
                float frac = hp.Health.Value / hp.MaxHp;
                var bar = new Rect(panel.x + 12, panel.y + 52, panel.width - 24, 26);
                var hpCol = Color.Lerp(UiTheme.Bad, UiTheme.Good, Mathf.InverseLerp(0.2f, 0.8f, frac));
                if (frac < 0.25f) hpCol = Color.Lerp(hpCol, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.time * 10f))); // low-health pulse
                if (_lastHp < 0f || frac > _hpTrail) _hpTrail = frac;
                if (_lastHp >= 0f && frac < _lastHp - 0.0001f) _hpTrailHold = Time.unscaledTime + 0.45f;
                _lastHp = frac;
                if (Time.unscaledTime > _hpTrailHold) _hpTrail = Mathf.MoveTowards(_hpTrail, frac, Time.unscaledDeltaTime * 0.7f);
                float shakeHp = Mathf.Clamp01(1f - (Time.time - hp.LastHurtTime) / 0.3f);
                bar.x += Mathf.Sin(Time.time * 70f) * 3f * shakeHp;
                UiTheme.Bar(bar, frac, hpCol, $"♥  {Mathf.CeilToInt(hp.Health.Value)} / {Mathf.CeilToInt(hp.MaxHp)}", null, _hpTrail);

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
            if (weapons.Equipped.Value != _lastEquipped)
            {
                if (_lastEquipped >= 0) _equipBorn = Time.unscaledTime;
                _lastEquipped = weapons.Equipped.Value;
            }
            // Big weapon name at the bottom centre right after switching (fades quickly).
            float since = Time.unscaledTime - _equipBorn;
            if (since < 1.1f && weapons.Current != null)
            {
                _equipName ??= new GUIStyle(UiTheme.HudBig) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                _equipName.normal.textColor = UiTheme.Gold;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((1.1f - since) / 0.35f));
                float k = EaseOutBack(since / 0.22f);
                UiTheme.Shadow(new Rect(0, Screen.height - 200 + (1f - k) * 14f, Screen.width, 34), weapons.Current.displayName.ToUpperInvariant(), _equipName);
                GUI.color = prev;
            }

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
                if (on)
                {
                    float k = EaseOutBack((Time.unscaledTime - _equipBorn) / 0.25f);
                    var hl = new Rect(row.x - (1f - k) * 12f, row.y, row.width + (1f - k) * 24f, row.height);
                    GUI.color = new Color(1f, 0.64f, 0.22f, 0.9f); UiTheme.Pill(hl); GUI.color = prev;
                }
                float keyW = UiTheme.KeyCap(row.x + 6, row.y + 2, FriendSlop.Weapons.WeaponCatalog.KeyLabel(s));
                string label = def.displayName;
                if (on && def.spinUpTime > 0f && weapons.SpinUp < 1f) label += $"  (spinning {weapons.SpinUp:P0})";
                _weapon.normal.textColor = on ? UiTheme.Gold : UiTheme.Text;
                _weapon.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                float lx = Mathf.Max(34f, keyW + 14f);
                UiTheme.Shadow(new Rect(row.x + lx, row.y, row.width - lx, row.height), label, _weapon);
                y += rowH;
            }
            var help = new GUIStyle(UiTheme.Small) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            help.normal.textColor = UiTheme.Muted;
            GUI.Label(new Rect(panel.x, panel.yMax - 26, pw, 20),
                $"{FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Inventory)} inventory  ·  {FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.DropWeapon)} drop  ·  {FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Map)} map", help);
            GUI.color = prev;
        }

        // ------------------------------------------------------------------ juice drawing

        GUIStyle _equipName;

        static Texture2D Vignette
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int n = 128;
                _vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1.0f, d));
                        _vignette.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                _vignette.Apply();
                return _vignette;
            }
        }

        static void Line(Vector2 center, float angleDeg, float from, float to, float thickness, Color color)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDeg, center);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.55f);
            GUI.DrawTexture(new Rect(center.x + from - 1f, center.y - thickness / 2f - 1f, to - from + 2f, thickness + 2f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x + from, center.y - thickness / 2f, to - from, thickness), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.matrix = m;
        }

        /// <summary>
        /// Crosshair: four ticks whose gap opens with weapon spread, movement and recent shots, and
        /// tightens when aiming. Hit markers are four diagonal strokes that punch out and fade (gold
        /// for headshots, red and bigger on a kill).
        /// </summary>
        void DrawCrosshair()
        {
            if (Event.current.type != EventType.Repaint) return;
            var me = NetworkPlayer.Local;
            if (me == null || me.InTank || me.IsDown || me.Sleeping.Value || GameMenus.AnyOpen || Cursor.lockState != CursorLockMode.Locked) return;
            var c = new Vector2(Mathf.Round(Screen.width / 2f), Mathf.Round(Screen.height / 2f));
            var w = FriendSlop.Weapons.PlayerWeapons.Local;
            float spread = w != null ? w.CrosshairSpread : 0f;
            bool aiming = w != null && w.Aiming;
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            float gap = (aiming ? 3f : 6f + spread * 26f) * scale;
            float len = (aiming ? 5f : 8f) * scale;
            float th = Mathf.Max(2f, 2f * scale);
            var col = new Color(1f, 1f, 1f, string.IsNullOrEmpty(NetworkPlayer.LocalHint) ? 0.9f : 0.55f);
            for (int i = 0; i < 4; i++) Line(c, i * 90f, gap, gap + len, th, col);
            GUI.color = col; GUI.DrawTexture(new Rect(c.x - th / 2f, c.y - th / 2f, th, th), Texture2D.whiteTexture); GUI.color = Color.white;

            float t = Time.unscaledTime - _hitMarkerStart;
            float life = _hitMarkerUntil - _hitMarkerStart + 0.12f;
            if (t >= 0f && t < life)
            {
                bool kill = Time.unscaledTime - _killStart < 0.5f;
                float k = t / life;
                float pop = EaseOutBack(t / 0.08f);
                float baseGap = (kill ? 10f : 7f) * scale + pop * 4f * scale;
                float mlen = (kill ? 14f : _hitMarkerHead ? 11f : 8f) * scale;
                var mc = kill ? new Color(1f, 0.25f, 0.2f) : _hitMarkerHead ? new Color(1f, 0.84f, 0.25f) : Color.white;
                mc.a = 1f - k * k;
                for (int i = 0; i < 4; i++) Line(c, 45f + i * 90f, baseGap, baseGap + mlen, th * (kill ? 1.6f : 1.2f), mc);
            }
        }

        GUIStyle _popupStyle, _killStyle, _killSub, _bannerTitle, _bannerSub;

        void DrawPopups()
        {
            if (_popups.Count == 0) return;
            float now = Time.unscaledTime;
            _popups.RemoveAll(p => now - p.Born > 0.95f);
            var cam = FriendSlop.Weapons.PlayerWeapons.Local != null && FriendSlop.Weapons.PlayerWeapons.Local.ViewCamera != null && FriendSlop.Weapons.PlayerWeapons.Local.ViewCamera.isActiveAndEnabled
                ? FriendSlop.Weapons.PlayerWeapons.Local.ViewCamera : Camera.main;
            if (cam == null) return;
            _popupStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var prev = GUI.color;
            foreach (var p in _popups)
            {
                var sp = cam.WorldToScreenPoint(p.World);
                if (sp.z <= 0f) continue;
                float t = (now - p.Born) / 0.95f;
                float pop = EaseOutBack(t / 0.18f);
                float size = Mathf.LerpUnclamped(0.6f, 1f, pop) * p.Size * (1f + Mathf.Max(0f, 0.25f - t) * 1.2f);
                _popupStyle.fontSize = Mathf.RoundToInt(26f * size * Mathf.Max(0.8f, Screen.height / 1080f));
                float x = sp.x + p.Drift.x * EaseOutCubic(t), y = Screen.height - sp.y - 40f * EaseOutCubic(t) - 10f;
                var r = new Rect(x - 100f, y - 20f, 200f, 40f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f * (1f - t));
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), p.Text, _popupStyle);
                GUI.color = new Color(p.Color.r, p.Color.g, p.Color.b, 1f - Mathf.Pow(t, 3f));
                GUI.Label(r, p.Text, _popupStyle);
            }
            GUI.color = prev;
        }

        void DrawKill()
        {
            float t = Time.unscaledTime - _killStart;
            if (t < 0f || t > 1.8f || string.IsNullOrEmpty(_killName)) return;
            _killStyle ??= new GUIStyle(UiTheme.HudBig) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _killSub ??= new GUIStyle(UiTheme.HudLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            float k = EaseOutBack(t / 0.25f);
            float a = t > 1.3f ? 1f - (t - 1.3f) / 0.5f : 1f;
            var prev = GUI.color;
            float y = Screen.height / 2f + 46f;
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.LerpUnclamped(1.6f, 1f, k), new Vector2(Screen.width / 2f, y + 16f));
            GUI.color = new Color(1f, 1f, 1f, a);
            _killStyle.normal.textColor = new Color(1f, 0.42f, 0.32f);
            UiTheme.Shadow(new Rect(0, y, Screen.width, 32), _killName, _killStyle);
            GUI.matrix = m;
            string sub = (_killTags.Length > 0 ? _killTags + "   " : "") + (_killValue > 0 ? $"~${_killValue:N0}" : "");
            if (sub.Length > 0)
            {
                _killSub.normal.textColor = UiTheme.Gold;
                float k2 = EaseOutCubic((t - 0.12f) / 0.25f);
                GUI.color = new Color(1f, 1f, 1f, a * k2);
                UiTheme.Shadow(new Rect(0, y + 32 + (1f - k2) * 8f, Screen.width, 22), sub, _killSub);
            }
            GUI.color = prev;
        }

        void DrawBanner()
        {
            float now = Time.unscaledTime;
            if (_banner == null && _banners.Count > 0) { var b = _banners.Dequeue(); b.Born = now; _banner = b; }
            if (_banner == null) return;
            var bn = _banner.Value;
            float t = now - bn.Born;
            const float life = 2.6f;
            if (t > life) { _banner = null; return; }
            _bannerTitle ??= new GUIStyle(UiTheme.HudBig) { fontSize = 44, alignment = TextAnchor.MiddleCenter };
            _bannerSub ??= new GUIStyle(UiTheme.HudLabel) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            float k = EaseOutBack(t / 0.35f);
            float a = t > life - 0.5f ? (life - t) / 0.5f : Mathf.Clamp01(t / 0.1f);
            float y = Screen.height * 0.24f;
            var prev = GUI.color;
            // Ribbon behind the text: grows out from the centre.
            float rw = Mathf.Min(Screen.width - 40f, 720f) * EaseOutCubic(t / 0.3f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f * a);
            GUI.DrawTexture(new Rect(Screen.width / 2f - rw / 2f, y - 8f, rw, 90f), Texture2D.whiteTexture);
            GUI.color = new Color(bn.Color.r, bn.Color.g, bn.Color.b, a);
            GUI.DrawTexture(new Rect(Screen.width / 2f - rw / 2f, y - 8f, rw, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width / 2f - rw / 2f, y + 79f, rw, 3f), Texture2D.whiteTexture);
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.LerpUnclamped(0.4f, 1f, k), new Vector2(Screen.width / 2f, y + 24f));
            _bannerTitle.normal.textColor = bn.Color;
            GUI.color = new Color(1f, 1f, 1f, a);
            UiTheme.Shadow(new Rect(0, y, Screen.width, 50), bn.Title, _bannerTitle);
            GUI.matrix = m;
            float k2 = EaseOutCubic((t - 0.15f) / 0.3f);
            GUI.color = new Color(1f, 1f, 1f, a * k2);
            UiTheme.Shadow(new Rect(0, y + 50 + (1f - k2) * 8f, Screen.width, 24), bn.Sub, _bannerSub);
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
