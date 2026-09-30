using System.Collections.Generic;
using System.Linq;
using PirateGame.Enemies;
using PirateGame.Quests;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateGame.Quests.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/QuestSystemTestScene.unity: brings over the player, cameras, UI and
    /// NavMeshSurface from TestScene, lays out a coast (shop, beach, sea, merchant ship) and an
    /// inland pirate hideout, and wires Quest A and Quest B together with the generic quest
    /// components. Safe to re-run — the scene is rebuilt from scratch each time.
    /// </summary>
    public static class QuestTestSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/QuestSystemTestScene.unity";
        public const string SourceScenePath = "Assets/Scenes/TestScene.unity";
        public const string EnemyPrefabPath = "Assets/Prefabs/Enemies/Enemy_Pirate.prefab";
        public const string MerchantModelPath = "Assets/Kevin Iglesias/Human Animations/Models/HumanM_Model.fbx";
        public const string ShipModelPath = "Assets/Art/ship/Ship.fbx";
        public const string HeaderFontPath = "Assets/UI/BlackOpsOne-Regular.ttf";

        // Layout
        private static readonly Vector3 PlayerStart = new Vector3(0f, 0.6f, -30f);
        private static readonly Vector3 ShopPos = new Vector3(-24f, 0f, 12f);
        private static readonly Vector3 SearchCenter = new Vector3(20f, 0f, 35f);
        private const float SearchRadius = 22f;
        private static readonly Vector3 ItemPos = new Vector3(31f, 0f, 34f);
        private static readonly Vector3 ShipStart = new Vector3(62f, -1.7f, 108f);
        private const float ShipStartYaw = 100f;
        private static readonly Vector3 ShipWreckPos = new Vector3(30f, -2.1f, 60f);
        private const float ShipWreckYaw = 65f;
        private const float ShipScale = 0.55f;
        private const float ShipCutZ = 5f;
        private static readonly Vector3 Hideout = new Vector3(-75f, 0f, -55f);

        private static QuestContentFactory.Content c;
        private static int navExcludedLayer;

        [MenuItem("Tools/Pirate Game/Quests/Build Quest Test Scene")]
        private static void MenuBuild()
        {
            if (!EditorUtility.DisplayDialog("Build Quest Test Scene",
                    $"This rebuilds {ScenePath} from scratch (player/camera/UI copied from TestScene).\n\nQuest, item and reward assets that already exist are kept as they are.",
                    "Build", "Cancel")) return;
            Build(false);
        }

        [MenuItem("Tools/Pirate Game/Quests/Rebuild Quest Test Scene + Reset Quest Assets")]
        private static void MenuBuildReset()
        {
            if (!EditorUtility.DisplayDialog("Rebuild + reset quest assets",
                    "Rebuilds the scene AND overwrites Quest A / Quest B / item / reward assets with the defaults from the design docs.",
                    "Rebuild", "Cancel")) return;
            Build(true);
        }

        public static string Build(bool overwriteContent)
        {
            if (Application.isPlaying) return "Exit play mode first.";
            var log = new System.Text.StringBuilder();

            c = QuestContentFactory.Build(overwriteContent);
            navExcludedLayer = LayerMask.NameToLayer("TransparentFX");

            Scene scene = OpenCleanScene();
            MergeFromTestScene(scene, log);

            GameObject player = FindRootWith<PlayerController>(scene);
            SetupPlayer(player, log);

            var env = new GameObject("Environment").transform;
            BuildTerrain(env);
            Physics.SyncTransforms();
            BuildDecor(env);

            var questRoot = new GameObject("Quests").transform;
            var systems = BuildSystems(scene);
            ShipWreck ship = BuildShip(questRoot);
            BuildStorm(questRoot, ship, scene);
            QuestNpc shopMerchant = BuildShop(questRoot);
            BuildSearchArea(questRoot);
            BuildHideout(questRoot);

            Physics.SyncTransforms();
            BakeNavMesh(scene, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();

            log.AppendLine("Built " + ScenePath);
            Debug.Log("[QuestSceneBuilder]\n" + log);
            return log.ToString();
        }

        // ================================================================ scene plumbing

        private static Scene OpenCleanScene()
        {
            Scene scene;
            if (System.IO.File.Exists(ScenePath))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            foreach (GameObject go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);
            return scene;
        }

        private static void MergeFromTestScene(Scene target, System.Text.StringBuilder log)
        {
            const string temp = "Assets/Scenes/__QuestBuild_Temp.unity";
            AssetDatabase.DeleteAsset(temp);
            AssetDatabase.CopyAsset(SourceScenePath, temp);
            Scene src = EditorSceneManager.OpenScene(temp, OpenSceneMode.Additive);
            EditorSceneManager.MergeScenes(src, target);
            AssetDatabase.DeleteAsset(temp);
            SceneManager.SetActiveScene(target);

            // Keep player, cameras, lights, volume, NavMeshSurface, death UI and EventSystem.
            // Drop the test arena and its enemies/routes.
            foreach (GameObject go in target.GetRootGameObjects())
            {
                bool drop = go.name == "Ground" || go.name == "Plane" || go.name.StartsWith("Route_") ||
                            go.GetComponent<EnemyAI>() != null || go.GetComponent<PatrolRoute>() != null;
                if (drop) { log.AppendLine("  removed test object " + go.name); Object.DestroyImmediate(go); }
            }
        }

        private static GameObject FindRootWith<T>(Scene s) where T : Component =>
            s.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<T>() != null);

        private static void SetupPlayer(GameObject player, System.Text.StringBuilder log)
        {
            if (player == null) { log.AppendLine("  !! no PlayerController found in TestScene"); return; }
            Vector3 old = player.transform.position;
            player.tag = "Player"; // EnemyAI, QuestPlayer etc. look the player up by tag
            player.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);
            Vector3 delta = PlayerStart - old;

            // Move cameras along so there's no big swoop at start.
            foreach (GameObject go in player.scene.GetRootGameObjects())
                if (go.GetComponent<Camera>() != null || go.GetComponent<Unity.Cinemachine.CinemachineCamera>() != null)
                    go.transform.position += delta;

            AddIfMissing<PlayerInventory>(player);
            AddIfMissing<PlayerUpgrades>(player);
            AddIfMissing<InteractionController>(player);
            log.AppendLine("  player: " + player.name + " tagged Player, + Inventory/Upgrades/Interaction");
        }

        private static T AddIfMissing<T>(GameObject go) where T : Component =>
            go.GetComponent<T>() != null ? go.GetComponent<T>() : go.AddComponent<T>();

        private static GameObject BuildSystems(Scene scene)
        {
            var go = new GameObject("QuestSystems");
            var manager = go.AddComponent<QuestManager>();
            Set(manager, so =>
            {
                SerializedProperty list = so.FindProperty("quests");
                list.arraySize = 2;
                list.GetArrayElementAtIndex(0).objectReferenceValue = c.questA;
                list.GetArrayElementAtIndex(1).objectReferenceValue = c.questB;
            });
            go.AddComponent<QuestAudio>();
            var ui = go.AddComponent<QuestUI>();
            Set(ui, so => so.FindProperty("headerFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(HeaderFontPath));
            var trail = go.AddComponent<QuestTrail>();
            Set(trail, so =>
            {
                so.FindProperty("ribbonMaterial").objectReferenceValue = c.mats["Ribbon"];
                so.FindProperty("glowMaterial").objectReferenceValue = c.mats["Glow"];
                so.FindProperty("waterLevel").floatValue = -0.33f;
                so.FindProperty("showRibbon").boolValue = false;
                so.FindProperty("showMotes").boolValue = true;
                so.FindProperty("showWisp").boolValue = true;
                so.FindProperty("motesPerMetre").floatValue = 2.5f;
            });
            return go;
        }

        // ================================================================ terrain

        private static void BuildTerrain(Transform env)
        {
            Prim(PrimitiveType.Cube, "Land_Grass", env, new Vector3(0, -1f, -57.5f), new Vector3(260, 2, 125), c.mats["Grass"]);
            Prim(PrimitiveType.Cube, "Land_Sand", env, new Vector3(0, -1f, 17.5f), new Vector3(260, 2, 25), c.mats["Sand"]);
            Slope(env, "Beach_Shallows", 30f, 0f, 50f, -2.5f, c.mats["WetSand"]);
            Slope(env, "Beach_DropOff", 50f, -2.5f, 58f, -6f, c.mats["SeaFloor"]);
            Prim(PrimitiveType.Cube, "SeaFloor", env, new Vector3(0, -7f, 158f), new Vector3(260, 2, 200), c.mats["SeaFloor"]);

            GameObject water = Prim(PrimitiveType.Plane, "Sea_Water", env, new Vector3(0, -0.35f, 146f), new Vector3(26, 1, 24), c.mats["Water"], null, false);
            water.layer = LayerMask.NameToLayer("Water");
            water.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void Slope(Transform parent, string name, float z0, float y0, float z1, float y1, Material m)
        {
            float dz = z1 - z0, dy = y0 - y1;
            float len = Mathf.Sqrt(dz * dz + dy * dy);
            float ang = Mathf.Atan2(dy, dz) * Mathf.Rad2Deg;
            Vector3 upDir = Quaternion.Euler(ang, 0, 0) * Vector3.up;
            Vector3 topMid = new Vector3(0, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            Prim(PrimitiveType.Cube, name, parent, topMid - upDir * 0.5f, new Vector3(260, 1, len + 0.2f), m, new Vector3(ang, 0, 0));
        }

        private static void BuildDecor(Transform env)
        {
            var rng = new System.Random(42);
            var decor = new GameObject("Decor").transform;
            decor.SetParent(env, false);

            Vector3[] palms =
            {
                new Vector3(-40, 0, 20), new Vector3(-34, 0, 26), new Vector3(-8, 0, 22), new Vector3(6, 0, 12), new Vector3(42, 0, 18),
                new Vector3(50, 0, 26), new Vector3(-55, 0, 8), new Vector3(-14, 0, -2), new Vector3(28, 0, 8), new Vector3(-30, 0, 2),
            };
            foreach (Vector3 p in palms) Palm(decor, p, (float)rng.NextDouble() * 360f, 0.85f + (float)rng.NextDouble() * 0.4f);

            for (int i = 0; i < 26; i++)
            {
                float x = -110 + (float)rng.NextDouble() * 220;
                float z = -110 + (float)rng.NextDouble() * 125;
                var p = new Vector3(x, 0, z);
                if (Vector3.Distance(p, Hideout) < 20 || Vector3.Distance(p, PlayerStart) < 8 || Vector3.Distance(p, ShopPos) < 10) continue;
                if (Mathf.Abs(x) < 6 && z > -35) continue; // keep the walk to the coast clear
                float s = 0.8f + (float)rng.NextDouble() * 2.5f;
                Prim(PrimitiveType.Sphere, "Rock", decor, p + Vector3.up * s * 0.2f, new Vector3(s * 1.4f, s * 0.8f, s), c.mats["Rock"],
                    new Vector3(0, (float)rng.NextDouble() * 360, 0));
            }
        }

        private static void Palm(Transform parent, Vector3 pos, float yaw, float scale)
        {
            var root = new GameObject("Palm").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0, yaw, 0);
            root.localScale = Vector3.one * scale;
            Vector3 top = Vector3.zero;
            for (int i = 0; i < 5; i++)
            {
                Vector3 seg = new Vector3(0.18f * i * i * 0.2f, 1.2f * i + 0.6f, 0);
                Prim(PrimitiveType.Cylinder, "Trunk", root, Vector3.zero, new Vector3(0.38f - i * 0.03f, 0.62f, 0.38f - i * 0.03f), c.mats["Bark"],
                    new Vector3(0, 0, -4f * i), i == 0).transform.localPosition = seg;
                top = seg + Vector3.up * 0.6f;
            }
            for (int i = 0; i < 7; i++)
            {
                GameObject leaf = Prim(PrimitiveType.Cube, "Leaf", root, Vector3.zero, new Vector3(0.7f, 0.05f, 3.2f), c.mats["Leaf"], null, false);
                leaf.transform.localPosition = top;
                leaf.transform.localRotation = Quaternion.Euler(22f, i * (360f / 7f), 0) * Quaternion.Euler(0, 0, 0);
                leaf.transform.localPosition += leaf.transform.localRotation * new Vector3(0, 0, 1.4f);
            }
        }

        // ================================================================ Quest A — ship & storm

        private static ShipWreck BuildShip(Transform parent)
        {
            var shipAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelPath);
            var parts = QuestContentFactory.SplitShip(shipAsset, ShipCutZ);

            var shipGo = new GameObject("MerchantShip");
            shipGo.transform.SetParent(parent, false);
            shipGo.transform.SetPositionAndRotation(ShipStart, Quaternion.Euler(0, ShipStartYaw, 0));
            shipGo.transform.localScale = Vector3.one * ShipScale;

            Vector3 pivotLocal = new Vector3(0f, 4f, ShipCutZ);
            Transform bow = new GameObject("Bow").transform;
            bow.SetParent(shipGo.transform, false);
            bow.localPosition = pivotLocal;
            Transform stern = new GameObject("Stern").transform;
            stern.SetParent(shipGo.transform, false);
            stern.localPosition = pivotLocal;

            foreach (Transform part in shipAsset.transform)
            {
                if (!parts.TryGetValue(part.name, out var halves)) continue;
                Material m = MaterialForShipPart(part.name);
                AddShipPart(bow, part, halves.bow, m, pivotLocal);
                AddShipPart(stern, part, halves.stern, m, pivotLocal);
            }

            SetLayerRecursive(shipGo, navExcludedLayer);
            var wreck = shipGo.AddComponent<ShipWreck>();
            wreck.Setup(bow, stern, ShipWreckPos, ShipWreckYaw, c.mats["Wood"]);
            Set(wreck, so =>
            {
                so.FindProperty("bowWreckEuler").vector3Value = new Vector3(-16f, 6f, -13f);
                so.FindProperty("bowWreckOffset").vector3Value = new Vector3(1.2f, -6.5f, 3.5f);
                so.FindProperty("sternWreckEuler").vector3Value = new Vector3(12f, -5f, 10f);
                so.FindProperty("sternWreckOffset").vector3Value = new Vector3(-1.2f, -5.5f, -3f);
            });
            return wreck;
        }

        private static void AddShipPart(Transform half, Transform original, Mesh mesh, Material mat, Vector3 pivotLocal)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            var go = new GameObject(original.name);
            go.transform.SetParent(half, false);
            go.transform.localPosition = original.localPosition - pivotLocal;
            go.transform.localRotation = original.localRotation;
            go.transform.localScale = original.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        private static Material MaterialForShipPart(string name)
        {
            switch (name)
            {
                case "Sails": case "Flag": return c.mats["Sail"];
                case "Rope": return c.mats["Rope"];
                case "Barrels": return c.mats["Barrel"];
                case "Cannons": return c.mats["Stone"];
                default: return c.mats["Wood"];
            }
        }

        private static void BuildStorm(Transform parent, ShipWreck ship, Scene scene)
        {
            var go = new GameObject("StormEvent (Quest A step 1)");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0, 3f, 20f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(200f, 10f, 22f);

            var center = new GameObject("StormCenter").transform;
            center.SetParent(go.transform, false);
            center.position = new Vector3(40f, 0f, 85f);

            Light sun = scene.GetRootGameObjects().Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);
            var storm = go.AddComponent<StormEvent>();
            storm.Setup(ship, sun, center, c.mats["Rain"], c.mats["Cloud"], c.mats["Bolt"]);
        }

        // ================================================================ shop + merchant

        private static QuestNpc BuildShop(Transform parent)
        {
            var shop = new GameObject("MerchantShop").transform;
            shop.SetParent(parent, false);
            shop.position = ShopPos;

            // Stall
            var stall = new GameObject("Stall").transform;
            stall.SetParent(shop, false);
            Material wood = c.mats["Wood"];
            LocalPrim(PrimitiveType.Cube, "Counter", stall, new Vector3(0, 0.55f, 0), new Vector3(4.2f, 1.1f, 0.8f), wood);
            LocalPrim(PrimitiveType.Cube, "CounterTop", stall, new Vector3(0, 1.13f, -0.05f), new Vector3(4.5f, 0.08f, 1f), wood);
            LocalPrim(PrimitiveType.Cube, "BackWall", stall, new Vector3(0, 1.4f, 3.1f), new Vector3(4.6f, 2.8f, 0.2f), wood);
            LocalPrim(PrimitiveType.Cube, "Shelf1", stall, new Vector3(0, 1.2f, 2.85f), new Vector3(4.2f, 0.08f, 0.4f), wood);
            LocalPrim(PrimitiveType.Cube, "Shelf2", stall, new Vector3(0, 2.0f, 2.85f), new Vector3(4.2f, 0.08f, 0.4f), wood);
            foreach (var p in new[] { new Vector3(-2.2f, 1.6f, -0.2f), new Vector3(2.2f, 1.6f, -0.2f), new Vector3(-2.2f, 1.6f, 3.1f), new Vector3(2.2f, 1.6f, 3.1f) })
                LocalPrim(PrimitiveType.Cylinder, "Post", stall, p, new Vector3(0.16f, 1.6f, 0.16f), wood);
            for (int i = 0; i < 6; i++)
                LocalPrim(PrimitiveType.Cube, "Awning", stall, new Vector3(-2.1f + i * 0.84f, 3.25f, 1.35f), new Vector3(0.84f, 0.06f, 4.4f),
                    i % 2 == 0 ? c.mats["ClothRed"] : c.mats["ClothCream"], new Vector3(-8f, 0, 0), false);
            for (int i = 0; i < 5; i++)
                LocalPrim(PrimitiveType.Cube, "Goods", stall, new Vector3(-1.7f + i * 0.85f, 1.38f, 2.85f), new Vector3(0.3f, 0.3f, 0.3f),
                    i % 2 == 0 ? c.mats["Barrel"] : c.mats["ClothCream"], null, false);
            LocalPrim(PrimitiveType.Cylinder, "Barrel", stall, new Vector3(-3.1f, 0.5f, 0.6f), new Vector3(0.8f, 0.5f, 0.8f), c.mats["Barrel"]);
            LocalPrim(PrimitiveType.Cube, "Crate", stall, new Vector3(3.1f, 0.4f, 0.8f), new Vector3(0.8f, 0.8f, 0.8f), wood, new Vector3(0, 20, 0));
            LocalPrim(PrimitiveType.Cube, "Crate", stall, new Vector3(3.2f, 1.1f, 0.9f), new Vector3(0.55f, 0.55f, 0.55f), wood, new Vector3(0, -15, 0));

            var lantern = new GameObject("Lantern");
            lantern.transform.SetParent(stall, false);
            lantern.transform.localPosition = new Vector3(0, 2.8f, 1f);
            var ll = lantern.AddComponent<Light>();
            ll.type = LightType.Point; ll.color = new Color(1f, 0.75f, 0.45f); ll.range = 8f; ll.intensity = 2f;
            lantern.AddComponent<LightFlicker>().Setup(2f, 0.25f);

            // Ransacked props — only while the merchant is kidnapped.
            var ransacked = new GameObject("Ransacked").transform;
            ransacked.SetParent(shop, false);
            LocalPrim(PrimitiveType.Cube, "ToppledCrate", ransacked, new Vector3(-1.2f, 0.35f, -1.3f), new Vector3(0.7f, 0.7f, 0.7f), wood, new Vector3(35, 40, 70));
            LocalPrim(PrimitiveType.Cube, "SmashedCrate", ransacked, new Vector3(1.4f, 0.12f, -1.6f), new Vector3(0.9f, 0.2f, 0.6f), wood, new Vector3(0, 60, 8));
            LocalPrim(PrimitiveType.Cylinder, "RolledBarrel", ransacked, new Vector3(0.3f, 0.4f, -2.4f), new Vector3(0.8f, 0.5f, 0.8f), c.mats["Barrel"], new Vector3(90, 30, 0));
            for (int i = 0; i < 7; i++)
                LocalPrim(PrimitiveType.Cube, "Scattered", ransacked, new Vector3(-2f + i * 0.6f, 0.08f, -1f - (i % 3) * 0.5f), new Vector3(0.25f, 0.15f, 0.25f),
                    i % 2 == 0 ? c.mats["ClothCream"] : c.mats["Barrel"], new Vector3(0, i * 37, 0), false);
            LocalPrim(PrimitiveType.Cube, "DragMarks", ransacked, new Vector3(-3.5f, 0.01f, -5f), new Vector3(0.5f, 0.02f, 6f), c.mats["WetSand"], new Vector3(0, -35, 0), false);

            // Merchant (behind the counter, facing the path)
            GameObject merchantGo = MakeHuman("Merchant_Shop", shop, new Vector3(0, 0, 1.3f), 180f, false, out NpcAnimator anim, out _);
            var target = merchantGo.AddComponent<QuestTarget>();
            target.targetId = "merchant_shop";
            target.markerHeight = 2.4f;

            var shopComp = merchantGo.AddComponent<MerchantShop>();
            shopComp.Setup("merchant", "The Merchant's Wares", new List<MerchantShop.Entry>
            {
                new MerchantShop.Entry { item = c.rum, basePrice = 30 },
                new MerchantShop.Entry { item = c.bandage, basePrice = 20 },
                new MerchantShop.Entry { item = c.whetstone, basePrice = 45 },
                new MerchantShop.Entry { item = c.spyglass, basePrice = 120 },
            });

            var npc = merchantGo.AddComponent<QuestNpc>();
            npc.Setup("Merchant", anim, shopComp);
            AddShopMerchantConversations(npc);

            // Quest B: kidnapping happens once the player has wandered away after Quest A.
            var kidnap = new GameObject("KidnapTrigger (Quest B)");
            kidnap.transform.SetParent(shop, false);
            kidnap.AddComponent<QuestFlagSetter>().Setup(
                QuestStateCondition.For(c.questB, QuestStatusMask.Available).Forbid(QuestContentFactory.FlagKidnapped),
                QuestContentFactory.FlagKidnapped, "merchant_kidnapped", 35f);

            // Quest B: walking into the empty shop discovers the quest.
            var zoneGo = new GameObject("EmptyShopZone (Quest B discover)");
            zoneGo.transform.SetParent(shop, false);
            zoneGo.transform.localPosition = new Vector3(0, 2.5f, 0.2f);
            var zb = zoneGo.AddComponent<BoxCollider>();
            zb.size = new Vector3(11f, 5f, 10f);
            var zone = zoneGo.AddComponent<QuestZone>();
            zone.Configure("merchant_shop_empty", true,
                QuestStateCondition.For(c.questB, QuestStatusMask.Available).Require(QuestContentFactory.FlagKidnapped));
            Set(zone, so => so.FindProperty("playSoundOnEnter").boolValue = true);

            // Bindings
            Binding(shop, "Binding: merchant at shop",
                new[] { new QuestStateCondition().Require(QuestContentFactory.FlagStormDone).Forbid(QuestContentFactory.FlagKidnapped) },
                new[] { merchantGo });
            Binding(shop, "Binding: ransacked shop",
                new[] { new QuestStateCondition().Require(QuestContentFactory.FlagKidnapped) },
                new[] { ransacked.gameObject });
            return npc;
        }

        private static void AddShopMerchantConversations(QuestNpc npc)
        {
            QuestDefinition A = c.questA, B = c.questB;
            var shopChoices = new List<DialogueChoice>
            {
                new DialogueChoice("Browse your wares", DialogueActionData.Of(DialogueAction.OpenShop)),
                new DialogueChoice("Farewell", DialogueActionData.Of(DialogueAction.None)),
            };

            npc.AddConversation(new NpcConversation
            {
                label = "After Quest B",
                condition = QuestStateCondition.For(B, QuestStatusMask.Completed),
                lines = { new DialogueLine("My rescuer! Thanks to you I'm back behind my counter where I belong."),
                          new DialogueLine("And don't worry — your discount still stands. Always will.", null, NpcGesture.Thank) },
                choices = new List<DialogueChoice>(shopChoices),
            });

            npc.AddConversation(new NpcConversation
            {
                label = "Quest A — hand in item",
                condition = QuestStateCondition.For(A, QuestStatusMask.Active, "Active_Return"),
                requiresItem = c.merchantItem,
                lines =
                {
                    new DialogueLine("I found this washed up near the wreck. Is this what you lost?", "You"),
                    new DialogueLine("My strongbox! You found it! My ledgers, my savings… everything is in here.", null, NpcGesture.Thank),
                    new DialogueLine("I am in your debt, friend. From now on you'll get ten percent off everything in my shop. Forever."),
                    new DialogueLine("Do come back and visit. I'll have fresh stock soon."),
                },
                onFinished = DialogueActionData.Of(DialogueAction.GiveItemAndReport, QuestContentFactory.EvItemDelivered, 0, c.merchantItem),
            });

            npc.AddConversation(new NpcConversation
            {
                label = "Quest A — searching",
                condition = QuestStateCondition.For(A, QuestStatusMask.Active),
                lines = { new DialogueLine("Any luck? Look along the beach and in the shallows by the wreck — a small iron-bound strongbox."),
                          new DialogueLine("And mind yourself. Scavengers have been picking over the wreckage.") },
            });

            npc.AddConversation(new NpcConversation
            {
                label = "Quest A — offer",
                condition = QuestStateCondition.For(A, QuestStatusMask.Available),
                lines =
                {
                    new DialogueLine("You there! Did you see it? That storm came out of nowhere and tore my ship apart!"),
                    new DialogueLine("I made it to shore, but something very important went over the side with the cargo."),
                    new DialogueLine("It'll have washed up somewhere along the coast near the wreck. Would you help me find it?"),
                },
                choices =
                {
                    new DialogueChoice("Accept — I'll find it.", DialogueActionData.Of(DialogueAction.AcceptQuest),
                        new DialogueLine("Bless you! Search the beach and the shallows around the wreck.", null, NpcGesture.Thank)),
                    new DialogueChoice("Not now.", DialogueActionData.Of(DialogueAction.DeclineQuest),
                        new DialogueLine("I understand… I'll be right here if you change your mind.")),
                },
            });

            npc.AddConversation(new NpcConversation
            {
                label = "After Quest A",
                condition = QuestStateCondition.For(A, QuestStatusMask.Completed),
                lines = { new DialogueLine("Welcome back, friend! Your ten percent is waiting for you.") },
                choices = new List<DialogueChoice>(shopChoices),
            });
        }

        // ================================================================ Quest A — search area

        private static void BuildSearchArea(Transform parent)
        {
            var area = new GameObject("SearchArea (Quest A)").transform;
            area.SetParent(parent, false);
            area.position = SearchCenter;
            var target = area.gameObject.AddComponent<QuestTarget>();
            target.targetId = "search_area";
            target.areaRadius = SearchRadius;
            target.markerHeight = 3f;

            // Washed-ashore debris (appears when the ship breaks).
            var debris = new GameObject("WashedAshoreDebris").transform;
            debris.SetParent(area, false);
            var rng = new System.Random(7);
            Vector3[] cratePos =
            {
                new Vector3(8, 0, 28), new Vector3(14, 0, 33), new Vector3(24, 0, 29), new Vector3(34, 0, 32), new Vector3(27, 0, 40), new Vector3(12, 0, 41),
            };
            foreach (Vector3 p in cratePos) Crate(debris, OnGround(p), (float)rng.NextDouble() * 360f);
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = OnGround(new Vector3(4 + (float)rng.NextDouble() * 34, 0, 26 + (float)rng.NextDouble() * 17));
                Prim(PrimitiveType.Cube, "Plank", debris, p + Vector3.up * 0.05f, new Vector3(0.3f, 0.08f, 1.4f + (float)rng.NextDouble() * 1.6f), c.mats["Wood"],
                    new Vector3((float)rng.NextDouble() * 8, (float)rng.NextDouble() * 360, (float)rng.NextDouble() * 8), false);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = OnGround(new Vector3(6 + (float)rng.NextDouble() * 30, 0, 27 + (float)rng.NextDouble() * 14));
                Prim(PrimitiveType.Cylinder, "Barrel", debris, p + Vector3.up * 0.4f, new Vector3(0.8f, 0.5f, 0.8f), c.mats["Barrel"],
                    new Vector3(90, (float)rng.NextDouble() * 360, 0));
            }
            Prim(PrimitiveType.Cylinder, "BrokenMast", debris, OnGround(new Vector3(19, 0, 38)) + Vector3.up * 0.3f, new Vector3(0.5f, 4.5f, 0.5f), c.mats["Wood"], new Vector3(88, 35, 0));
            Prim(PrimitiveType.Cube, "TornSail", debris, OnGround(new Vector3(22, 0, 36)) + Vector3.up * 0.05f, new Vector3(3.5f, 0.03f, 2.5f), c.mats["Sail"], new Vector3(3, 20, 4), false);

            Binding(area, "Binding: debris after wreck",
                new[] { new QuestStateCondition().Require(QuestContentFactory.FlagShipWrecked) }, new[] { debris.gameObject });

            // The lost item (in the shallows).
            BuildItem(area, OnGround(ItemPos));

            // Scavenger pirates on the beach during the quest.
            var pirates = new GameObject("BeachPirates").transform;
            pirates.SetParent(area, false);
            PatrolRoute route = Route(pirates, "Route_Beach", PatrolRouteModePingPong(), new Vector3(2, 0, 30), new Vector3(16, 0, 26), new Vector3(26, 0, 34));
            Pirate(pirates, OnGround(new Vector3(4, 0, 30)), 90, route);
            Pirate(pirates, OnGround(new Vector3(24, 0, 33)), -90, route);
            Binding(area, "Binding: beach pirates during Quest A",
                new[] { QuestStateCondition.For(c.questA, QuestStatusMask.Active | QuestStatusMask.Completed) }, new[] { pirates.gameObject });
        }

        private static void Crate(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("DebrisCrate");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            LocalPrim(PrimitiveType.Cube, "Box", root.transform, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.7f, 0.7f), c.mats["Wood"]);
            var hinge = new GameObject("LidHinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(0, 0.72f, 0.35f);
            LocalPrim(PrimitiveType.Cube, "Lid", hinge, new Vector3(0, 0.03f, -0.35f), new Vector3(0.94f, 0.07f, 0.74f), c.mats["Wood"], null, false);
            root.AddComponent<SearchableCrate>().Setup(hinge);
        }

        private static void BuildItem(Transform parent, Vector3 pos)
        {
            var root = new GameObject("MerchantsItem (pickup Q)");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 25, 12));
            root.layer = navExcludedLayer;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            GameObject body = LocalPrim(PrimitiveType.Cube, "Strongbox", visual, new Vector3(0, 0.2f, 0), new Vector3(0.6f, 0.36f, 0.4f), c.mats["Wood"], null, false);
            LocalPrim(PrimitiveType.Cube, "Lid", visual, new Vector3(0, 0.41f, 0), new Vector3(0.62f, 0.08f, 0.42f), c.mats["Wood"], null, false);
            var glow = new List<Renderer>();
            foreach (float x in new[] { -0.2f, 0.2f })
                glow.Add(LocalPrim(PrimitiveType.Cube, "Band", visual, new Vector3(x, 0.24f, 0), new Vector3(0.06f, 0.46f, 0.44f), c.mats["ItemGlow"], null, false).GetComponent<Renderer>());
            glow.Add(LocalPrim(PrimitiveType.Cube, "Lock", visual, new Vector3(0, 0.26f, -0.215f), new Vector3(0.1f, 0.12f, 0.03f), c.mats["ItemGlow"], null, false).GetComponent<Renderer>());

            // Sparkles
            var sparkGo = new GameObject("Sparkles");
            sparkGo.transform.SetParent(visual, false);
            sparkGo.transform.localPosition = new Vector3(0, 0.3f, 0);
            var ps = sparkGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.4f; main.startSpeed = 0.25f; main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(1f, 0.85f, 0.45f); main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.45f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = sparkGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = c.mats["Glow"];

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.8f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 5f; l.intensity = 1f; l.color = new Color(1f, 0.75f, 0.3f); l.shadows = LightShadows.None;

            var pickup = root.AddComponent<QuestItemPickup>();
            pickup.Setup(c.merchantItem, QuestStateCondition.For(c.questA, QuestStatusMask.Active, "Active_Search"), visual.gameObject, l, glow.ToArray());
        }

        // ================================================================ Quest B — hideout

        private static void BuildHideout(Transform parent)
        {
            var root = new GameObject("KidnappersHideout (Quest B)").transform;
            root.SetParent(parent, false);
            root.position = Hideout;

            Vector3 toShop = ShopPos - Hideout; toShop.y = 0; toShop.Normalize();
            float entranceAngle = Mathf.Atan2(toShop.z, toShop.x) * Mathf.Rad2Deg;
            Quaternion face = Quaternion.LookRotation(toShop);

            // Palisade
            var wall = new GameObject("Palisade").transform;
            wall.SetParent(root, false);
            const float r = 13f;
            var rng = new System.Random(3);
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * 360f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, entranceAngle)) < 13f) continue;
                Vector3 p = Hideout + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad)) * r;
                float h = 1.5f + (float)rng.NextDouble() * 0.4f;
                Prim(PrimitiveType.Cylinder, "Log", wall, p + Vector3.up * h, new Vector3(0.62f, h, 0.62f), c.mats["Bark"],
                    new Vector3((float)rng.NextDouble() * 4 - 2, 0, (float)rng.NextDouble() * 4 - 2));
            }
            // Entrance torches
            foreach (float side in new[] { -1f, 1f })
            {
                float a = (entranceAngle + side * 17f) * Mathf.Deg2Rad;
                Vector3 p = Hideout + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r + 0.8f);
                Prim(PrimitiveType.Cylinder, "TorchPost", root, p + Vector3.up * 1.2f, new Vector3(0.14f, 1.2f, 0.14f), c.mats["Wood"]);
                Prim(PrimitiveType.Sphere, "TorchFlame", root, p + Vector3.up * 2.5f, Vector3.one * 0.3f, c.mats["Ember"], null, false);
                var tl = new GameObject("TorchLight");
                tl.transform.SetParent(root, false);
                tl.transform.position = p + Vector3.up * 2.7f;
                var light = tl.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.2f); light.range = 9f; light.intensity = 2.5f;
                tl.AddComponent<LightFlicker>().Setup(2.5f, 0.8f);
            }

            // Tents
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = Hideout + Quaternion.Euler(0, side * 105f, 0) * toShop * 7.5f;
                var tent = new GameObject("Tent").transform;
                tent.SetParent(root, false);
                tent.SetPositionAndRotation(p, Quaternion.LookRotation(Hideout - p));
                LocalPrim(PrimitiveType.Cube, "SideL", tent, new Vector3(-0.95f, 1.1f, 0), new Vector3(2.6f, 0.08f, 3.8f), c.mats["Canvas"], new Vector3(0, 0, 55));
                LocalPrim(PrimitiveType.Cube, "SideR", tent, new Vector3(0.95f, 1.1f, 0), new Vector3(2.6f, 0.08f, 3.8f), c.mats["Canvas"], new Vector3(0, 0, -55));
            }

            // Campfire
            Vector3 fire = Hideout + toShop * 1.5f;
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                Prim(PrimitiveType.Sphere, "Stone", root, fire + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * 0.9f, new Vector3(0.4f, 0.25f, 0.35f), c.mats["Rock"], null, false);
            }
            Prim(PrimitiveType.Sphere, "Embers", root, fire + Vector3.up * 0.1f, new Vector3(1f, 0.25f, 1f), c.mats["Ember"], null, false);
            var fl = new GameObject("FireLight");
            fl.transform.SetParent(root, false);
            fl.transform.position = fire + Vector3.up * 1.2f;
            var fireLight = fl.AddComponent<Light>();
            fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.5f, 0.18f); fireLight.range = 14f; fireLight.intensity = 3f;
            fl.AddComponent<LightFlicker>().Setup(3f, 1f);
            FireParticles(root, fire);

            // Captive merchant, tied to a post at the back of the camp.
            Vector3 captivePos = Hideout - toShop * 8f;
            Prim(PrimitiveType.Cylinder, "CaptivePost", root, captivePos - toShop * 0.45f + Vector3.up * 1.4f, new Vector3(0.3f, 1.4f, 0.3f), c.mats["Wood"]);
            GameObject captive = MakeHuman("Merchant_Captive", root, Vector3.zero, 0f, true, out NpcAnimator anim, out GameObject[] ropes);
            captive.transform.SetPositionAndRotation(captivePos, face);
            var target = captive.AddComponent<QuestTarget>();
            target.targetId = "merchant_captive";
            target.markerHeight = 2.4f;
            captive.AddComponent<CaptiveNpc>().Setup(anim, ropes, "encounter_cleared:" + QuestContentFactory.Encounter, QuestContentFactory.FlagFreed);
            var npc = captive.AddComponent<QuestNpc>();
            npc.Setup("Merchant", anim, null);
            AddCaptiveConversations(npc);

            Binding(root, "Binding: captive merchant",
                new[] { new QuestStateCondition().Require(QuestContentFactory.FlagKidnapped) }, new[] { captive }, null, 4f);

            // Guidance target
            var hideoutTarget = new GameObject("HideoutTarget");
            hideoutTarget.transform.SetParent(root, false);
            hideoutTarget.transform.position = Hideout + toShop * 4f;
            var ht = hideoutTarget.AddComponent<QuestTarget>();
            ht.targetId = "hideout";
            ht.markerHeight = 5f;

            // Encounter
            var encGo = new GameObject("KidnapperEncounter");
            encGo.transform.SetParent(root, false);
            encGo.transform.position = Hideout + Vector3.up;
            var sphere = encGo.AddComponent<SphereCollider>();
            sphere.radius = 11.5f;
            var points = new List<Transform>();
            float[] angles = { 35f, 125f, 235f, 325f };
            for (int i = 0; i < 4; i++)
            {
                var sp = new GameObject("Spawn_" + (i + 1)).transform;
                sp.SetParent(encGo.transform, false);
                sp.position = Hideout + Quaternion.Euler(0, angles[i], 0) * toShop * 6f;
                sp.rotation = face;
                points.Add(sp);
            }
            var enc = encGo.AddComponent<CombatEncounter>();
            enc.Setup(QuestContentFactory.Encounter, AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath), points,
                QuestStateCondition.For(c.questB, QuestStatusMask.Active, "Active_Investigate", "Active_Rescue"));

            // Pirates along the road inland (Quest B step 4: "fight small pirate enemies if needed").
            var roadPirates = new GameObject("RoadPirates").transform;
            roadPirates.SetParent(root.parent, false);
            // Kept well outside the hideout's group-alert range so the rescue stays a 4-kidnapper fight.
            PatrolRoute route = Route(roadPirates, "Route_Road", PatrolRouteModePingPong(), new Vector3(-34, 0, -6), new Vector3(-40, 0, -14), new Vector3(-46, 0, -22));
            Pirate(roadPirates, OnGround(new Vector3(-35, 0, -7)), 200, route);
            Pirate(roadPirates, OnGround(new Vector3(-45, 0, -21)), 20, route);
            Binding(root, "Binding: road pirates during Quest B",
                new[] { QuestStateCondition.For(c.questB, QuestStatusMask.Active | QuestStatusMask.Completed) }, new[] { roadPirates.gameObject });
        }

        private static void AddCaptiveConversations(QuestNpc npc)
        {
            QuestDefinition B = c.questB;
            string back = "I'll make my way back to the shop now. Come and see me!";
            npc.AddConversation(new NpcConversation
            {
                label = "Quest B — rescued, choose reward",
                condition = QuestStateCondition.For(B, QuestStatusMask.Active, "Rescue_Talk"),
                lines =
                {
                    new DialogueLine("You came for me! I truly thought I was done for.", null, NpcGesture.Thank),
                    new DialogueLine("Those brutes wanted the key to my strongbox. I'd have gone to the bottom of the sea before handing it over."),
                    new DialogueLine("Please, let me repay you. Choose whichever you like — but choose wisely, I can only part with one."),
                },
                choices =
                {
                    new DialogueChoice("Weapon Upgrade  (+25% sword damage)", DialogueActionData.Of(DialogueAction.CompleteQuestWithReward, null, 0),
                        new DialogueLine("A fine blade deserves a keener edge. There — it'll bite deeper now.", null, NpcGesture.Thank),
                        new DialogueLine(back)),
                    new DialogueChoice("Merchant's Lucky Compass  (unique item)", DialogueActionData.Of(DialogueAction.CompleteQuestWithReward, null, 1),
                        new DialogueLine("My lucky compass. It has never once steered me wrong — may it do the same for you.", null, NpcGesture.Thank),
                        new DialogueLine(back)),
                },
            });
            npc.AddConversation(new NpcConversation
            {
                label = "Quest B — mid fight",
                condition = QuestStateCondition.For(B, QuestStatusMask.Active, "Active_Rescue"),
                lines = { new DialogueLine("Behind you! Watch out!") },
            });
            npc.AddConversation(new NpcConversation
            {
                label = "Quest B — discovered, not accepted",
                condition = QuestStateCondition.For(B, QuestStatusMask.Discovered),
                lines = { new DialogueLine("Help! Please… get me out of here before they come back!") },
                choices =
                {
                    new DialogueChoice("Accept — hold on, I'll get you out.", DialogueActionData.Of(DialogueAction.AcceptQuest),
                        new DialogueLine("Hurry! They're coming!")),
                    new DialogueChoice("Not now.", DialogueActionData.Of(DialogueAction.DeclineQuest),
                        new DialogueLine("Don't leave me here…")),
                },
            });
            npc.AddConversation(new NpcConversation
            {
                label = "Quest B — investigating",
                condition = QuestStateCondition.For(B, QuestStatusMask.Active),
                lines = { new DialogueLine("Careful — they'll be back any moment!") },
            });
        }

        private static void FireParticles(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.position = pos + Vector3.up * 0.2f;
            go.layer = navExcludedLayer;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.8f, 0.3f));
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 40f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.4f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = c.mats["Glow"];
        }

        // ================================================================ characters

        /// <summary>Kevin Iglesias humanoid with the merchant animator, hat and sash (plus ropes if captive).</summary>
        private static GameObject MakeHuman(string name, Transform parent, Vector3 localPos, float yaw, bool captive, out NpcAnimator anim, out GameObject[] ropes)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MerchantModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SetLayerRecursive(model, navExcludedLayer);
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = c.merchantController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.sharedMaterial = c.mats["Merchant"];

            Transform head = HumanBone(model, "Head");
            Transform hips = HumanBone(model, "Hips");
            Transform chest = HumanBone(model, "Chest") ?? HumanBone(model, "Spine");
            if (head != null)
            {
                Accessory(PrimitiveType.Cylinder, "HatBrim", head, head.position + Vector3.up * 0.17f, new Vector3(0.44f, 0.012f, 0.44f), c.mats["Hat"]);
                Accessory(PrimitiveType.Cylinder, "HatCrown", head, head.position + Vector3.up * 0.25f, new Vector3(0.25f, 0.08f, 0.25f), c.mats["Hat"]);
            }
            if (hips != null)
                Accessory(PrimitiveType.Cylinder, "Sash", hips, hips.position + Vector3.up * 0.1f, new Vector3(0.36f, 0.05f, 0.26f), c.mats["Sash"]);

            var ropeList = new List<GameObject>();
            if (captive && chest != null)
            {
                foreach (float h in new[] { -0.15f, 0.05f, 0.22f })
                    ropeList.Add(Accessory(PrimitiveType.Cylinder, "Rope", chest, chest.position + Vector3.up * h, new Vector3(0.46f, 0.025f, 0.34f), c.mats["Rope"]));
                if (hips != null)
                    ropeList.Add(Accessory(PrimitiveType.Cylinder, "RopeHips", hips, hips.position + Vector3.down * 0.05f, new Vector3(0.42f, 0.025f, 0.32f), c.mats["Rope"]));
            }
            ropes = ropeList.ToArray();

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0); col.height = 1.8f; col.radius = 0.3f;

            anim = root.AddComponent<NpcAnimator>();
            Set(anim, so =>
            {
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("restrained").boolValue = captive;
                so.FindProperty("canTurn").boolValue = !captive;
            });
            return root;
        }

        private static Transform HumanBone(GameObject model, string humanName)
        {
            var importer = AssetImporter.GetAtPath(MerchantModelPath) as ModelImporter;
            if (importer == null) return null;
            foreach (HumanBone b in importer.humanDescription.human)
                if (b.humanName == humanName) return FindDeep(model.transform, b.boneName);
            return null;
        }

        private static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform ch in t)
            {
                Transform f = FindDeep(ch, n);
                if (f != null) return f;
            }
            return null;
        }

        private static GameObject Accessory(PrimitiveType type, string name, Transform bone, Vector3 worldPos, Vector3 worldScale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(worldPos, Quaternion.identity);
            go.transform.localScale = worldScale;
            go.transform.SetParent(bone, true);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.layer = navExcludedLayer;
            return go;
        }

        private static void Pirate(Transform parent, Vector3 pos, float yaw, PatrolRoute route)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null && route != null) Set(ai, so => so.FindProperty("patrolRoute").objectReferenceValue = route);
        }

        private static int PatrolRouteModePingPong() => 1;

        private static PatrolRoute Route(Transform parent, string name, int mode, params Vector3[] points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            for (int i = 0; i < points.Length; i++)
            {
                var wp = new GameObject("Waypoint_" + (i + 1));
                wp.transform.SetParent(go.transform, false);
                wp.transform.position = OnGround(points[i]);
            }
            var route = go.AddComponent<PatrolRoute>();
            Set(route, so =>
            {
                SerializedProperty m = so.FindProperty("mode");
                if (m != null) m.enumValueIndex = mode;
            });
            return route;
        }

        // ================================================================ navmesh

        private static void BakeNavMesh(Scene scene, System.Text.StringBuilder log)
        {
            NavMeshSurface surface = Object.FindObjectsByType<NavMeshSurface>().FirstOrDefault(s => s.gameObject.scene == scene);
            if (surface == null)
            {
                surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
                log.AppendLine("  created NavMeshSurface");
            }
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            int exclude = LayerMask.GetMask("Water", "Player", "Enemy", "UI", "TransparentFX", "Ignore Raycast");
            surface.layerMask = ~exclude;

            // Hide toggled groups' state doesn't matter for baking; everything active is included.
            surface.RemoveData();
            surface.navMeshData = null;
            surface.BuildNavMesh();
            string path = QuestContentFactory.GenDir + "/QuestSystemTestScene_NavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, path);
                log.AppendLine("  navmesh baked -> " + path);
            }
            EditorUtility.SetDirty(surface);
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ================================================================ helpers

        private static void Binding(Transform parent, string name, IEnumerable<QuestStateCondition> conds, IEnumerable<GameObject> whenTrue,
            IEnumerable<GameObject> whenFalse = null, float hideDelay = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<QuestStateBinding>().Setup(QuestStateBinding.Match.All, conds, whenTrue, whenFalse, hideDelay);
        }

        private static Vector3 OnGround(Vector3 p)
        {
            int mask = ~LayerMask.GetMask("Water", "Player", "Enemy", "TransparentFX", "UI");
            if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(p.x, 0f, p.z);
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 worldPos, Vector3 scale, Material m,
            Vector3? euler = null, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject LocalPrim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m,
            Vector3? euler = null, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static void Set(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
