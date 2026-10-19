using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheetHappens.EditorTools
{
    /// <summary>
    /// Menu: Sheet Happens > Build Game Test Scene / Play Game Test.
    /// Copies S1_Level into GameTest.unity (S1_Level itself is never modified) and adds the game systems and the player.
    /// </summary>
    public static class GameTestBuilder
    {
        const string Source = "Assets/_Project/Scenes/S1_Level.unity";
        const string Target = "Assets/_Project/Scenes/GameTest.unity";
        const string ResetKey = "SheetHappens.ResetPlayModeStartScene";

        [MenuItem("Sheet Happens/Build Game Test Scene", priority = 1)]
        public static void Build()
        {
            if (!File.Exists(Target))
            {
                if (!AssetDatabase.CopyAsset(Source, Target)) { Debug.LogError($"[SheetHappens] Could not copy {Source}."); return; }
                AssetDatabase.Refresh();
            }

            Scene scene = SceneManager.GetSceneByPath(Target);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Additive);
                openedHere = true;
            }

            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "[GAME]" || r.name == "[PLAYER]" || r.name == "[ROOF]" || r.name == "[LADDERS]" || r.name == "AtticBell") Object.DestroyImmediate(r);

            // Never touch the hand-made furniture layout: just mark it as a current layout.
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "[FURNITURE]" && r.GetComponent<FurnitureLayoutInfo>() == null) r.AddComponent<FurnitureLayoutInfo>();

            // Make sure the house can be walked on and blocks ghosts: add mesh colliders where a mesh has none.
            int colliders = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mr in (root.name.StartsWith("[FURNITURE") ? new MeshRenderer[0] : root.GetComponentsInChildren<MeshRenderer>(true)))
                {
                    if (mr.GetComponentInParent<Collider>(true) != null || mr.GetComponentInChildren<Collider>(true) != null) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    colliders++;
                }

            // Walls: many wall pieces from the asset pack have a MeshCollider with no mesh (a broken reference),
            // so the player walked straight through them. Give each one its own render mesh.
            int fixedCols = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.sharedMesh != null) continue;
                    var mf = mc.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mc.sharedMesh = mf.sharedMesh;
                    fixedCols++;
                }

            // Doors: swing open with E, ghosts float through, so they must not be static-batched.
            int doors = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!LevelSetup.IsDoorName(t.name) || t.GetComponentInChildren<Renderer>(true) == null) continue;
                    if (t.GetComponentInParent<Door>(true) || t.GetComponentInChildren<Door>(true)) continue;
                    t.gameObject.AddComponent<Door>();
                    t.gameObject.AddComponent<GhostPassable>();
                    foreach (var c in t.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);
                    doors++;
                }

            // Roof over the top floor (the attic), the ladder to the attic, and the bell hanging from the roof.
            Bounds deck = TopFloorBounds(scene, out float deckY);
            string roofInfo = "no top floor found";
            if (deck.size.x > 1f)
            {
                var roof = BuildRoof(scene, deck, deckY, out float ridgeUnderside);
                int ladders = BuildLadders(scene, deck);
                BuildBell(scene, deck, deckY, ridgeUnderside);
                roofInfo = $"roof over {deck.size.x:0.0} x {deck.size.z:0.0} m at y={deckY:0.0}, {ladders} ladder(s)";
            }

            // Game systems
            var game = new GameObject("[GAME]");
            SceneManager.MoveGameObjectToScene(game, scene);
            game.AddComponent<GhostGrid>();
            var lvl = game.AddComponent<LevelSetup>();
            game.AddComponent<GameManager>();
            game.AddComponent<GameHUD>();
            game.AddComponent<NightAmbience>();
            var night = game.AddComponent<NightLighting>();
            night.lampPrefab = Load("Assets/BK_AlchemistHouse/Prefabs/Items/Lights/Oillamp01_on.prefab");
            lvl.wispPrefab = Load("Assets/_Project/Art/Prefabs/Ghost_Wisp_Unity.prefab");
            lvl.blueBooPrefab = Load("Assets/_Project/Art/Prefabs/Ghost_BlueBoo_Unity.prefab");
            lvl.greenSneakPrefab = Load("Assets/_Project/Art/Prefabs/Ghost_GreenSneak_Unity.prefab");
            lvl.grandpaPrefab = Load("Assets/_Project/Art/Prefabs/Ghost_Grandpa_Unity.prefab");
            const string bk = "Assets/BK_AlchemistHouse/Prefabs/";
            lvl.hideSpotPrefab = Load(bk + "Furniture/WoodTableSquare01.prefab") ?? Load(bk + "Furniture/Table01.prefab");
            lvl.throwablePrefabs = new[] { Load(bk + "Items/Books/Book01.prefab"), Load(bk + "Items/Books/Book02.prefab"), Load(bk + "Items/Books/Book03.prefab"),
                                           Load(bk + "Items/Dishes/ClutterTanker01.prefab"), Load(bk + "Items/Dishes/ClutterBowl01.prefab") }.Where(x => x != null).ToArray();

            // Our own furniture (built in the house, already house-sized) comes first so it always appears.
            const string own = "Assets/_Project/Prefabs/Furniture/";
            var pantry = Load(own + "Pantry.prefab");
            var bed = Load(own + "Beed.prefab");
            var dining = Load(own + "Dinning table.prefab");
            lvl.houseSizedPrefabs = new[] { pantry, bed, dining }.Where(x => x != null).ToArray();
            lvl.wallHungPrefabs = new[] { pantry }.Where(x => x != null).ToArray();
            lvl.hideCupboards = new[] { pantry }.Concat(LoadAll(bk + "Furniture/", "WoodCupboard03")).Where(x => x != null).ToArray();
            lvl.wallFurniture = new[] { bed, bed }.Where(x => x != null).Concat(LoadAll(bk + "Furniture/", "WoodCupboard01", "WoodCupboard02", "Drawer01", "Drawer02", "Drawer03", "WoodDrawer01", "Stove", "Workbench", "TableSink", "Alembic02")).ToArray();
            lvl.floorDecor = LoadAll(bk + "Items/", "Misc/Globe", "Lab/CaldronTripod01", "Lab/CaldronTripod02", "Lab/CaldronTripod03", "Lab/Caldron01",
                                     "Misc/Basket03", "Misc/Trunk01", "Misc/Log01", "Organic/DragonSkull");
            lvl.wallDecor = LoadAll(bk, "Items/Misc/Painting01", "Items/Misc/painting02", "Items/Misc/painting03", "Items/Misc/painting04", "Furniture/Board", "Furniture/WoodShelf01", "Furniture/WoodShelf01");
            lvl.clutter = LoadAll(bk + "Items/", "Books/Books1", "Books/Books2", "Books/Books3", "Books/Books4", "Books/Book05", "Books/Book09",
                                  "Bottles/Bottle01", "Bottles/Bottle02", "Bottles/Bottle03", "Bottles/Bottle04", "Bottles/Flask01", "Bottles/Flask03", "Bottles/Flask05",
                                  "Dishes/ClutterBowl01", "Dishes/ClutterBowl02", "Dishes/ClutterPichet01", "Dishes/ClutterPichet02", "Dishes/ClutterPlate01", "Dishes/ClutterTanker01", "Dishes/ClutterGlass01",
                                  "Lights/Candle01", "Lights/Candle01c", "Organic/Skull01b", "Organic/Skull02", "Organic/Jar01", "Organic/JarBrain", "Organic/JarBaby01", "Organic/Apple",
                                  "Lab/LabSetup01", "Lab/LabSetup02", "Lab/LabSetup03");
            lvl.diningTables = new[] { dining }.Where(x => x != null).Concat(LoadAll(bk + "Furniture/", "Table01", "table03", "Table04", "WoodTableSquare01b")).ToArray();
            lvl.chairs = LoadAll(bk + "Furniture/", "Chair01", "WoodStool");
            lvl.carpets = LoadAll(bk + "Furniture/", "Carpet01", "Carpet02", "Carpet03");
            lvl.barricadePrefabs = LoadAll(bk, "Items/Misc/Trunk01", "Items/Misc/Basket01", "Items/Misc/Basket02");
            lvl.urpTemplate = AssetDatabase.LoadAssetAtPath<Material>(bk.Replace("Prefabs/", "Textures/Surfaces/Materials/WoodRough03.mat"));

            // Player: first-person camera + the Ghost Vacuum model
            var player = new GameObject("[PLAYER]");
            SceneManager.MoveGameObjectToScene(player, scene);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.875f, 0f); cc.stepOffset = 0.45f; cc.slopeLimit = 50f;
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();
            var pc = player.AddComponent<PlayerController>();
            pc.cameraRoot = camGo.transform;
            var vac = player.AddComponent<Vacuum>();
            vac.cam = cam;
            var vacPrefab = Load("Assets/_Project/Art/Prefabs/Ghost_Vacuum_V2.prefab");
            if (vacPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(vacPrefab, scene);
                model.transform.SetParent(camGo.transform, false);
                model.transform.localPosition = new Vector3(0.26f, -0.26f, 0.55f);
                model.transform.localRotation = Quaternion.identity;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                vac.model = model.transform;
            }
            player.AddComponent<Interactor>();
            player.AddComponent<FirstPersonHands>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == Target)) { list.Add(new EditorBuildSettingsScene(Target, true)); EditorBuildSettings.scenes = list.ToArray(); }

            if (openedHere && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[SheetHappens] GameTest scene built ({fixedCols} broken wall colliders fixed, {colliders} mesh colliders added, {doors} doors, {roofInfo}). Use Sheet Happens > Play Game Test.");
        }

        [MenuItem("Sheet Happens/Play Game Test", priority = 2)]
        public static void Play()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Target);
            if (asset == null) { Build(); asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Target); }
            if (asset == null) return;
            EditorSceneManager.playModeStartScene = asset; // plays GameTest without closing the scene you have open
            SessionState.SetBool(ResetKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ResetKey, false))
                {
                    EditorSceneManager.playModeStartScene = null;
                    SessionState.SetBool(ResetKey, false);
                }
            };
        }

        static GameObject[] LoadAll(string folder, params string[] names) =>
            names.Select(n => Load(folder + n + ".prefab")).Where(x => x != null).ToArray();

        const string BkMats = "Assets/BK_AlchemistHouse/Textures/Surfaces/Materials/";
        const string OwnMats = "Assets/_Project/Art/Materials/GameTest";

        /// <summary>The highest flat floor of the house (the top deck) and its footprint.</summary>
        static Bounds TopFloorBounds(Scene scene, out float topY)
        {
            var floors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.name.ToLower().Contains("floor") && r.bounds.size.y < 0.6f && r.bounds.size.x > 1f).ToList();
            topY = floors.Count > 0 ? floors.Max(r => r.bounds.max.y) : 0f;
            float y = topY;
            var top = floors.Where(r => r.bounds.max.y > y - 0.3f).ToList();
            if (top.Count == 0) return default;
            Bounds b = top[0].bounds;
            foreach (var r in top) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>A gable roof (two sloped slabs, gable ends, low side walls and a ridge beam) over the top deck.</summary>
        static GameObject BuildRoof(Scene scene, Bounds deck, float deckY, out float ridgeUnderside)
        {
            const float knee = 2.3f, rise = 3.2f, overhang = 0.6f, slab = 0.18f; // knee walls tall enough to stand next to (the ladder comes up at the edge)
            var roofMat = AssetDatabase.LoadAssetAtPath<Material>(BkMats + "WoodRough04.mat");
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>(BkMats + "PlasterBare05.mat");
            var beamMat = AssetDatabase.LoadAssetAtPath<Material>(BkMats + "WoodRough03.mat");
            var root = new GameObject("[ROOF]");
            SceneManager.MoveGameObjectToScene(root, scene);

            bool ridgeAlongX = deck.size.x >= deck.size.z;
            float len = ridgeAlongX ? deck.size.x : deck.size.z;   // along the ridge
            float span = ridgeAlongX ? deck.size.z : deck.size.x;  // across the ridge
            float half = span * 0.5f;
            float eaveY = deckY + knee, ridgeY = eaveY + rise;
            Vector3 c = new Vector3(deck.center.x, 0f, deck.center.z);
            Vector3 along = ridgeAlongX ? Vector3.right : Vector3.forward;
            Vector3 across = ridgeAlongX ? Vector3.forward : Vector3.right;
            float angle = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            float slopeLen = Mathf.Sqrt(half * half + rise * rise) + overhang;

            for (int side = -1; side <= 1; side += 2)
            {
                // Each slab runs from the ridge down past the eave by 'overhang'.
                Vector3 dirDown = (across * side * half + Vector3.down * rise).normalized;
                Vector3 ridgePt = c + Vector3.up * ridgeY;
                Vector3 mid = ridgePt + dirDown * (slopeLen * 0.5f);
                Vector3 normal = Vector3.Cross(dirDown, along * side).normalized;
                if (normal.y < 0) normal = -normal;
                var s = Box(root.transform, "RoofSlope" + (side < 0 ? "A" : "B"), mid + normal * (slab * 0.5f),
                            Quaternion.LookRotation(dirDown, normal), new Vector3(len + overhang * 2f, slab, slopeLen), roofMat);
                // low side wall under the eave
                Box(root.transform, "KneeWall" + (side < 0 ? "A" : "B"), c + across * side * (half - 0.1f) + Vector3.up * (deckY + knee * 0.5f),
                    Quaternion.LookRotation(along), new Vector3(0.2f, knee, len), wallMat);
            }
            // ridge beam the bell hangs from
            ridgeUnderside = ridgeY - 0.45f;
            Box(root.transform, "RidgeBeam", c + Vector3.up * (ridgeY - 0.3f), Quaternion.LookRotation(along), new Vector3(0.25f, 0.3f, len), beamMat);

            // gable ends (pentagon walls) at both ends of the ridge
            var mesh = GableMesh(span, knee, rise);
            string meshPath = "Assets/_Project/Art/Meshes/GameTest_RoofGable.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            for (int end = -1; end <= 1; end += 2)
            {
                var g = new GameObject("GableEnd" + (end < 0 ? "A" : "B"));
                g.transform.SetParent(root.transform, false);
                g.transform.position = c + along * end * (len * 0.5f - 0.05f) + Vector3.up * deckY;
                g.transform.rotation = Quaternion.LookRotation(along);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = wallMat;
                g.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            // a dim warm lamp so the attic is not pitch black
            var lamp = new GameObject("AtticLamp").AddComponent<Light>();
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.position = c + Vector3.up * (eaveY + 1.2f);
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.68f, 0.38f); lamp.range = 9f; lamp.intensity = 2.5f;
            return root;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = size;
            if (m) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>A double-sided pentagon in the local XY plane: width 'span', a 'knee' high rectangle and a 'rise' high triangle on top.</summary>
        static Mesh GableMesh(float span, float knee, float rise)
        {
            float h = span * 0.5f;
            var v = new[] { new Vector3(-h, 0, 0), new Vector3(h, 0, 0), new Vector3(h, knee, 0), new Vector3(0, knee + rise, 0), new Vector3(-h, knee, 0) };
            var verts = v.Concat(v).ToArray();
            int[] front = { 0, 4, 2, 0, 2, 1, 4, 3, 2 };
            var tris = front.Concat(new[] { 5, 7, 9, 5, 6, 7, 9, 7, 8 }).ToArray();
            var uv = verts.Select(p => new Vector2(p.x / 2.5f, p.y / 2.5f)).ToArray();
            var mesh = new Mesh { name = "RoofGable", vertices = verts, triangles = tris, uv = uv };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Finds ladders (upright fence pieces stacked between floors) and adds a climbing zone in front of each.</summary>
        static int BuildLadders(Scene scene, Bounds deck)
        {
            var pieces = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.name.StartsWith("IntWoodFence01") && r.bounds.size.y > 2f && Mathf.Min(r.bounds.size.x, r.bounds.size.z) < 0.3f).ToList();
            var root = new GameObject("[LADDERS]");
            SceneManager.MoveGameObjectToScene(root, scene);
            var groups = new System.Collections.Generic.List<Bounds>();
            foreach (var p in pieces)
            {
                int k = groups.FindIndex(g => Mathf.Abs(g.center.x - p.bounds.center.x) < 0.5f && Mathf.Abs(g.center.z - p.bounds.center.z) < 0.5f);
                if (k < 0) groups.Add(p.bounds); else { var g = groups[k]; g.Encapsulate(p.bounds); groups[k] = g; }
            }
            foreach (var g in groups)
            {
                bool thinZ = g.size.z < g.size.x;
                Vector3 toRoom = thinZ ? Vector3.forward * Mathf.Sign(deck.center.z - g.center.z) : Vector3.right * Mathf.Sign(deck.center.x - g.center.x);
                var zone = new GameObject("LadderZone");
                zone.transform.SetParent(root.transform, false);
                float bottom = g.min.y - 0.1f, top = g.max.y + 0.45f;
                zone.transform.position = new Vector3(g.center.x, (bottom + top) * 0.5f, g.center.z) + toRoom * 0.55f;
                var box = zone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                float width = Mathf.Max(thinZ ? g.size.x : g.size.z, 0.9f) + 0.2f;
                box.size = thinZ ? new Vector3(width, top - bottom, 1.1f) : new Vector3(1.1f, top - bottom, width);
                zone.AddComponent<Ladder>();
            }
            return groups.Count;
        }

        /// <summary>The bell hangs on a rope from the ridge beam, on the far side of the attic from the ladder.</summary>
        static void BuildBell(Scene scene, Bounds deck, float deckY, float anchorY)
        {
            if (!AssetDatabase.IsValidFolder(OwnMats)) AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "GameTest");
            var template = AssetDatabase.LoadAssetAtPath<Material>(BkMats + "WoodRough03.mat");
            var brass = MatAsset("M_GT_BellBrass", template, new Color(0.85f, 0.65f, 0.2f), new Color(0.3f, 0.2f, 0.05f));
            var rope = MatAsset("M_GT_BellRope", template, new Color(0.45f, 0.33f, 0.2f), default);
            bool ridgeAlongX = deck.size.x >= deck.size.z;
            var ladder = Object.FindObjectsByType<Ladder>(FindObjectsSortMode.None).FirstOrDefault(l => l.gameObject.scene == scene);
            Vector3 c = deck.center;
            float offset = (ridgeAlongX ? deck.size.x : deck.size.z) * 0.25f;
            float sign = 1f;
            if (ladder) sign = ridgeAlongX ? -Mathf.Sign(ladder.transform.position.x - c.x) : -Mathf.Sign(ladder.transform.position.z - c.z);
            Vector3 floor = new Vector3(c.x, deckY, c.z) + (ridgeAlongX ? Vector3.right : Vector3.forward) * offset * sign;
            var bell = Bell.CreateHanging(floor, anchorY, rope, brass);
            SceneManager.MoveGameObjectToScene(bell.gameObject, scene);
        }

        static Material MatAsset(string name, Material template, Color color, Color emission)
        {
            string path = OwnMats + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(template);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetColor("_BaseColor", color);
            m.color = color;
            if (emission != default) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return go != null ? go : null;
        }
    }
}
