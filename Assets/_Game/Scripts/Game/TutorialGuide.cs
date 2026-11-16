using FriendSlop.Player;
using FriendSlop.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Game
{
    /// <summary>
    /// A short, optional first-hunt tour. The first time a hunter loads into a game it offers the
    /// tour (Enter to take it, Backspace to skip); declining or finishing marks it seen in the profile,
    /// and Options → Controls can replay it.
    ///
    /// Design notes (GMTK "Can we Improve Tutorials for Complex Games?", Voidborn devlog "Teaching a
    /// Roguelite Without a Wall of Text"): teach the controls and the core loop, not every system;
    /// one thing at a time; each card ends when you actually do the thing (no "press OK"); everything
    /// can be skipped; it shows once per profile with a replay in settings. The loop here is
    /// move → shoot → hunt → carry → sell → shop. Steps you already did earlier complete at once.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        enum Step { Offer, Move, Shoot, Hunt, Carry, Sell, Shop, Done, Off }

        static TutorialGuide _instance;
        Step _step = Step.Off;
        float _stepBorn, _offerUntil, _doneUntil;
        Vector3 _lastPos;
        float _moved;
        bool _killed, _sold, _carried;
        int _startSold = -1;
        GUIStyle _title, _body, _foot, _marker;

        /// <summary>True while the tour card is up and wants Enter / Backspace (chat input stays out of the way).</summary>
        public static bool WantsKeys => _instance != null && _instance._step == Step.Offer && Time.unscaledTime >= _instance._stepBorn;

        /// <summary>Start the tour again from the top (Options button).</summary>
        public static void Replay()
        {
            if (_instance == null) return;
            _instance.Begin(Step.Move);
        }

        /// <summary>The local hunter killed an animal (called from the kill confirm).</summary>
        public static void NoteKill() { if (_instance != null) _instance._killed = true; }

        /// <summary>Any animal died; counts for the hunt step if it was near you (a friend's kill is fine).</summary>
        public static void NoteAnimalDied(Vector3 at)
        {
            if (_instance == null || NetworkPlayer.Local == null) return;
            if ((NetworkPlayer.Local.transform.position - at).sqrMagnitude < 40f * 40f) _instance._killed = true;
        }

        /// <summary>Dev / bridge: current step name.</summary>
        public static string StepName => _instance == null ? "none" : _instance._step.ToString();

        /// <summary>Dev / bridge: offer the tour as if this were a first hunt.</summary>
        public static void DevOffer() { if (_instance != null) _instance.Offer(); }

        /// <summary>Dev / bridge: skip to the next step.</summary>
        public static void DevNext() { if (_instance != null && _instance._step is > Step.Offer and < Step.Off) _instance.Next(); }

        void Awake() => _instance = this;
        void OnDestroy() { if (_instance == this) _instance = null; }

        void Offer()
        {
            _step = Step.Offer;
            _stepBorn = Time.unscaledTime;
            _offerUntil = Time.unscaledTime + 40f;
        }

        void Begin(Step step)
        {
            _step = step;
            _stepBorn = Time.unscaledTime;
            if (step == Step.Move)
            {
                _moved = 0f; _killed = _sold = _carried = false; _startSold = -1;
                if (NetworkPlayer.Local) _lastPos = NetworkPlayer.Local.transform.position;
            }
            if (step == Step.Done) _doneUntil = Time.unscaledTime + 12f;
        }

        void Next()
        {
            FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.UiClick, 0.8f, 1.25f);
            Begin(_step + 1);
            if (_step == Step.Done) FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.Fanfare, 0.5f, 1.1f);
        }

        void Finish()
        {
            _step = Step.Off;
            var p = FriendSlop.Profile.PlayerProfile.Current;
            if (!p.tutorialSeen) { p.tutorialSeen = true; p.Save(); }
        }

        void Update()
        {
            var state = GameState.Instance;
            var me = NetworkPlayer.Local;
            if (state == null || !state.IsSpawned || me == null)
            {
                if (_step != Step.Off && _step != Step.Offer) _step = Step.Off; // left the hunt mid-tour
                return;
            }

            // First hunt on this profile: offer the tour a moment after spawning in.
            if (_step == Step.Off && !FriendSlop.Profile.PlayerProfile.Current.tutorialSeen && !_offeredThisSession)
            {
                _offeredThisSession = true;
                Offer();
                _stepBorn += 2.5f; // let the spawn settle first
            }
            if (_step == Step.Off) return;

            var kb = Keyboard.current;
            bool menus = GameMenus.AnyOpen;
            if (_startSold < 0) _startSold = state.AnimalsSold.Value;
            if (state.AnimalsSold.Value > _startSold) _sold = true;
            if (me.IsCarrying) _carried = true;

            if (_step == Step.Offer)
            {
                if (Time.unscaledTime < _stepBorn) return;
                if (menus) { _offerUntil += Time.unscaledDeltaTime; return; }
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.UiClick, 0.8f); Begin(Step.Move); }
                else if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || Time.unscaledTime > _offerUntil) Finish();
                return;
            }

            // Skip keys work outside menus and chat: Backspace skips this step, Delete ends the tour.
            if (!menus && kb != null)
            {
                if (kb.deleteKey.wasPressedThisFrame) { Finish(); return; }
                if (kb.backspaceKey.wasPressedThisFrame) { if (_step == Step.Done) Finish(); else Next(); return; }
            }

            switch (_step)
            {
                case Step.Move:
                    var pos = me.transform.position;
                    _moved += Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(_lastPos.x, 0f, _lastPos.z));
                    _lastPos = pos;
                    if (_moved > 10f) Next();
                    break;
                case Step.Shoot:
                    if (!menus && !me.IsCarrying && Controls.Pressed(GameAction.Fire) && Time.unscaledTime > _stepBorn + 0.4f) Next();
                    break;
                case Step.Hunt:
                    if (_killed || _carried || _sold) Next();
                    break;
                case Step.Carry:
                    if (_carried || _sold) Next();
                    break;
                case Step.Sell:
                    if (_sold) Next();
                    break;
                case Step.Shop:
                    if (GameMenus.Open == GameMenus.Menu.Shop) Next();
                    break;
                case Step.Done:
                    if (Time.unscaledTime > _doneUntil) Finish();
                    break;
            }
        }

        bool _offeredThisSession;

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (_step == Step.Off) return;
            var state = GameState.Instance;
            if (state == null || !state.IsSpawned || NetworkPlayer.Local == null) return;
            if (GameMenus.Open is GameMenus.Menu.Pause or GameMenus.Menu.Options or GameMenus.Menu.Inventory) return;
            if (_step == Step.Offer && Time.unscaledTime < _stepBorn) return;
            GUI.skin = UiTheme.Skin;
            _title ??= new GUIStyle(UiTheme.Header) { fontSize = 19, wordWrap = true };
            _body ??= new GUIStyle(UiTheme.Body) { fontSize = 15, wordWrap = true, richText = true };
            _foot ??= new GUIStyle(UiTheme.Small) { wordWrap = true };
            _marker ??= new GUIStyle(UiTheme.Center) { fontSize = 15, fontStyle = FontStyle.Bold };

            string head, text;
            int n = (int)_step; // Move = 1 ... Shop = 6
            switch (_step)
            {
                case Step.Offer:
                    head = "First hunt?";
                    text = $"Want a quick tour? About two minutes, and you play the real game the whole time.\n\n{Key("Enter")} take the tour\n{Key("Backspace")} no thanks";
                    break;
                case Step.Move:
                    head = "Get moving";
                    text = $"{Key(GameAction.MoveForward)}{Key(GameAction.MoveLeft)}{Key(GameAction.MoveBack)}{Key(GameAction.MoveRight)} to walk, {Key(GameAction.Sprint)} to sprint, {Key(GameAction.Jump)} to jump. Have a wander.";
                    break;
                case Step.Shoot:
                    head = "Try your gun";
                    text = $"{Key(GameAction.Fire)} to shoot, hold {Key(GameAction.Aim)} to aim. Number keys or the scroll wheel switch weapons.";
                    break;
                case Step.Hunt:
                    head = "Bag an animal";
                    text = "Animals roam the woods around camp. Gunshots spook them, so sneak up close. Headshots, long shots and jump shots pay extra. Some animals fight back!";
                    break;
                case Step.Carry:
                    head = "Grab the carcass";
                    text = $"Look at the body and hold {Key(GameAction.Interact)} to pick it up. {Key(GameAction.Fire)} throws it. Big animals are heavy, so bring a friend.";
                    break;
                case Step.Sell:
                    head = "Cash it in";
                    text = "Carry it to the trader's sell pad (follow the marker) and drop it on. The money goes to the whole team.";
                    break;
                case Step.Shop:
                    head = "Gear up";
                    text = $"Find the gunsmith (marker) and press {Key(GameAction.Interact)} to browse. Better guns unlock as your hunter levels up.";
                    break;
                default:
                    head = "That's the loop. Happy hunting!";
                    text = $"{Key(GameAction.Inventory)} inventory    {Key(GameAction.Map)} map\n{Key(GameAction.Chat)} chat    {Key(GameAction.PushToTalk)} voice    {Key(GameAction.Emote)} emote\n\n" +
                           $"Hold {Key(GameAction.Interact)} on a downed friend to revive them. Nights are dangerous, so sleep in a bed to skip them.";
                    break;
            }

            const float w = 360f;
            float h = 60f + _body.CalcHeight(new GUIContent(text), w - 32f) + (_step == Step.Offer ? 0f : 26f);
            float age = Time.unscaledTime - _stepBorn;
            float slide = 1f - Mathf.Clamp01(age / 0.25f);
            slide = slide * slide;
            var r = new Rect(18f - slide * 60f, Screen.height * 0.32f, w, h);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - slide);
            UiTheme.Panel(r);
            if (_step is > Step.Offer and < Step.Done)
            {
                GUI.Label(new Rect(r.x + 16, r.y + 10, w - 32, 18), $"TOUR  {n}/6", UiTheme.Small);
                for (int i = 1; i <= 6; i++)
                {
                    var dot = new Rect(r.xMax - 16 - (7 - i) * 14, r.y + 15, 9, 9);
                    GUI.color = new Color(1f, 1f, 1f, 1f - slide) * (i < n ? UiTheme.Good : i == n ? UiTheme.Accent : new Color(1f, 1f, 1f, 0.25f));
                    GUI.DrawTexture(dot, Texture2D.whiteTexture);
                }
                GUI.color = new Color(1f, 1f, 1f, 1f - slide);
            }
            float y = r.y + (_step is > Step.Offer and < Step.Done ? 28f : 12f);
            UiTheme.Shadow(new Rect(r.x + 16, y, w - 32, 26), head, _title);
            GUI.Label(new Rect(r.x + 16, y + 28, w - 32, h - 60), text, _body);
            if (_step != Step.Offer)
                GUI.Label(new Rect(r.x + 16, r.yMax - 26, w - 32, 20), _step == Step.Done ? "Backspace to close · replay any time in Options → Controls" : "Backspace skip step  ·  Delete end tour", _foot);
            GUI.color = prev;

            if (_step == Step.Sell) DrawMarker(FindTarget<FriendSlop.World.SellZone>(ref _sellZone), "SELL");
            if (_step == Step.Shop) DrawMarker(FindTarget<FriendSlop.World.GunShop>(ref _gunShop), "GUNSMITH");
        }

        Transform _sellZone, _gunShop;

        static Transform FindTarget<T>(ref Transform cache) where T : Component
        {
            if (cache == null)
            {
                var c = FindAnyObjectByType<T>();
                cache = c ? c.transform : null;
            }
            return cache;
        }

        /// <summary>A bobbing world marker with distance; a turn arrow under the crosshair when it's off screen or behind you.</summary>
        void DrawMarker(Transform target, string label)
        {
            var pw = FriendSlop.Weapons.PlayerWeapons.Local;
            var cam = pw != null && pw.ViewCamera != null && pw.ViewCamera.isActiveAndEnabled ? pw.ViewCamera : Camera.main;
            if (target == null || cam == null || NetworkPlayer.Local == null) return;
            Vector3 world = target.position + Vector3.up * 2.4f;
            Vector3 sp = cam.WorldToScreenPoint(world);
            float dist = Vector3.Distance(NetworkPlayer.Local.transform.position, target.position);
            bool behind = sp.z < 0f;
            Vector2 p = new Vector2(sp.x, Screen.height - sp.y);
            if (behind) p = new Vector2(Screen.width - p.x, Screen.height - p.y);
            const float pad = 60f;
            bool off = behind || p.x < pad || p.x > Screen.width - pad || p.y < pad || p.y > Screen.height - pad;
            string s = $"▼ {label}  {dist:0} m";
            if (off)
            {
                // Off screen: a turn arrow under the crosshair instead of a marker pinned to the edge (it would land on the HUD).
                Vector3 to = target.position - cam.transform.position; to.y = 0f;
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                float yaw = Vector3.SignedAngle(fwd, to, Vector3.up);
                s = yaw < 0f ? $"◀  {label}  {dist:0} m" : $"{label}  {dist:0} m  ▶";
                p = new Vector2(Screen.width / 2f + Mathf.Sign(yaw) * 120f, Screen.height * 0.6f);
            }
            p.y += Mathf.Sin(Time.unscaledTime * 4f) * 4f;
            float tw = _marker.CalcSize(new GUIContent(s)).x + 28f;
            var r = new Rect(p.x - tw / 2f, p.y - 15f, tw, 30f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            UiTheme.Pill(r);
            GUI.color = prev;
            UiTheme.Shadow(r, s, _marker);
        }

        static string Key(GameAction a) => Key(Controls.Label(a));
        static string Key(string k) => $"<b><color=#FFB347>[{k}]</color></b> ";
    }
}
