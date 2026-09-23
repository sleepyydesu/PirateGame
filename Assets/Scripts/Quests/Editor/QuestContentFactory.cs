using System.Collections.Generic;
using System.IO;
using PirateGame.Quests;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PirateGame.Quests.EditorTools
{
    /// <summary>
    /// Creates the data assets for Quest A / Quest B (quests, items, rewards) plus the generated
    /// art the test scene needs (materials, textures, merchant animator, split ship hull).
    /// Existing quest/item/reward assets are left untouched unless overwrite is true, so designer
    /// edits survive a scene rebuild.
    /// </summary>
    public static class QuestContentFactory
    {
        public const string Root = "Assets/Quests";
        public const string DataDir = Root + "/Data";
        public const string QuestDir = DataDir + "/Quests";
        public const string ItemDir = DataDir + "/Items";
        public const string RewardDir = DataDir + "/Rewards";
        public const string GenDir = Root + "/Generated";
        public const string MatDir = GenDir + "/Materials";
        public const string TexDir = GenDir + "/Textures";
        public const string MeshDir = GenDir + "/ShipWreck";
        public const string AnimDir = GenDir + "/Animation";

        public class Content
        {
            public QuestDefinition questA, questB;
            public ItemDefinition merchantItem, compass, rum, bandage, whetstone, spyglass;
            public ShopDiscountReward discount;
            public WeaponDamageReward weaponUpgrade;
            public ItemReward compassReward;
            public readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
            public RuntimeAnimatorController merchantController;
            public Texture2D softDot, cloudPuff;
        }

        // Event keys / flags shared between the quests and the scene.
        public const string FlagStormStarted = "storm_started";
        public const string FlagStormDone = "storm_done";
        public const string FlagShipWrecked = "ship_wrecked";
        public const string FlagKidnapped = "merchant_kidnapped";
        public const string FlagFreed = "merchant_freed";
        public const string FlagRescued = "merchant_rescued";
        public const string EvItemCollected = "item_collected:merchant_item";
        public const string EvItemDelivered = "delivered:merchant_item";
        public const string EvShopEmpty = "entered:merchant_shop_empty";
        public const string Encounter = "kidnappers";

        public static Content Build(bool overwrite)
        {
            EnsureDirs();
            var c = new Content();
            BuildItems(c, overwrite);
            BuildRewards(c, overwrite);
            BuildQuests(c, overwrite);
            BuildTextures(c);
            BuildMaterials(c);
            c.merchantController = BuildMerchantController();
            AssetDatabase.SaveAssets();
            return c;
        }

        // ================================================================ data

        private static void BuildItems(Content c, bool overwrite)
        {
            c.merchantItem = Item("Item_MerchantsItem", "merchant_item", "Merchant's Item",
                "An iron-bound strongbox bearing the merchant's seal. Heavy.", new Color(1f, 0.78f, 0.35f), true, true, overwrite);
            c.compass = Item("Item_LuckyCompass", "lucky_compass", "Merchant's Lucky Compass",
                "A brass compass that has never once steered its owner wrong. Unique.", new Color(0.95f, 0.75f, 1f), false, true, overwrite);
            c.rum = Item("Item_Rum", "rum", "Flask of Rum", "Restores courage, if not health.", new Color(0.8f, 0.5f, 0.3f), false, false, overwrite);
            c.bandage = Item("Item_Bandage", "bandage", "Bandage Roll", "Clean linen for patching wounds.", Color.white, false, false, overwrite);
            c.whetstone = Item("Item_Whetstone", "whetstone", "Whetstone", "Keeps a blade keen.", new Color(0.7f, 0.75f, 0.8f), false, false, overwrite);
            c.spyglass = Item("Item_Spyglass", "spyglass", "Brass Spyglass", "See sails long before they see you.", new Color(1f, 0.85f, 0.5f), false, true, overwrite);
        }

        private static void BuildRewards(Content c, bool overwrite)
        {
            c.discount = Asset<ShopDiscountReward>(RewardDir + "/Reward_MerchantDiscount.asset", overwrite, r =>
            {
                r.displayName = "10% Shop Discount";
                r.description = "Permanent 10% off everything at the Merchant's shop.";
                r.shopId = "merchant";
                r.discount = 0.10f;
            });
            c.weaponUpgrade = Asset<WeaponDamageReward>(RewardDir + "/Reward_WeaponUpgrade.asset", overwrite, r =>
            {
                r.displayName = "Weapon Upgrade";
                r.description = "Permanently increases your sword damage by 25%.";
                r.bonus = 0.25f;
            });
            c.compassReward = Asset<ItemReward>(RewardDir + "/Reward_LuckyCompass.asset", overwrite, r =>
            {
                r.displayName = "Merchant's Lucky Compass";
                r.description = "A unique item — the merchant's most treasured possession.";
                r.item = c.compass;
                r.count = 1;
            });
        }

        private static void BuildQuests(Content c, bool overwrite)
        {
            c.questA = Asset<QuestDefinition>(QuestDir + "/Quest_A_MerchantsLostItem.asset", overwrite, q =>
            {
                q.questId = "quest_a_merchants_lost_item";
                q.title = "The Merchant's Lost Item";
                q.description = "A sudden storm wrecked a merchant's ship off the coast. He made it ashore, but an important item was lost in the wreckage. Search the coast near the wreck and bring it back to him.";
                q.questType = QuestType.Side;
                q.giverName = "Merchant";
                q.requirements = new List<QuestRequirement> { new QuestRequirement { kind = QuestRequirement.Kind.FlagSet, flag = FlagStormDone } };
                q.startMode = QuestStartMode.AcceptFromNpc;
                q.lockedLabel = "Inactive";
                q.availableLabel = "Available";
                q.completedLabel = "Completed";
                q.lockedFlagLabels = new List<FlagLabel> { new FlagLabel { flag = FlagStormStarted, label = "Storm_Triggered" } };
                q.stages = new List<QuestStage>
                {
                    new QuestStage
                    {
                        id = "Active_Search", objective = "Search for the merchant's lost item.",
                        targetId = "search_area", completeOnEvent = EvItemCollected,
                    },
                    new QuestStage
                    {
                        id = "Active_Return", objective = "Return the item to the merchant.",
                        targetId = "merchant_shop", completeOnEvent = EvItemDelivered,
                        requiredItem = c.merchantItem, revertToStageId = "Active_Search",
                    },
                };
                q.rewards = new List<QuestReward> { c.discount };
                q.rewardIsChoice = false;
            });

            c.questB = Asset<QuestDefinition>(QuestDir + "/Quest_B_KidnappedMerchant.asset", overwrite, q =>
            {
                q.questId = "quest_b_kidnapped_merchant";
                q.title = "The Kidnapped Merchant";
                q.discoverHeadline = "Merchant Has Been Kidnapped";
                q.description = "The merchant's shop has been ransacked and he is nowhere to be seen. Drag marks lead inland, toward a pirate hideout. Find him and bring him home.";
                q.questType = QuestType.Side;
                q.giverName = "";
                q.requirements = new List<QuestRequirement> { new QuestRequirement { kind = QuestRequirement.Kind.QuestCompleted, quest = c.questA } };
                q.startMode = QuestStartMode.DiscoverOnEvent;
                q.discoverOnEvent = EvShopEmpty;
                q.promptOnDiscover = true;
                q.lockedLabel = "Locked";
                q.availableLabel = "Available";
                q.discoveredLabel = "Discovered";
                q.completedLabel = "Completed";
                q.stages = new List<QuestStage>
                {
                    new QuestStage
                    {
                        id = "Active_Investigate", objective = "Find and rescue the kidnapped merchant.",
                        targetId = "hideout", completeOnEvent = "encounter_started:" + Encounter,
                    },
                    new QuestStage
                    {
                        id = "Active_Rescue", objective = "Defeat the kidnappers.",
                        targetId = "hideout", showMarker = false, showTrail = false,
                        completeOnEvent = "enemy_defeated:" + Encounter, requiredCount = 4,
                        revertOnEvent = "encounter_reset:" + Encounter, revertToStageId = "Active_Investigate",
                    },
                    new QuestStage
                    {
                        id = "Rescue_Talk", stateLabel = "Active_Rescue", objective = "Talk to the freed merchant.",
                        targetId = "merchant_captive",
                    },
                };
                q.rewards = new List<QuestReward> { c.weaponUpgrade, c.compassReward };
                q.rewardIsChoice = true;
                q.clearFlagsOnComplete = new List<string> { FlagKidnapped };
                q.setFlagsOnComplete = new List<string> { FlagRescued };
            });
        }

        // ================================================================ art

        private static void BuildTextures(Content c)
        {
            c.softDot = SaveTexture("Tex_SoftDot", 64, (x, y, s) =>
            {
                float d = new Vector2(x - (s - 1) * 0.5f, y - (s - 1) * 0.5f).magnitude / (s * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                return a * a;
            });
            c.cloudPuff = SaveTexture("Tex_CloudPuff", 128, (x, y, s) =>
            {
                float cx = (x - (s - 1) * 0.5f) / (s * 0.5f), cy = (y - (s - 1) * 0.5f) / (s * 0.5f);
                float d = Mathf.Sqrt(cx * cx + cy * cy);
                float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 7f, y * 0.15f) * 0.4f;
                return Mathf.Clamp01((1f - d) * 1.6f) * Mathf.Clamp01(n * 1.4f - 0.1f);
            });
        }

        private static void BuildMaterials(Content c)
        {
            c.mats["Sand"] = Mat("M_Sand", () => QuestMaterials.LitColor(new Color(0.86f, 0.77f, 0.56f), 0.15f));
            c.mats["WetSand"] = Mat("M_WetSand", () => QuestMaterials.LitColor(new Color(0.62f, 0.54f, 0.38f), 0.45f));
            c.mats["Grass"] = Mat("M_Grass", () => QuestMaterials.LitColor(new Color(0.46f, 0.55f, 0.3f), 0.1f));
            c.mats["SeaFloor"] = Mat("M_SeaFloor", () => QuestMaterials.LitColor(new Color(0.33f, 0.36f, 0.3f), 0.2f));
            c.mats["Water"] = Mat("M_Water", () => QuestMaterials.LitTransparent(new Color(0.06f, 0.32f, 0.42f, 0.85f), 0.95f, true));
            c.mats["Rock"] = Mat("M_Rock", () => QuestMaterials.LitColor(new Color(0.45f, 0.45f, 0.47f), 0.25f));
            c.mats["Leaf"] = Mat("M_PalmLeaf", () => QuestMaterials.LitColor(new Color(0.24f, 0.5f, 0.22f), 0.2f));
            c.mats["Bark"] = Mat("M_Bark", () => QuestMaterials.LitColor(new Color(0.45f, 0.33f, 0.22f), 0.1f));
            c.mats["ClothRed"] = Mat("M_ClothRed", () => QuestMaterials.LitColor(new Color(0.7f, 0.16f, 0.12f), 0.15f));
            c.mats["ClothCream"] = Mat("M_ClothCream", () => QuestMaterials.LitColor(new Color(0.92f, 0.86f, 0.72f), 0.15f));
            c.mats["Canvas"] = Mat("M_TentCanvas", () => QuestMaterials.LitColor(new Color(0.55f, 0.48f, 0.36f), 0.1f));
            c.mats["Iron"] = Mat("M_Iron", () => QuestMaterials.LitColor(new Color(0.22f, 0.22f, 0.24f), 0.6f, 0.8f));
            c.mats["Merchant"] = Mat("M_Merchant", () => QuestMaterials.LitColor(new Color(0.7f, 0.55f, 0.4f), 0.3f));
            c.mats["Hat"] = Mat("M_MerchantHat", () => QuestMaterials.LitColor(new Color(0.2f, 0.13f, 0.08f), 0.2f));
            c.mats["Sash"] = Mat("M_MerchantSash", () => QuestMaterials.LitColor(new Color(0.55f, 0.12f, 0.35f), 0.25f));
            c.mats["Ember"] = Mat("M_Ember", () => QuestMaterials.LitEmissive(new Color(0.2f, 0.08f, 0.02f), new Color(4f, 1.3f, 0.3f)));
            c.mats["ItemGlow"] = Mat("M_ItemBand", () => QuestMaterials.LitEmissive(new Color(0.9f, 0.7f, 0.3f), new Color(1f, 0.75f, 0.3f)));
            c.mats["Glow"] = Mat("M_GlowAdditive", () => QuestMaterials.Additive(Color.white, c.softDot));
            c.mats["Ribbon"] = Mat("M_GuideRibbon", () => QuestMaterials.Additive(Color.white));
            c.mats["Rain"] = Mat("M_Rain", () => QuestMaterials.AlphaBlended(Color.white, c.softDot));
            c.mats["Cloud"] = Mat("M_StormCloud", () => QuestMaterials.AlphaBlended(Color.white, c.cloudPuff));
            c.mats["Bolt"] = Mat("M_Lightning", () => QuestMaterials.Additive(new Color(2.5f, 2.7f, 3.2f), c.softDot));

            // The ship's own materials (Assets/Art/ship/*_Mat) use a 2D sprite shader in the transparent
            // queue, which draws through the water. Build opaque URP Lit versions from the same textures.
            c.mats["Wood"] = Mat("M_Ship_Wood", () => TexturedLit("Assets/Art/ship/Wood/Wood066_2K-PNG", new Color(0.45f, 0.3f, 0.18f), 0.25f));
            c.mats["Sail"] = Mat("M_Ship_Sail", () => TexturedLit("Assets/Art/ship/Sails/Fabric019_2K-PNG", new Color(0.9f, 0.87f, 0.78f), 0.1f));
            c.mats["Rope"] = Mat("M_Ship_Rope", () => TexturedLit("Assets/Art/ship/Ropes/Rope003_2K-PNG", new Color(0.6f, 0.5f, 0.35f), 0.1f));
            c.mats["Barrel"] = Mat("M_Ship_Barrel", () => TexturedLit("Assets/Art/ship/Barrel/Planks023A_2K-PNG", new Color(0.5f, 0.35f, 0.2f), 0.2f));
            c.mats["Stone"] = Mat("M_Ship_Cannon", () => TexturedLit("Assets/Art/ship/Cannon/Asphalt031_2K-PNG", new Color(0.25f, 0.25f, 0.27f), 0.35f));
        }

        /// <summary>Opaque URP Lit using "&lt;prefix&gt;_Color.png" (+ "_NormalDX.png" if imported as a normal map).</summary>
        private static Material TexturedLit(string prefix, Color fallback, float smoothness)
        {
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Color.png");
            Material m = QuestMaterials.LitColor(color != null ? Color.white : fallback, smoothness);
            if (color != null) m.SetTexture("_BaseMap", color);
            string normalPath = prefix + "_NormalDX.png";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter ti && ti.textureType == TextureImporterType.NormalMap)
            {
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }

        private static RuntimeAnimatorController BuildMerchantController()
        {
            string path = AnimDir + "/Merchant.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Talk", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Thank", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Freed", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Restrained", AnimatorControllerParameterType.Bool);

            const string ki = "Assets/Kevin Iglesias/Human Animations/Animations/Male/";
            AnimationClip idle = Clip(ki + "Idles/HumanM@Idle01.fbx");
            AnimationClip idle2 = Clip(ki + "Idles/HumanM@Idle02.fbx");
            AnimationClip talk = Clip(ki + "Social/Conversation/HumanM@Talk01.fbx");
            AnimationClip hurt = Clip(ki + "Combat/HumanM@CombatDamage01.fbx");

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState sIdle = sm.AddState("Idle", new Vector3(300, 0));
            sIdle.motion = idle;
            sm.defaultState = sIdle;
            AnimatorState sTalk = sm.AddState("Talk", new Vector3(560, -80)); sTalk.motion = talk;
            AnimatorState sThank = sm.AddState("Thank", new Vector3(560, 0)); sThank.motion = talk; sThank.speed = 0.8f; sThank.mirror = true;
            AnimatorState sRestrained = sm.AddState("Restrained", new Vector3(300, 160)); sRestrained.motion = hurt; sRestrained.speed = 0.35f;
            AnimatorState sFreed = sm.AddState("Freed", new Vector3(560, 160)); sFreed.motion = idle2;

            AnyTo(sm, sTalk, "Talk");
            AnyTo(sm, sThank, "Thank");
            foreach (AnimatorState s in new[] { sTalk, sThank, sFreed }) Back(s, sIdle, 0.9f);

            AnimatorStateTransition toR = sIdle.AddTransition(sRestrained);
            toR.hasExitTime = false; toR.duration = 0.2f;
            toR.AddCondition(AnimatorConditionMode.If, 0, "Restrained");
            AnimatorStateTransition loop = sRestrained.AddTransition(sRestrained);
            loop.hasExitTime = true; loop.exitTime = 0.98f; loop.duration = 0.25f;
            AnimatorStateTransition free = sRestrained.AddTransition(sFreed);
            free.hasExitTime = false; free.duration = 0.25f;
            free.AddCondition(AnimatorConditionMode.If, 0, "Freed");
            AnimatorStateTransition unR = sRestrained.AddTransition(sIdle);
            unR.hasExitTime = false; unR.duration = 0.3f;
            unR.AddCondition(AnimatorConditionMode.IfNot, 0, "Restrained");

            AssetDatabase.SaveAssets();
            return ctrl;
        }

        private static void AnyTo(AnimatorStateMachine sm, AnimatorState to, string trigger)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.hasExitTime = false;
            t.duration = 0.15f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Restrained");
        }

        private static void Back(AnimatorState from, AnimatorState to, float exit)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exit;
            t.duration = 0.25f;
        }

        private static AnimationClip Clip(string fbx)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) return clip;
            Debug.LogWarning("Clip not found in " + fbx);
            return null;
        }

        // ================================================================ ship split

        /// <summary>
        /// Splits every mesh of Ship.fbx along a jagged plane across the hull, producing bow and
        /// stern mesh assets (in each part's own local space so transforms can be copied as-is).
        /// Returns [partName] -> (bowMesh, sternMesh).
        /// </summary>
        public static Dictionary<string, (Mesh bow, Mesh stern)> SplitShip(GameObject shipAsset, float cutZ)
        {
            var result = new Dictionary<string, (Mesh, Mesh)>();
            var temp = (GameObject)Object.Instantiate(shipAsset);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            try
            {
                foreach (MeshFilter mf in temp.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh src = mf.sharedMesh;
                    if (src == null) continue;
                    Matrix4x4 toRoot = mf.transform.localToWorldMatrix;
                    string bowPath = $"{MeshDir}/{mf.name}_Bow.asset", sternPath = $"{MeshDir}/{mf.name}_Stern.asset";
                    var (bow, stern) = Split(src, toRoot, cutZ, mf.name);
                    result[mf.name] = (SaveMesh(bow, bowPath), SaveMesh(stern, sternPath));
                }
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static (Mesh, Mesh) Split(Mesh src, Matrix4x4 toRoot, float cutZ, string name)
        {
            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            Vector4[] tg = src.tangents;
            Vector2[] uv = src.uv;
            Color[] col = src.colors;

            var bowSubs = new List<List<int>>();
            var sternSubs = new List<List<int>>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] tris = src.GetTriangles(s);
                var b = new List<int>(); var st = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 c = toRoot.MultiplyPoint3x4((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f);
                    // Jagged break line: the cut wanders fore/aft with height.
                    float cut = cutZ + Mathf.Sin(c.y * 0.9f) * 1.3f + Mathf.Sin(c.y * 2.3f + 1f) * 0.6f + Mathf.Sin(c.x * 1.7f) * 0.5f;
                    List<int> target = c.z > cut ? b : st;
                    target.Add(tris[i]); target.Add(tris[i + 1]); target.Add(tris[i + 2]);
                }
                bowSubs.Add(b);
                sternSubs.Add(st);
            }
            return (Build(name + "_Bow", v, n, tg, uv, col, bowSubs), Build(name + "_Stern", v, n, tg, uv, col, sternSubs));
        }

        private static Mesh Build(string name, Vector3[] v, Vector3[] n, Vector4[] tg, Vector2[] uv, Color[] col, List<List<int>> subs)
        {
            var remap = new int[v.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu = new List<Vector2>(); var nc = new List<Color>();
            var newSubs = new List<int[]>();
            foreach (List<int> tris in subs)
            {
                var o = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++)
                {
                    int old = tris[i];
                    if (remap[old] < 0)
                    {
                        remap[old] = nv.Count;
                        nv.Add(v[old]);
                        if (n.Length == v.Length) nn.Add(n[old]);
                        if (tg.Length == v.Length) nt.Add(tg[old]);
                        if (uv.Length == v.Length) nu.Add(uv[old]);
                        if (col.Length == v.Length) nc.Add(col[old]);
                    }
                    o[i] = remap[old];
                }
                newSubs.Add(o);
            }
            var m = new Mesh { name = name };
            if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(nv);
            if (nn.Count == nv.Count) m.SetNormals(nn);
            if (nt.Count == nv.Count) m.SetTangents(nt);
            if (nu.Count == nv.Count) m.SetUVs(0, nu);
            if (nc.Count == nv.Count) m.SetColors(nc);
            m.subMeshCount = newSubs.Count;
            for (int s = 0; s < newSubs.Count; s++) m.SetTriangles(newSubs[s], s);
            m.RecalculateBounds();
            return m;
        }

        // ================================================================ helpers

        private static void EnsureDirs()
        {
            foreach (string d in new[] { Root, DataDir, QuestDir, ItemDir, RewardDir, GenDir, MatDir, TexDir, MeshDir, AnimDir })
            {
                if (AssetDatabase.IsValidFolder(d)) continue;
                string parent = Path.GetDirectoryName(d).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
        }

        private static ItemDefinition Item(string file, string id, string name, string desc, Color color, bool quest, bool unique, bool overwrite)
        {
            return Asset<ItemDefinition>($"{ItemDir}/{file}.asset", overwrite, i =>
            {
                i.itemId = id; i.displayName = name; i.description = desc; i.uiColor = color; i.isQuestItem = quest; i.unique = unique;
            });
        }

        private static T Asset<T>(string path, bool overwrite, System.Action<T> fill) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null && !overwrite) return existing;
            T a = existing != null ? existing : ScriptableObject.CreateInstance<T>();
            fill(a);
            if (existing == null) AssetDatabase.CreateAsset(a, path);
            else EditorUtility.SetDirty(a);
            return a;
        }

        private static Material Mat(string name, System.Func<Material> make)
        {
            string path = $"{MatDir}/{name}.mat";
            Material fresh = make();
            fresh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = fresh.shader;
                existing.CopyPropertiesFromMaterial(fresh);
                existing.shaderKeywords = fresh.shaderKeywords;
                existing.renderQueue = fresh.renderQueue;
                existing.SetOverrideTag("RenderType", fresh.GetTag("RenderType", false));
                existing.SetShaderPassEnabled("DepthOnly", fresh.GetShaderPassEnabled("DepthOnly"));
                existing.SetShaderPassEnabled("ShadowCaster", fresh.GetShaderPassEnabled("ShadowCaster"));
                existing.globalIlluminationFlags = fresh.globalIlluminationFlags;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fresh);
                return existing;
            }
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }

        private static Texture2D SaveTexture(string name, int size, System.Func<int, int, int, float> alpha)
        {
            string path = $"{TexDir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color(1f, 1f, 1f, alpha(x, y, size));
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh SaveMesh(Mesh m, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
