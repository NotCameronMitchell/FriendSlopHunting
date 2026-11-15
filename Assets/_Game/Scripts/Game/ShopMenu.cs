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
        bool _upgradesTab;
        Vector2 _upScroll;

        /// <summary>Upgrades for this hunt: team money, gated by YOUR hunter level.</summary>
        void DrawUpgrades(GameState state)
        {
            var up = FriendSlop.Player.PlayerUpgrades.Local;
            int level = FriendSlop.Profile.PlayerProfile.Current.Level;
            GUILayout.Label($"Your hunter level: {level}. Upgrades last this hunt and are paid from the team pot.", _blurb);
            if (up == null) return;
            _upScroll = GUILayout.BeginScrollView(_upScroll);
            for (int t = 0; t < FriendSlop.Player.PlayerUpgrades.Tracks.Length; t++)
            {
                var info = FriendSlop.Player.PlayerUpgrades.Tracks[t];
                int tier = up.Tier((FriendSlop.Player.UpgradeTrack)t);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                string pips = new string('#', tier) + new string('-', info.MaxTier - tier);
                GUILayout.Label($"{info.Name}   [{pips}]", _name);
                GUILayout.Label(info.Description, _blurb);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(150));
                if (tier >= info.MaxTier)
                {
                    GUILayout.Label("Maxed", _price);
                }
                else
                {
                    int need = info.Levels[tier], cost = info.Costs[tier];
                    GUILayout.Label($"{FriendSlop.Player.PlayerUpgrades.Roman(tier + 1)}  ${cost:N0}", _price);
                    if (level < need) { GUI.enabled = false; GUILayout.Button($"Needs Lv {need}", GUILayout.Height(26)); GUI.enabled = true; }
                    else
                    {
                        GUI.enabled = state.Money.Value >= cost;
                        if (GUILayout.Button("Upgrade", GUILayout.Height(26))) up.RequestBuy((FriendSlop.Player.UpgradeTrack)t);
                        GUI.enabled = true;
                    }
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

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
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!_upgradesTab, "  Weapons", GUI.skin.button, GUILayout.Height(28))) _upgradesTab = false;
            if (GUILayout.Toggle(_upgradesTab, "  Upgrades", GUI.skin.button, GUILayout.Height(28))) _upgradesTab = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_upgradesTab) DrawUpgrades(state);
            else
            {
            // Vehicles
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label("Tank (drivable)", _name);
            GUILayout.Label("Seats a driver, a gunner and two riders. Explosive cannon. Flattens anything it runs over. Rolls out at the garage.", _blurb);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(120));
            bool haveTank = FriendSlop.World.Tank.All.Count > 0;
            GUILayout.Label(haveTank ? "Owned" : $"${GameState.TankPrice:N0}", _price);
            GUI.enabled = haveTank || state.Money.Value >= GameState.TankPrice;
            if (GUILayout.Button(haveTank ? "Recall" : "Buy", GUILayout.Height(26))) state.RequestTank();
            GUI.enabled = true;
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

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
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Close (E / Esc)", GUILayout.Height(30))) Close();
            GUILayout.EndArea();
        }
    }
}
