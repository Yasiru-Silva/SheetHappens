using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>
    /// The Ghost Vacuum. Hold the left mouse button to suck: a swirling purple beam locks onto the ghost under the
    /// crosshair, the ghost struggles while the capture meter fills, then it is stretched and pulled into the nozzle and
    /// vanishes in a burst of sparks. 3 power levels from purple energy balls (faster capture, longer reach).
    /// Jams for 3 s when the player is hit (suction stops and the ghost starts to recover).
    /// </summary>
    public class Vacuum : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("The Ghost_Vacuum_V2 model held in front of the camera.")]
        public Transform model;
        public float[] range = { 12f, 16f, 20f };
        public float[] radius = { 0.35f, 0.45f, 0.65f };
        [Tooltip("How fast each power level fills the capture meter (1 = normal).")]
        public float[] power = { 1f, 1.35f, 1.8f };
        [Tooltip("Keeps the lock on a ghost while the crosshair stays within this angle of it.")]
        public float lockAngle = 14f;

        public int Energy { get; private set; }
        public int Level => Energy >= 10 ? 3 : Energy >= 5 ? 2 : 1;
        public bool Jammed => Time.time < jamUntil;
        public float JamRemaining => Mathf.Max(0f, jamUntil - Time.time);
        public Transform Nozzle => model ? model : (cam ? cam.transform : transform);
        /// <summary>Point where the beam leaves the vacuum.</summary>
        public Vector3 Muzzle => model ? model.position + model.forward * 0.3f : (cam ? cam.transform.position + cam.transform.forward * 0.4f : transform.position);
        /// <summary>The trigger is held and the vacuum is working.</summary>
        public bool Sucking { get; private set; }
        /// <summary>The ghost currently locked in the beam (null when sucking air).</summary>
        public GhostAgent Target { get; private set; }

        float jamUntil, holdUntil, spin, intensity;
        Vector3 modelBase, beamEnd, holdPoint;
        SuctionBeam beam;
        AudioSource hum;

        void Awake()
        {
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (model) { FitModel(); modelBase = model.localPosition; }
            beam = new GameObject("VacuumBeam").AddComponent<SuctionBeam>();
            beam.transform.SetParent(transform, false);
        }

        void FitModel()
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (size > 0.01f) model.localScale *= 0.45f / size; // fit the held vacuum to ~45 cm on screen
            foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Start()
        {
            // Gloved hands holding the vacuum (built after the game has set up its materials).
            if (model && cam)
            {
                var hands = GetComponent<FirstPersonHands>();
                if (hands == null) hands = gameObject.AddComponent<FirstPersonHands>();
                hands.Build(model, cam.transform);
            }
            var clip = Sfx.Clip("Vacuum_Loop");
            if (clip)
            {
                hum = gameObject.AddComponent<AudioSource>();
                hum.clip = clip; hum.loop = true; hum.playOnAwake = false; hum.spatialBlend = 0f; hum.volume = 0f;
            }
        }

        /// <summary>Keeps the beam on a point while a captured ghost is being pulled in.</summary>
        public void HoldBeam(Vector3 to)
        {
            holdPoint = to;
            holdUntil = Time.time + 0.05f;
        }

        /// <summary>Spark burst at the nozzle when a ghost disappears into the vacuum.</summary>
        public void CaptureBurst(Color tint)
        {
            if (beam) beam.Burst(Muzzle + (model ? model.forward : Vector3.forward) * 0.15f, tint);
            Sfx.Play("Capture_Pop", Muzzle, 0.9f, 0f, Random.Range(0.95f, 1.05f));
            recoil = 1f;
        }

        public void Jam(float seconds)
        {
            jamUntil = Mathf.Max(jamUntil, Time.time + seconds);
            if (Target) Target.ReleaseSuction();
            Target = null;
        }

        public void AddEnergy(int amount) => Energy += amount;

        float recoil;

        void Update()
        {
            var gm = GameManager.I;
            bool playing = gm == null || gm.State == GameState.Playing;
            var m = Mouse.current;
            bool held = playing && m != null && m.leftButton.isPressed && Cursor.lockState == CursorLockMode.Locked && !Jammed && cam != null;
            bool pulling = Time.time < holdUntil; // a captured ghost is flying into the nozzle

            if (held) Suck();
            else if (Target) { Target.ReleaseSuction(); Target = null; }
            Sucking = held;

            // Beam and vortex.
            float want = pulling ? 1f : held ? (Target ? 1f : 0.55f) : 0f;
            intensity = Mathf.MoveTowards(intensity, want, Time.deltaTime * (want > intensity ? 8f : 5f));
            Vector3 end = pulling ? holdPoint : beamEnd;
            float prog = Target ? Target.SuckProgress : pulling ? 1f : 0f;
            spin += Time.deltaTime * (8f + 10f * prog);
            if (beam) beam.Draw(Muzzle, end, intensity, spin, prog, Target != null || pulling, model ? model.forward : transform.forward);

            // Motor hum: louder and higher as the meter fills.
            if (hum)
            {
                float v = intensity * (0.35f + 0.25f * prog);
                hum.volume = Mathf.MoveTowards(hum.volume, v, Time.deltaTime * 3f);
                hum.pitch = 0.9f + 0.35f * prog + (Target ? 0.05f * Mathf.Sin(Time.time * 30f) : 0f);
                if (hum.volume > 0.01f && !hum.isPlaying) hum.Play();
                else if (hum.volume <= 0.01f && hum.isPlaying) hum.Stop();
            }

            // The vacuum kicks back while it works, harder when a ghost fights the beam.
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 5f);
            float shake = intensity * (Target ? 0.004f + 0.01f * prog : 0.0025f);
            if (model) model.localPosition = modelBase + Vector3.back * (recoil * 0.06f + intensity * 0.01f)
                                             + (Jammed ? Random.insideUnitSphere * 0.01f : Vector3.zero)
                                             + Random.insideUnitSphere * shake;
        }

        void Suck()
        {
            int L = Level - 1;
            var ray = new Ray(cam.transform.position, cam.transform.forward);

            // Keep the current ghost while it stays roughly under the crosshair and in view.
            if (Target && (Target.IsCaptured || !StillLocked(Target, ray, range[L]))) { Target.ReleaseSuction(); Target = null; }

            Vector3 end = ray.GetPoint(range[L]);
            // What solid thing the crosshair is on (thin ray: furniture next to the line of fire doesn't block it).
            float wall = range[L];
            foreach (var h in SortedRay(ray, range[L]))
            {
                if (h.collider.isTrigger || h.collider.GetComponentInParent<PlayerController>() || h.collider.GetComponentInParent<GhostAgent>()) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic)
                {
                    h.collider.attachedRigidbody.AddForce(-ray.direction * 6f * Time.deltaTime, ForceMode.Impulse); // suction tugs loose objects
                    continue;
                }
                wall = h.distance; end = h.point; break;
            }
            if (Target == null)
            {
                // A ghost near the crosshair (wide sphere) that is in front of the wall and in clear view.
                var hits = Physics.SphereCastAll(ray, radius[L], Mathf.Min(range[L], wall + 0.6f), ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    var ghost = h.collider.GetComponentInParent<GhostAgent>();
                    if (ghost == null || ghost.IsCaptured || !ghost.CanBeSucked) continue;
                    if (!StillLocked(ghost, ray, range[L])) continue;
                    Target = ghost;
                    break;
                }
            }

            if (Target)
            {
                end = Target.SuckPoint;
                Target.Suck(power[L] * Time.deltaTime, Muzzle);
            }
            beamEnd = end;
        }

        static RaycastHit[] SortedRay(Ray ray, float dist)
        {
            var hits = Physics.RaycastAll(ray, dist, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            return hits;
        }

        bool StillLocked(GhostAgent g, Ray ray, float maxRange)
        {
            if (!g.CanBeSucked) return false;
            Vector3 to = g.SuckPoint - ray.origin;
            if (to.magnitude > maxRange + 1.5f) return false;
            if (Vector3.Angle(ray.direction, to) > lockAngle) return false;
            // A wall in between breaks the beam (the middle or the head of the ghost must be in view).
            return Clear(ray.origin, g.SuckPoint, g) || Clear(ray.origin, g.Eye, g);
        }

        static bool Clear(Vector3 from, Vector3 to, GhostAgent g)
        {
            if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            if (hit.collider.GetComponentInParent<GhostAgent>() == g) return true;
            if (hit.collider.GetComponentInParent<PlayerController>()) return true;
            return hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic;
        }
    }

    /// <summary>
    /// The suction effect: three purple strands twisting round each other from the nozzle to the target, a bright core,
    /// a vortex of sparks spiralling into the nozzle, and a burst of sparks when a ghost is captured.
    /// </summary>
    public class SuctionBeam : MonoBehaviour
    {
        const int Points = 28;
        LineRenderer core;
        readonly LineRenderer[] strands = new LineRenderer[3];
        ParticleSystem vortex, sparks;
        Light glow;
        static Material additive;

        static readonly Color Purple = new Color(0.62f, 0.28f, 1f);
        static readonly Color Pale = new Color(0.86f, 0.74f, 1f);

        void Awake()
        {
            core = MakeLine("Core");
            for (int i = 0; i < strands.Length; i++) strands[i] = MakeLine("Strand" + i);
            vortex = MakeParticles("Vortex", 400);
            sparks = MakeParticles("CaptureSparks", 300);
            glow = new GameObject("BeamGlow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point; glow.color = Purple; glow.range = 3.5f; glow.intensity = 0f; glow.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------ materials

        static Material Additive()
        {
            if (additive) return additive;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            additive = new Material(sh) { name = "SuctionGlow" };
            additive.mainTexture = SoftDot();
            if (additive.HasProperty("_BaseMap")) additive.SetTexture("_BaseMap", SoftDot());
            if (additive.HasProperty("_Surface"))
            {
                // Transparent, additive blending.
                additive.SetFloat("_Surface", 1f);
                additive.SetFloat("_Blend", 2f);
                additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                additive.SetFloat("_ZWrite", 0f);
                additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                additive.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (additive.HasProperty("_BaseColor")) additive.SetColor("_BaseColor", Color.white);
            return additive;
        }

        static Texture2D dot;
        static Texture2D SoftDot()
        {
            if (dot) return dot;
            const int n = 32;
            dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    dot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            dot.Apply();
            return dot;
        }

        LineRenderer MakeLine(string n)
        {
            var lr = new GameObject(n).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.positionCount = Points;
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Additive();
            lr.enabled = false;
            return lr;
        }

        ParticleSystem MakeParticles(string n, int max)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.startLifetime = 0.4f; main.startSize = 0.05f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Additive();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------ drawing

        /// <summary>Draws the beam for this frame. 'k' fades it in and out; 'progress' is the capture meter (0..1).</summary>
        public void Draw(Vector3 from, Vector3 to, float k, float spin, float progress, bool locked, Vector3 nozzleForward)
        {
            bool on = k > 0.01f;
            core.enabled = on;
            foreach (var s in strands) s.enabled = on;
            glow.intensity = on ? k * (1.5f + 2.5f * progress) : 0f;
            if (!on) return;
            glow.transform.position = from + nozzleForward * 0.2f;

            Vector3 dir = to - from;
            float len = Mathf.Max(0.01f, dir.magnitude);
            Vector3 f = dir / len;
            Vector3 u = Vector3.Cross(f, Vector3.up); if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(f, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(f, u);

            // Core: thin and bright near the nozzle, widening towards the ghost.
            float w = locked ? 1f : 0.6f;
            for (int i = 0; i < Points; i++)
            {
                float t = i / (Points - 1f);
                Vector3 p = from + dir * t + (u * Mathf.Sin(t * 9f - spin * 1.3f) + v * Mathf.Cos(t * 7f - spin)) * 0.015f * t;
                core.SetPosition(i, p);
            }
            core.widthCurve = AnimationCurve.Linear(0f, 0.05f * w, 1f, (0.16f + 0.1f * progress) * w);
            Color cc = Color.Lerp(Pale, Color.white, progress * 0.6f); cc.a = 0.85f * k;
            core.startColor = cc; core.endColor = new Color(Purple.r, Purple.g, Purple.b, 0.6f * k);

            // Strands: twisting round the core, flaring out at the ghost end.
            for (int s = 0; s < strands.Length; s++)
            {
                float phase = s * Mathf.PI * 2f / strands.Length;
                float turns = Mathf.Max(2f, len * 1.1f);
                for (int i = 0; i < Points; i++)
                {
                    float t = i / (Points - 1f);
                    float rad = Mathf.Lerp(0.03f, 0.22f + 0.1f * progress, t * t) * w;
                    float a = phase + t * turns * Mathf.PI * 2f - spin * 2.2f;
                    strands[s].SetPosition(i, from + dir * t + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rad);
                }
                strands[s].widthCurve = AnimationCurve.Linear(0f, 0.018f, 1f, 0.045f + 0.03f * progress);
                Color sc = Color.Lerp(Purple, Pale, 0.3f + 0.4f * progress); sc.a = 0.95f * k;
                strands[s].startColor = sc;
                strands[s].endColor = new Color(Purple.r, Purple.g, Purple.b, 0.5f * k);
            }

            // Vortex: sparks swirl in from around the beam and get swallowed by the nozzle.
            int count = Mathf.RoundToInt((locked ? 2.5f + 3f * progress : 1.2f) * k * 60f * Time.deltaTime * 6f);
            for (int i = 0; i < count; i++)
            {
                float t = Random.Range(0.08f, Mathf.Min(1f, 1.6f / len + 0.25f));
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Random.Range(0.12f, 0.35f) * (0.6f + t);
                Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
                Vector3 p = from + dir * t + radial * rad;
                Vector3 tangent = Vector3.Cross(f, radial);
                float life = Random.Range(0.18f, 0.32f);
                Vector3 vel = (from - p) / life + tangent * Random.Range(1.5f, 3f);
                vortex.Emit(new ParticleSystem.EmitParams
                {
                    position = p, velocity = vel, startLifetime = life,
                    startSize = Random.Range(0.02f, 0.05f),
                    startColor = Color.Lerp(Purple, Pale, Random.value),
                }, 1);
            }
            // A few sparks streaming along the beam from the ghost towards the vacuum.
            if (locked)
            {
                int streams = Mathf.RoundToInt(k * (20f + 40f * progress) * Time.deltaTime * 6f);
                for (int i = 0; i < streams; i++)
                {
                    Vector3 p = to + Random.insideUnitSphere * 0.25f;
                    float life = Mathf.Clamp(len / 14f, 0.12f, 0.6f);
                    vortex.Emit(new ParticleSystem.EmitParams
                    {
                        position = p, velocity = (from - p) / life, startLifetime = life,
                        startSize = Random.Range(0.03f, 0.07f),
                        startColor = Color.Lerp(Purple, Color.white, Random.value * 0.5f),
                    }, 1);
                }
            }
        }

        /// <summary>An explosion of sparks (purple mixed with the ghost's own colour).</summary>
        public void Burst(Vector3 at, Color tint)
        {
            glow.transform.position = at;
            for (int i = 0; i < 90; i++)
            {
                Vector3 d = Random.onUnitSphere;
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = at + d * 0.05f,
                    velocity = d * Random.Range(1.5f, 5f),
                    startLifetime = Random.Range(0.35f, 0.8f),
                    startSize = Random.Range(0.03f, 0.09f),
                    startColor = Random.value < 0.6f ? Color.Lerp(Purple, Pale, Random.value) : Color.Lerp(tint, Color.white, 0.3f),
                }, 1);
            }
            StartCoroutine(Flash());
        }

        System.Collections.IEnumerator Flash()
        {
            for (float t = 0; t < 0.35f; t += Time.deltaTime)
            {
                glow.intensity = Mathf.Max(glow.intensity, 8f * (1f - t / 0.35f));
                yield return null;
            }
        }
    }
}
