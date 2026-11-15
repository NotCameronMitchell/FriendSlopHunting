using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// One look for every IMGUI screen: warm "campfire" palette, rounded translucent panels with a
    /// soft border, chunky accent buttons with hover/press states, pill chips and rounded bars.
    /// Call <c>GUI.skin = UiTheme.Skin;</c> at the top of an OnGUI, then use the helpers.
    /// All textures are generated at runtime (no assets).
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Text = new Color(1f, 0.96f, 0.88f);
        public static readonly Color Muted = new Color(0.82f, 0.77f, 0.68f);
        public static readonly Color Accent = new Color(1f, 0.64f, 0.22f);
        public static readonly Color AccentDark = new Color(0.78f, 0.42f, 0.12f);
        public static readonly Color Good = new Color(0.45f, 0.88f, 0.42f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.32f);
        public static readonly Color Gold = new Color(1f, 0.84f, 0.32f);
        static readonly Color PanelFill = new Color(0.11f, 0.09f, 0.08f, 0.86f);
        static readonly Color PanelEdge = new Color(1f, 0.75f, 0.4f, 0.55f);
        static readonly Color CardFill = new Color(1f, 0.92f, 0.8f, 0.07f);

        static GUISkin _skin;
        static Texture2D _panel, _card, _pill, _bar, _btn, _btnHover, _btnDown, _btnOff, _tabOn, _tabOff, _white;
        public static GUIStyle CardStyle, Title, Header, Body, Small, Price, Center, HudBig, HudLabel, Key;

        public static GUISkin Skin
        {
            get
            {
                if (_skin != null) return _skin;
                Build();
                return _skin;
            }
        }

        static void Build()
        {
            _panel = Rounded(48, 14, PanelFill, PanelEdge, 2);
            _card = Rounded(32, 10, CardFill, new Color(1f, 0.85f, 0.6f, 0.16f), 1);
            _pill = Rounded(32, 15, new Color(0.08f, 0.07f, 0.06f, 0.72f), new Color(1f, 0.8f, 0.5f, 0.3f), 1);
            _bar = Rounded(16, 7, Color.white, Color.clear, 0);
            _btn = Rounded(32, 9, Accent, new Color(1f, 0.85f, 0.55f), 1, shade: 0.18f);
            _btnHover = Rounded(32, 9, Color.Lerp(Accent, Color.white, 0.18f), new Color(1f, 0.92f, 0.7f), 1, shade: 0.18f);
            _btnDown = Rounded(32, 9, AccentDark, new Color(1f, 0.8f, 0.5f), 1, shade: -0.1f);
            _btnOff = Rounded(32, 9, new Color(0.35f, 0.32f, 0.3f, 0.8f), new Color(0.6f, 0.55f, 0.5f, 0.4f), 1);
            _tabOn = Rounded(32, 9, new Color(1f, 0.64f, 0.22f, 0.95f), new Color(1f, 0.9f, 0.65f), 1);
            _tabOff = Rounded(32, 9, new Color(1f, 0.9f, 0.75f, 0.08f), new Color(1f, 0.85f, 0.6f, 0.25f), 1);
            _white = Texture2D.whiteTexture;

            _skin = ScriptableObject.CreateInstance<GUISkin>();
            var baseSkin = GUI.skin;
            _skin.font = baseSkin.font;
            _skin.settings.cursorColor = Accent;
            _skin.settings.selectionColor = new Color(1f, 0.64f, 0.22f, 0.4f);

            _skin.label = new GUIStyle(baseSkin.label) { fontSize = 13, wordWrap = false };
            _skin.label.normal.textColor = Text;
            _skin.box = new GUIStyle(baseSkin.box) { border = new RectOffset(16, 16, 16, 16), padding = new RectOffset(14, 14, 12, 12) };
            _skin.box.normal.background = _panel; _skin.box.normal.textColor = Text; // menus use boxes as backdrops: keep them dark
            CardStyle = new GUIStyle(_skin.box) { border = new RectOffset(12, 12, 12, 12), padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(2, 2, 3, 3) };
            CardStyle.normal.background = _card;
            _skin.window = new GUIStyle(_skin.box);
            _skin.window.normal.background = _panel;

            _skin.button = new GUIStyle(baseSkin.button)
            {
                fontSize = 15, fontStyle = FontStyle.Bold, border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(12, 12, 6, 7), margin = new RectOffset(4, 4, 4, 4), alignment = TextAnchor.MiddleCenter,
            };
            SetStates(_skin.button, _btn, _btnHover, _btnDown, new Color(0.18f, 0.1f, 0.04f));
            _skin.button.onNormal.background = _btnDown; _skin.button.onNormal.textColor = new Color(0.18f, 0.1f, 0.04f);

            _skin.toggle = new GUIStyle(baseSkin.toggle) { fontSize = 15 };
            _skin.toggle.normal.textColor = _skin.toggle.onNormal.textColor = Text;
            _skin.toggle.hover.textColor = _skin.toggle.onHover.textColor = Color.white;

            _skin.textField = new GUIStyle(baseSkin.textField) { fontSize = 15, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(8, 8, 5, 5) };
            var field = Rounded(24, 7, new Color(0f, 0f, 0f, 0.45f), new Color(1f, 0.8f, 0.5f, 0.4f), 1);
            _skin.textField.normal.background = _skin.textField.hover.background = _skin.textField.focused.background = field;
            _skin.textField.normal.textColor = _skin.textField.hover.textColor = _skin.textField.focused.textColor = Text;
            _skin.textArea = new GUIStyle(_skin.textField) { wordWrap = true };

            _skin.horizontalSlider = new GUIStyle(baseSkin.horizontalSlider);
            _skin.horizontalSliderThumb = new GUIStyle(baseSkin.horizontalSliderThumb);
            _skin.verticalScrollbar = new GUIStyle(baseSkin.verticalScrollbar) { fixedWidth = 10, border = new RectOffset(5, 5, 5, 5) };
            _skin.verticalScrollbar.normal.background = Rounded(12, 5, new Color(0f, 0f, 0f, 0.3f), Color.clear, 0);
            _skin.verticalScrollbarThumb = new GUIStyle(baseSkin.verticalScrollbarThumb) { fixedWidth = 10, border = new RectOffset(5, 5, 5, 5) };
            _skin.verticalScrollbarThumb.normal.background = Rounded(12, 5, new Color(1f, 0.7f, 0.35f, 0.7f), Color.clear, 0);
            _skin.verticalScrollbarUpButton = new GUIStyle(baseSkin.verticalScrollbarUpButton) { fixedHeight = 0 };
            _skin.verticalScrollbarDownButton = new GUIStyle(baseSkin.verticalScrollbarDownButton) { fixedHeight = 0 };
            _skin.horizontalScrollbar = new GUIStyle(baseSkin.horizontalScrollbar);
            _skin.horizontalScrollbarThumb = new GUIStyle(baseSkin.horizontalScrollbarThumb);
            _skin.horizontalScrollbarLeftButton = new GUIStyle(baseSkin.horizontalScrollbarLeftButton);
            _skin.horizontalScrollbarRightButton = new GUIStyle(baseSkin.horizontalScrollbarRightButton);
            _skin.scrollView = new GUIStyle(baseSkin.scrollView);
            _skin.customStyles = baseSkin.customStyles;

            Title = Style(26, FontStyle.Bold, Text);
            Header = Style(17, FontStyle.Bold, Text);
            Body = Style(13, FontStyle.Normal, Muted); Body.wordWrap = true;
            Small = Style(13, FontStyle.Bold, Text);
            Price = Style(16, FontStyle.Bold, Gold); Price.alignment = TextAnchor.MiddleRight;
            Center = Style(15, FontStyle.Bold, Text); Center.alignment = TextAnchor.MiddleCenter;
            HudBig = Style(26, FontStyle.Bold, Text); HudBig.alignment = TextAnchor.MiddleRight;
            HudLabel = Style(14, FontStyle.Bold, Text); HudLabel.alignment = TextAnchor.MiddleLeft;
            Key = Style(12, FontStyle.Bold, new Color(0.18f, 0.1f, 0.04f)); Key.alignment = TextAnchor.MiddleCenter;
            Key.normal.background = Rounded(20, 5, new Color(1f, 0.9f, 0.72f), new Color(1f, 1f, 1f, 0.8f), 1);
            Key.border = new RectOffset(6, 6, 6, 6);
        }

        static GUIStyle Style(int size, FontStyle fs, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, richText = true };
            s.normal.textColor = c;
            return s;
        }

        static void SetStates(GUIStyle s, Texture2D n, Texture2D h, Texture2D d, Color text)
        {
            s.normal.background = n; s.hover.background = h; s.active.background = d; s.focused.background = n;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
        }

        // ------------------------------------------------------------------ drawing helpers

        public static void Panel(Rect r) { if (_skin == null) Build(); if (Event.current.type == EventType.Repaint) Nine(r, _panel, 16); }
        public static void Card(Rect r) { if (_skin == null) Build(); if (Event.current.type == EventType.Repaint) Nine(r, _card, 12); }
        public static void Pill(Rect r) { if (_skin == null) Build(); if (Event.current.type == EventType.Repaint) Nine(r, _pill, 15); }

        /// <summary>A rounded bar with a track, fill and an optional label inside.</summary>
        public static void Bar(Rect r, float fraction, Color fill, string label = null, GUIStyle style = null)
        {
            if (_skin == null) Build();
            if (Event.current.type == EventType.Repaint)
            {
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f * prev.a);
                Nine(r, _bar, 7);
                fraction = Mathf.Clamp01(fraction);
                if (fraction > 0.001f)
                {
                    GUI.color = new Color(fill.r, fill.g, fill.b, fill.a * prev.a);
                    var f = new Rect(r.x + 2, r.y + 2, Mathf.Max(r.height - 4, (r.width - 4) * fraction), r.height - 4);
                    Nine(f, _bar, 6);
                    GUI.color = new Color(1f, 1f, 1f, 0.18f * prev.a); // gloss
                    GUI.DrawTexture(new Rect(f.x + 4, f.y + 1, Mathf.Max(0f, f.width - 8), Mathf.Max(1f, f.height * 0.3f)), _white);
                }
                GUI.color = prev;
            }
            if (!string.IsNullOrEmpty(label)) Shadow(new Rect(r.x + 8, r.y, r.width - 16, r.height), label, style ?? HudLabel);
        }

        /// <summary>A small key-cap ("Q") followed by text, e.g. for control hints.</summary>
        public static float KeyCap(float x, float y, string key, float h = 20f)
        {
            if (_skin == null) Build();
            float w = Mathf.Max(h, Key.CalcSize(new GUIContent(key)).x + 10f);
            GUI.Label(new Rect(x, y, w, h), key, Key);
            return w;
        }

        public static void Shadow(Rect r, string text, GUIStyle style)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, prev.a * 0.7f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = prev;
            GUI.Label(r, text, style);
        }

        /// <summary>A tab-style toggle button. Returns true if clicked.</summary>
        public static bool Tab(string text, bool on, params GUILayoutOption[] opts)
        {
            if (_skin == null) Build();
            var s = new GUIStyle(_skin.button);
            s.normal.background = s.hover.background = on ? _tabOn : _tabOff;
            s.active.background = _tabOn;
            s.normal.textColor = s.hover.textColor = on ? new Color(0.18f, 0.1f, 0.04f) : Text;
            return GUILayout.Button(text, s, opts);
        }

        /// <summary>Disabled-looking button (GUI.enabled = false still applies the grey).</summary>
        public static bool Button(string text, bool enabled, params GUILayoutOption[] opts)
        {
            if (_skin == null) Build();
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            var s = _skin.button;
            if (!enabled) { s = new GUIStyle(_skin.button); s.normal.background = s.hover.background = s.active.background = _btnOff; s.normal.textColor = s.hover.textColor = Muted; }
            bool clicked = GUILayout.Button(text, s, opts);
            GUI.enabled = was;
            return clicked;
        }

        static void Nine(Rect r, Texture2D tex, int border)
        {
            // GUI.DrawTexture can't nine-slice; use a style background instead.
            var s = new GUIStyle { border = new RectOffset(border, border, border, border) };
            s.normal.background = tex;
            s.Draw(r, false, false, false, false);
        }

        // ------------------------------------------------------------------ textures

        /// <summary>Anti-aliased rounded rectangle with an optional border and a vertical shade.</summary>
        static Texture2D Rounded(int size, int radius, Color fill, Color edge, int edgeWidth, float shade = 0f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rect edge (negative inside).
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                    float inside = Mathf.Clamp01(0.5f - d);
                    float edgeK = edgeWidth > 0 ? Mathf.Clamp01(d + edgeWidth + 0.5f) : 0f;
                    var c = Color.Lerp(fill, edge, edgeK * edge.a > 0f ? edgeK : 0f);
                    if (edgeWidth > 0 && edgeK > 0f) c.a = Mathf.Lerp(fill.a, edge.a, edgeK);
                    if (shade != 0f) { float k = (y / (float)size - 0.5f) * shade; c = new Color(Mathf.Clamp01(c.r + k), Mathf.Clamp01(c.g + k), Mathf.Clamp01(c.b + k), c.a); }
                    c.a *= inside;
                    px[y * size + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
