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
        GUIStyle _money, _toast, _hint;

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

            Shadowed(new Rect(Screen.width - 320, 16, 300, 40), $"${state.Money.Value:N0}", _money);
            Shadowed(new Rect(Screen.width - 320, 50, 300, 24), $"{state.AnimalsSold.Value} sold", _hint);

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
