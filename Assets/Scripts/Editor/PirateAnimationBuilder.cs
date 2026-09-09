using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PirateGame.EditorTools
{
    /// <summary>
    /// Generates the enemy's Idle / Run / Attack animation clips from code.
    ///
    /// WHY THIS EXISTS: the pirate model ships with no usable animations, and the
    /// bought sword clips are built for a different character. Rather than hand-key
    /// in the Animation window, poses are described here in plain bone angles and
    /// baked into HUMANOID clips, so they retarget to any humanoid rig later
    /// (the player, the boss, the smuggler variant...).
    ///
    /// HOW IT WORKS: each pose is a set of rotations expressed in CHARACTER space
    /// (X = the character's right, Y = up, Z = forward). Rotations compound down the
    /// skeleton, so writing upperarm +30 and lowerarm +50 gives the forearm a total
    /// +80 swing, which is how you'd think about it by eye. Each posed frame is fed
    /// through HumanPoseHandler to read Unity's normalised muscle values, and those
    /// become the clip's curves.
    ///
    /// Run it from the menu: Tools > Pirate Game > Rebuild Enemy Animations
    /// Tweak the numbers in BuildIdle/BuildRun/BuildAttack and re-run.
    /// </summary>
    public static class PirateAnimationBuilder
    {
        // NOTE: you cannot author hip height directly on a humanoid rig. Unity's
        // HumanPoseHandler reconstructs bodyPosition entirely from the LEG POSE -
        // measured: translating the mapped Hips bone by 100mm changes bodyPosition by
        // exactly 0mm. So the up-and-down of a walk has to come from the legs
        // straightening and folding, which is how it works on a real body anyway.
        // That is what Gait()'s "absorb" term is for.

        /// <summary>
        /// Which way the blade leaves the fist, in hand-local space.
        /// A handle does not run along the fingers - it runs through the closed fist,
        /// roughly along the knuckle line (measured on this rig: pinky->index is
        /// (0.27, 0.07, 0.96)), angled toward the fingers the way a sabre sits
        /// diagonally across the palm. Aligning it with the fingers instead is what
        /// left the sword hovering outside the hand.
        /// </summary>
        public static readonly Vector3 GripBladeAxis = new Vector3(0.81f, -0.02f, 0.58f).normalized;

        /// <summary>Middle of the closed fist, in hand-local space.</summary>
        public static readonly Vector3 GripFistCentre = new Vector3(0.111f, -0.020f, -0.013f);

        /// <summary>How far down the sword the hand grips (the middle of the handle).</summary>
        public const float GripHandleOffset = 0.15f;

        private const string ModelPath = "Assets/Pirate/Mesh/Pirate.FBX";
        private const string OutDir   = "Assets/Animations/Generated";

        // Character-space axes. The rig is authored facing +Z.
        private static readonly Vector3 Right = Vector3.right;   // +X
        private static readonly Vector3 Up    = Vector3.up;      // +Y
        private static readonly Vector3 Fwdz  = Vector3.forward; // +Z

        // Skeleton, parents before children so rotations can compound.
        private static readonly string[,] Chain =
        {
            { "pelvis",     null        },
            { "spine_01",   "pelvis"    },
            { "spine_02",   "spine_01"  },
            { "spine_03",   "spine_02"  },
            { "neck_01",    "spine_03"  },
            { "head",       "neck_01"   },
            { "clavicle_l", "spine_03"  },
            { "upperarm_l", "clavicle_l"},
            { "lowerarm_l", "upperarm_l"},
            { "hand_l",     "lowerarm_l"},
            { "clavicle_r", "spine_03"  },
            { "upperarm_r", "clavicle_r"},
            { "lowerarm_r", "upperarm_r"},
            { "hand_r",     "lowerarm_r"},
            { "thigh_l",    "pelvis"    },
            { "calf_l",     "thigh_l"   },
            { "foot_l",     "calf_l"    },
            { "ball_l",     "foot_l"    },
            { "thigh_r",    "pelvis"    },
            { "calf_r",     "thigh_r"   },
            { "foot_r",     "calf_r"    },
            { "ball_r",     "foot_r"    },
        };

        /// <summary>First child of each bone, used to measure which way a bone points.</summary>
        private static readonly Dictionary<string, string> ChildOf = BuildChildMap();

        private static Dictionary<string, string> BuildChildMap()
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i < Chain.GetLength(0); i++)
            {
                string parent = Chain[i, 1];
                if (parent != null && !map.ContainsKey(parent)) map[parent] = Chain[i, 0];
            }
            return map;
        }

        // ------------------------------------------------------------------ pose

        /// <summary>One keyframe: per-bone rotations relative to the bind pose.</summary>
        private class Pose
        {
            public readonly Dictionary<string, Quaternion> Rot = new Dictionary<string, Quaternion>();

            /// <summary>
            /// Bones whose final direction is stated outright, in character space
            /// (+X right, +Y up, +Z the way the character faces). Far easier to reason
            /// about than stacked euler angles: you say where the limb points and the
            /// builder works out the rotation, after the torso has had its say.
            /// </summary>
            public readonly Dictionary<string, Vector3> Aim = new Dictionary<string, Vector3>();

            /// <summary>
            /// Elbow flexion in degrees, 0 = straight. A real elbow is a HINGE: it bends
            /// on one axis only and cannot hyperextend. Aiming the forearm freely (as
            /// this used to) let it bend sideways and backwards, which is what made the
            /// sword swing look like nothing a body could do.
            /// </summary>
            public readonly Dictionary<string, float> ElbowBend = new Dictionary<string, float>();

            /// <summary>Where the held sword's blade should point, in character space.</summary>
            public Vector3 BladeAim = Vector3.zero;

            public Vector3 PelvisOffset;   // metres, for the vertical bob / crouch

            /// <summary>Rotate a bone by `deg` around a character-space axis.</summary>
            public Pose R(string bone, Vector3 axis, float deg)
            {
                Quaternion q = Quaternion.AngleAxis(deg, axis);
                Rot[bone] = Rot.TryGetValue(bone, out var cur) ? q * cur : q;
                return this;
            }

            // The rig binds in a T-POSE: arms point along +/-X, legs and spine along
            // -/+Y. Every helper below is written for that starting point, so the
            // numbers in the poses read as "how far from T-pose", not raw euler angles.

            /// <summary>Swing a DOWN-pointing limb (arm, leg) forward (+) or back (-).</summary>
            public Pose Fwd(string bone, float deg) => R(bone, Right, -deg);

            /// <summary>Lean an UP-pointing part (spine, neck, head) forward (+) or back (-).</summary>
            public Pose Lean(string bone, float deg) => R(bone, Right, deg);

            /// <summary>Bend a knee (foot travels backwards). Positive = more bend.</summary>
            public Pose Knee(string bone, float deg) => R(bone, Right, deg);

            /// <summary>Rotate a limb toward the character's RIGHT (+) or LEFT (-).</summary>
            public Pose Side(string bone, float deg) => R(bone, Fwdz, deg);

            /// <summary>Drop an arm from the T-pose: 0 = straight out, 90 = hanging down.</summary>
            public Pose ArmDown(bool right, float deg) =>
                R(right ? "upperarm_r" : "upperarm_l", Fwdz, right ? -deg : deg);

            /// <summary>Twist around the vertical axis: + turns to the character's right.</summary>
            public Pose Twist(string bone, float deg) => R(bone, Up, deg);

            /// <summary>Tilt sideways: negative drops the character's RIGHT hip/shoulder.</summary>
            public Pose Roll(string bone, float deg) => R(bone, Fwdz, deg);

            /// <summary>Aim a bone along a direction in character space.</summary>
            public Pose Point(string bone, float x, float y, float z)
            {
                Aim[bone] = new Vector3(x, y, z).normalized;
                return this;
            }

            /// <summary>Bend an elbow. 0 = straight, ~90 = right angle. Clamped to a real range.</summary>
            public Pose Elbow(string bone, float deg)
            {
                ElbowBend[bone] = Mathf.Clamp(deg, 0f, 145f);
                return this;
            }

            /// <summary>Point the sword blade in a direction in character space.</summary>
            public Pose Blade(float x, float y, float z)
            {
                BladeAim = new Vector3(x, y, z).normalized;
                return this;
            }

            /// <summary>Aim a limb using an angle from straight down: + swings forward.</summary>
            public Pose PointLeg(string bone, float degFromDown, float sideways = 0f)
            {
                float r = degFromDown * Mathf.Deg2Rad;
                return Point(bone, sideways, -Mathf.Cos(r), Mathf.Sin(r));
            }

            /// <summary>
            /// Kept only so poses read naturally. Has NO effect: hip height on a
            /// humanoid rig comes from the leg pose, not from moving the hips.
            /// To make a pose sit lower, bend the knees instead.
            /// </summary>
            public Pose Lift(float metres) { PelvisOffset += Vector3.up * metres; return this; }
        }

        private class Frame
        {
            public float Time;
            public Pose Pose;
        }

        // ------------------------------------------------------------------ entry point

        [MenuItem("Tools/Pirate Game/Rebuild Enemy Animations")]
        public static void Rebuild()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError("Pirate model not found at " + ModelPath); return; }

            Avatar avatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is Avatar a) avatar = a;
            if (avatar == null || !avatar.isHuman)
            {
                Debug.LogError("Pirate.FBX needs a valid HUMANOID avatar. Set Rig > Animation Type = Humanoid.");
                return;
            }

            // A throwaway instance we pose and sample. Identity transform at the
            // origin so character space and world space line up.
            var rig = (GameObject)Object.Instantiate(model);
            rig.name = "__PirateAnimRig";
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var bones = new Dictionary<string, Transform>();
                foreach (var t in rig.GetComponentsInChildren<Transform>()) bones[t.name] = t;

                // Remember the bind pose so every frame starts from a clean slate.
                var bindLocalRot = new Dictionary<string, Quaternion>();
                var bindLocalPos = new Dictionary<string, Vector3>();
                var bindWorldRot = new Dictionary<string, Quaternion>();
                for (int i = 0; i < Chain.GetLength(0); i++)
                {
                    string n = Chain[i, 0];
                    if (!bones.ContainsKey(n)) { Debug.LogError("Missing bone: " + n); return; }
                    bindLocalRot[n] = bones[n].localRotation;
                    bindLocalPos[n] = bones[n].localPosition;
                    bindWorldRot[n] = bones[n].rotation;
                }

                var handler = new HumanPoseHandler(avatar, rig.transform);

                if (!AssetDatabase.IsValidFolder(OutDir))
                    AssetDatabase.CreateFolder("Assets/Animations", "Generated");

                Bake("Enemy_Idle",   BuildIdle(),   true,  rig, bones, bindLocalRot, bindLocalPos, bindWorldRot, handler);
                Bake("Enemy_Walk",   BuildWalk(),   true,  rig, bones, bindLocalRot, bindLocalPos, bindWorldRot, handler);
                Bake("Enemy_Run",    BuildRun(),    true,  rig, bones, bindLocalRot, bindLocalPos, bindWorldRot, handler);
                Bake("Enemy_Attack", BuildAttack(), false, rig, bones, bindLocalRot, bindLocalPos, bindWorldRot, handler);

                handler.Dispose();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Rebuilt enemy animations into " + OutDir);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        // ------------------------------------------------------------------ baking

        private static void Bake(string name, List<Frame> frames, bool loop,
            GameObject rig, Dictionary<string, Transform> bones,
            Dictionary<string, Quaternion> bindLocalRot,
            Dictionary<string, Vector3> bindLocalPos,
            Dictionary<string, Quaternion> bindWorldRot,
            HumanPoseHandler handler)
        {
            int muscleCount = HumanTrait.MuscleCount;
            var muscleKeys = new List<Keyframe>[muscleCount];
            for (int m = 0; m < muscleCount; m++) muscleKeys[m] = new List<Keyframe>();
            var rootTKeys = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
            var rootQKeys = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };

            var pose = new HumanPose();

            foreach (var f in frames)
            {
                ApplyPose(f.Pose, bones, bindLocalRot, bindLocalPos, bindWorldRot);
                handler.GetHumanPose(ref pose);

                CloseSwordHand(pose.muscles);

                for (int m = 0; m < muscleCount; m++)
                    muscleKeys[m].Add(new Keyframe(f.Time, pose.muscles[m]));

                rootTKeys[0].Add(new Keyframe(f.Time, pose.bodyPosition.x));
                rootTKeys[1].Add(new Keyframe(f.Time, pose.bodyPosition.y));
                rootTKeys[2].Add(new Keyframe(f.Time, pose.bodyPosition.z));
                rootQKeys[0].Add(new Keyframe(f.Time, pose.bodyRotation.x));
                rootQKeys[1].Add(new Keyframe(f.Time, pose.bodyRotation.y));
                rootQKeys[2].Add(new Keyframe(f.Time, pose.bodyRotation.z));
                rootQKeys[3].Add(new Keyframe(f.Time, pose.bodyRotation.w));
            }

            string path = OutDir + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = 30f;

            for (int m = 0; m < muscleCount; m++)
                SetCurve(clip, MuscleBindingName(HumanTrait.MuscleName[m]), muscleKeys[m]);

            SetCurve(clip, "RootT.x", rootTKeys[0]);
            SetCurve(clip, "RootT.y", rootTKeys[1]);
            SetCurve(clip, "RootT.z", rootTKeys[2]);
            SetCurve(clip, "RootQ.x", rootQKeys[0]);
            SetCurve(clip, "RootQ.y", rootQKeys[1]);
            SetCurve(clip, "RootQ.z", rootQKeys[2]);
            SetCurve(clip, "RootQ.w", rootQKeys[3]);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (isNew) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);

            Debug.Log("  baked " + name + " (" + frames.Count + " keys, " +
                      frames[frames.Count - 1].Time.ToString("F2") + "s, loop=" + loop + ")");
        }

        /// <summary>
        /// Curls the right hand into a fist so the sword looks gripped rather than
        /// balanced on an open palm, and relaxes the left hand slightly.
        /// Finger muscles are set directly - posing 15 finger bones by hand is not
        /// worth it when the value we want is "closed".
        /// </summary>
        private static void CloseSwordHand(float[] muscles)
        {
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
            {
                string n = HumanTrait.MuscleName[m];
                bool right = n.StartsWith("Right ");
                bool left  = n.StartsWith("Left ");
                if (!right && !left) continue;

                bool isFinger = n.Contains("Thumb") || n.Contains("Index") ||
                                n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");
                if (!isFinger) continue;

                if (n.EndsWith("Spread")) { muscles[m] = 0f; continue; }

                if (right)
                    muscles[m] = n.Contains("Thumb") ? -0.35f : -0.85f;   // fist round the grip
                else
                    muscles[m] = n.Contains("Thumb") ? -0.10f : -0.30f;   // loose, natural
            }
        }

        private static void SetCurve(AnimationClip clip, string prop, List<Keyframe> keys)
        {
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(
                clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), curve);
        }

        /// <summary>
        /// Finger muscles are stored under a different name than HumanTrait reports
        /// ("Left Index 1 Stretched" -> "LeftHand.Index.1 Stretched"). Everything else
        /// uses its HumanTrait name verbatim.
        /// </summary>
        private static string MuscleBindingName(string muscle)
        {
            string side = muscle.StartsWith("Left ") ? "Left" : muscle.StartsWith("Right ") ? "Right" : null;
            if (side == null) return muscle;

            string rest = muscle.Substring(side.Length + 1);
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                if (!rest.StartsWith(finger + " ")) continue;
                string tail = rest.Substring(finger.Length + 1);          // "1 Stretched" | "Spread"
                if (tail == "Spread") return side + "Hand." + finger + ".Spread";
                string joint = tail.Substring(0, 1);                       // "1" | "2" | "3"
                return side + "Hand." + finger + "." + joint + " Stretched";
            }
            return muscle;
        }

        private static void ApplyPose(Pose p, Dictionary<string, Transform> bones,
            Dictionary<string, Quaternion> bindLocalRot,
            Dictionary<string, Vector3> bindLocalPos,
            Dictionary<string, Quaternion> bindWorldRot)
        {
            // Back to bind pose first, so poses never accumulate frame to frame.
            for (int i = 0; i < Chain.GetLength(0); i++)
            {
                string n = Chain[i, 0];
                bones[n].localRotation = bindLocalRot[n];
                bones[n].localPosition = bindLocalPos[n];
            }

            // Walk parents -> children, compounding each bone's rotation onto its parent's.
            var accum = new Dictionary<string, Quaternion>();
            for (int i = 0; i < Chain.GetLength(0); i++)
            {
                string n = Chain[i, 0];
                string parent = Chain[i, 1];

                Quaternion parentAccum = parent != null && accum.ContainsKey(parent)
                    ? accum[parent] : Quaternion.identity;
                Quaternion own = p.Rot.TryGetValue(n, out var q) ? q : Quaternion.identity;

                Quaternion total = parentAccum * own;

                // A forearm is a hinge off its upper arm, not a free-floating limb.
                // Rotating it about the elbow axis carried by the shoulder keeps the
                // joint inside what an arm can actually do.
                if (p.ElbowBend.TryGetValue(n, out float flex))
                {
                    float side = n.EndsWith("_r") ? -1f : 1f;   // both arms fold forwards
                    Vector3 hinge = parentAccum * Vector3.up;
                    total = Quaternion.AngleAxis(side * flex, hinge) * parentAccum;
                }

                if (total != Quaternion.identity)
                    bones[n].rotation = total * bindWorldRot[n];

                // If this bone was given an explicit aim, correct it AFTER the torso
                // has moved, so the stated direction is what you actually get.
                if (p.Aim.TryGetValue(n, out var target) && ChildOf.TryGetValue(n, out var childName))
                {
                    Vector3 cur = (bones[childName].position - bones[n].position).normalized;
                    if (cur.sqrMagnitude > 0.0001f)
                    {
                        Quaternion fix = Quaternion.FromToRotation(cur, target);
                        bones[n].rotation = fix * bones[n].rotation;
                        total = fix * total;
                    }
                }

                accum[n] = total;
            }

            bones["pelvis"].localPosition = bindLocalPos["pelvis"];

            // Finally, roll the wrist so the sword points where the pose asked for.
            // The blade leaves the fist along the knuckle line, so this is the only
            // thing that decides where the sword actually ends up.
            if (p.BladeAim != Vector3.zero)
            {
                Transform hand = bones["hand_r"];
                Vector3 current = hand.TransformDirection(GripBladeAxis);
                Quaternion fix = Quaternion.FromToRotation(current, p.BladeAim);
                hand.rotation = fix * hand.rotation;
            }
        }

        // ------------------------------------------------------------------ the poses
        //
        // Limbs are AIMED (Point/PointLeg) in character space: +X right, +Y up,
        // +Z the way the pirate faces. The torso uses small Lean/Twist/Roll angles.

        /// <summary>
        /// Shared gait mechanics. Legs alone look like a doll being slid along the
        /// ground, so this also does the things a body actually does when walking:
        ///   - the hips rise and fall twice per stride, lowest as a foot lands
        ///   - the pelvis turns with the leading leg, shoulders counter-turn
        ///   - the hip on the swinging side drops
        ///   - the knee folds through the swing and straightens to take the weight
        /// </summary>
        private static void Gait(Pose p, float a, float thighSwing, float kneeSwing,
                                 float absorb, float hipTurn, float hipDrop)
        {
            float legR = Mathf.Sin(a);          // +1 = right leg forward (footfall)
            float legL = -legR;
            float underR = Mathf.Cos(a);        // +1 = right leg passing under the body

            // Pelvis turns with the leading leg; the swing-side hip drops.
            p.Twist("pelvis", -hipTurn * legR);
            p.Roll("pelvis", -hipDrop * underR);

            // Shoulders counter-rotate against the hips. Along with the rise and fall
            // this is most of what separates walking from being slid along the floor.
            p.Twist("spine_02", hipTurn * 1.1f * legR);
            p.Twist("spine_03", hipTurn * 0.5f * legR);
            p.Roll("spine_02", hipDrop * 0.6f * underR);
            p.Twist("head", -hipTurn * 0.5f * legR);

            // Knee bend per leg:
            //   folds through the swing (peaks passing under the body),
            //   flexes to absorb the landing (peaks at footfall),
            //   and is STRAIGHT at mid-stance - which is what lifts the body.
            float bendR = 4f + kneeSwing * Mathf.Max(0f, underR) + absorb * Mathf.Max(0f, legR);
            float bendL = 4f + kneeSwing * Mathf.Max(0f, -underR) + absorb * Mathf.Max(0f, legL);

            float thighR = thighSwing * legR;
            float thighL = thighSwing * legL;

            p.PointLeg("thigh_r", thighR, 0.09f);
            p.PointLeg("calf_r", thighR - bendR, 0.07f);
            p.PointLeg("thigh_l", thighL, -0.09f);
            p.PointLeg("calf_l", thighL - bendL, -0.07f);
        }

        /// <summary>Relaxed stance: sword carried low, not squared up for a fight.</summary>
        private static Pose Rest()
        {
            var p = new Pose();
            p.Lean("spine_01", 3).Lean("spine_02", 1);
            p.Twist("spine_02", -5);

            // Sword arm hangs; blade angled down and forward, tip clear of the leg.
            p.Point("upperarm_r", 0.20f, -0.96f, 0.16f);
            p.Elbow("lowerarm_r", 22f);
            p.Blade(0.22f, -0.62f, 0.75f);

            p.Point("upperarm_l", -0.22f, -0.96f, 0.12f);
            p.Elbow("lowerarm_l", 20f);

            p.PointLeg("thigh_l", 3f, -0.11f);
            p.PointLeg("calf_l", -2f, -0.09f);
            p.PointLeg("thigh_r", -3f, 0.11f);
            p.PointLeg("calf_r", -8f, 0.09f);
            p.Lift(-0.02f);
            return p;
        }

        /// <summary>Combat stance: sword up, bladed to the target.</summary>
        private static Pose Guard()
        {
            var p = new Pose();
            p.Lean("spine_01", 5).Lean("spine_02", 3);
            p.Twist("spine_02", -10).Twist("spine_03", -6);

            p.Point("upperarm_r", 0.26f, -0.90f, 0.34f);
            p.Elbow("lowerarm_r", 82f);
            p.Blade(0.18f, 0.46f, 0.87f);                 // blade up and forward, on guard
            p.Point("upperarm_l", -0.40f, -0.88f, 0.26f);
            p.Elbow("lowerarm_l", 58f);

            p.PointLeg("thigh_l", 10f, -0.12f);
            p.PointLeg("calf_l", -4f, -0.10f);
            p.PointLeg("thigh_r", -12f, 0.16f);
            p.PointLeg("calf_r", -20f, 0.12f);
            p.Lift(-0.04f);
            p.Lean("head", -4);
            return p;
        }

        /// <summary>Idle: standing at rest, breathing and shifting weight. 3s loop.</summary>
        private static List<Frame> BuildIdle()
        {
            var frames = new List<Frame>();
            const float dur = 3f;
            const int steps = 12;

            for (int i = 0; i <= steps; i++)
            {
                float t = dur * i / steps;
                float br = Mathf.Sin(t / dur * Mathf.PI * 2f);
                float sw = Mathf.Sin(t / dur * Mathf.PI * 2f + 0.9f);

                var p = Rest();
                p.Lean("spine_01", 1.8f * br);
                p.Lean("head", -2.5f * br);
                p.Twist("head", 5f * sw);
                p.Roll("pelvis", 1.6f * sw);          // weight drifts foot to foot
                p.Twist("spine_02", 2f * sw);
                p.Point("upperarm_r", 0.20f, -0.96f, 0.16f + 0.05f * br);
                p.Point("upperarm_l", -0.22f, -0.96f, 0.12f + 0.05f * br);
                p.Elbow("lowerarm_r", 22f + 4f * br);
                p.Blade(0.22f, -0.62f + 0.05f * br, 0.75f);
                p.Lift(0.008f * br);
                frames.Add(new Frame { Time = t, Pose = p });
            }
            return frames;
        }

        /// <summary>
        /// Walk: the out-of-combat patrol gait. 1.1s stride, arms swinging gently,
        /// sword carried down at the side.
        /// </summary>
        private static List<Frame> BuildWalk()
        {
            var frames = new List<Frame>();
            const float dur = 1.1f;
            const int steps = 16;

            for (int i = 0; i <= steps; i++)
            {
                float t = dur * i / steps;
                float a = t / dur * Mathf.PI * 2f;
                float legR = Mathf.Sin(a);

                var p = new Pose();
                p.Lean("spine_01", 4).Lean("spine_02", 2);

                Gait(p, a,
                     thighSwing: 26f, kneeSwing: 52f, absorb: 20f,
                     hipTurn: 6f, hipDrop: 4f);

                // Arms swing a little, opposite the legs. Sword carried down at the side.
                p.Point("upperarm_r", 0.20f, -0.95f, -0.22f * legR);
                p.Elbow("lowerarm_r", 26f + 8f * legR);
                p.Blade(0.24f, -0.60f, 0.76f - 0.10f * legR);

                p.Point("upperarm_l", -0.22f, -0.95f, 0.22f * legR);
                p.Elbow("lowerarm_l", 28f + 14f * Mathf.Max(0f, legR));

                p.Lift(-0.02f);
                frames.Add(new Frame { Time = t, Pose = p });
            }
            return frames;
        }

        /// <summary>
        /// Run: the combat charge. 0.62s stride, bigger everything, sword up and
        /// ready so the approach already reads as an attack.
        /// </summary>
        private static List<Frame> BuildRun()
        {
            var frames = new List<Frame>();
            const float dur = 0.62f;
            const int steps = 16;

            for (int i = 0; i <= steps; i++)
            {
                float t = dur * i / steps;
                float a = t / dur * Mathf.PI * 2f;
                float legR = Mathf.Sin(a);

                var p = new Pose();
                p.Lean("spine_01", 11).Lean("spine_02", 5);
                p.Lean("head", -10);                  // eyes up on the target

                Gait(p, a,
                     thighSwing: 40f, kneeSwing: 88f, absorb: 38f,
                     hipTurn: 8f, hipDrop: 5f);

                // Sword arm stays up and ready, bouncing with the stride. Elbow tucked
                // in rather than winged out - a runner keeps the blade close.
                p.Point("upperarm_r", 0.24f, -0.90f, 0.36f + 0.06f * legR);
                p.Elbow("lowerarm_r", 86f + 6f * legR);
                p.Blade(0.20f, 0.42f + 0.06f * legR, 0.88f);

                // Off arm drives hard, opposite the right leg.
                p.Point("upperarm_l", -0.30f, -0.90f, 0.32f * legR);
                p.Elbow("lowerarm_l", 74f + 20f * Mathf.Max(0f, legR));

                p.Lift(-0.03f);
                frames.Add(new Frame { Time = t, Pose = p });
            }
            return frames;
        }

        /// <summary>
        /// Attack: overhead slash. The blade goes up behind the head, then down and
        /// forward through the target. 0.30 = coiled (parry read), 0.44 = impact.
        /// </summary>
        private static List<Frame> BuildAttack()
        {
            var frames = new List<Frame>();
            frames.Add(new Frame { Time = 0f, Pose = Guard() });

            var wind = new Pose();
            wind.Lean("spine_01", -3);
            wind.Twist("spine_02", -20).Twist("spine_03", -12);
            wind.Point("upperarm_r", 0.30f, -0.20f, 0.28f);   // shoulder stays low
            wind.Elbow("lowerarm_r", 105f);                   // the fold does the lifting
            wind.Blade(0.22f, 0.86f, 0.06f);                  // blade coming up
            wind.Point("upperarm_l", -0.42f, -0.86f, 0.28f);
            wind.Elbow("lowerarm_l", 62f);
            wind.Twist("head", -8);
            wind.PointLeg("thigh_l", 8f, -0.12f);
            wind.PointLeg("calf_l", 2f, -0.10f);
            wind.PointLeg("thigh_r", -14f, 0.16f);
            wind.PointLeg("calf_r", -24f, 0.12f);
            wind.Lift(-0.04f);
            frames.Add(new Frame { Time = 0.18f, Pose = wind });

            var coil = new Pose();
            coil.Lean("spine_01", -6);
            coil.Twist("spine_02", -28).Twist("spine_03", -18);
            coil.Roll("pelvis", 3);
            coil.Point("upperarm_r", 0.32f, 0.10f, 0.10f);    // upper arm barely above level
            coil.Elbow("lowerarm_r", 128f);                   // hand cocked behind the ear
            coil.Blade(0.18f, 0.72f, -0.67f);                 // blade back over the shoulder
            coil.Point("upperarm_l", -0.44f, -0.84f, 0.32f);
            coil.Elbow("lowerarm_l", 68f);
            coil.Twist("head", -10);
            coil.PointLeg("thigh_l", 6f, -0.12f);
            coil.PointLeg("calf_l", 2f, -0.10f);
            coil.PointLeg("thigh_r", -16f, 0.16f);
            coil.PointLeg("calf_r", -28f, 0.12f);
            coil.Lift(-0.05f);
            frames.Add(new Frame { Time = 0.30f, Pose = coil });

            var hit = new Pose();
            hit.Lean("spine_01", 14).Lean("spine_02", 6);
            hit.Twist("spine_02", 22).Twist("spine_03", 14);
            hit.Roll("pelvis", -3);
            hit.Point("upperarm_r", 0.20f, -0.52f, 0.83f);
            hit.Elbow("lowerarm_r", 28f);                     // arm extends through the cut
            hit.Blade(0.06f, -0.52f, 0.85f);                  // blade driving down and forward
            hit.Point("upperarm_l", -0.42f, -0.88f, -0.22f);
            hit.Elbow("lowerarm_l", 52f);
            hit.Lean("head", 6).Twist("head", 10);
            hit.PointLeg("thigh_l", 20f, -0.12f);
            hit.PointLeg("calf_l", 8f, -0.10f);
            hit.PointLeg("thigh_r", -24f, 0.16f);
            hit.PointLeg("calf_r", -34f, 0.12f);
            hit.Lift(-0.07f);
            frames.Add(new Frame { Time = 0.44f, Pose = hit });

            var follow = new Pose();
            follow.Lean("spine_01", 12).Lean("spine_02", 5);
            follow.Twist("spine_02", 26).Twist("spine_03", 16);
            follow.Point("upperarm_r", -0.02f, -0.86f, 0.51f);
            follow.Elbow("lowerarm_r", 44f);
            follow.Blade(-0.26f, -0.74f, 0.62f);              // blade finishes low, across
            follow.Point("upperarm_l", -0.44f, -0.86f, -0.26f);
            follow.Elbow("lowerarm_l", 48f);
            follow.Twist("head", 8);
            follow.PointLeg("thigh_l", 17f, -0.12f);
            follow.PointLeg("calf_l", 7f, -0.10f);
            follow.PointLeg("thigh_r", -21f, 0.16f);
            follow.PointLeg("calf_r", -31f, 0.12f);
            follow.Lift(-0.06f);
            frames.Add(new Frame { Time = 0.58f, Pose = follow });

            frames.Add(new Frame { Time = 0.80f, Pose = Guard() });
            return frames;
        }
    }
}
