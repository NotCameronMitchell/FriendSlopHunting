using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FriendSlop.Settings
{
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight,
        Jump, Sprint,
        Fire, Aim,
        Interact, DropWeapon, Inventory,
        PushToTalk, ToggleMute,
        Pause,
        UseProteinBar, UseEnergyDrink, Map,
        Chat, Emote,
    }

    /// <summary>
    /// Rebindable controls. Every gameplay input goes through here instead of hard-coded keys.
    /// A binding is a keyboard key or a mouse button; bindings are saved on this PC.
    /// Weapon slots (1–0) and the scroll wheel stay fixed.
    /// </summary>
    public static class Controls
    {
        const string Prefix = "fsh.bind.";

        public struct Binding
        {
            public bool IsMouse;
            public Key Key;
            public int MouseButton; // 0 left, 1 right, 2 middle, 3 back, 4 forward

            public static Binding K(Key key) => new Binding { Key = key };
            public static Binding M(int button) => new Binding { IsMouse = true, MouseButton = button };

            public override string ToString() => IsMouse ? $"m:{MouseButton}" : $"k:{Key}";

            public static bool TryParse(string s, out Binding b)
            {
                b = default;
                if (string.IsNullOrEmpty(s) || s.Length < 3) return false;
                if (s.StartsWith("m:") && int.TryParse(s.Substring(2), out int mb)) { b = M(mb); return true; }
                if (s.StartsWith("k:") && Enum.TryParse(s.Substring(2), out Key k)) { b = K(k); return true; }
                return false;
            }

            public string Label
            {
                get
                {
                    if (IsMouse)
                        return MouseButton switch { 0 => "Left Mouse", 1 => "Right Mouse", 2 => "Middle Mouse", 3 => "Mouse 4", 4 => "Mouse 5", _ => $"Mouse {MouseButton}" };
                    return Key switch
                    {
                        Key.LeftShift => "Left Shift", Key.RightShift => "Right Shift",
                        Key.LeftCtrl => "Left Ctrl", Key.RightCtrl => "Right Ctrl",
                        Key.LeftAlt => "Left Alt", Key.RightAlt => "Right Alt",
                        Key.Space => "Space", Key.Escape => "Esc", Key.Tab => "Tab",
                        _ => Key.ToString(),
                    };
                }
            }
        }

        public static readonly IReadOnlyDictionary<GameAction, string> DisplayNames = new Dictionary<GameAction, string>
        {
            { GameAction.MoveForward, "Move forward" }, { GameAction.MoveBack, "Move back" },
            { GameAction.MoveLeft, "Move left" }, { GameAction.MoveRight, "Move right" },
            { GameAction.Jump, "Jump" }, { GameAction.Sprint, "Sprint" },
            { GameAction.Fire, "Shoot / throw" }, { GameAction.Aim, "Aim" },
            { GameAction.Interact, "Interact / pick up" }, { GameAction.DropWeapon, "Drop weapon" },
            { GameAction.Inventory, "Inventory" },
            { GameAction.PushToTalk, "Push to talk" }, { GameAction.ToggleMute, "Mute / unmute mic" },
            { GameAction.Pause, "Pause menu" },
            { GameAction.UseProteinBar, "Eat a protein bar" }, { GameAction.UseEnergyDrink, "Drink an energy drink" },
            { GameAction.Map, "Map" },
            { GameAction.Chat, "Team chat" },
            { GameAction.Emote, "Emote (dance / cheer)" },
        };

        static readonly Dictionary<GameAction, Binding> Defaults = new Dictionary<GameAction, Binding>
        {
            { GameAction.MoveForward, Binding.K(Key.W) }, { GameAction.MoveBack, Binding.K(Key.S) },
            { GameAction.MoveLeft, Binding.K(Key.A) }, { GameAction.MoveRight, Binding.K(Key.D) },
            { GameAction.Jump, Binding.K(Key.Space) }, { GameAction.Sprint, Binding.K(Key.LeftShift) },
            { GameAction.Fire, Binding.M(0) }, { GameAction.Aim, Binding.M(1) },
            { GameAction.Interact, Binding.K(Key.E) }, { GameAction.DropWeapon, Binding.K(Key.G) },
            { GameAction.Inventory, Binding.K(Key.Tab) },
            { GameAction.PushToTalk, Binding.K(Key.V) }, { GameAction.ToggleMute, Binding.K(Key.N) },
            { GameAction.Pause, Binding.K(Key.Escape) },
            { GameAction.UseProteinBar, Binding.K(Key.Q) }, { GameAction.UseEnergyDrink, Binding.K(Key.X) },
            { GameAction.Map, Binding.K(Key.M) },
            { GameAction.Chat, Binding.K(Key.T) },
            { GameAction.Emote, Binding.K(Key.B) },
        };

        static Dictionary<GameAction, Binding> _bindings;

        public static event Action Changed;

        static Dictionary<GameAction, Binding> Bindings
        {
            get
            {
                if (_bindings != null) return _bindings;
                _bindings = new Dictionary<GameAction, Binding>();
                foreach (var kv in Defaults)
                {
                    string saved = PlayerPrefs.GetString(Prefix + kv.Key, null);
                    _bindings[kv.Key] = Binding.TryParse(saved, out var b) ? b : kv.Value;
                }
                return _bindings;
            }
        }

        public static Binding Get(GameAction action) => Bindings[action];
        public static string Label(GameAction action) => Bindings[action].Label;

        /// <summary>Rebind an action. Anything else using that input swaps to this action's old input.</summary>
        public static void Rebind(GameAction action, Binding binding)
        {
            var old = Bindings[action];
            foreach (var kv in new List<KeyValuePair<GameAction, Binding>>(Bindings))
                if (kv.Key != action && Same(kv.Value, binding)) Save(kv.Key, old);
            Save(action, binding);
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            foreach (var kv in Defaults)
            {
                PlayerPrefs.DeleteKey(Prefix + kv.Key);
                Bindings[kv.Key] = kv.Value;
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void Save(GameAction action, Binding binding)
        {
            Bindings[action] = binding;
            PlayerPrefs.SetString(Prefix + action, binding.ToString());
            PlayerPrefs.Save();
        }

        static bool Same(Binding a, Binding b) =>
            a.IsMouse == b.IsMouse && (a.IsMouse ? a.MouseButton == b.MouseButton : a.Key == b.Key);

        // ------------------------------------------------------------------ queries

        public static bool Held(GameAction action) => Control(action)?.isPressed ?? false;
        public static bool Pressed(GameAction action) => Control(action)?.wasPressedThisFrame ?? false;
        public static bool Released(GameAction action) => Control(action)?.wasReleasedThisFrame ?? false;

        static ButtonControl Control(GameAction action)
        {
            var b = Bindings[action];
            if (b.IsMouse)
            {
                var m = Mouse.current;
                if (m == null) return null;
                return b.MouseButton switch
                {
                    0 => m.leftButton, 1 => m.rightButton, 2 => m.middleButton,
                    3 => m.backButton, 4 => m.forwardButton, _ => null,
                };
            }
            var kb = Keyboard.current;
            return kb == null || b.Key == Key.None ? null : kb[b.Key];
        }

        /// <summary>For the rebind screen: whatever key or mouse button was pressed this frame.</summary>
        public static bool TryCaptureAny(out Binding binding)
        {
            binding = default;
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame)
            {
                foreach (var key in kb.allKeys)
                {
                    if (!key.wasPressedThisFrame) continue;
                    binding = Binding.K(key.keyCode);
                    return true;
                }
            }
            var m = Mouse.current;
            if (m == null) return false;
            if (m.leftButton.wasPressedThisFrame) { binding = Binding.M(0); return true; }
            if (m.rightButton.wasPressedThisFrame) { binding = Binding.M(1); return true; }
            if (m.middleButton.wasPressedThisFrame) { binding = Binding.M(2); return true; }
            if (m.backButton.wasPressedThisFrame) { binding = Binding.M(3); return true; }
            if (m.forwardButton.wasPressedThisFrame) { binding = Binding.M(4); return true; }
            return false;
        }
    }
}
