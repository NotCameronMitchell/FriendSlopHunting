using System;
using System.Collections.Generic;
using System.IO;
using FriendSlop.Weapons;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Creates the weapon assets and catalog with first-pass stats. Existing assets are left
    /// alone so hand-tuning in the Inspector survives rebuilds; only missing weapons are created
    /// and the catalog order is refreshed. Order = number keys (index 0 = pistol = key 1).
    /// </summary>
    public static class WeaponCatalogBuilder
    {
        const string Dir = "Assets/_Game/Data/Weapons";
        const string CatalogPath = "Assets/_Game/Data/WeaponCatalog.asset";

        static readonly Color Dark = new Color(0.14f, 0.14f, 0.15f);

        static IEnumerable<(string file, Action<WeaponDefinition> setup)> Recipes()
        {
            yield return ("Pistol", w =>
            {
                Name(w, "Pistol", 0, "Trusty starter. Free, accurate, unlimited ammo.", WeaponModel.Pistol);
                Fire(w, WeaponKind.Hitscan, FireMode.Semi, 0.25f, 30f, 3f, spread: 1.5f, aimSpread: 0.3f, range: 120f);
                Feel(w, ShotSound.LightGunshot, 1.25f, 0.6f, recoil: 2f, impulse: 8f, aimFov: 60f, noise: 30f);
            });
            yield return ("Revolver", w =>
            {
                Name(w, "Revolver", 60, "Six-shooter energy. Slow, hits like a truck.", WeaponModel.Revolver);
                Fire(w, WeaponKind.Hitscan, FireMode.Semi, 0.6f, 75f, 3f, spread: 1f, aimSpread: 0.15f, range: 150f);
                Feel(w, ShotSound.HeavyGunshot, 0.9f, 0.8f, recoil: 5f, impulse: 14f, aimFov: 55f, noise: 35f);
            });
            yield return ("Slingshot", w =>
            {
                Name(w, "Slingshot", 40, "Bonk. Quiet enough to sneak up on things.", WeaponModel.Slingshot);
                Fire(w, WeaponKind.Projectile, FireMode.Semi, 0.5f, 25f, 2f, spread: 0.5f, aimSpread: 0.2f, range: 80f);
                Feel(w, ShotSound.Slingshot, 1f, 0.6f, recoil: 1f, impulse: 12f, aimFov: 60f, noise: 6f);
                w.projectileSpeed = 32f; w.projectileGravity = 12f; w.tracer = false;
            });
            yield return ("Uzi", w =>
            {
                Name(w, "Uzi", 150, "Brrrt. Sprays everything within spitting distance.", WeaponModel.Uzi);
                Fire(w, WeaponKind.Hitscan, FireMode.Auto, 0.07f, 12f, 2.5f, spread: 4f, aimSpread: 2f, range: 70f);
                Feel(w, ShotSound.LightGunshot, 1.4f, 0.45f, recoil: 0.8f, impulse: 4f, aimFov: 62f, noise: 35f);
            });
            yield return ("Shotgun", w =>
            {
                Name(w, "Shotgun", 180, "Eight pellets. The ultimate carcass-juggling tool.", WeaponModel.Shotgun);
                Fire(w, WeaponKind.Hitscan, FireMode.Semi, 0.8f, 18f, 2f, spread: 4f, aimSpread: 2f, range: 60f);
                w.pellets = 8;
                Feel(w, ShotSound.HeavyGunshot, 0.8f, 0.9f, recoil: 7f, impulse: 7f, aimFov: 62f, noise: 40f);
            });
            yield return ("Boomerang", w =>
            {
                Name(w, "Boomerang", 120, "Hits on the way out AND on the way back. Catch it!", WeaponModel.Boomerang);
                Fire(w, WeaponKind.Boomerang, FireMode.Semi, 0.3f, 45f, 2f, spread: 0f, aimSpread: 0f, range: 40f);
                Feel(w, ShotSound.Throw, 1f, 0.6f, recoil: 0.5f, impulse: 10f, aimFov: 65f, noise: 8f);
                w.projectileSpeed = 22f; w.boomerangRange = 18f; w.tracer = false;
                w.tint = new Color(0.95f, 0.55f, 0.15f);
            });
            yield return ("Bow", w =>
            {
                Name(w, "Bow", 220, "Silent. Animals won't hear you coming. Arrows drop over distance.", WeaponModel.Bow);
                Fire(w, WeaponKind.Projectile, FireMode.Semi, 0.9f, 80f, 3f, spread: 0.5f, aimSpread: 0f, range: 150f);
                Feel(w, ShotSound.Bow, 1f, 0.7f, recoil: 1f, impulse: 10f, aimFov: 50f, noise: 0f);
                w.projectileSpeed = 48f; w.projectileGravity = 9.81f; w.tracer = false;
                w.tint = new Color(0.85f, 0.2f, 0.2f);
            });
            yield return ("BoltRifle", w =>
            {
                Name(w, "Bolt Rifle", 300, "Scoped, long range. Headshots basically always kill.", WeaponModel.Rifle);
                Fire(w, WeaponKind.Hitscan, FireMode.Semi, 0.9f, 60f, 8f, spread: 0.8f, aimSpread: 0f, range: 250f);
                Feel(w, ShotSound.Gunshot, 1f, 0.75f, recoil: 4f, impulse: 16f, aimFov: 30f, noise: 40f);
            });
            yield return ("MachineGun", w =>
            {
                Name(w, "Machine Gun", 500, "Accurate sustained fire. Keep that carcass in the air.", WeaponModel.MachineGun);
                Fire(w, WeaponKind.Hitscan, FireMode.Auto, 0.1f, 20f, 2.5f, spread: 2.5f, aimSpread: 1f, range: 150f);
                Feel(w, ShotSound.Gunshot, 1.1f, 0.55f, recoil: 1.2f, impulse: 6f, aimFov: 50f, noise: 45f);
                w.tint = new Color(0.35f, 0.4f, 0.22f);
            });
            yield return ("Minigun", w =>
            {
                Name(w, "Minigun", 1000, "Hold to spin up. Then: absolute chaos.", WeaponModel.Minigun);
                Fire(w, WeaponKind.Hitscan, FireMode.Auto, 0.035f, 12f, 2f, spread: 3.5f, aimSpread: 3f, range: 120f);
                w.spinUpTime = 0.9f;
                Feel(w, ShotSound.Gunshot, 1.5f, 0.4f, recoil: 0.5f, impulse: 4f, aimFov: 65f, noise: 50f);
                w.tint = new Color(0.4f, 0.42f, 0.45f);
            });
            yield return ("Bazooka", w =>
            {
                Name(w, "Bazooka", 1500, "Rocket with a big boom. Everything nearby goes flying.", WeaponModel.Bazooka);
                Fire(w, WeaponKind.Projectile, FireMode.Semi, 1.5f, 100f, 1f, spread: 0.5f, aimSpread: 0.2f, range: 200f);
                Feel(w, ShotSound.Rocket, 1f, 0.9f, recoil: 8f, impulse: 20f, aimFov: 55f, noise: 60f);
                w.projectileSpeed = 30f; w.projectileGravity = 2f; w.tracer = false;
                w.explosionRadius = 6f; w.explosionDamage = 150f; w.explosionForce = 14f;
                w.tint = new Color(0.25f, 0.35f, 0.2f);
            });
        }

        public static WeaponCatalog CreateOrUpdate()
        {
            EnsureFolder(Dir);
            var list = new List<WeaponDefinition>();
            foreach (var (file, setup) in Recipes())
            {
                string path = $"{Dir}/{file}.asset";
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<WeaponDefinition>();
                    setup(def);
                    AssetDatabase.CreateAsset(def, path);
                }
                // v2 tuning: the shotgun was near useless (7° spread, 11 dmg pellets, 45 m on a much bigger map).
                if (file == "Shotgun" && def.damage < 17f && def.spread > 3.9f)
                {
                    def.damage = 18f; def.spread = 4f; def.aimSpread = 2f; def.range = 60f;
                    EditorUtility.SetDirty(def);
                }
                list.Add(def);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.weapons = list.ToArray();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static void Name(WeaponDefinition w, string name, int price, string blurb, WeaponModel model)
        {
            w.displayName = name; w.price = price; w.blurb = blurb; w.model = model; w.tint = Dark;
        }

        static void Fire(WeaponDefinition w, WeaponKind kind, FireMode mode, float interval, float damage, float headMult,
                         float spread, float aimSpread, float range)
        {
            w.kind = kind; w.fireMode = mode; w.fireInterval = interval; w.damage = damage; w.headshotMultiplier = headMult;
            w.spread = spread; w.aimSpread = aimSpread; w.range = range; w.pellets = 1;
        }

        static void Feel(WeaponDefinition w, ShotSound sound, float pitch, float volume, float recoil, float impulse,
                         float aimFov, float noise)
        {
            w.sound = sound; w.soundPitch = pitch; w.soundVolume = volume; w.recoil = recoil; w.impulse = impulse;
            w.aimFov = aimFov; w.noiseRadius = noise;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
