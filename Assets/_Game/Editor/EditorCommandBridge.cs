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
        static readonly System.Collections.Generic.List<Animal> _zoo = new System.Collections.Generic.List<Animal>();

        static void Run(string cmd)
        {
            if (cmd.StartsWith("time "))
            {
                // time 0.85 : jump the shared clock (0 midnight, 0.25 dawn, 0.5 noon, 0.75 dusk).
                var cycle = FriendSlop.World.DayNightCycle.Instance;
                float t = float.Parse(cmd.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                if (cycle == null || !cycle.IsServer) { Debug.LogWarning("[Bridge] time: host first"); return; }
                cycle.ServerSetTime(t);
                Debug.Log($"[Bridge] time → {cycle.ClockText} day {cycle.Day} night={cycle.IsNight}");
                return;
            }
            if (cmd.StartsWith("lookat "))
            {
                // lookat Ghost Goose : stand a few meters in front of the first live animal with that name.
                string want = cmd.Substring(7).Trim();
                var hp = FriendSlop.Player.PlayerHealth.Local;
                Animal found = null;
                foreach (var a in Animal.All) if (string.Equals(a.Species.displayName, want, System.StringComparison.OrdinalIgnoreCase) && !a.IsDead.Value) { found = a; break; }
                if (hp == null || found == null) { Debug.LogWarning($"[Bridge] lookat: no {want}"); return; }
                var t = found.transform;
                float dist = 2.5f + found.Species.bodyLength * found.Species.scale * 1.6f;
                var at = t.position + t.forward * dist;
                at.y = FriendSlop.World.Ground.HeightAt(at) + 0.3f;
                var agent = found.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent && agent.enabled) agent.isStopped = true;
                var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                np.Teleport(at);
                var flat = t.position - at; flat.y = 0f;
                np.transform.rotation = Quaternion.LookRotation(flat);
                Debug.Log($"[Bridge] looking at {want} from {dist:0.0} m");
                return;
            }
            if (cmd == "selftest-mpb")
            {
                foreach (var a in Animal.All)
                {
                    if (!a.Species.shadowy) continue;
                    foreach (var r in a.GetComponentsInChildren<Renderer>())
                    {
                        if (r.name != "Pupil") continue;
                        var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b);
                        Debug.Log($"[Bridge] mpb {r.name} mat={r.sharedMaterial.name} shader={r.sharedMaterial.shader.name} kw={r.sharedMaterial.IsKeywordEnabled("_EMISSION")} emis={b.GetColor("_EmissionColor")} base={b.GetColor("_BaseColor")} hasProp={r.sharedMaterial.HasProperty("_EmissionColor")}");
                        var asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/Animal.mat");
                        Debug.Log($"[Bridge] mpb asset same={asset == r.sharedMaterial} assetKw={asset.IsKeywordEnabled("_EMISSION")} enabledKw=[{string.Join(",", r.sharedMaterial.shaderKeywords)}] localKw=[{string.Join(",", System.Linq.Enumerable.Select(r.sharedMaterial.enabledKeywords, k => k.name))}]");
                    }
                    break;
                }
                return;
            }
            if (cmd == "profile-xp")
            {
                FriendSlop.Profile.PlayerProfile.Current.AddXp(1200);
                Debug.Log($"[Bridge] xp → {FriendSlop.Profile.PlayerProfile.Current.xp} level {FriendSlop.Profile.PlayerProfile.Current.Level}");
                return;
            }
            if (cmd.StartsWith("upgrade "))
            {
                var up = FriendSlop.Player.PlayerUpgrades.Local;
                if (up == null || !System.Enum.TryParse(cmd.Substring(8), true, out FriendSlop.Player.UpgradeTrack track)) { Debug.LogWarning("[Bridge] upgrade: in a hunt, upgrade <track>"); return; }
                up.RequestBuy(track);
                Debug.Log($"[Bridge] upgrade {track} requested");
                return;
            }
            if (cmd == "selftest-upgrades")
            {
                var up = FriendSlop.Player.PlayerUpgrades.Local;
                var hp = FriendSlop.Player.PlayerHealth.Local;
                if (up == null) return;
                var sb = new System.Text.StringBuilder("[Bridge] upgrades →");
                foreach (FriendSlop.Player.UpgradeTrack t in System.Enum.GetValues(typeof(FriendSlop.Player.UpgradeTrack))) sb.Append($" {t}:{up.Tier(t)}");
                sb.Append($" speed×{up.SpeedMult:0.00} dmg×{up.DamageMult:0.00} maxHp={hp?.MaxHp} hp={hp?.Health.Value} money=${FriendSlop.Game.GameState.Instance?.Money.Value}");
                Debug.Log(sb.ToString());
                return;
            }
            if (cmd.StartsWith("gamble-money "))
            {
                // gamble-money <station> <option> <amount> : bet team money exactly like a player would.
                var g = cmd.Split(' ');
                FriendSlop.Game.GameState.Instance?.RequestGambleBet(int.Parse(g[1]), int.Parse(g[2]), int.Parse(g[3]));
                Debug.Log($"[Bridge] gamble-money → station {g[1]} option {g[2]} ${g[3]}");
                return;
            }
            if (cmd.StartsWith("gamble-force "))
            {
                var g = cmd.Split(' ');
                FriendSlop.World.GambleStation.DebugNextSlots = new[] { int.Parse(g[1]), int.Parse(g[2]), int.Parse(g[3]) };
                Debug.Log("[Bridge] next slots forced");
                return;
            }
            if (cmd.StartsWith("gamble "))
            {
                // gamble <station 0-2> [red|black|green] : drop a freshly killed deer onto a Lucky Lodge pad.
                var a2 = cmd.Split(' ');
                int id = int.Parse(a2[1]);
                FriendSlop.World.GambleStation st = null;
                foreach (var s in FriendSlop.World.GambleStation.All) if (s.id == id) st = s;
                var deerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Animals/Deer.prefab");
                if (st == null || deerPrefab == null || !Unity.Netcode.NetworkManager.Singleton.IsServer) { Debug.LogWarning("[Bridge] gamble: host + station"); return; }
                float side = a2.Length > 2 ? (a2[2] == "red" ? 0.5f : a2[2] == "black" ? -0.5f : 0f) : 0f;
                var at = st.transform.TransformPoint(new Vector3(side, 1.2f, 0f));
                var go = UnityEngine.Object.Instantiate(deerPrefab, at, st.transform.rotation);
                go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                go.GetComponent<Animal>().ServerApplyHit(99999f, Vector3.down * 0.01f, ShotStyle.None, 0);
                go.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                go.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
                go.transform.position = at;
                Debug.Log($"[Bridge] gamble → deer on station {id} ({st.game})");
                return;
            }
            if (cmd == "selftest-elite")
            {
                var hp = FriendSlop.Player.PlayerHealth.Local;
                var wolf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Animals/Boar.prefab");
                if (hp == null || wolf == null) return;
                var at = hp.transform.position + hp.transform.forward * 6f;
                at.y = FriendSlop.World.Ground.HeightAt(at);
                var go = UnityEngine.Object.Instantiate(wolf, at, Quaternion.LookRotation(-hp.transform.forward));
                go.GetComponent<Animal>().Elite.Value = true;
                go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (agent) agent.enabled = false;
                Debug.Log($"[Bridge] elite → {go.GetComponent<Animal>().DisplayName}");
                return;
            }
            if (cmd == "tank-buy") { FriendSlop.Game.GameState.Instance?.RequestTank(); Debug.Log("[Bridge] tank requested"); return; }
            if (cmd == "tank-enter")
            {
                var t = FriendSlop.World.Tank.All.Count > 0 ? FriendSlop.World.Tank.All[0] : null;
                if (t == null) { Debug.LogWarning("[Bridge] no tank"); return; }
                t.RequestEnter();
                Debug.Log("[Bridge] entering tank");
                return;
            }
            if (cmd == "tank-exit") { if (FriendSlop.World.Tank.All.Count > 0) FriendSlop.World.Tank.All[0].RequestExit(); return; }
            if (cmd.StartsWith("tank-drive "))
            {
                var a = cmd.Split(' ');
                var t = FriendSlop.World.Tank.All.Count > 0 ? FriendSlop.World.Tank.All[0] : null;
                t?.DebugDrive(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture));
                Debug.Log("[Bridge] tank drive");
                return;
            }
            if (cmd.StartsWith("tank-aim "))
            {
                var a = cmd.Split(' ');
                FriendSlop.World.Tank.All[0].DebugAim(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (cmd.StartsWith("tank-place "))
            {
                var a = cmd.Split(' ');
                var t = FriendSlop.World.Tank.All[0];
                var p = new Vector3(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                p.y = FriendSlop.World.Ground.HeightAt(p) + 0.5f;
                var rb = t.GetComponent<Rigidbody>();
                var rot = a.Length > 3 ? Quaternion.Euler(0f, float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture), 0f) : t.transform.rotation;
                rb.position = p; rb.rotation = rot; t.transform.SetPositionAndRotation(p, rot); rb.linearVelocity = Vector3.zero;
                var hits = Physics.OverlapBox(p + Vector3.up * 0.85f, new Vector3(1.6f, 0.6f, 2.6f), t.transform.rotation);
                Debug.Log($"[Bridge] tank placed at {p}; overlapping: {string.Join(", ", System.Linq.Enumerable.Select(hits, h => h.name + "/" + h.attachedRigidbody?.name))}");
                return;
            }
            if (cmd == "tank-fire") { if (FriendSlop.World.Tank.All.Count > 0) FriendSlop.World.Tank.All[0].DebugFire(); Debug.Log("[Bridge] tank fire"); return; }
            if (cmd == "tank-status")
            {
                var t = FriendSlop.World.Tank.All.Count > 0 ? FriendSlop.World.Tank.All[0] : null;
                var me = FriendSlop.Player.NetworkPlayer.Local;
                if (t == null) { Debug.Log("[Bridge] tank → none"); return; }
                Debug.Log($"[Bridge] tank → pos={t.transform.position:0.0} yaw={t.transform.eulerAngles.y:0} speed={t.GetComponent<Rigidbody>().linearVelocity.magnitude:0.0} kin={t.GetComponent<Rigidbody>().isKinematic} sleep={t.GetComponent<Rigidbody>().IsSleeping()} constraints={t.GetComponent<Rigidbody>().constraints} owner={t.OwnerClientId} seats=[{t.Occupant(0)},{t.Occupant(1)},{t.Occupant(2)},{t.Occupant(3)}] mySeat={me?.TankSeat} myPos={me?.transform.position:0.0}");
                return;
            }
            if (cmd == "selftest-haunt") { FriendSlop.Game.NightEvents.Instance?.ServerDebugHaunt(); Debug.Log("[Bridge] haunting queued"); return; }
            if (cmd == "selftest-stag") { FriendSlop.Game.NightEvents.Instance?.ServerDebugStag(); Debug.Log("[Bridge] shadow stag spawned"); return; }
            if (cmd == "selftest-sleep")
            {
                var me = FriendSlop.Player.NetworkPlayer.Local;
                var bed = FriendSlop.World.Bed.All.Count > 0 ? FriendSlop.World.Bed.All[0] : null;
                if (me == null || bed == null) { Debug.LogWarning("[Bridge] selftest-sleep: no player/bed"); return; }
                me.GetType().GetMethod("GoToSleep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(me, new object[] { bed });
                Debug.Log("[Bridge] sleeping");
                return;
            }
            if (cmd == "selftest-night")
            {
                var cycle = FriendSlop.World.DayNightCycle.Instance;
                var ev = FriendSlop.Game.NightEvents.Instance;
                int night = 0, ghosts = 0, stags = 0;
                foreach (var a in Animal.All) { if (a.Species.nocturnal && !a.IsDead.Value) night++; if (a.Species.ghostly) ghosts++; if (a.Species.shadowy) stags++; }
                Debug.Log($"[Bridge] night → {cycle?.ClockText} day {cycle?.Day} isNight={cycle?.IsNight} nightCritters={night} ghosts={ghosts} stags={stags} ghostNight={ev?.GhostNight.Value} geese={ev?.GeeseKilled.Value} sleeping={FriendSlop.Player.NetworkPlayer.Local?.Sleeping.Value}");
                return;
            }
            if (cmd.StartsWith("near "))
            {
                // near Boulder 3 14 : stand 14 m from the 3rd object named "Boulder", facing it.
                var a = cmd.Substring(5).Split(' ');
                var hp = FriendSlop.Player.PlayerHealth.Local;
                int nth = a.Length > 1 ? int.Parse(a[1]) : 0; float dist = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 12f;
                GameObject found = null; int seen = 0;
                foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (string.Equals(go.name, a[0], System.StringComparison.OrdinalIgnoreCase) && seen++ == nth) { found = go.gameObject; break; }
                if (hp == null || found == null) { Debug.LogWarning($"[Bridge] near: no {a[0]} #{nth}"); return; }
                var tp = found.transform.position; var dirToCamp = (-new Vector3(tp.x, 0f, tp.z)).normalized;
                var stand = tp + dirToCamp * dist; stand.y = FriendSlop.World.Ground.HeightAt(stand) + 0.2f;
                var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                np.Teleport(stand);
                np.transform.rotation = Quaternion.LookRotation(new Vector3(-dirToCamp.x, 0f, -dirToCamp.z));
                Debug.Log($"[Bridge] near → {a[0]} #{nth} at {tp}");
                return;
            }
            if (cmd.StartsWith("shoot-zoo "))
            {
                // shoot-zoo deer 12 head : put a live zoo animal 12 m ahead and fire the equipped gun at its body (or head).
                var a = cmd.Substring(10).Split(' ');
                var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                Animal target = null;
                foreach (var z in _zoo) if (z && !z.IsDead.Value && string.Equals(z.Species.displayName, a[0], System.StringComparison.OrdinalIgnoreCase)) { target = z; break; }
                if (pw == null || target == null) { Debug.LogWarning($"[Bridge] shoot-zoo: no live {a[0]} (selftest-zoo first)"); return; }
                float dist = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 12f;
                bool head = a.Length > 2 && a[2] == "head";
                var me = pw.transform.position; var fwd = pw.transform.forward; fwd.y = 0f; fwd.Normalize();
                var spot = me + fwd * dist; spot.y = FriendSlop.World.Ground.HeightAt(spot);
                var agent = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.enabled) agent.Warp(spot); else target.transform.position = spot;
                Physics.SyncTransforms();
                var sp = target.Species;
                Vector3 aim = head
                    ? target.transform.position + target.transform.forward * sp.bodyLength * 0.55f * sp.scale + Vector3.up * (sp.legLength + sp.bodyHeight) * sp.scale
                    : target.transform.position + Vector3.up * (sp.legLength + sp.bodyHeight * 0.5f) * sp.scale;
                pw.TestFireAt(aim);
                Debug.Log($"[Bridge] shoot-zoo {sp.displayName} at {dist} m ({(head ? "head" : "body")}) with {pw.Current?.displayName}");
                return;
            }
            if (cmd.StartsWith("tp "))
            {
                // tp x z yaw : stand on the ground at (x, z) facing yaw degrees.
                var a = cmd.Substring(3).Split(' ');
                var hp = FriendSlop.Player.PlayerHealth.Local;
                if (hp == null || a.Length < 3) { Debug.LogWarning("[Bridge] tp: in a hunt, tp x z yaw"); return; }
                float x = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                float yaw = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                var p = new Vector3(x, 0f, z);
                p.y = FriendSlop.World.Ground.HeightAt(p) + 0.3f;
                var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                np.Teleport(p);
                np.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Debug.Log($"[Bridge] tp → {p} yaw {yaw} water depth {FriendSlop.World.WaterVolume.DepthAt(p):0.00}");
                return;
            }
            if (cmd.StartsWith("equip ") && int.TryParse(cmd.Substring(6), out int equipIndex))
            {
                var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                pw?.RequestEquip(equipIndex);
                Debug.Log($"[Bridge] equip {equipIndex} → {pw?.Current?.displayName}");
                return;
            }

            switch (cmd)
            {
                // ---- Profile / cosmetics (backup first so tests never touch the real profile) ----
                case "profile-backup":
                {
                    string path = System.IO.Path.Combine(Application.persistentDataPath, "profile.json");
                    if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", true);
                    else System.IO.File.WriteAllText(path + ".none", "no profile before the test");
                    Debug.Log($"[Bridge] profile backed up (exists={System.IO.File.Exists(path)})");
                    break;
                }
                case "profile-restore":
                {
                    // Only ever undo a matching backup: with no backup on record, the profile is left alone
                    // (and a safety copy is always kept as profile.json.prev before anything is replaced).
                    string path = System.IO.Path.Combine(Application.persistentDataPath, "profile.json");
                    if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".prev", true);
                    if (System.IO.File.Exists(path + ".bak")) { System.IO.File.Copy(path + ".bak", path, true); System.IO.File.Delete(path + ".bak"); }
                    else if (System.IO.File.Exists(path + ".none")) { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); System.IO.File.Delete(path + ".none"); }
                    else Debug.LogWarning("[Bridge] profile-restore: no backup on record, leaving the profile as is");
                    var f = typeof(FriendSlop.Profile.PlayerProfile).GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    f?.SetValue(null, null); // force reload from disk
                    Debug.Log($"[Bridge] profile restored → bucks={FriendSlop.Profile.PlayerProfile.Current.bucks} hat={FriendSlop.Profile.PlayerProfile.Current.hat}");
                    break;
                }
                case "profile-reset":
                    FriendSlop.Profile.PlayerProfile.ResetForTesting();
                    Debug.Log("[Bridge] profile reset (test)");
                    break;
                case "profile-print":
                {
                    var p = FriendSlop.Profile.PlayerProfile.Current;
                    Debug.Log($"[Bridge] profile → name='{p.EffectiveName}' bucks={p.bucks} kills={p.kills} heads={p.headshots} hat={p.hat} acc={p.accessory} color={p.colorIndex} owned=[{string.Join(",", p.owned)}] ach=[{string.Join(",", p.achievements)}]");
                    break;
                }
                case "profile-bucks":
                    FriendSlop.Profile.PlayerProfile.Current.AddBucks(1000);
                    Debug.Log($"[Bridge] bucks → {FriendSlop.Profile.PlayerProfile.Current.bucks}");
                    break;
                case "profile-buy":
                {
                    var p = FriendSlop.Profile.PlayerProfile.Current;
                    bool ok = p.TrySpend(400);
                    if (ok) { p.Grant("tophat"); p.hat = "tophat"; p.accessory = "scarf"; p.colorIndex = 2; p.Save(); }
                    Debug.Log($"[Bridge] buy tophat → ok={ok} bucks={p.bucks}");
                    break;
                }
                case "profile-kills":
                {
                    var p = FriendSlop.Profile.PlayerProfile.Current;
                    for (int i = 0; i < 10; i++) p.RecordKill("Wolf", headshot: true, jumpShot: i == 0, longShot: i < 5, boss: false);
                    p.RecordKill("Moose", false, false, false, false);
                    Debug.Log($"[Bridge] kills recorded → ach=[{string.Join(",", p.achievements)}] owned=[{string.Join(",", p.owned)}]");
                    break;
                }
                case "profile-push":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    hp?.GetComponent<FriendSlop.Player.PlayerAppearance>()?.PushFromProfile();
                    Debug.Log("[Bridge] appearance pushed from profile");
                    break;
                }
                case "selftest-players":
                {
                    var sb = new System.Text.StringBuilder("[Bridge] players →");
                    foreach (var a in UnityEngine.Object.FindObjectsByType<FriendSlop.Player.PlayerAppearance>(FindObjectsSortMode.None))
                        sb.Append($" [{a.OwnerClientId}: '{a.DisplayName.Value}' hat={a.HatIndex.Value} acc={a.AccessoryIndex.Value} color={a.ColorIndex.Value}]");
                    Debug.Log(sb.ToString());
                    break;
                }
                case "selftest-customize":
                    FriendSlop.Profile.CustomizeMenu.IsOpen = !FriendSlop.Profile.CustomizeMenu.IsOpen;
                    Debug.Log($"[Bridge] customize open={FriendSlop.Profile.CustomizeMenu.IsOpen}");
                    break;
                case "selftest-leave":
                    NetBootstrap.Instance?.Leave();
                    Debug.Log("[Bridge] left session");
                    break;
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
                case "selftest-giveall":
                {
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    if (pw == null || !pw.IsServer) { Debug.LogWarning("[Bridge] selftest-giveall: host first"); return; }
                    pw.Owned.Value = (1 << pw.Catalog.Count) - 1;
                    Debug.Log($"[Bridge] gave all {pw.Catalog.Count} weapons");
                    break;
                }
                case "selftest-zoo":
                {
                    // Line up one of every species in front of the host for a photo (AI paused).
                    var nm = Unity.Netcode.NetworkManager.Singleton;
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    if (nm == null || !nm.IsServer || hp == null) { Debug.LogWarning("[Bridge] selftest-zoo: host first"); return; }
                    var np = hp.GetComponent<FriendSlop.Player.NetworkPlayer>();
                    np.Teleport(new Vector3(0f, 0.2f, -7f));
                    np.transform.rotation = Quaternion.identity;
                    var prefabs = new System.Collections.Generic.List<GameObject>();
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs/Animals" }))
                        prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
                    prefabs.Sort((a, b) => a.GetComponent<Animal>().Species.bodyLength.CompareTo(b.GetComponent<Animal>().Species.bodyLength));
                    _zoo.Clear();
                    int n = 0;
                    foreach (var pf in prefabs)
                    {
                        var sp = pf.GetComponent<Animal>().Species;
                        Vector3 at = sp.isBoss ? new Vector3(0f, 0f, 13f) : new Vector3(-8f + n++ * 2.3f, 0f, 1.5f + Mathf.Abs(n - 4.5f) * 0.6f);
                        var go = UnityEngine.Object.Instantiate(pf, at, Quaternion.Euler(0f, 180f, 0f));
                        go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(true);
                        var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        if (agent) agent.enabled = false; // hold still for the photo
                        _zoo.Add(go.GetComponent<Animal>());
                    }
                    Debug.Log($"[Bridge] zoo → {_zoo.Count} animals");
                    break;
                }
                case "selftest-zoo-angry":
                    foreach (var a in _zoo) if (a) a.IsAngry.Value = !a.IsAngry.Value;
                    Debug.Log("[Bridge] zoo angry toggled");
                    break;
                case "selftest-zoo-side":
                {
                    // Spread the zoo out in a row, all facing +x, for profile shots from -z.
                    var sb = new System.Text.StringBuilder();
                    float zx = -16f;
                    foreach (var a in _zoo)
                    {
                        if (!a || a.Species.isBoss) continue;
                        float len = a.Species.bodyLength * a.Species.scale;
                        zx += len * 0.5f + 1.2f;
                        a.transform.SetPositionAndRotation(new Vector3(zx, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));
                        sb.Append($"{a.Species.displayName}@{zx:0.0} ");
                        zx += len * 0.5f + 1.2f;
                    }
                    Debug.Log($"[Bridge] zoo side → {sb}");
                }
                    break;
                case "selftest-zoo-kill":
                    foreach (var a in _zoo) if (a && !a.IsDead.Value) a.ServerApplyHit(99999f, Vector3.forward * 0.2f, ShotStyle.None, 0);
                    Debug.Log("[Bridge] zoo killed");
                    break;
                case "selftest-cam":
                {
                    var cam = FriendSlop.Player.NetworkPlayer.LocalCamera;
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    var hunter = hp != null ? hp.GetComponentInChildren<FriendSlop.Player.HunterBody>() : null;
                    var anim = hunter != null ? hunter.GetComponent<Animator>() : null;
                    Debug.Log($"[Bridge] cam pos={(cam ? cam.position.ToString("0.00") : "-")} euler={(cam ? cam.eulerAngles.ToString("0.0") : "-")} local={(cam ? cam.localPosition.ToString("0.00") : "-")} player={(hp ? hp.transform.position.ToString("0.00") : "-")} " +
                              $"hunter={(hunter ? hunter.transform.localPosition.ToString("0.00") + " rot " + hunter.transform.localEulerAngles.ToString("0") : "-")} state={(anim ? anim.GetCurrentAnimatorStateInfo(0).shortNameHash.ToString() : "-")} speed={(anim ? anim.GetFloat("Speed").ToString("0.0") : "-")} grounded={(anim ? anim.GetBool("Grounded").ToString() : "-")}");
                    break;
                }
                case "art-dump-kenney":
                    ArtInspector.Dump("Assets/_Game/ThirdParty/KenneyNature");
                    break;
                case "art-dump":
                    ArtInspector.Dump("Assets/_Game/ThirdParty/QuaterniusVillage");
                    ArtInspector.Bones("Assets/_Game/ThirdParty/KayKitAdventurers/Characters/Knight.fbx");
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
                case "ui-map":
                    FriendSlop.Game.MapOverlay.IsOpen = !FriendSlop.Game.MapOverlay.IsOpen;
                    Debug.Log($"[Bridge] map open={FriendSlop.Game.MapOverlay.IsOpen}");
                    break;
                case "ui-shop-weapons": case "ui-shop-snacks": case "ui-shop-upgrades":
                    FriendSlop.Game.ShopMenu.RequestedTab = cmd.EndsWith("weapons") ? 0 : cmd.EndsWith("snacks") ? 1 : 2;
                    FriendSlop.Game.ShopMenu.Open();
                    Debug.Log("[Bridge] shop open");
                    break;
                case "ui-close":
                    FriendSlop.Game.GameMenus.Close(); FriendSlop.Game.MapOverlay.IsOpen = false;
                    break;
                case "items-buy":
                {
                    var it = FriendSlop.Player.PlayerItems.Local;
                    if (it == null) { Debug.LogWarning("[Bridge] items-buy: in a hunt"); return; }
                    it.RequestBuy(FriendSlop.Player.ItemKind.ProteinBar); it.RequestBuy(FriendSlop.Player.ItemKind.ProteinBar); it.RequestBuy(FriendSlop.Player.ItemKind.EnergyDrink);
                    Debug.Log("[Bridge] bought 2 bars + 1 drink");
                    break;
                }
                case "items-use":
                {
                    var it = FriendSlop.Player.PlayerItems.Local;
                    it?.RequestUse(FriendSlop.Player.ItemKind.ProteinBar); it?.RequestUse(FriendSlop.Player.ItemKind.EnergyDrink);
                    break;
                }
                case "items-status":
                {
                    var it = FriendSlop.Player.PlayerItems.Local; var hp = FriendSlop.Player.PlayerHealth.Local;
                    Debug.Log($"[Bridge] items bars={it?.Count(FriendSlop.Player.ItemKind.ProteinBar)} drinks={it?.Count(FriendSlop.Player.ItemKind.EnergyDrink)} boost={it?.BoostLeft:0.0}s hp={hp?.Health.Value:0}");
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
                case "selftest-shotgun":
                {
                    // One aimed shotgun blast at a zoo deer 18 m away (run selftest-giveall and selftest-zoo first).
                    var pw = FriendSlop.Weapons.PlayerWeapons.Local;
                    Animal deer = null; foreach (var a in _zoo) if (a && a.Species.displayName == "Deer" && !a.IsDead.Value) deer = a;
                    if (pw == null || deer == null) { Debug.LogWarning("[Bridge] selftest-shotgun: giveall + zoo first"); return; }
                    int sg = -1; for (int i = 0; i < pw.Catalog.Count; i++) if (pw.Catalog.Get(i).displayName == "Shotgun") sg = i;
                    pw.RequestEquip(sg);
                    var me = pw.transform.position; var fwd = pw.transform.forward; fwd.y = 0f; fwd.Normalize();
                    deer.transform.position = me + fwd * 18f;
                    var aim = deer.transform.position + Vector3.up * (deer.Species.legLength + deer.Species.bodyHeight * 0.5f) * deer.Species.scale;
                    pw.TestFireAt(aim);
                    Debug.Log($"[Bridge] shotgun fired at deer 18 m (equipped {pw.Current?.displayName})");
                    break;
                }
                case "banner-test":
                    FriendSlop.Game.GameHud.ShowBanner("LEVEL UP!", "Hunter level 11  ·  new upgrades at the gun shop", FriendSlop.Game.UiTheme.Gold);
                    FriendSlop.Audio.SfxPlayer.Play2D(FriendSlop.Audio.Sfx.Fanfare, 0.8f);
                    Debug.Log("[Bridge] banner shown");
                    break;
                case "hurt-test":
                {
                    var hp = FriendSlop.Player.PlayerHealth.Local;
                    if (hp == null || !hp.IsServer) { Debug.LogWarning("[Bridge] hurt-test: host first"); return; }
                    hp.ServerDamage(30f, Vector3.zero, "test");
                    Debug.Log($"[Bridge] hurt → {hp.Health.Value}");
                    break;
                }
                case "zoo-hp":
                {
                    var sb = new System.Text.StringBuilder("[Bridge] zoo hp →");
                    foreach (var a in _zoo) if (a) sb.Append($" {a.Species.displayName}:{(a.IsDead.Value ? "dead" : a.HealthFraction.Value.ToString("0.00"))}");
                    Debug.Log(sb.ToString());
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
