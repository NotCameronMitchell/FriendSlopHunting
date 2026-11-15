using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FriendSlop.Profile
{
    /// <summary>
    /// Your persistent hunter: name, career bucks (cosmetics currency that survives between
    /// hunts), lifetime stats, unlocked cosmetics and the outfit you wear. Saved as JSON on this
    /// PC (each friend has their own). Team money stays per-session; a cut of every sale your
    /// team makes is banked here as bucks.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public const float BucksPerDollar = 0.25f; // you bank 25% of every team sale

        public string displayName = "";
        public int bucks;
        public int lifetimeBucks;

        // Stats (achievements read these)
        public int kills;
        public int headshots;
        public int jumpShotKills;
        public int longShotKills;
        public int bossKills;
        public int xp; // hunter experience (levels unlock upgrades)
        public int moneyEarnedWithTeam;
        public List<string> speciesKilled = new List<string>();
        public List<SpeciesCount> killsBySpecies = new List<SpeciesCount>();

        // Cosmetics
        public List<string> owned = new List<string>();
        public List<string> achievements = new List<string>();
        public string outfit = Cosmetics.DefaultOutfit;
        public string head = Cosmetics.DefaultHead;
        public string hat = "none";
        public string accessory = "none";
        public int colorIndex = -1; // -1 = auto (based on player id)

        [Serializable]
        public struct SpeciesCount
        {
            public string species;
            public int count;
        }

        // ------------------------------------------------------------------ access

        static PlayerProfile _current;
        static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, "profile.json");

        /// <summary>Fires after any change (bucks, unlocks, outfit…).</summary>
        public static event Action Changed;

        /// <summary>Fires when an achievement unlocks: (id, display name).</summary>
        public static event Action<string, string> AchievementUnlocked;

        /// <summary>Fires when you reach a new hunter level (new level).</summary>
        public static event Action<int> LevelUp;

        /// <summary>Total XP needed to reach a level (Lv1 = 0, Lv2 = 100, Lv3 = 300, Lv4 = 600, ... Lv10 = 4,500).</summary>
        public static int XpForLevel(int level) => 50 * level * (level - 1);
        public const int MaxLevel = 30;

        public int Level
        {
            get
            {
                int l = 1;
                while (l < MaxLevel && xp >= XpForLevel(l + 1)) l++;
                return l;
            }
        }

        /// <summary>Earn XP; fires <see cref="LevelUp"/> for each level gained. Saves.</summary>
        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            int before = Level;
            xp += amount;
            int after = Level;
            Save();
            for (int l = before + 1; l <= after; l++) LevelUp?.Invoke(l);
        }

        public static PlayerProfile Current
        {
            get
            {
                if (_current != null) return _current;
                _current = Load();
                return _current;
            }
        }

        static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(PathOnDisk));
                    if (p != null) return p.Sanitized();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Profile] Couldn't read profile, starting fresh: {e.Message}");
            }
            return new PlayerProfile().Sanitized();
        }

        PlayerProfile Sanitized()
        {
            owned ??= new List<string>();
            achievements ??= new List<string>();
            speciesKilled ??= new List<string>();
            killsBySpecies ??= new List<SpeciesCount>();
            if (string.IsNullOrWhiteSpace(outfit)) outfit = Cosmetics.DefaultOutfit;
            if (string.IsNullOrWhiteSpace(head)) head = Cosmetics.DefaultHead;
            if (string.IsNullOrWhiteSpace(hat)) hat = "none";
            if (string.IsNullOrWhiteSpace(accessory)) accessory = "none";
            return this;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(PathOnDisk, JsonUtility.ToJson(this, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Profile] Couldn't save profile: {e.Message}");
            }
            Changed?.Invoke();
        }

        /// <summary>Name shown to friends; falls back to the Steam name, then "Hunter".</summary>
        public string EffectiveName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
                string steam = FriendSlop.Net.Steam.SteamService.IsReady ? FriendSlop.Net.Steam.SteamService.PersonaName : null;
                return string.IsNullOrWhiteSpace(steam) ? "Hunter" : steam;
            }
        }

        // ------------------------------------------------------------------ economy

        public void AddBucks(int amount)
        {
            if (amount <= 0) return;
            bucks += amount;
            lifetimeBucks += amount;
            Save();
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || bucks < amount) return false;
            bucks -= amount;
            Save();
            return true;
        }

        public bool Owns(string cosmeticId) => cosmeticId == "none" || owned.Contains(cosmeticId);

        public void Grant(string cosmeticId)
        {
            if (string.IsNullOrEmpty(cosmeticId) || owned.Contains(cosmeticId)) return;
            owned.Add(cosmeticId);
            Save();
        }

        // ------------------------------------------------------------------ stats

        public int KillsOf(string species)
        {
            foreach (var c in killsBySpecies) if (c.species == species) return c.count;
            return 0;
        }

        public void RecordKill(string species, bool headshot, bool jumpShot, bool longShot, bool boss)
        {
            kills++;
            if (headshot) headshots++;
            if (jumpShot) jumpShotKills++;
            if (longShot) longShotKills++;
            if (boss) bossKills++;
            if (!speciesKilled.Contains(species)) speciesKilled.Add(species);
            bool found = false;
            for (int i = 0; i < killsBySpecies.Count; i++)
            {
                if (killsBySpecies[i].species != species) continue;
                var c = killsBySpecies[i];
                c.count++;
                killsBySpecies[i] = c;
                found = true;
                break;
            }
            if (!found) killsBySpecies.Add(new SpeciesCount { species = species, count = 1 });
            CheckAchievements();
            Save();
        }

        public void RecordTeamEarnings(int dollars)
        {
            if (dollars <= 0) return;
            moneyEarnedWithTeam += dollars;
            bucks += Mathf.Max(1, Mathf.RoundToInt(dollars * BucksPerDollar));
            lifetimeBucks += Mathf.Max(1, Mathf.RoundToInt(dollars * BucksPerDollar));
            CheckAchievements();
            Save();
            AddXp(Mathf.Max(1, dollars / 4)); // selling levels you up too
        }

        /// <summary>Unlock any achievement whose condition is now met (and its reward).</summary>
        public void CheckAchievements()
        {
            foreach (var a in Cosmetics.Achievements)
            {
                if (achievements.Contains(a.Id) || !a.IsMet(this)) continue;
                achievements.Add(a.Id);
                if (!string.IsNullOrEmpty(a.RewardId) && !owned.Contains(a.RewardId)) owned.Add(a.RewardId);
                AchievementUnlocked?.Invoke(a.Id, a.Name);
            }
        }

        /// <summary>Dev/testing: wipe the profile file.</summary>
        public static void ResetForTesting()
        {
            _current = new PlayerProfile().Sanitized();
            _current.Save();
        }
    }
}
