using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheetHappens.EditorTools
{
    /// <summary>
    /// Keeps a furniture layout so it can be edited by hand:
    /// play the game, choose "Save Furniture Layout", stop playing; the furniture is written into GameTest under [FURNITURE].
    /// </summary>
    public static class FurnitureLayout
    {
        const string Target = "Assets/_Project/Scenes/GameTest.unity";
        const string Pending = "Temp/SheetHappens_FurnitureLayout.json";
        const string UrpMats = "Assets/_Project/Art/Materials/GameTest/URP";

        [System.Serializable] class Item { public string prefab; public int kind; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
        [System.Serializable] class Layout { public List<Item> items = new List<Item>(); }

        [MenuItem("Sheet Happens/Save Furniture Layout (while playing)", priority = 20)]
        static void Save()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Save Furniture Layout", "Start the game first (Sheet Happens > Play Game Test), then use this while it is running. The furniture you see is written into the GameTest scene when you stop playing.", "OK");
                return;
            }
            var layout = new Layout();
            foreach (var p in Object.FindObjectsByType<PlacedProp>(FindObjectsSortMode.None))
            {
                string path = p.prefab ? AssetDatabase.GetAssetPath(p.prefab) : null;
                if (p.transform.parent != null && p.transform.parent.name == "[FURNITURE]") continue; // already saved pieces
                if (string.IsNullOrEmpty(path)) continue;
                layout.items.Add(new Item { prefab = path, kind = (int)p.kind, pos = p.transform.position, rot = p.transform.rotation, scale = p.transform.localScale });
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Pending, JsonUtility.ToJson(layout));
            Debug.Log($"[SheetHappens] Saved {layout.items.Count} furniture pieces. Stop playing to write them into the GameTest scene.");
        }

        [MenuItem("Sheet Happens/Clear Saved Furniture (random again)", priority = 21)]
        static void Clear()
        {
            if (Application.isPlaying) { Debug.LogWarning("[SheetHappens] Stop playing first."); return; }
            Scene scene = OpenTarget(out bool openedHere);
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "[FURNITURE]") Object.DestroyImmediate(r);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (openedHere && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[SheetHappens] Saved furniture removed; the game furnishes the house randomly again.");
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && File.Exists(Pending)) EditorApplication.delayCall += Bake;
            };
        }

        static Scene OpenTarget(out bool openedHere)
        {
            Scene scene = SceneManager.GetSceneByPath(Target);
            openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Additive);
                openedHere = true;
            }
            return scene;
        }

        static void Bake()
        {
            if (!File.Exists(Pending)) return;
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Pending));
            File.Delete(Pending);
            Scene scene = OpenTarget(out bool openedHere);
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "[FURNITURE]") Object.DestroyImmediate(r);
            var root = new GameObject("[FURNITURE]");
            root.AddComponent<FurnitureLayoutInfo>();
            SceneManager.MoveGameObjectToScene(root, scene);
            int n = 0;
            foreach (var it in layout.items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(it.prefab);
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.SetParent(root.transform, true);
                go.transform.SetPositionAndRotation(it.pos, it.rot);
                go.transform.localScale = it.scale;
                var tag = go.AddComponent<PlacedProp>();
                tag.kind = (PropKind)it.kind;
                tag.prefab = prefab;
                UseUrpMaterials(go);
                n++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (openedHere && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[SheetHappens] {n} furniture pieces written into GameTest under [FURNITURE]. Open the GameTest scene to move, delete or duplicate them.");
        }

        /// <summary>The asset pack's furniture still uses the built-in shader (pink in URP): give the scene copies URP materials.</summary>
        static void UseUrpMaterials(GameObject go)
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/BK_AlchemistHouse/Textures/Surfaces/Materials/WoodRough03.mat");
            if (template == null) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/Materials/GameTest")) AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "GameTest");
            if (!AssetDatabase.IsValidFolder(UrpMats)) AssetDatabase.CreateFolder("Assets/_Project/Art/Materials/GameTest", "URP");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || src.shader.name.StartsWith("Universal Render Pipeline") || src.shader.name.StartsWith("Shader Graphs")) continue;
                    string path = $"{UrpMats}/{src.name}.mat";
                    var dst = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (dst == null)
                    {
                        dst = new Material(template) { name = src.name };
                        dst.SetTexture("_BaseMap", src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);
                        dst.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
                        var bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
                        dst.SetTexture("_BumpMap", bump);
                        if (bump) dst.EnableKeyword("_NORMALMAP"); else dst.DisableKeyword("_NORMALMAP");
                        dst.SetFloat("_Smoothness", 0.25f);
                        AssetDatabase.CreateAsset(dst, path);
                    }
                    mats[i] = dst;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
