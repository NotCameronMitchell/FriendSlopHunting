using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Shared state for full-screen menus (shop, inventory): only one at a time, cursor freed
    /// while open, and gameplay input paused.
    /// </summary>
    public static class GameMenus
    {
        public enum Menu { None, Shop, Inventory }

        public static Menu Open { get; private set; } = Menu.None;
        public static bool AnyOpen => Open != Menu.None;

        public static void Show(Menu menu)
        {
            Open = menu;
            bool free = menu != Menu.None;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        public static void Toggle(Menu menu) => Show(Open == menu ? Menu.None : menu);

        public static void Close() => Show(Menu.None);

        /// <summary>Reset without touching the cursor (e.g. when leaving a session).</summary>
        public static void Reset() => Open = Menu.None;
    }
}
