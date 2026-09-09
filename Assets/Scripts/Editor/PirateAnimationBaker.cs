using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PirateGame.EditorTools
{
    /// <summary>
    /// Converts the generated HUMANOID clips into GENERIC clips you can open in the
    /// Animation window and edit by hand.
    ///
    /// WHY: humanoid clips are stored as 95 normalised muscle values, so the dopesheet
    /// shows rows like "Right Arm Down-Up" instead of bones, and Unity cannot author
    /// them by posing the rig. Generic clips hold plain transform curves, so every bone
    /// shows up as "…/upperarm_r : Rotation" and you can scrub, key and drag normally.
    ///
    /// WHAT IT DOES: plays each source clip on the humanoid rig one frame at a time,
    /// reads every bone's local rotation, and writes those out as curves. The walk and
    /// run also get their hip dip baked in here, so it lives in the clip instead of in
    /// a script - one place to edit.
    ///
    /// ORDER MATTERS: bake BEFORE switching the model's rig to Generic, because the
    /// source clips can only be played while the rig is still Humanoid. The menu item
    /// "Convert Pirate To Editable Animation" does both steps in the right order.
    /// </summary>
    public static class PirateAnimationBaker
    {
        private const string ModelPath = "Assets/Pirate/Mesh/Pirate.FBX";
        private const string SourceDir = "Assets/Animations/Generated";
        private const string OutputDir = "Assets/Animations/Editable";
        private const float FrameRate = 30f;

        /// <summary>Clips to bake: source path, output name, loop, and hip dip in metres.</summary>
        private static readonly (string source, string name, bool loop, float hipDip)[] Clips =
        {
            (SourceDir + "/Enemy_Idle.anim",   "Enemy_Idle",   true,  0f),
            (SourceDir + "/Enemy_Walk.anim",   "Enemy_Walk",   true,  0.035f),
            (SourceDir + "/Enemy_Run.anim",    "Enemy_Run",    true,  0.075f),
            (SourceDir + "/Enemy_Attack.anim", "Enemy_Attack", false, 0f),
            // The two bought reaction clips are humanoid too, so they need baking as
            // well or they stop playing once the rig is Generic.
            ("Assets/EEJANAI_Team/FreeSwordAnimations/Animations/damaged (tired) stance.anim",
                "Enemy_Hit", false, 0f),
            ("Assets/EEJANAI_Team/FreeSwordAnimations/Animations/deffensive stance.anim",
                "Enemy_Parried", false, 0f),
        };

        [MenuItem("Tools/Pirate Game/Bake Editable (Generic) Clips")]
        public static void Bake()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError("Pirate model missing at " + ModelPath); return; }

            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                Debug.LogError("Bake needs the rig to still be Humanoid so the source clips can play. " +
                               "Set Pirate.FBX > Rig > Humanoid, bake, then convert to Generic.");
                return;
            }

            Avatar avatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is Avatar a) avatar = a;

            var rig = (GameObject)Object.Instantiate(model);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.hideFlags = HideFlags.HideAndDontSave;
            var animator = rig.GetComponent<Animator>();
            if (animator == null) animator = rig.AddComponent<Animator>();
            animator.avatar = avatar;

            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/Animations", "Editable");

            // Every bone under "root", with its path relative to the model root -
            // that is what an animation curve binds to.
            var bones = new List<(Transform t, string path)>();
            CollectBones(rig.transform, rig.transform, bones);

            foreach (var spec in Clips)
            {
                var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.source);
                if (source == null) { Debug.LogWarning("Skipping missing clip " + spec.source); continue; }
                BakeOne(source, spec.name, spec.loop, spec.hipDip, rig, bones);
            }

            Object.DestroyImmediate(rig);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Baked editable clips into " + OutputDir);
        }

        private static void CollectBones(Transform t, Transform root, List<(Transform, string)> into)
        {
            foreach (Transform child in t)
            {
                // Skip the skinned meshes; only the skeleton needs curves.
                if (child.GetComponent<Renderer>() == null)
                    into.Add((child, AnimationUtility.CalculateTransformPath(child, root)));
                CollectBones(child, root, into);
            }
        }

        private static void BakeOne(AnimationClip source, string name, bool loop, float hipDip,
                                    GameObject rig, List<(Transform t, string path)> bones)
        {
            int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * FrameRate));
            float step = source.length / (frames - 1);

            var rotX = new Dictionary<string, AnimationCurve>();
            var rotY = new Dictionary<string, AnimationCurve>();
            var rotZ = new Dictionary<string, AnimationCurve>();
            var rotW = new Dictionary<string, AnimationCurve>();
            foreach (var b in bones)
            {
                rotX[b.path] = new AnimationCurve();
                rotY[b.path] = new AnimationCurve();
                rotZ[b.path] = new AnimationCurve();
                rotW[b.path] = new AnimationCurve();
            }

            // The hips also carry position, which is where the walk/run dip lives.
            var hips = bones.Find(b => b.t.name == "pelvis");
            var posX = new AnimationCurve();
            var posY = new AnimationCurve();
            var posZ = new AnimationCurve();

            for (int f = 0; f < frames; f++)
            {
                float t = step * f;
                source.SampleAnimation(rig, t);

                foreach (var b in bones)
                {
                    Quaternion q = b.t.localRotation;
                    rotX[b.path].AddKey(new Keyframe(t, q.x));
                    rotY[b.path].AddKey(new Keyframe(t, q.y));
                    rotZ[b.path].AddKey(new Keyframe(t, q.z));
                    rotW[b.path].AddKey(new Keyframe(t, q.w));
                }

                if (hips.t != null)
                {
                    Vector3 p = hips.t.localPosition;

                    // Same footstep dip BodyBob used to apply at runtime: two dips per
                    // stride, lowest as a foot lands, never lifting above the pose.
                    if (hipDip > 0f && source.length > 0f)
                    {
                        float phase = (t / source.length) * Mathf.PI * 4f;
                        p.y += (Mathf.Cos(phase) - 1f) * 0.5f * hipDip;
                    }

                    posX.AddKey(new Keyframe(t, p.x));
                    posY.AddKey(new Keyframe(t, p.y));
                    posZ.AddKey(new Keyframe(t, p.z));
                }
            }

            string path = OutputDir + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = FrameRate;

            foreach (var b in bones)
            {
                clip.SetCurve(b.path, typeof(Transform), "m_LocalRotation.x", rotX[b.path]);
                clip.SetCurve(b.path, typeof(Transform), "m_LocalRotation.y", rotY[b.path]);
                clip.SetCurve(b.path, typeof(Transform), "m_LocalRotation.z", rotZ[b.path]);
                clip.SetCurve(b.path, typeof(Transform), "m_LocalRotation.w", rotW[b.path]);
            }
            if (hips.t != null)
            {
                clip.SetCurve(hips.path, typeof(Transform), "m_LocalPosition.x", posX);
                clip.SetCurve(hips.path, typeof(Transform), "m_LocalPosition.y", posY);
                clip.SetCurve(hips.path, typeof(Transform), "m_LocalPosition.z", posZ);
            }

            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (isNew) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);

            Debug.Log("  baked " + name + " (" + frames + " frames, " + bones.Count + " bones, loop=" + loop + ")");
        }
    }
}
