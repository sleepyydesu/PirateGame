#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
namespace PirateGame.PlayerV2.Editor
{
    public static class PlayerV2ControllerMigration
    {
        [MenuItem("Tools/Pirate Game/Player V2/Create Controller Copy From Selection")]
        public static void CreateCopy()
        {
            var source = Selection.activeObject as AnimatorController;
            if (source == null) { Debug.LogError("Select your original PlayerAnimation.controller asset first."); return; }
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string dest = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(sourcePath, null) + "_V2.controller");
            if (!AssetDatabase.CopyAsset(sourcePath, dest)) throw new InvalidOperationException("Controller copy failed.");
            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(dest);
            var layers = copy.layers;
            if (layers.Length != 4) throw new InvalidOperationException("Expected your supplied four-layer controller.");
            string[] names = { "Base Layer", "Combat Layer", "Arms Layer", "Damage Layer" };
            for (int i = 0; i < 4; i++) if (layers[i].name != names[i]) throw new InvalidOperationException("Unexpected layer names/order.");
            var upper = new AvatarMask { name = "PlayerV2_UpperBody" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) upper.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK }) upper.SetHumanoidBodyPartActive(part, true);
            string maskPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(dest, ".mask"));
            AssetDatabase.CreateAsset(upper, maskPath);
            layers[0].defaultWeight = 1f; layers[0].avatarMask = null;
            layers[1].defaultWeight = 0f; layers[1].avatarMask = null;
            layers[2].defaultWeight = 0f; layers[2].avatarMask = upper;
            layers[3].defaultWeight = 0f; layers[3].avatarMask = upper;
            foreach (var l in layers) l.blendingMode = AnimatorLayerBlendingMode.Override;
            foreach (string name in new[] { "combat_attack", "combat_attack2" })
            {
                AnimatorState original = Find(layers[1].stateMachine, name);
                if (original == null) throw new InvalidOperationException("Missing original move " + name);
                AnimatorState added = Find(layers[2].stateMachine, name) ?? layers[2].stateMachine.AddState(name);
                added.motion = original.motion; added.speed = 1f;
            }
            // Direct CrossFade is the single transition authority for the NEW controller copy.
            string parent = System.IO.Path.GetDirectoryName(dest).Replace('\\', '/');
            string clipsName = System.IO.Path.GetFileNameWithoutExtension(dest) + "_Clips";
            string clipsFolder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + clipsName);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(clipsFolder));
            var clips = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var layer in layers) { CopyActionClips(layer.stateMachine, clipsFolder, clips); Clean(layer.stateMachine); }
            layers[0].stateMachine.defaultState = Find(layers[0].stateMachine, "Locomotion_Blend_Tree");
            for (int i = 1; i < 4; i++) layers[i].stateMachine.defaultState = Find(layers[i].stateMachine, "Default");
            copy.layers = layers; EditorUtility.SetDirty(copy); AssetDatabase.SaveAssets();
            Selection.activeObject = copy;
            Debug.Log("Created " + dest + ". Action clips were copied and their events cleared. Author AE_ events on the NEW clips. Locomotion blend trees remain shared.");
        }
        private static void CopyActionClips(AnimatorStateMachine machine, string folder, Dictionary<AnimationClip, AnimationClip> copied)
        {
            foreach (var item in machine.states)
            {
                if (!(item.state.motion is AnimationClip original)) continue;
                if (!copied.TryGetValue(original, out var clip))
                {
                    clip = UnityEngine.Object.Instantiate(original); clip.name = original.name + "_V2";
                    AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                    if (item.state.name != "Combat_Defense")
                    {
                        var settings = AnimationUtility.GetAnimationClipSettings(clip);
                        settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, settings);
                    }
                    AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + clip.name.Replace('/', '_') + ".anim"));
                    copied.Add(original, clip);
                }
                item.state.motion = clip;
            }
            foreach (var child in machine.stateMachines) CopyActionClips(child.stateMachine, folder, copied);
        }
        private static AnimatorState Find(AnimatorStateMachine machine, string name) =>
            machine.states.Select(x => x.state).FirstOrDefault(x => x.name == name);
        private static void Clean(AnimatorStateMachine machine)
        {
            foreach (var t in machine.anyStateTransitions.ToArray()) machine.RemoveAnyStateTransition(t);
            foreach (var t in machine.entryTransitions.ToArray()) machine.RemoveEntryTransition(t);
            foreach (var item in machine.states)
            {
                var state = item.state;
                foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
                state.writeDefaultValues = false; state.speed = 1f;
                if (state.motion != null && !(state.motion is BlendTree) &&
                    !state.behaviours.Any(b => b is PlayerActionAnimationState))
                    state.AddStateMachineBehaviour<PlayerActionAnimationState>();
                EditorUtility.SetDirty(state);
            }
            foreach (var child in machine.stateMachines) Clean(child.stateMachine);
        }
        [MenuItem("Tools/Pirate Game/Player V2/Create Starter Attack Assets")]
        public static void CreateAttacks()
        {
            string folder = EditorUtility.SaveFolderPanel("Save new attack assets inside Assets", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder)) return;
            string assetsRoot = Application.dataPath.Replace('\\', '/'); folder = folder.Replace('\\', '/');
            if (folder != assetsRoot && !folder.StartsWith(assetsRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Choose a folder inside this project's Assets.");
            string relative = "Assets" + folder.Substring(assetsRoot.Length);
            PlayerAttackDefinition Make(string name, string path, int layer, float movement, float damage)
            {
                var a = ScriptableObject.CreateInstance<PlayerAttackDefinition>(); a.fullStatePath = path;
                a.layer = layer; a.movementScale = movement; a.damage = damage;
                AssetDatabase.CreateAsset(a, AssetDatabase.GenerateUniqueAssetPath(relative + "/" + name + ".asset")); return a;
            }
            var first = Make("LightCut1", "Arms Layer.combat_attack", 2, 0.46f, 20f);
            var second = Make("LightCut2", "Arms Layer.combat_attack2", 2, 0.46f, 22f);
            first.followup = second; second.followup = first;
            Make("Special", "Combat Layer.Combat_SpecialAttack", 1, 0f, 35f);
            Make("Thrust", "Combat Layer.Combat_Thrust", 1, 0.15f, 28f);
            Make("Special2", "Combat Layer.Combat_SpecialAttack2", 1, 0f, 40f);
            EditorUtility.SetDirty(first); EditorUtility.SetDirty(second); AssetDatabase.SaveAssets();
        }
    }
}
#endif
