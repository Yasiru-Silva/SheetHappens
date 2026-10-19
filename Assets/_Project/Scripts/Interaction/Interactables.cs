using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>Crouch inside this area (next to a table) to hide from Blue Boo and Grandpa Ghost.</summary>
    public class HideSpot : MonoBehaviour
    {
        [Tooltip("Crouch within this distance of the table's centre to hide. 0 = fit to the table's size.")]
        public float radius = 0f;
        public static readonly List<HideSpot> All = new List<HideSpot>();
        Vector3 centre;
        float r;

        void OnEnable() { All.Add(this); Fit(); }
        void OnDisable() => All.Remove(this);

        void Fit()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { centre = transform.position; r = radius > 0f ? radius : 1.5f; return; }
            Bounds b = rs[0].bounds;
            foreach (var x in rs) b.Encapsulate(x.bounds);
            centre = new Vector3(b.center.x, b.min.y, b.center.z);
            r = radius > 0f ? radius : Mathf.Max(b.extents.x, b.extents.z) + 0.45f;
        }

        public static bool Contains(Vector3 feet)
        {
            foreach (var s in All)
            {
                Vector3 d = feet - s.centre;
                if (Mathf.Abs(d.y) < 1.2f && new Vector2(d.x, d.z).magnitude < s.r) return true;
            }
            return false;
        }
    }

    /// <summary>A pushable object that blocks ghost paths; ghosts re-path around it.</summary>
    public class Barricade : MonoBehaviour
    {
        Bounds last;
        bool registered;
        float nextCheck;

        void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.4f;
            var grid = GameManager.I ? GameManager.I.Grid : null;
            if (grid == null || !grid.Built) return;
            Bounds b = CurrentBounds();
            if (registered && (b.center - last.center).sqrMagnitude < 0.04f) return;
            if (registered) grid.AddBlock(last, -1);
            grid.AddBlock(b, +1);
            last = b; registered = true;
        }

        Bounds CurrentBounds()
        {
            var cols = GetComponentsInChildren<Collider>();
            Bounds b = cols.Length > 0 ? cols[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var c in cols) b.Encapsulate(c.bounds);
            return b;
        }

        void OnDestroy()
        {
            var grid = GameManager.I ? GameManager.I.Grid : null;
            if (registered && grid != null) grid.AddBlock(last, -1);
        }
    }

    /// <summary>Pillows, books, cups: pick up with E, throw with right-click. Ghosts near the impact are distracted.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Throwable : MonoBehaviour
    {
        public Rigidbody Rb { get; private set; }
        float thrownAt = -99f;

        void Awake() => Rb = GetComponent<Rigidbody>();
        public void MarkThrown() => thrownAt = Time.time;

        void OnCollisionEnter(Collision c)
        {
            if (Time.time - thrownAt > 3f) return;
            thrownAt = -99f;
            var gm = GameManager.I;
            if (gm == null) return;
            foreach (var g in gm.Ghosts)
                if (g && Vector3.Distance(g.Logical, transform.position) < 6f) g.Distract(transform.position, 3f);
        }
    }

    /// <summary>Glowing purple pick-up. Walk into it to power up the vacuum.</summary>
    public class EnergyBall : MonoBehaviour
    {
        Vector3 basePos;

        public static EnergyBall Create(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PurpleEnergyBall";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * 0.25f;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.55f, 0.2f, 0.9f), new Color(1.6f, 0.5f, 3f));
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(0.7f, 0.3f, 1f); light.range = 2.5f; light.intensity = 1.5f;
            return go.AddComponent<EnergyBall>();
        }

        void Start() => basePos = transform.position;

        void Update()
        {
            transform.position = basePos + Vector3.up * Mathf.Sin(Time.time * 2f + basePos.x) * 0.1f;
            var gm = GameManager.I;
            if (gm == null || gm.Player == null || gm.State != GameState.Playing) return;
            Vector3 body = gm.Player.transform.position + Vector3.up * 0.8f;
            if ((body - transform.position).sqrMagnitude < 1.2f)
            {
                if (gm.Vacuum) gm.Vacuum.AddEnergy(1);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Runtime material helper. Uses a URP Lit template material so new materials render in URP,
    /// and converts props whose materials still use the built-in shader (they would render pink).</summary>
    public static class Mats
    {
        public static Material Template;
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

        static Material NewLit()
        {
            if (Template != null) return new Material(Template);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            return new Material(sh);
        }

        public static Material Lit(Color c, Color emission = default)
        {
            var m = NewLit();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (emission != default)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission);
            }
            return m;
        }

        public static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return Lit(c, c);
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        static bool IsUrp(Material m) => m != null && m.shader != null && (m.shader.name.StartsWith("Universal Render Pipeline") || m.shader.name.StartsWith("Shader Graphs"));

        /// <summary>Gives every non-URP material on this object a URP copy with the same texture and colour.</summary>
        public static void FixForUrp(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || IsUrp(src)) continue;
                    if (!converted.TryGetValue(src, out var dst))
                    {
                        dst = NewLit();
                        dst.name = src.name + " (URP)";
                        Texture tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                        Color col = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                        if (dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", tex);
                        if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", col);
                        Texture bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
                        if (dst.HasProperty("_BumpMap")) dst.SetTexture("_BumpMap", bump);
                        if (bump != null) dst.EnableKeyword("_NORMALMAP"); else dst.DisableKeyword("_NORMALMAP");
                        if (dst.HasProperty("_Smoothness")) dst.SetFloat("_Smoothness", 0.25f);
                        converted[src] = dst;
                    }
                    mats[i] = dst;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
