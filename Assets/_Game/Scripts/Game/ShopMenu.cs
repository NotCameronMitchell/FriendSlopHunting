using FriendSlop.Player;
using FriendSlop.Weapons;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// The gun shop, opened at the gunsmith: weapons and the tank, snacks, and upgrades. Everything is
    /// paid from the team pot; weapons and snacks go into YOUR inventory.
    /// </summary>
    public class ShopMenu : MonoBehaviour
    {
        enum Tab { Weapons, Snacks, Upgrades }
        Tab _tab;
        public static int RequestedTab = -1; // editor tests: open on a given tab
        Vector2 _scroll;

        public static bool IsOpen => GameMenus.Open == GameMenus.Menu.Shop;
        public static void Open() => GameMenus.Show(GameMenus.Menu.Shop);
        public static void Close() => GameMenus.Close();

        void OnGUI()
        {
            if (!IsOpen) return;
            var state = GameState.Instance;
            var weapons = PlayerWeapons.Local;
            if (state == null || weapons == null || weapons.Catalog == null) { GameMenus.Reset(); return; }
            if (RequestedTab >= 0) { _tab = (Tab)RequestedTab; RequestedTab = -1; }
            GUI.skin = UiTheme.Skin;

            float w = Mathf.Min(720f, Screen.width - 40f), h = Mathf.Min(700f, Screen.height - 60f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            UiTheme.Panel(area);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, w - 40, h - 32));

            // Header: title + team money chip.
            GUILayout.BeginHorizontal();
            GUILayout.Label("GUN SHOP", UiTheme.Title);
            GUILayout.FlexibleSpace();
            var chip = GUILayoutUtility.GetRect(new GUIContent($"${state.Money.Value:N0}"), UiTheme.Header, GUILayout.Width(180), GUILayout.Height(34));
            UiTheme.Pill(chip);
            GUI.Label(new Rect(chip.x + 12, chip.y, 60, chip.height), "TEAM", UiTheme.Small);
            var money = new GUIStyle(UiTheme.Price) { fontSize = 18 };
            GUI.Label(new Rect(chip.x, chip.y, chip.width - 14, chip.height), $"${state.Money.Value:N0}", money);
            GUILayout.EndHorizontal();
            GUILayout.Label("Paid from the team pot. Weapons and snacks go into YOUR pockets: drop guns with G to share.", UiTheme.Body);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (UiTheme.Tab("Weapons & Vehicles", _tab == Tab.Weapons, GUILayout.Height(32))) _tab = Tab.Weapons;
            if (UiTheme.Tab("Snacks", _tab == Tab.Snacks, GUILayout.Height(32))) _tab = Tab.Snacks;
            if (UiTheme.Tab("Upgrades", _tab == Tab.Upgrades, GUILayout.Height(32))) _tab = Tab.Upgrades;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Weapons: DrawWeapons(state, weapons); break;
                case Tab.Snacks: DrawSnacks(state); break;
                case Tab.Upgrades: DrawUpgrades(state); break;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            if (FriendSlop.Game.UiTheme.Btn("Close  (E / Esc)", GUILayout.Height(34))) Close();
            GUILayout.EndArea();
        }

        /// <summary>One shop row: a card with name + blurb on the left, price + action on the right.</summary>
        static bool Row(string name, string blurb, string price, string action, bool canAct, string tag = null)
        {
            GUILayout.BeginHorizontal(UiTheme.CardStyle);
            GUILayout.BeginVertical();
            GUILayout.Label(tag == null ? name : $"{name}  <color=#ffcf5a><size=12>{tag}</size></color>", UiTheme.Header);
            GUILayout.Label(blurb, UiTheme.Body);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(140));
            GUILayout.Label(price, UiTheme.Price);
            bool clicked = UiTheme.Button(action, canAct, GUILayout.Height(30));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return clicked;
        }

        void DrawWeapons(GameState state, PlayerWeapons weapons)
        {
            bool haveTank = FriendSlop.World.Tank.All.Count > 0;
            int myLevel = FriendSlop.Profile.PlayerProfile.Current.Level;
            bool tankLocked = !haveTank && myLevel < GameState.TankLevel;
            if (Row("Tank", "Seats a driver, a gunner and two riders. Explosive cannon. Flattens anything it runs over. Rolls out at the garage.",
                    haveTank ? "Owned" : $"${GameState.TankPrice:N0}", haveTank ? "Recall" : tankLocked ? $"Lv {GameState.TankLevel}" : "Buy",
                    !tankLocked && (haveTank || state.Money.Value >= GameState.TankPrice), tankLocked ? $"VEHICLE · UNLOCKS AT LV {GameState.TankLevel}" : "VEHICLE"))
                state.RequestTank();

            var catalog = weapons.Catalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                var def = catalog.Get(i);
                if (def == null) continue;
                bool owned = weapons.Owns(i);
                bool locked = !owned && myLevel < def.requiredLevel;
                string action = owned ? (i == 0 ? "Always yours" : "In your bag") : locked ? $"Lv {def.requiredLevel}" : "Buy";
                if (Row(def.displayName, def.blurb, def.price == 0 ? "Free" : $"${def.price:N0}", action, !owned && !locked && state.Money.Value >= def.price,
                        locked ? $"UNLOCKS AT LV {def.requiredLevel}" : null))
                    state.RequestBuyWeapon(i);
            }
        }

        void DrawSnacks(GameState state)
        {
            var items = PlayerItems.Local;
            GUILayout.Label("One-time use. Eat or drink them any time from your pockets.", UiTheme.Body);
            if (items == null) return;
            for (int k = 0; k < PlayerItems.Items.Length; k++)
            {
                var info = PlayerItems.Items[k];
                var kind = (ItemKind)k;
                int have = items.Count(kind);
                var key = FriendSlop.Settings.Controls.Label(kind == ItemKind.ProteinBar ? FriendSlop.Settings.GameAction.UseProteinBar : FriendSlop.Settings.GameAction.UseEnergyDrink);
                bool full = have >= info.Max;
                if (Row(info.Name, $"{info.Blurb}  Use with [{key}].", $"${info.Price}", full ? "Pockets full" : "Buy",
                        !full && state.Money.Value >= info.Price, $"{have}/{info.Max} in pockets"))
                    items.RequestBuy(kind);
            }
        }

        /// <summary>Upgrades for this hunt: team money, gated by YOUR hunter level.</summary>
        void DrawUpgrades(GameState state)
        {
            var up = PlayerUpgrades.Local;
            int level = FriendSlop.Profile.PlayerProfile.Current.Level;
            GUILayout.Label($"Your hunter level: {level}. Upgrades last this hunt.", UiTheme.Body);
            if (up == null) return;
            for (int t = 0; t < PlayerUpgrades.Tracks.Length; t++)
            {
                var info = PlayerUpgrades.Tracks[t];
                int tier = up.Tier((UpgradeTrack)t);
                string pips = new string('■', tier) + new string('□', info.MaxTier - tier);
                if (tier >= info.MaxTier) { Row(info.Name, info.Description, "Maxed", "Maxed", false, pips); continue; }
                int need = info.Levels[tier], cost = info.Costs[tier];
                bool locked = level < need;
                if (Row(info.Name, info.Description, $"{PlayerUpgrades.Roman(tier + 1)}  ${cost:N0}", locked ? $"Needs Lv {need}" : "Upgrade",
                        !locked && state.Money.Value >= cost, pips))
                    up.RequestBuy((UpgradeTrack)t);
            }
        }
    }
}
