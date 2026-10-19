using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Gives a ghost the "SheetHappens/GhostWisp" look: same texture and colours, a glowing rim, and the bottom of the
    /// body dissolving into drifting smoke. Each frame it moves the fade with the ghost (it bobs and stretches) and
    /// passes the animation glow (eyes blaze during a scare). If the shader is missing the original materials stay.
    /// </summary>
    public class GhostDissolve : MonoBehaviour
    {
        [Tooltip("How much of the body (from the bottom) turns into smoke.")]
        [Range(0.1f, 0.9f)] public float smokyPart = 0.5f;

        readonly List<Renderer> bodies = new List<Renderer>();
        MaterialPropertyBlock block;
        float glow = 1f, opacity = 0.95f;
        static Shader shader;
        static bool warned;
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        static readonly int FadeY0 = Shader.PropertyToID("_FadeY0"), FadeY1 = Shader.PropertyToID("_FadeY1"), GlowId = Shader.PropertyToID("_Glow"), OpacityId = Shader.PropertyToID("_Opacity");

        public bool Active => bodies.Count > 0;

        /// <summary>Swaps the ghost's materials. 'rim' is the ghost's glow colour.</summary>
        public static GhostDissolve Apply(GameObject ghost, Color rim)
        {
            if (shader == null) shader = Shader.Find("SheetHappens/GhostWisp");
            if (shader == null || !shader.isSupported)
            {
                if (!warned) { warned = true; Debug.LogWarning("[SheetHappens] GhostWisp shader not found or not supported: ghosts keep their normal materials."); }
                return null;
            }
            var d = ghost.AddComponent<GhostDissolve>();
            d.block = new MaterialPropertyBlock();
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue; // not the smoke particles
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Convert(mats[i], rim);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                d.bodies.Add(r);
            }
            return d;
        }

        static Material Convert(Material src, Color rim)
        {
            if (src == null) return null;
            if (converted.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(shader) { name = src.name + " (Ghost)" };
            Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex) m.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            c.a = 1f;
            m.SetColor("_BaseColor", c);
            Color e = Color.black;
            if (src.HasProperty("_EmissionColor") && (src.IsKeywordEnabled("_EMISSION") || src.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack))
                e = src.GetColor("_EmissionColor");
            m.SetColor("_EmissionColor", e);
            m.SetColor("_RimColor", rim);
            m.renderQueue = 3000;
            converted[src] = m;
            return m;
        }

        /// <summary>Eye/emission brightness for this frame (1 = normal).</summary>
        public void SetGlow(float g) => glow = g;
        /// <summary>How solid the ghost is (0.95 = normal; lower while it flickers).</summary>
        public void SetOpacity(float a) => opacity = a;

        void LateUpdate()
        {
            if (bodies.Count == 0) return;
            bool any = false;
            Bounds b = default;
            foreach (var r in bodies)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            float y0 = b.min.y, y1 = b.min.y + b.size.y * smokyPart;
            foreach (var r in bodies)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(FadeY0, y0);
                block.SetFloat(FadeY1, y1);
                block.SetFloat(GlowId, glow);
                block.SetFloat(OpacityId, opacity);
                r.SetPropertyBlock(block);
            }
        }
    }
}
