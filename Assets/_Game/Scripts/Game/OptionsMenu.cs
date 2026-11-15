using FriendSlop.Net;
using FriendSlop.Settings;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Pause menu (Esc in a hunt) and the tabbed Options screen (also reachable from the main
    /// menu). Options: Controls (view + rebind every action), Audio, Mouse &amp; View, Voice.
    /// Everything saves immediately on this PC.
    /// </summary>
    public class OptionsMenu : MonoBehaviour
    {
        enum Tab { Controls, Audio, MouseView, Voice }

        /// <summary>True while waiting for a key press to rebind (other input handlers should ignore keys).</summary>
        public static bool IsCapturingKey => _capturing.HasValue || Time.frameCount == _captureEndFrame;
        static int _captureEndFrame = -1;

        /// <summary>Main menu (outside a hunt) uses this to show options without the pause frame.</summary>
        public static bool OpenFromMainMenu { get; set; }

        static GameAction? _capturing;
        static int _captureFrame;
        Tab _tab;
        Vector2 _scroll;
        GUIStyle _title, _section, _label, _small, _keyButton;

        void Update()
        {
            if (!_capturing.HasValue || Time.frameCount == _captureFrame) return; // skip the click that started capture
            if (Controls.TryCaptureAny(out var binding))
            {
                // Esc cancels unless we're rebinding Pause itself.
                bool cancel = !binding.IsMouse && binding.Key == UnityEngine.InputSystem.Key.Escape && _capturing != GameAction.Pause;
                if (!cancel) Controls.Rebind(_capturing.Value, binding);
                _capturing = null;
                _captureEndFrame = Time.frameCount; // so the same key press doesn't also close the menu
            }
        }

        void OnGUI()
        {
            bool inPause = GameMenus.Open == GameMenus.Menu.Pause;
            bool inOptions = GameMenus.Open == GameMenus.Menu.Options || OpenFromMainMenu;
            if (!inPause && !inOptions) return;

            EnsureStyles();
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (inPause && !OpenFromMainMenu) DrawPause();
            else DrawOptions();
        }

        void DrawPause()
        {
            float w = 340f, h = 330f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, w - 40, h - 32));
            GUILayout.Label("Paused", _title);
            GUILayout.Label("The hunt keeps going for everyone else.", _small);
            GUILayout.Space(14);
            if (GUILayout.Button("Resume", GUILayout.Height(40))) GameMenus.Close();
            GUILayout.Space(6);
            if (GUILayout.Button("Options", GUILayout.Height(40))) GameMenus.Show(GameMenus.Menu.Options);
            GUILayout.Space(6);
            if (GUILayout.Button("Leave hunt", GUILayout.Height(40)))
            {
                GameMenus.Reset();
                NetBootstrap.Instance?.Leave();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Quit to desktop", GUILayout.Height(34)))
            {
                NetBootstrap.Instance?.Leave();
                Application.Quit();
            }
            GUILayout.EndArea();
        }

        void DrawOptions()
        {
            float w = Mathf.Min(720f, Screen.width - 40f), h = Mathf.Min(600f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, w - 40, h - 32));

            GUILayout.Label("Options", _title);
            GUILayout.BeginHorizontal();
            TabButton(Tab.Controls, "Controls");
            TabButton(Tab.Audio, "Audio");
            TabButton(Tab.MouseView, "Mouse & View");
            TabButton(Tab.Voice, "Voice");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Controls: DrawControls(); break;
                case Tab.Audio: DrawAudio(); break;
                case Tab.MouseView: DrawMouseView(); break;
                case Tab.Voice: DrawVoice(); break;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(34), GUILayout.Width(140)))
            {
                _capturing = null;
                if (OpenFromMainMenu) OpenFromMainMenu = false;
                else GameMenus.Show(GameMenus.Menu.Pause);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Changes save automatically.", _small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void TabButton(Tab tab, string label)
        {
            bool on = _tab == tab;
            GUI.color = on ? new Color(1f, 0.85f, 0.4f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(32))) { _tab = tab; _capturing = null; _scroll = Vector2.zero; }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ tabs

        void DrawControls()
        {
            GUILayout.Label("Click a binding, then press the new key or mouse button. Esc cancels.", _small);
            GUILayout.Space(6);
            foreach (GameAction action in System.Enum.GetValues(typeof(GameAction)))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Controls.DisplayNames[action], _label, GUILayout.Width(260));
                bool waiting = _capturing == action;
                GUI.color = waiting ? new Color(1f, 0.8f, 0.3f) : Color.white;
                if (GUILayout.Button(waiting ? "Press a key…" : Controls.Label(action), _keyButton, GUILayout.Width(200), GUILayout.Height(28)))
                {
                    _capturing = action;
                    _captureFrame = Time.frameCount;
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Weapon slots", _label, GUILayout.Width(260));
            GUILayout.Label("1 – 0, mouse wheel to cycle (fixed)", _small);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (GUILayout.Button("Reset controls to defaults", GUILayout.Width(260), GUILayout.Height(30))) Controls.ResetToDefaults();
        }

        void DrawAudio()
        {
            GameSettings.MasterVolume = Slider("Master volume", GameSettings.MasterVolume, 0f, 1f, v => $"{v:P0}");
            GameSettings.SfxVolume = Slider("Effects volume", GameSettings.SfxVolume, 0f, 1f, v => $"{v:P0}");
            GameSettings.VoiceVolume = Slider("Voice chat volume", GameSettings.VoiceVolume, 0f, 1f, v => $"{v:P0}");
            GUILayout.Space(10);
            if (GUILayout.Button("Reset all settings to defaults", GUILayout.Width(260), GUILayout.Height(30))) GameSettings.ResetToDefaults();
        }

        void DrawMouseView()
        {
            GameSettings.MouseSensitivity = Slider("Mouse sensitivity", GameSettings.MouseSensitivity, 0.02f, 0.6f, v => $"{v:0.00}");
            GameSettings.InvertY = Toggle("Invert mouse Y", GameSettings.InvertY);
            GameSettings.FieldOfView = Slider("Field of view", GameSettings.FieldOfView, 60f, 100f, v => $"{v:0}°");
        }

        void DrawVoice()
        {
            GUILayout.Label("Proximity voice chat is coming in a later update; these settings are saved for it now.", _small);
            GUILayout.Space(8);
            bool ptt = GameSettings.PushToTalk;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mic mode", _label, GUILayout.Width(220));
            if (GUILayout.Toggle(ptt, $"Push to talk ({Controls.Label(GameAction.PushToTalk)})", GUILayout.Width(220)) != ptt && !ptt) GameSettings.PushToTalk = true;
            if (GUILayout.Toggle(!ptt, "Open mic", GUILayout.Width(140)) != !ptt && ptt) GameSettings.PushToTalk = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GameSettings.MicMuted = Toggle($"Mic muted (toggle in game with {Controls.Label(GameAction.ToggleMute)})", GameSettings.MicMuted);
            GUILayout.Space(6);
            GUILayout.Label("Rebind the push-to-talk and mute keys on the Controls tab.", _small);
        }

        // ------------------------------------------------------------------ widgets

        float Slider(string label, float value, float min, float max, System.Func<float, string> format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label, GUILayout.Width(220));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(300));
            GUILayout.Label(format(v), _label, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            return v;
        }

        bool Toggle(string label, bool value)
        {
            bool v = GUILayout.Toggle(value, "  " + label);
            GUILayout.Space(8);
            return v;
        }

        void EnsureStyles()
        {
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _section ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            _keyButton ??= new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
    }
}
