using UnityEngine;

namespace FriendSlop.Profile
{
    /// <summary>
    /// Home-menu character screen: name, hats, accessories, colors (equip / buy with career
    /// bucks / see which achievement unlocks it) and an achievements list, with a live preview.
    /// Placeholder IMGUI until the real UI pass.
    /// </summary>
    public class CustomizeMenu : MonoBehaviour
    {
        enum Tab { Outfits, Faces, Hats, Accessories, Colors, Achievements }

        public static bool IsOpen { get; set; }

        Tab _tab;
        Vector2 _scroll;
        bool _dragging;
        string _nameField;
        GUIStyle _title, _label, _small, _bucks, _item;

        void OnGUI()
        {
            GUI.skin = FriendSlop.Game.UiTheme.Skin;
            if (!IsOpen) return;
            var p = PlayerProfile.Current;
            _nameField ??= p.displayName;
            EnsureStyles();

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Min(1100f, Screen.width - 40f), h = Mathf.Min(680f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);

            // ---------------- left: options ----------------
            float leftW = w * 0.58f;
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, leftW - 30, h - 32));
            GUILayout.Label("Your Hunter", _title);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _label, GUILayout.Width(60));
            string typed = GUILayout.TextField(_nameField ?? "", 24, GUILayout.Width(260), GUILayout.Height(26));
            if (typed != _nameField) _nameField = typed;
            if (GUILayout.Button("Save", GUILayout.Width(70), GUILayout.Height(26)))
            {
                p.displayName = _nameField.Trim();
                p.Save();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrWhiteSpace(p.displayName) ? $"Friends see: {p.EffectiveName} (Steam name)" : $"Friends see: {p.EffectiveName}", _small);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            TabButton(Tab.Outfits, "Outfits");
            TabButton(Tab.Faces, "Faces");
            TabButton(Tab.Hats, "Hats");
            TabButton(Tab.Accessories, "Gear");
            TabButton(Tab.Colors, "Colors");
            TabButton(Tab.Achievements, "Trophies");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Outfits: DrawItems(CosmeticSlot.Outfit, p); break;
                case Tab.Faces: DrawItems(CosmeticSlot.Head, p); break;
                case Tab.Hats: DrawItems(CosmeticSlot.Hat, p); break;
                case Tab.Accessories: DrawItems(CosmeticSlot.Accessory, p); break;
                case Tab.Colors: DrawItems(CosmeticSlot.Color, p); break;
                case Tab.Achievements: DrawAchievements(p); break;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("Back", GUILayout.Height(34), GUILayout.Width(140))) IsOpen = false;
            GUILayout.EndArea();

            // ---------------- right: preview + wallet ----------------
            var right = new Rect(area.x + leftW, area.y + 16, w - leftW - 20, h - 32);
            GUI.Label(new Rect(right.x, right.y, right.width, 40), $"{p.bucks:N0} bucks", _bucks);
            GUI.Label(new Rect(right.x, right.y + 34, right.width, 22), $"You bank {PlayerProfile.BucksPerDollar:P0} of every sale your team makes.", _small);

            var preview = CharacterPreview.Instance;
            var imgRect = new Rect(right.x, right.y + 62, right.width, right.height - 90);
            if (preview != null)
            {
                var tex = preview.Render(p.outfit, p.head, p.hat, p.accessory, p.colorIndex);
                float aspect = 512f / 640f;
                float iw = Mathf.Min(imgRect.width, imgRect.height * aspect);
                float ih = iw / aspect;
                var r = new Rect(imgRect.x + (imgRect.width - iw) / 2f, imgRect.y, iw, ih);
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                HandleDrag(r, preview);
                GUI.Label(new Rect(r.x, r.yMax + 4, r.width, 20), "Drag to spin", _small);
            }
        }

        void DrawItems(CosmeticSlot slot, PlayerProfile p)
        {
            foreach (var item in Cosmetics.Items)
            {
                if (item.Slot != slot) continue;
                bool unlocked = Cosmetics.IsUnlocked(item, p);
                bool equipped = IsEquipped(item, p);

                GUILayout.BeginHorizontal(GUI.skin.box);
                if (slot == CosmeticSlot.Color)
                {
                    var prev = GUI.color;
                    GUI.color = item.Color;
                    GUILayout.Box(Texture2D.whiteTexture, GUILayout.Width(28), GUILayout.Height(28));
                    GUI.color = prev;
                }
                GUILayout.BeginVertical();
                GUILayout.Label(item.Name, _item);
                GUILayout.Label(unlocked ? (equipped ? "Wearing" : "Owned") : Cosmetics.HowToUnlock(item), _small);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();

                if (equipped)
                {
                    GUI.enabled = false;
                    GUILayout.Button("Equipped", GUILayout.Width(120), GUILayout.Height(30));
                    GUI.enabled = true;
                }
                else if (unlocked)
                {
                    if (GUILayout.Button("Equip", GUILayout.Width(120), GUILayout.Height(30))) Equip(item, p);
                }
                else if (item.Unlock == UnlockKind.Bucks)
                {
                    GUI.enabled = p.bucks >= item.Cost;
                    if (GUILayout.Button($"Buy {item.Cost:N0}", GUILayout.Width(120), GUILayout.Height(30)) && p.TrySpend(item.Cost))
                    {
                        p.Grant(item.Id);
                        Equip(item, p);
                    }
                    GUI.enabled = true;
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button("Locked", GUILayout.Width(120), GUILayout.Height(30));
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }

            if (slot == CosmeticSlot.Color)
            {
                GUILayout.Space(4);
                bool auto = p.colorIndex < 0;
                GUI.enabled = !auto;
                if (GUILayout.Button(auto ? "Auto color (current)" : "Use auto color", GUILayout.Width(200), GUILayout.Height(28)))
                {
                    p.colorIndex = -1;
                    p.Save();
                }
                GUI.enabled = true;
            }
        }

        void DrawAchievements(PlayerProfile p)
        {
            GUILayout.Label($"Kills {p.kills} · Headshots {p.headshots} · Bosses {p.bossKills} · Team earnings ${p.moneyEarnedWithTeam:N0}", _small);
            GUILayout.Space(4);
            foreach (var a in Cosmetics.Achievements)
            {
                bool done = p.achievements.Contains(a.Id);
                var reward = string.IsNullOrEmpty(a.RewardId) ? null : Cosmetics.Find(a.RewardId);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUI.color = done ? new Color(0.6f, 1f, 0.6f) : Color.white;
                GUILayout.Label(done ? "✔" : "•", _item, GUILayout.Width(24));
                GUI.color = Color.white;
                GUILayout.BeginVertical();
                GUILayout.Label(a.Name, _item);
                GUILayout.Label(a.Description + (reward != null ? $"  Reward: {reward.Name}" : ""), _small);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(done ? "Done" : a.Progress(p), _label, GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }
        }

        static bool IsEquipped(CosmeticItem item, PlayerProfile p) => item.Slot switch
        {
            CosmeticSlot.Hat => p.hat == item.Id,
            CosmeticSlot.Outfit => p.outfit == item.Id,
            CosmeticSlot.Head => p.head == item.Id,
            CosmeticSlot.Accessory => p.accessory == item.Id,
            _ => p.colorIndex >= 0 && item.Id == $"color_{p.colorIndex}",
        };

        static void Equip(CosmeticItem item, PlayerProfile p)
        {
            switch (item.Slot)
            {
                case CosmeticSlot.Hat: p.hat = item.Id; break;
                case CosmeticSlot.Outfit: p.outfit = item.Id; break;
                case CosmeticSlot.Head: p.head = item.Id; break;
                case CosmeticSlot.Accessory: p.accessory = item.Id; break;
                default: p.colorIndex = Cosmetics.Items.FindAll(i => i.Slot == CosmeticSlot.Color).IndexOf(item); break;
            }
            p.Save();
        }

        void HandleDrag(Rect r, CharacterPreview preview)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { _dragging = true; e.Use(); }
            else if (e.type == EventType.MouseUp) _dragging = false;
            else if (e.type == EventType.MouseDrag && _dragging) { preview.Spin(-e.delta.x * 0.6f); e.Use(); }
        }

        void TabButton(Tab tab, string label)
        {
            GUI.color = _tab == tab ? new Color(1f, 0.85f, 0.4f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(30))) { _tab = tab; _scroll = Vector2.zero; }
            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _item ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            _bucks ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
        }
    }
}
