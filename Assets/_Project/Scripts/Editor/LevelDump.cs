using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheetHappens.EditorTools
{
    /// <summary>Writes every renderer and collider of the open scenes to Temp/level_dump.tsv (for debugging the level layout).</summary>
    public static class LevelDump
    {
        [MenuItem("Sheet Happens/Debug/Dump Level Layout", priority = 50)]
        public static void Dump()
        {
            var sb = new StringBuilder("scene\tpath\tkind\tcenter\tsize\trot\textra\n");
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        sb.Append($"{scene.name}\t{Path(r.transform)}\tR\t{V(r.bounds.center)}\t{V(r.bounds.size)}\t{V(r.transform.eulerAngles)}\t{(mf && mf.sharedMesh ? mf.sharedMesh.name : "")}\n");
                    }
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    {
                        string extra = c.GetType().Name + (c.enabled ? "" : " DISABLED") + (c.isTrigger ? " TRIGGER" : "");
                        if (c is MeshCollider mc) extra += " mesh=" + (mc.sharedMesh ? mc.sharedMesh.name : "null") + (mc.convex ? " convex" : "");
                        sb.Append($"{scene.name}\t{Path(c.transform)}\tC\t{V(c.bounds.center)}\t{V(c.bounds.size)}\t{V(c.transform.eulerAngles)}\t{extra}\n");
                    }
                }
            }
            // Prefab sizes (furniture and items) and their child parts.
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/BK_AlchemistHouse/Prefabs/Furniture", "Assets/BK_AlchemistHouse/Prefabs/Items", "Assets/BK_AlchemistHouse/Prefabs/Architecture" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = Object.Instantiate(prefab);
                go.transform.position = Vector3.zero;
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.Append($"PREFAB\t{path}\tP\t{V(b.center)}\t{V(b.size)}\t{V(prefab.transform.eulerAngles)}\tscale={V(prefab.transform.localScale)} parts={rs.Length} cols={go.GetComponentsInChildren<Collider>(true).Length} rb={(go.GetComponentInChildren<Rigidbody>() != null)}\n");
                    if (rs.Length > 1)
                        foreach (var r in rs) sb.Append($"PART\t{path}/{r.name}\tp\t{V(r.bounds.center)}\t{V(r.bounds.size)}\t{V(r.transform.localEulerAngles)}\tpivot={V(r.transform.position)}\n");
                }
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/level_dump.tsv", sb.ToString());
            Debug.Log("[SheetHappens] Level layout written to Temp/level_dump.tsv");
        }

        [MenuItem("Sheet Happens/Debug/Dump Vacuum Mesh", priority = 51)]
        public static void DumpVacuum()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Prefabs/Ghost_Vacuum_V2.prefab");
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var sb = new StringBuilder("part,x,y,z\n");
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var verts = mesh.vertices;
                var mr = mf.GetComponent<Renderer>();
                string mat = mr && mr.sharedMaterials.Length > 0 && mr.sharedMaterials[0] ? mr.sharedMaterials[0].name : "";
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    string m = mr && sub < mr.sharedMaterials.Length && mr.sharedMaterials[sub] ? mr.sharedMaterials[sub].name : mat;
                    var seen = new System.Collections.Generic.HashSet<int>(tris);
                    foreach (int i in seen)
                    {
                        Vector3 w = mf.transform.TransformPoint(verts[i]);
                        sb.Append($"{mf.name}|{m},{w.x:0.0000},{w.y:0.0000},{w.z:0.0000}\n");
                    }
                }
            }
            Object.DestroyImmediate(go);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/vacuum_mesh.csv", sb.ToString());
            Debug.Log("[SheetHappens] Vacuum mesh written to Temp/vacuum_mesh.csv");
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
        static string V(Vector3 v) => $"{v.x:0.00},{v.y:0.00},{v.z:0.00}";
    }
}
