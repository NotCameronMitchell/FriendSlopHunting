using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// One-click Windows build zipped up for friends.
    /// Menu: FriendSlop ▸ Build Windows Zip
    /// Output: Builds/Windows/ (playable folder) and Builds/FriendSlopHunting-Windows-&lt;stamp&gt;.zip
    /// </summary>
    public static class BuildTools
    {
        const string BuildRoot = "Builds";
        const string WindowsDir = BuildRoot + "/Windows";
        const string ExeName = "FriendSlopHunting.exe";

        [MenuItem("FriendSlop/Build Windows Zip", priority = 20)]
        public static void BuildWindowsZip()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("FriendSlop", "No scenes in Build Settings. Run FriendSlop ▸ Build M0 Net Test Scene first.", "OK");
                return;
            }

            if (Directory.Exists(WindowsDir)) Directory.Delete(WindowsDir, true);
            Directory.CreateDirectory(WindowsDir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(WindowsDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[FriendSlop] Build failed: {report.summary.result} ({report.summary.totalErrors} errors)");
                return;
            }

            // Lets the Steam overlay (invites) attach when the exe isn't launched through Steam.
            File.WriteAllText(Path.Combine(WindowsDir, "steam_appid.txt"), "480");

            // Unity's debug-symbol folder isn't needed by players.
            foreach (var dir in Directory.GetDirectories(WindowsDir, "*BurstDebugInformation_DoNotShip"))
                Directory.Delete(dir, true);

            string zipPath = Path.Combine(BuildRoot, $"FriendSlopHunting-Windows-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(WindowsDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            double mb = new FileInfo(zipPath).Length / (1024.0 * 1024.0);
            Debug.Log($"[FriendSlop] Build OK → {Path.GetFullPath(zipPath)} ({mb:F1} MB)");
            EditorUtility.RevealInFinder(zipPath);
        }
    }
}
