using System.Collections.Generic;
using System.Linq;
using FriendSlop.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Builds HunterRig.prefab from the KayKit Adventurers pack (CC0): the Knight's skeleton with
    /// every character's mesh parts re-bound onto it (so outfits, heads, helmets and capes mix
    /// freely), anchors for primitive hats/glasses/scarves, and an animator (idle / walk / run /
    /// jump / downed) using the pack's Rig_Medium animations.
    /// </summary>
    public static class HunterRigBuilder
    {
        const string Root = "Assets/_Game/ThirdParty/KayKitAdventurers";
        const string CharDir = Root + "/Characters";
        const string AnimDir = Root + "/Animations";
        const string PrefabPath = "Assets/_Game/Prefabs/HunterRig.prefab";
        const string ControllerPath = "Assets/_Game/Resources/Hunter.controller"; // also loadable at runtime
        static readonly string[] Characters = { "Knight", "Barbarian", "Mage", "Ranger", "Rogue", "Rogue_Hooded" };
        static readonly HashSet<string> Looping = new() { "Idle_A", "Idle_B", "Walking_A", "Walking_B", "Walking_C", "Running_A", "Running_B", "Jump_Idle" };

        public static GameObject Build()
        {
            var baseAvatar = SetupImporters();
            var controller = BuildController();

            var knight = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharDir}/Knight.fbx");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(knight);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.name = "HunterRig";
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one;

            var bones = rig.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponent<SkinnedMeshRenderer>() == null)
                           .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

            foreach (var c in Characters.Skip(1))
            {
                var src = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharDir}/{c}.fbx"));
                PrefabUtility.UnpackPrefabInstance(src, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var smr in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.bones = smr.bones.Select(b => b != null && bones.TryGetValue(b.name, out var mine) ? mine : null).ToArray();
                    if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
                    smr.transform.SetParent(rig.transform, false);
                }
                Object.DestroyImmediate(src);
            }

            var parts = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var p in parts)
            {
                p.enabled = false;
                p.updateWhenOffscreen = false;
                p.localBounds = new Bounds(new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 2.8f, 2.4f)); // generous: arms swing out
            }

            // ---- anchors (measured at bind pose) ----
            var head = bones["head"];
            var chest = bones["chest"];
            // Bake at bind pose: raw skinned-mesh bounds are in bind space, not rig space.
            var headMesh = BakedBounds(parts.First(p => p.name == "Rogue_Head"));
            var bodyMesh = BakedBounds(parts.First(p => p.name == "Rogue_Body"));
            float headScale = headMesh.size.x / 0.7f * 0.92f;
            float bodyScale = bodyMesh.size.x / 0.7f;

            var hatAnchor = Anchor("HatAnchor", head, new Vector3(0f, headMesh.max.y - 0.04f, headMesh.center.z), headScale);
            var faceAnchor = Anchor("FaceAnchor", head, new Vector3(0f, headMesh.center.y - 0.02f, headMesh.max.z), headScale);
            var neckAnchor = Anchor("NeckAnchor", chest, new Vector3(0f, head.position.y - 0.02f, 0f), bodyScale);

            var animator = rig.GetComponent<Animator>() ?? rig.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = baseAvatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var body = rig.AddComponent<HunterBody>();
            var so = new SerializedObject(body);
            so.FindProperty("animator").objectReferenceValue = animator;
            var arr = so.FindProperty("parts");
            arr.arraySize = parts.Length;
            for (int i = 0; i < parts.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
            so.FindProperty("hatAnchor").objectReferenceValue = hatAnchor;
            so.FindProperty("faceAnchor").objectReferenceValue = faceAnchor;
            so.FindProperty("neckAnchor").objectReferenceValue = neckAnchor;
            so.FindProperty("upperArmR").objectReferenceValue = bones["upperarm.r"];
            so.FindProperty("lowerArmR").objectReferenceValue = bones["lowerarm.r"];
            so.FindProperty("handR").objectReferenceValue = bones["hand.r"];
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(rig, PrefabPath);
            Object.DestroyImmediate(rig);
            Debug.Log($"[Hunter] rig built: {parts.Length} parts, head {headMesh.size}, body {bodyMesh.size}");
            return prefab;
        }

        static Bounds BakedBounds(SkinnedMeshRenderer smr)
        {
            var m = new Mesh();
            smr.BakeMesh(m, true);
            var b = new Bounds(smr.transform.TransformPoint(m.vertices[0]), Vector3.zero);
            foreach (var v in m.vertices) b.Encapsulate(smr.transform.TransformPoint(v));
            Object.DestroyImmediate(m);
            return b;
        }

        static Transform Anchor(string name, Transform bone, Vector3 rigSpacePos, float scale)
        {
            var a = new GameObject(name).transform;
            a.SetParent(bone, false);
            a.position = rigSpacePos;            // rig sits at the origin, unscaled, while we build
            a.rotation = Quaternion.identity;    // aligned with the rig, not the bone
            var ls = bone.lossyScale;
            a.localScale = new Vector3(scale / ls.x, scale / ls.y, scale / ls.z);
            return a;
        }

        static Avatar SetupImporters()
        {
            string knightPath = $"{CharDir}/Knight.fbx";
            foreach (var c in Characters)
            {
                var path = $"{CharDir}/{c}.fbx";
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                bool dirty = false;
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
                if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(knightPath).OfType<Avatar>().FirstOrDefault();

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                bool dirty = false;
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
                if (mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || mi.sourceAvatar != avatar)
                {
                    mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = avatar;
                    dirty = true;
                }
                var clips = (mi.clipAnimations != null && mi.clipAnimations.Length > 0) ? mi.clipAnimations : mi.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    bool loop = Looping.Contains(clip.name);
                    if (clip.loopTime != loop) { clip.loopTime = loop; dirty = true; }
                }
                if (dirty) { mi.clipAnimations = clips; mi.SaveAndReimport(); }
            }
            return avatar;
        }

        static AnimationClip Clip(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimDir }))
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
                if (clip != null) return clip;
            }
            Debug.LogWarning($"[Hunter] missing clip {name}");
            return null;
        }

        static AnimatorController BuildController()
        {
            // Rebuild in place (never delete): prefabs keep pointing at the same asset.
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources")) AssetDatabase.CreateFolder("Assets/_Game", "Resources");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Game/Data/Hunter.controller") != null) AssetDatabase.DeleteAsset("Assets/_Game/Data/Hunter.controller");
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in ac.parameters.ToArray()) ac.RemoveParameter(p);
            var oldSm = ac.layers[0].stateMachine;
            foreach (var s in oldSm.states.ToArray()) oldSm.RemoveState(s.state);
            foreach (var old in oldSm.anyStateTransitions.ToArray()) oldSm.RemoveAnyStateTransition(old);
            foreach (var bt in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().ToArray()) Object.DestroyImmediate(bt, true);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            ac.AddParameter("Down", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;

            var loco = sm.AddState("Locomotion");
            var tree = new BlendTree { name = "Locomotion", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ac);
            tree.AddChild(Clip("Idle_A"), 0f);
            tree.AddChild(Clip("Walking_A"), 2.2f);
            tree.AddChild(Clip("Running_A"), 5.5f);
            tree.AddChild(Clip("Running_B"), 8.5f);
            loco.motion = tree;
            sm.defaultState = loco;

            var air = sm.AddState("Air");
            air.motion = Clip("Jump_Idle");
            var down = sm.AddState("Down");
            down.motion = Clip("Death_A");
            var wave = sm.AddState("Wave");
            wave.motion = Clip("Interact");

            var t = loco.AddTransition(air); t.hasExitTime = false; t.duration = 0.12f; t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            t = air.AddTransition(loco); t.hasExitTime = false; t.duration = 0.1f; t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            t = sm.AddAnyStateTransition(down); t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false; t.AddCondition(AnimatorConditionMode.If, 0, "Down");
            t = down.AddTransition(loco); t.hasExitTime = false; t.duration = 0.25f; t.AddCondition(AnimatorConditionMode.IfNot, 0, "Down");
            t = loco.AddTransition(wave); t.hasExitTime = false; t.duration = 0.15f; t.AddCondition(AnimatorConditionMode.If, 0, "Wave");
            t = wave.AddTransition(loco); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.2f;
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}
