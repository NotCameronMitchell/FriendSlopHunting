using System;
using System.IO;
using FriendSlop.Animals;
using FriendSlop.Net;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Dev-only automation hook. Lets tools outside Unity (e.g. Claude Code) trigger a small,
    /// fixed set of editor actions by writing one command per line to Temp/fsh_command.txt.
    /// Only the whitelisted commands below exist; anything else is ignored. Temp/ is git-ignored
    /// and this file is editor-only, so nothing here ships in builds.
    ///
    /// Commands: build-scene · build-zip · play · stop · host · screenshot
    ///           selftest-kill · selftest-juggle · selftest-sell · selftest-status (host-side logic checks)
    /// Results are logged with a "[Bridge]" prefix in the Editor log.
    /// </summary>
    [InitializeOnLoad]
    static class EditorCommandBridge
    {
        const string CommandFile = "Temp/fsh_command.txt";
        const string ScreenshotFile = "Temp/fsh_screenshot.png";
        static double _nextPoll;

        static EditorCommandBridge() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CommandFile)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandFile);
                File.Delete(CommandFile);
            }
            catch (IOException) { return; } // writer still has it open; try next poll

            foreach (var raw in lines)
            {
                string cmd = raw.Trim().ToLowerInvariant();
                if (cmd.Length == 0) continue;
                try { Run(cmd); }
                catch (Exception e) { Debug.LogError($"[Bridge] '{cmd}' failed: {e}"); }
            }
        }

        static Vector3 _shotStart;
        static FriendSlop.Weapons.WeaponPickup _shotTarget;

        static void Run(string cmd)
        {
            if (cmd.StartsWith("equip ") && int.TryParse(cmd.Substring(6), out int equipIndex))
            {
                var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                pw?.RequestEquip(equipIndex);
                Debug.Log($"[Bridge] equip {equipIndex} → {pw?.Current?.displayName}");
                return;
            }

            switch (cmd)
            {
                case "selftest-pause":
                    FriendSlop.Game.GameMenus.Show(FriendSlop.Game.GameMenus.Menu.Pause);
                    Debug.Log($"[Bridge] pause open={FriendSlop.Game.GameMenus.Open}");
                    break;
                case "selftest-options":
                    FriendSlop.Game.GameMenus.Show(FriendSlop.Game.GameMenus.Menu.Options);
                    Debug.Log($"[Bridge] options open={FriendSlop.Game.GameMenus.Open}");
                    break;
                case "selftest-closemenus":
                    FriendSlop.Game.GameMenus.Close();
                    FriendSlop.Game.OptionsMenu.OpenFromMainMenu = false;
                    Debug.Log("[Bridge] menus closed");
                    break;
                case "selftest-rebind":
                {
                    var C = typeof(FriendSlop.Settings.Controls);
                    string before = $"jump={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Jump)} fwd={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.MoveForward)}";
                    FriendSlop.Settings.Controls.Rebind(FriendSlop.Settings.GameAction.Jump, FriendSlop.Settings.Controls.Binding.K(UnityEngine.InputSystem.Key.J));
                    string mid = $"jump={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Jump)}";
                    // Collision: bind Jump to W → Move forward should take Jump's old key (J).
                    FriendSlop.Settings.Controls.Rebind(FriendSlop.Settings.GameAction.Jump, FriendSlop.Settings.Controls.Binding.K(UnityEngine.InputSystem.Key.W));
                    string after = $"jump={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Jump)} fwd={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.MoveForward)}";
                    string saved = PlayerPrefs.GetString("fsh.bind.Jump", "(none)");
                    FriendSlop.Settings.Controls.ResetToDefaults();
                    string reset = $"jump={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.Jump)} fwd={FriendSlop.Settings.Controls.Label(FriendSlop.Settings.GameAction.MoveForward)}";
                    Debug.Log($"[Bridge] rebind → before[{before}] set-J[{mid}] set-W[{after}] saved='{saved}' reset[{reset}]");
                    break;
                }
                case "selftest-settings":
                {
                    float sens = FriendSlop.Settings.GameSettings.MouseSensitivity, fov = FriendSlop.Settings.GameSettings.FieldOfView, vol = FriendSlop.Settings.GameSettings.MasterVolume;
                    FriendSlop.Settings.GameSettings.MouseSensitivity = 0.3f;
                    FriendSlop.Settings.GameSettings.FieldOfView = 90f;
                    FriendSlop.Settings.GameSettings.MasterVolume = 0.5f;
                    string set = $"sens={FriendSlop.Settings.GameSettings.MouseSensitivity:0.00} fov={FriendSlop.Settings.GameSettings.FieldOfView:0} master={FriendSlop.Settings.GameSettings.MasterVolume:0.00} listener={AudioListener.volume:0.00} prefs-sens={PlayerPrefs.GetFloat("fsh.settings.sens", -1):0.00}";
                    FriendSlop.Settings.GameSettings.MouseSensitivity = sens;
                    FriendSlop.Settings.GameSettings.FieldOfView = fov;
                    FriendSlop.Settings.GameSettings.MasterVolume = vol;
                    Debug.Log($"[Bridge] settings → set[{set}] restored sens={sens:0.00} fov={fov:0} master={vol:0.00}");
                    break;
                }
                case "selftest-animals":
                {
                    var counts = new System.Collections.Generic.SortedDictionary<string, (int alive, int dead, int angry)>();
                    foreach (var a in Animal.All)
                    {
                        if (a == null || a.Species == null) continue;
                        counts.TryGetValue(a.Species.displayName, out var c);
                        if (a.IsDead.Value) c.dead++; else c.alive++;
                        if (a.IsAngry.Value) c.angry++;
                        counts[a.Species.displayName] = c;
                    }
                    var sb = new System.Text.StringBuilder("[Bridge] animals →");
                    foreach (var kv in counts) sb.Append($" {kv.Key}:{kv.Value.alive}a/{kv.Value.dead}d/{kv.Value.angry}!");
                    Debug.Log(sb.ToString());
                    break;
                }
                case "selftest-hurtme":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    bool down = hp != null && hp.ServerDamage(40f, Vector3.back * 6f + Vector3.up * 3f, "test goose");
                    Debug.Log($"[Bridge] hurtme → hp={hp?.Health.Value} down={down}");
                    break;
                }
                case "selftest-killme":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    bool down = hp != null && hp.ServerDamage(500f, Vector3.zero, "test bear");
                    Debug.Log($"[Bridge] killme → hp={hp?.Health.Value} down={down}");
                    break;
                }
                case "selftest-hp":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    Debug.Log($"[Bridge] hp → {hp?.Health.Value} down={hp?.IsDown.Value} pos={hp?.transform.position}");
                    break;
                }
                case "selftest-summon":
                {
                    var t = UnityEngine.Object.FindFirstObjectByType<FriendSlop.World.BossTotem>();
                    if (t == null) { Debug.LogWarning("[Bridge] selftest-summon: no totem"); return; }
                    t.RequestSummon();
                    Debug.Log("[Bridge] summon requested");
                    break;
                }
                case "selftest-tocamp":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    hp?.GetComponent<FriendSlop.Player.NetworkPlayer>().Teleport(new Vector3(0f, 0.2f, -3f));
                    Debug.Log("[Bridge] teleported to camp");
                    break;
                }
                case "selftest-lookback":
                {
                    // Stand behind the gun shop's back wall, looking at it (sign text must NOT show through).
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    var shop = UnityEngine.Object.FindFirstObjectByType<FriendSlop.World.GunShop>();
                    if (hp == null || shop == null) { Debug.LogWarning("[Bridge] selftest-lookback: missing"); return; }
                    var root = shop.transform.parent; // the hut
                    var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                    Vector3 from = root.position + Vector3.left * 11f + Vector3.up * 0.2f;
                    np.Teleport(from);
                    np.transform.rotation = Quaternion.LookRotation(Vector3.right);
                    Debug.Log("[Bridge] behind the gun shop");
                    break;
                }
                case "selftest-lookfront":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    var shop = UnityEngine.Object.FindFirstObjectByType<FriendSlop.World.GunShop>();
                    if (hp == null || shop == null) { Debug.LogWarning("[Bridge] selftest-lookfront: missing"); return; }
                    var root = shop.transform.parent;
                    var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                    np.Teleport(root.position + Vector3.right * 11f + Vector3.up * 0.2f);
                    np.transform.rotation = Quaternion.LookRotation(Vector3.left);
                    Debug.Log("[Bridge] in front of the gun shop");
                    break;
                }
                case "selftest-lookboss":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    Animal boss = null;
                    foreach (var a in Animal.All) if (a != null && a.Species.isBoss && !a.IsDead.Value) boss = a;
                    if (hp == null || boss == null) { Debug.LogWarning("[Bridge] selftest-lookboss: no boss"); return; }
                    var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                    Vector3 side = Vector3.Cross(Vector3.up, boss.transform.forward).normalized;
                    Vector3 from = boss.transform.position + side * 12f + boss.transform.forward * 4f;
                    np.Teleport(from + Vector3.up * 0.2f);
                    Vector3 look = boss.transform.position - from; look.y = 0f;
                    np.transform.rotation = Quaternion.LookRotation(look);
                    Debug.Log($"[Bridge] looking at {boss.Species.bossTitle} from 12 m (hp {boss.HealthFraction.Value:P0}, angry={boss.IsAngry.Value})");
                    break;
                }
                case "selftest-bosskill":
                {
                    foreach (var a in Animal.All)
                        if (a != null && a.Species.isBoss && !a.IsDead.Value) { a.ServerApplyHit(99999f, Vector3.forward, ShotStyle.Headshot, 0); break; }
                    Debug.Log($"[Bridge] bosskill → money=${FriendSlop.Game.GameState.Instance?.Money.Value}");
                    break;
                }
                case "selftest-lure":
                {
                    // Stand right next to the nearest live fighter and see if it attacks.
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    Animal fighter = null;
                    float best = float.MaxValue;
                    foreach (var a in Animal.All)
                    {
                        if (a == null || a.IsDead.Value || !a.Species.Fights || a.Species.isBoss) continue;
                        float d = (a.transform.position - hp.transform.position).sqrMagnitude;
                        if (d < best) { best = d; fighter = a; }
                    }
                    if (hp == null || fighter == null) { Debug.LogWarning("[Bridge] selftest-lure: no fighter"); return; }
                    var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                    np.Teleport(fighter.transform.position + fighter.transform.forward * 2.5f + Vector3.up * 0.2f);
                    Debug.Log($"[Bridge] lure → standing next to a {fighter.Species.displayName} ({fighter.Species.temperament})");
                    break;
                }
                case "selftest-shootdrop":
                {
                    // Real path: stand 4 m away, aim the camera at the gun's hitbox, fire the equipped weapon.
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    var target = UnityEngine.Object.FindFirstObjectByType<FriendSlop.Weapons.WeaponPickup>();
                    if (pw == null || target == null) { Debug.LogWarning("[Bridge] selftest-shootdrop: need a player and a dropped gun"); return; }
                    var aim = target.GetComponent<BoxCollider>().bounds.center;
                    var cc = pw.GetComponent<CharacterController>();
                    Vector3 from = aim + new Vector3(-4f, 0f, 0f);
                    from.y = target.transform.position.y;
                    cc.enabled = false;
                    pw.transform.position = from;
                    pw.transform.rotation = Quaternion.LookRotation(Vector3.right);
                    cc.enabled = true;
                    _shotStart = target.transform.position;
                    _shotTarget = target;
                    var cam = pw.GetComponentInChildren<Camera>(true);
                    Vector3 eye = cam.transform.position;
                    var hits = Physics.RaycastAll(eye, (aim - eye).normalized, 10f, ~0, QueryTriggerInteraction.Ignore);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    var sbh = new System.Text.StringBuilder();
                    foreach (var h in hits) sbh.Append($" {h.collider.name}@{h.distance:0.00}");
                    Debug.Log($"[Bridge] aim ray (eye {eye.y:0.00} → aim {aim.y:0.00}) hits:{sbh}");
                    FriendSlop.Weapons.Projectile.DebugLog = true;
                    pw.TestFireAt(aim);
                    Debug.Log($"[Bridge] shot {target.Definition?.displayName} with {pw.Current?.displayName} from 4 m");
                    break;
                }
                case "selftest-dropmoved":
                {
                    if (_shotTarget == null) { Debug.LogWarning("[Bridge] selftest-dropmoved: no target"); return; }
                    var moved = _shotTarget.transform.position - _shotStart;
                    Debug.Log($"[Bridge] dropped {_shotTarget.Definition?.displayName} moved {moved.magnitude:0.00} m (dx={moved.x:0.00} dy={moved.y:0.00} dz={moved.z:0.00})");
                    break;
                }
                case "build-scene":
                    if (EditorApplication.isPlaying) { Debug.LogWarning("[Bridge] build-scene: exit Play first"); return; }
                    HuntSceneBuilder.Build(interactive: false);
                    Debug.Log("[Bridge] build-scene done");
                    break;
                case "build-zip":
                    if (EditorApplication.isPlaying) { Debug.LogWarning("[Bridge] build-zip: exit Play first"); return; }
                    BuildTools.BuildWindowsZip();
                    Debug.Log("[Bridge] build-zip done");
                    break;
                case "refresh":
                    AssetDatabase.Refresh();
                    Debug.Log("[Bridge] refresh done");
                    break;
                case "play":
                    EditorApplication.isPlaying = true;
                    Debug.Log("[Bridge] play requested");
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    Debug.Log("[Bridge] stop requested");
                    break;
                case "host":
                    if (!EditorApplication.isPlaying || NetBootstrap.Instance == null) { Debug.LogWarning("[Bridge] host: not in Play"); return; }
                    Debug.Log($"[Bridge] host → {NetBootstrap.Instance.HostDirect()}");
                    break;
                // ---- Host-side self tests (Play mode, after "host") ----
                case "selftest-kill":
                {
                    var a = FirstAnimal(dead: false);
                    if (a == null) { Debug.LogWarning("[Bridge] selftest-kill: no live animal"); return; }
                    a.ServerApplyHit(999f, Vector3.forward, ShotStyle.Headshot | ShotStyle.LongShot, 0);
                    Debug.Log($"[Bridge] selftest-kill → dead={a.IsDead.Value} value=${a.SaleValue} style='{a.StyleDescription}'");
                    break;
                }
                case "selftest-juggle":
                {
                    var a = FirstAnimal(dead: true);
                    if (a == null) { Debug.LogWarning("[Bridge] selftest-juggle: no carcass"); return; }
                    a.ServerApplyHit(60f, Vector3.forward, ShotStyle.None, 0);
                    Debug.Log($"[Bridge] selftest-juggle → juggles={a.Juggles.Value} value=${a.SaleValue}");
                    break;
                }
                case "selftest-sell":
                {
                    var a = FirstAnimal(dead: true);
                    var zone = UnityEngine.Object.FindFirstObjectByType<FriendSlop.World.SellZone>();
                    if (a == null || zone == null) { Debug.LogWarning("[Bridge] selftest-sell: need a carcass and a SellZone"); return; }
                    var rb = a.GetComponent<Rigidbody>();
                    rb.linearVelocity = Vector3.zero;
                    rb.position = zone.transform.position + Vector3.up * 0.5f;
                    a.transform.position = rb.position;
                    Debug.Log($"[Bridge] selftest-sell → moved carcass worth ${a.SaleValue} onto the pad");
                    break;
                }
                case "selftest-sfx":
                {
                    var clips = new[]
                    {
                        FriendSlop.Audio.Sfx.Gunshot, FriendSlop.Audio.Sfx.BoltCycle, FriendSlop.Audio.Sfx.HitTick,
                        FriendSlop.Audio.Sfx.HeadshotDing, FriendSlop.Audio.Sfx.Squeak, FriendSlop.Audio.Sfx.Boing,
                        FriendSlop.Audio.Sfx.ChaChing, FriendSlop.Audio.Sfx.Thud, FriendSlop.Audio.Sfx.Whoosh,
                        FriendSlop.Audio.Sfx.RandomFootstep(),
                    };
                    var sb = new System.Text.StringBuilder("[Bridge] sfx →");
                    foreach (var c in clips)
                    {
                        sb.Append($" {c.name}={c.length:0.00}s");
                        if (EditorApplication.isPlaying) FriendSlop.Audio.SfxPlayer.Play2D(c, 0.05f);
                    }
                    Debug.Log(sb.ToString());
                    break;
                }
                case "selftest-money":
                    FriendSlop.Game.GameState.Instance?.ServerGrantMoney(5000);
                    Debug.Log($"[Bridge] money → ${FriendSlop.Game.GameState.Instance?.Money.Value}");
                    break;
                case "selftest-buy":
                {
                    // Buy every weapon through the real purchase RPC, cheapest first.
                    var gs = FriendSlop.Game.GameState.Instance;
                    if (gs == null || gs.Catalog == null) { Debug.LogWarning("[Bridge] selftest-buy: no GameState"); return; }
                    for (int i = 1; i < gs.Catalog.Count; i++) gs.RequestBuyWeapon(i);
                    Debug.Log("[Bridge] selftest-buy requested");
                    break;
                }
                case "selftest-weapons":
                {
                    var gs = FriendSlop.Game.GameState.Instance;
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    Debug.Log($"[Bridge] weapons → owned=0b{System.Convert.ToString(pw != null ? pw.OwnedMask() : 0, 2)} slots={(pw != null ? pw.Slots().Count : 0)} equipped={(pw != null ? pw.Current?.displayName : "none")} money=${gs?.Money.Value} pickupsInWorld={UnityEngine.Object.FindObjectsByType<FriendSlop.Weapons.WeaponPickup>(FindObjectsSortMode.None).Length}");
                    break;
                }
                case "selftest-drop":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    if (pw == null) { Debug.LogWarning("[Bridge] selftest-drop: no local player"); return; }
                    string name = pw.Current?.displayName;
                    pw.RequestDropEquipped();
                    Debug.Log($"[Bridge] drop requested → {name}");
                    break;
                }
                case "selftest-pickup":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    var pickups = UnityEngine.Object.FindObjectsByType<FriendSlop.Weapons.WeaponPickup>(FindObjectsSortMode.None);
                    if (pw == null || pickups.Length == 0) { Debug.LogWarning("[Bridge] selftest-pickup: nothing to pick up"); return; }
                    FriendSlop.Weapons.WeaponPickup nearest = null;
                    float best = float.MaxValue;
                    foreach (var p in pickups)
                    {
                        float d = (p.transform.position - pw.transform.position).sqrMagnitude;
                        if (d < best) { best = d; nearest = p; }
                    }
                    nearest.RequestPickup();
                    Debug.Log($"[Bridge] pickup requested → {nearest.Definition?.displayName} at {Mathf.Sqrt(best):0.0} m");
                    break;
                }
                case "selftest-pickupinfo":
                {
                    var sb = new System.Text.StringBuilder("[Bridge] pickups →");
                    foreach (var p in UnityEngine.Object.FindObjectsByType<FriendSlop.Weapons.WeaponPickup>(FindObjectsSortMode.None))
                    {
                        var rb = p.GetComponent<Rigidbody>();
                        var box = p.GetComponent<BoxCollider>();
                        float ground = Physics.Raycast(p.transform.position + Vector3.up * 2f, Vector3.down, out var gh, 10f, ~0, QueryTriggerInteraction.Ignore) && gh.collider.attachedRigidbody != rb
                            ? gh.point.y : float.NaN;
                        var bottom = box.bounds.min.y;
                        sb.Append($" [{p.Definition?.displayName}: bottom={bottom:0.00} groundY={ground:0.00} gap={(bottom - ground):0.00} sleeping={rb.IsSleeping()} speed={rb.linearVelocity.magnitude:0.00} box={box.size.x:0.00}x{box.size.y:0.00}x{box.size.z:0.00}]");
                    }
                    Debug.Log(sb.ToString());
                    break;
                }
                case "selftest-shootpickup":
                {
                    var p = UnityEngine.Object.FindFirstObjectByType<FriendSlop.Weapons.WeaponPickup>();
                    if (p == null) { Debug.LogWarning("[Bridge] selftest-shootpickup: none"); return; }
                    var before = p.transform.position;
                    p.GetComponent<FriendSlop.World.NetworkPhysicsBox>().RequestImpulse(Vector3.right * 15f + Vector3.up * 6f, p.transform.position);
                    Debug.Log($"[Bridge] shot pickup {p.Definition?.displayName} at {before}");
                    break;
                }
                case "selftest-lookdrop":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    var p = UnityEngine.Object.FindFirstObjectByType<FriendSlop.Weapons.WeaponPickup>();
                    if (pw == null || p == null) { Debug.LogWarning("[Bridge] selftest-lookdrop: missing"); return; }
                    var cc = pw.GetComponent<CharacterController>();
                    Vector3 from = p.transform.position + new Vector3(-3f, 0f, -1.5f);
                    from.y = p.transform.position.y + 0.05f;
                    cc.enabled = false;
                    pw.transform.position = from;
                    Vector3 look = p.transform.position - from; look.y = 0f;
                    pw.transform.rotation = Quaternion.LookRotation(look);
                    cc.enabled = true;
                    Debug.Log("[Bridge] looking at the dropped weapon");
                    break;
                }
                case "selftest-inventory":
                    FriendSlop.Game.InventoryMenu.Toggle();
                    Debug.Log($"[Bridge] inventory open={FriendSlop.Game.InventoryMenu.IsOpen}");
                    break;
                case "selftest-shop":
                    FriendSlop.Game.GameMenus.Toggle(FriendSlop.Game.GameMenus.Menu.Shop);
                    Debug.Log($"[Bridge] shop open={FriendSlop.Game.ShopMenu.IsOpen}");
                    break;
                case "selftest-lookshop":
                {
                    // Teleport the local player in front of the gunsmith, facing the rack, for a screenshot.
                    var np = FriendSlop.Weapons.PlayerWeapons.Local;
                    var smith = UnityEngine.Object.FindFirstObjectByType<FriendSlop.World.GunShop>();
                    if (np == null || smith == null) { Debug.LogWarning("[Bridge] selftest-lookshop: missing player or gunsmith"); return; }
                    var cc = np.GetComponent<CharacterController>();
                    cc.enabled = false;
                    np.transform.position = smith.transform.position + smith.transform.forward * 4.2f + Vector3.up * 0.1f;
                    np.transform.rotation = Quaternion.LookRotation(-smith.transform.forward);
                    cc.enabled = true;
                    Debug.Log("[Bridge] moved to the gun shop");
                    break;
                }
                case "selftest-explode":
                {
                    var a = FirstAnimal(dead: false);
                    var gs = FriendSlop.Game.GameState.Instance;
                    var bazooka = gs?.Catalog?.Get(gs.Catalog.Count - 1);
                    if (a == null || bazooka == null || !bazooka.IsExplosive) { Debug.LogWarning("[Bridge] selftest-explode: need an animal and an explosive last weapon"); return; }
                    FriendSlop.Weapons.Explosions.ServerExplode(a.transform.position + Vector3.right, bazooka, 0);
                    FriendSlop.Weapons.Explosions.PlayFx(a.transform.position + Vector3.right, bazooka.explosionRadius);
                    Debug.Log($"[Bridge] selftest-explode → target dead={a.IsDead.Value}");
                    break;
                }
                case "selftest-equip":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    if (pw == null) { Debug.LogWarning("[Bridge] selftest-equip: no local player"); return; }
                    for (int i = pw.Catalog.Count - 1; i >= 0; i--) { pw.RequestEquip(i); if (pw.Equipped.Value == i) break; }
                    Debug.Log($"[Bridge] equip → {pw.Current?.displayName}");
                    break;
                }
                case "selftest-fireall":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    if (pw == null) { Debug.LogWarning("[Bridge] selftest-fireall: no local player"); return; }
                    var fired = new System.Text.StringBuilder("[Bridge] fireall →");
                    for (int i = 0; i < pw.Catalog.Count; i++)
                    {
                        pw.RequestEquip(i);
                        if (pw.Equipped.Value != i) { fired.Append($" {i}:locked"); continue; }
                        pw.TestFire();
                        fired.Append($" {pw.Current.displayName}");
                    }
                    Debug.Log(fired.ToString());
                    break;
                }
                case "selftest-status":
                {
                    int alive = 0, dead = 0;
                    foreach (var a in Animal.All) { if (a.IsDead.Value) dead++; else alive++; }
                    var gs = FriendSlop.Game.GameState.Instance;
                    Debug.Log($"[Bridge] status → animals alive={alive} dead={dead} money=${(gs != null ? gs.Money.Value : -1)} sold={(gs != null ? gs.AnimalsSold.Value : -1)}");
                    break;
                }
                case "screenshot":
                    if (File.Exists(ScreenshotFile)) File.Delete(ScreenshotFile);
                    ScreenCapture.CaptureScreenshot(ScreenshotFile);
                    Debug.Log($"[Bridge] screenshot → {ScreenshotFile}");
                    break;
                default:
                    Debug.LogWarning($"[Bridge] unknown command '{cmd}'");
                    break;
            }
        }

        static Animal FirstAnimal(bool dead)
        {
            if (!EditorApplication.isPlaying) return null;
            foreach (var a in Animal.All)
                if (a != null && a.IsSpawned && a.IsDead.Value == dead) return a;
            return null;
        }
    }
}
