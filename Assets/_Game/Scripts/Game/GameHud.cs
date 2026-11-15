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

        void OnEnable() => GameState.Announced += OnAnnounced;
        void OnDisable() => GameState.Announced -= OnAnnounced;

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
