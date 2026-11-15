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

        static void Run(string cmd)
        {
            switch (cmd)
            {
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
