using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PirateGame.EditorTools
{
    /// <summary>
    /// Switches the pirate from a Humanoid rig (clips stored as muscle values, not
    /// hand-editable) to a Generic rig (clips stored as plain bone transform curves,
    /// fully editable in the Animation window).
    ///
    /// Run: Tools > Pirate Game > Convert Pirate To Editable Animation
    ///
    /// It does the steps in the only order that works:
    ///   1. bake the humanoid clips to generic ones  (needs the rig still Humanoid)
    ///   2. switch Pirate.FBX to Generic
    ///   3. point the animator controller at the baked clips
    ///   4. clear the now-meaningless humanoid Avatar off the enemies
    ///
    /// AFTER THIS: open any clip in Assets/Animations/Editable with an enemy selected
    /// and you get one row per bone. Nothing overwrites those files unless you run the
    /// bake again, so your hand edits are safe.
    ///
    /// TRADE-OFF: a Generic rig cannot retarget animation from other characters, so
    /// Mixamo-style clips will no longer drop onto this pirate. Reverse it by setting
    /// Pirate.FBX > Rig > Animation Type back to Humanoid.
    /// </summary>
    public static class PirateRigConverter
    {
        private const string ModelPath = "Assets/Pirate/Mesh/Pirate.FBX";
        private const string ControllerPath = "Assets/Animations/EnemyAnimator.controller";
        private const string EditableDir = "Assets/Animations/Editable";

        [MenuItem("Tools/Pirate Game/Convert Pirate To Editable Animation")]
        public static void Convert()
        {
            // --- 1. bake while the rig is still humanoid -------------------------
            PirateAnimationBaker.Bake();

            AnimationClip Baked(string n) =>
                AssetDatabase.LoadAssetAtPath<AnimationClip>(EditableDir + "/" + n + ".anim");

            if (Baked("Enemy_Walk") == null)
            {
                Debug.LogError("Bake produced nothing - aborting before the rig is touched.");
                return;
            }

            // --- 2. rig to Generic ----------------------------------------------
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.SaveAndReimport();
            Debug.Log("Pirate.FBX rig -> Generic");

            // --- 3. controller uses the editable clips ---------------------------
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var sm = ac.layers[0].stateMachine;

            var loco = sm.states.First(s => s.state.name == "Locomotion").state;
            var tree = (BlendTree)loco.motion;
            tree.children = new[]
            {
                new ChildMotion { motion = Baked("Enemy_Idle"), threshold = 0f,   timeScale = 1f },
                new ChildMotion { motion = Baked("Enemy_Walk"), threshold = 0.5f, timeScale = 1f },
                new ChildMotion { motion = Baked("Enemy_Run"),  threshold = 1f,   timeScale = 1f },
            };

            sm.states.First(s => s.state.name == "Attack").state.motion  = Baked("Enemy_Attack");
            sm.states.First(s => s.state.name == "Hit").state.motion     = Baked("Enemy_Hit");
            sm.states.First(s => s.state.name == "Parried").state.motion = Baked("Enemy_Parried");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssetIfDirty(ac);
            Debug.Log("Animator controller -> editable clips");

            // --- 4. drop the humanoid avatar and the runtime bob ------------------
            // The dip is baked into the walk/run clips now, so BodyBob would double it.
            var generic = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();

            void FixUp(GameObject root)
            {
                var an = root.GetComponent<Animator>();
                if (an != null) an.avatar = generic;
                var bob = root.GetComponent<PirateGame.Enemies.BodyBob>();
                if (bob != null) Object.DestroyImmediate(bob, true);
            }

            const string prefabPath = "Assets/Prefabs/Enemies/Enemy_Pirate.prefab";
            var pr = PrefabUtility.LoadPrefabContents(prefabPath);
            FixUp(pr);
            PrefabUtility.SaveAsPrefabAsset(pr, prefabPath);
            PrefabUtility.UnloadPrefabContents(pr);

            foreach (var ai in Object.FindObjectsByType<PirateGame.Enemies.EnemyAI>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var an = ai.GetComponent<Animator>();
                if (an != null) an.avatar = generic;
                var bob = ai.GetComponent<PirateGame.Enemies.BodyBob>();
                if (bob != null) Object.DestroyImmediate(bob);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Done. Edit clips in " + EditableDir + " - one row per bone.");
        }
    }
}
