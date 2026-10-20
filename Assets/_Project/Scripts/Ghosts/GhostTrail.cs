using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Ghostly smoke, two layers:
    /// - the tail: thick smoke pouring off the lower half (where the body dissolves), drifting and trailing behind
    ///   as the ghost moves (more the faster it goes);
    /// - the aura: a thin haze rising off the whole body all the time.
    /// Tinted with the ghost's colour; none while it hides in a cupboard; extra while it thrashes in the vacuum beam.
    /// Added by GhostAgent at spawn.
    /// </summary>
    public class GhostTrail : MonoBehaviour
    {
        ParticleSystem tail, aura;
        ParticleSystem.EmissionModule tailEmission, auraEmission;
        GhostAgent ghost;
        float size, rate, auraRate;
        static Material smokeMat;

        /// <summary>Builds the smoke around the ghost's body (world-space bounds of its meshes).</summary>
        public static GhostTrail Attach(GhostAgent g, Bounds body, Color tint, float density)
        {
            var go = new GameObject("GhostSmoke");
            go.transform.SetParent(g.transform, false);
            go.transform.position = body.center - Vector3.up * body.size.y * 0.25f; // the lower half, where the body turns to smoke
            var t = go.AddComponent<GhostTrail>();
            t.ghost = g;
            float radius = Mathf.Max(body.extents.x, body.extents.z);
            t.size = Mathf.Clamp(radius * 1.3f, 0.32f, 0.9f);
            t.rate = 40f * density;
            t.auraRate = 14f * density;
            Color a = Color.Lerp(tint, Color.white, 0.35f), b = Color.Lerp(tint, new Color(0.55f, 0.55f, 0.6f), 0.45f);

            // Tail: the bottom half pouring smoke.
            t.tail = MakeSystem(go, new Vector3(Mathf.Max(0.2f, body.size.x * 0.95f), Mathf.Max(0.2f, body.size.y * 0.55f), Mathf.Max(0.2f, body.size.z * 0.95f)),
                                t.size, new Vector2(1.2f, 2.0f), a, b, 0.32f, new Vector2(-0.15f, 0.22f), 520);
            t.tailEmission = t.tail.emission;
            t.tailEmission.rateOverTime = t.rate * 0.8f; // always streaming, more when it moves
            t.tailEmission.rateOverDistance = t.rate;

            // Aura: thin haze off the whole body.
            var auraGo = new GameObject("GhostAura");
            auraGo.transform.SetParent(go.transform, false);
            auraGo.transform.position = body.center;
            t.aura = MakeSystem(auraGo, new Vector3(Mathf.Max(0.2f, body.size.x * 1.05f), Mathf.Max(0.3f, body.size.y * 1.0f), Mathf.Max(0.2f, body.size.z * 1.05f)),
                                t.size * 0.8f, new Vector2(1.4f, 2.4f), a, b, 0.15f, new Vector2(0.15f, 0.4f), 200);
            t.auraEmission = t.aura.emission;
            t.auraEmission.rateOverTime = t.auraRate;
            t.auraEmission.rateOverDistance = t.auraRate * 0.5f;
            return t;
        }

        /// <summary>One smoke particle system: puffs start on the surface of an egg shape ('shapeSize', metres).</summary>
        static ParticleSystem MakeSystem(GameObject go, Vector3 shapeSize, float puff, Vector2 life, Color a, Color b, float alpha, Vector2 rise, int max)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // puffs stay behind as the ghost moves on
            main.scalingMode = ParticleSystemScalingMode.Local;          // sizes in metres, whatever the ghost model's scale
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.3f); // wafts outwards from the body
            main.startSize = new ParticleSystem.MinMaxCurve(puff * 0.75f, puff * 1.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            shape.radiusThickness = 0.25f;
            shape.scale = shapeSize;

            // Drift and curl like smoke.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(rise.x, rise.y);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            // Swell as it fades.
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1.15f), new Keyframe(1f, 2.8f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.85f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.15f), new GradientAlphaKey(alpha * 0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SmokeMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 2f;

            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (!tail || !ghost) return;
            // No smoke while hidden in a cupboard; extra smoke while thrashing in the vacuum beam.
            bool hidden = ghost.IsHiddenInside, held = ghost.BeingSucked;
            tailEmission.enabled = !hidden;
            auraEmission.enabled = !hidden;
            tailEmission.rateOverDistance = held ? rate * 2.5f : rate;
            auraEmission.rateOverTime = held ? auraRate * 3f : auraRate;
        }

        /// <summary>Called when the ghost is gone: the smoke stays where it is and fades out instead of vanishing.</summary>
        public void Release()
        {
            if (!tail) return;
            transform.SetParent(null, true);
            foreach (var r in GetComponentsInChildren<ParticleSystemRenderer>(true)) r.enabled = true;
            tail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ghost = null;
            Destroy(gameObject, 2.6f);
        }

        // ------------------------------------------------------------ material

        static Material SmokeMaterial()
        {
            if (smokeMat) return smokeMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            smokeMat = new Material(sh) { name = "GhostSmoke" };
            var tex = SmokeTexture();
            smokeMat.mainTexture = tex;
            if (smokeMat.HasProperty("_BaseMap")) smokeMat.SetTexture("_BaseMap", tex);
            if (smokeMat.HasProperty("_BaseColor")) smokeMat.SetColor("_BaseColor", Color.white);
            if (smokeMat.HasProperty("_Surface"))
            {
                // Transparent, normal alpha blending (soft, not glowing).
                smokeMat.SetFloat("_Surface", 1f);
                smokeMat.SetFloat("_Blend", 0f);
                smokeMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                smokeMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                smokeMat.SetFloat("_ZWrite", 0f);
                smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                smokeMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            // Smoke right in front of the camera fades away, so a ghost in your face doesn't fog the whole screen.
            if (smokeMat.HasProperty("_CameraFadingEnabled"))
            {
                const float near = 0.5f, far = 2.0f;
                smokeMat.SetFloat("_CameraFadingEnabled", 1f);
                smokeMat.SetFloat("_CameraNearFadeDistance", near);
                smokeMat.SetFloat("_CameraFarFadeDistance", far);
                smokeMat.SetVector("_CameraFadeParams", new Vector4(near, 1f / (far - near), 0f, 0f));
                smokeMat.EnableKeyword("_FADING_ON");
            }
            return smokeMat;
        }

        /// <summary>A soft, cloudy puff (radial falloff broken up with noise).</summary>
        static Texture2D SmokeTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SmokePuff" };
            float ox = Random.value * 100f, oy = Random.value * 100f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float cloud = 0.55f * Mathf.PerlinNoise(ox + u * 2.2f, oy + v * 2.2f) + 0.45f * Mathf.PerlinNoise(ox + 40f + u * 5f, oy + v * 5f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f); // very soft edge: no visible "bubble" rim
                    a *= Mathf.Lerp(0.55f, 1.1f, cloud);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            tex.Apply();
            return tex;
        }
    }
}
