using FriendSlop.Weapons;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Placeholder IMGUI inventory (Tab): your weapons, which slot key equips each, and buttons to
    /// equip or drop. Dropped weapons land in front of you for anyone to pick up (E).
    /// </summary>
    public class InventoryMenu : MonoBehaviour
    {
        public static bool IsOpen => GameMenus.Open == GameMenus.Menu.Inventory;

        GUIStyle _title, _name, _blurb, _stat;
        Vector2 _scroll;

        public static void Toggle() => GameMenus.Toggle(GameMenus.Menu.Inventory);

        void OnGUI()
        {
            GUI.skin = FriendSlop.Game.UiTheme.Skin;
            if (!IsOpen) return;
            var weapons = PlayerWeapons.Local;
            if (weapons == null || weapons.Catalog == null) { GameMenus.Reset(); return; }

            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            _name ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _blurb ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _stat ??= new GUIStyle(GUI.skin.label) { fontSize = 12 };

            var slots = weapons.Slots();
            float w = 560f, h = Mathf.Min(140f + slots.Count * 78f, Screen.height - 80f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, w - 32, h - 24));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Inventory", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{slots.Count} weapon{(slots.Count == 1 ? "" : "s")}", _name);
            GUILayout.EndHorizontal();
            GUILayout.Label("Give a gun to a friend: drop it (G or Drop) near them and they press E on it.", _blurb);
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int s = 0; s < slots.Count; s++)
            {
                int index = slots[s];
                var def = weapons.Catalog.Get(index);
                bool equipped = weapons.Equipped.Value == index;

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"[{WeaponCatalog.KeyLabel(s)}]", _name, GUILayout.Width(58));
                GUILayout.BeginVertical();
                GUILayout.Label(equipped ? $"{def.displayName}  (equipped)" : def.displayName, _name);
                GUILayout.Label(Stats(def), _stat);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(90));
                GUI.enabled = !equipped;
                if (FriendSlop.Game.UiTheme.Btn("Equip", GUILayout.Height(24))) weapons.RequestEquip(index);
                GUI.enabled = index != 0;
                if (FriendSlop.Game.UiTheme.Btn(index == 0 ? "Permanent" : "Drop", GUILayout.Height(24))) weapons.RequestDrop(index);
                GUI.enabled = true;
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (FriendSlop.Game.UiTheme.Btn("Close (Tab / Esc)", GUILayout.Height(28))) GameMenus.Close();
            GUILayout.EndArea();
            DrawFishLog(area);
        }

        /// <summary>Catch log beside the inventory: every species, how many you've landed, and your personal best.</summary>
        void DrawFishLog(Rect beside)
        {
            var prof = FriendSlop.Profile.PlayerProfile.Current;
            float w = 260f, h = 90f + FriendSlop.Fishing.FishSpecies.All.Length * 24f;
            var area = new Rect(beside.xMax + 12f, beside.y, w, h);
            if (area.xMax > Screen.width - 8f) return; // no room on tiny screens
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14, area.y + 10, w - 28, h - 20));
            GUILayout.Label("Fish log", _title);
            GUILayout.Label(prof.fishCaught == 0 ? "Buy a Fishing Rod and cast into any lake or stream."
                : $"{prof.fishCaught} caught · best: {prof.biggestFishKg:0.0} kg {prof.biggestFish}", _blurb);
            foreach (var sp in FriendSlop.Fishing.FishSpecies.All)
            {
                int n = 0;
                foreach (var c in prof.fishBySpecies) if (c.species == sp.Name) n = c.count;
                var prev = GUI.color;
                GUI.color = n > 0 ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                GUILayout.Label(n > 0 ? $"{sp.Name}  ×{n}" : "???", _stat);
                GUI.color = prev;
            }
            GUILayout.EndArea();
        }

        static string Stats(WeaponDefinition d)
        {
            if (d.kind == WeaponKind.Rod) return "Fishing · cast at water, click on the bite, hold to reel";
            if (d.kind == WeaponKind.Call) return $"Animal call · lures animals within {d.noiseRadius:0} m · {d.fireInterval:0} s cooldown";
            string fire = d.IsAuto ? "auto" : "semi";
            string dmg = d.pellets > 1 ? $"{d.damage:0}×{d.pellets}" : $"{d.damage:0}";
            if (d.IsExplosive) dmg += $" + {d.explosionDamage:0} blast";
            string extra = d.noiseRadius <= 0f ? " · silent" : "";
            return $"{d.kind} · {fire} · dmg {dmg} · {1f / Mathf.Max(0.01f, d.fireInterval):0.#}/s{extra}";
        }
    }
}
