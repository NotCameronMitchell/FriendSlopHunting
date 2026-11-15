using FriendSlop.Weapons;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Placeholder IMGUI gun shop, opened at the gunsmith. Buying spends the team's shared money
    /// and puts the weapon in *your* inventory. Replaced by real UI in the menus milestone.
    /// </summary>
    public class ShopMenu : MonoBehaviour
    {
        public static bool IsOpen => GameMenus.Open == GameMenus.Menu.Shop;

        Vector2 _scroll;
        GUIStyle _title, _name, _blurb, _price;

        public static void Open() => GameMenus.Show(GameMenus.Menu.Shop);
        public static void Close() => GameMenus.Close();

        void OnGUI()
        {
            if (!IsOpen) return;
            var state = GameState.Instance;
            var weapons = PlayerWeapons.Local;
            if (state == null || weapons == null || weapons.Catalog == null) { GameMenus.Reset(); return; }

            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            _name ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _blurb ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _price ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };

            float w = 640f, h = Mathf.Min(660f, Screen.height - 80f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 16, area.y + 12, w - 32, h - 24));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gun Shop", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Team money: ${state.Money.Value:N0}", _name);
            GUILayout.EndHorizontal();
            GUILayout.Label("Unlimited ammo. Paid from the team pot, goes into YOUR inventory (Tab). Drop with G to give it to a friend.", _blurb);
            GUILayout.Space(6);

            var catalog = weapons.Catalog;
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < catalog.Count; i++)
            {
                var def = catalog.Get(i);
                if (def == null) continue;
                bool owned = weapons.Owns(i);

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label(def.displayName, _name);
                GUILayout.Label(def.blurb, _blurb);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(120));
                GUILayout.Label(def.price == 0 ? "Free" : $"${def.price:N0}", _price);
                if (owned)
                {
                    GUI.enabled = false;
                    GUILayout.Button(i == 0 ? "Always yours" : "In your bag", GUILayout.Height(26));
                    GUI.enabled = true;
                }
                else
                {
                    GUI.enabled = state.Money.Value >= def.price;
                    if (GUILayout.Button("Buy", GUILayout.Height(26))) state.RequestBuyWeapon(i);
                    GUI.enabled = true;
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("Close (E / Esc)", GUILayout.Height(30))) Close();
            GUILayout.EndArea();
        }
    }
}
