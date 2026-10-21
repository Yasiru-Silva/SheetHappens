using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SheetHappens
{
    /// <summary>
    /// Turns the scene into night when the game starts: dim blue moonlight, a dark sky, dark ambient light and light fog,
    /// and warm flickering oil lamps hanging in every room of the house.
    /// The player can change the brightness: L cycles through levels, or the slider in Settings (saved between games).
    /// </summary>
    public class NightLighting : MonoBehaviour
    {
        // ------------------------------------------------------------ brightness

        public static readonly float[] Levels = { 0.6f, 0.8f, 1f, 1.25f, 1.5f, 1.8f, 2.2f };
        /// <summary>Brightness multiplier (1 = normal). Lamps, moonlight and ambient light all follow it.</summary>
        public static float Brightness { get; private set; } = 1f;
        static NightLighting current;

        public static void SetBrightness(float b)
        {
            Brightness = Mathf.Clamp(b, 0.4f, 2.5f);
            try { PlayerPrefs.SetFloat("SH_Brightness", Brightness); } catch { }
            if (current) current.ApplyBrightness();
        }

        /// <summary>Next brightness level (wraps round from brightest to darkest).</summary>
        public static void CycleBrightness()
        {
            int next = 0;
            for (int i = 0; i < Levels.Length; i++) if (Levels[i] > Brightness + 0.01f) { next = i; break; }
            SetBrightness(Levels[next]);
        }

        readonly List<Light> moons = new List<Light>();

        void ApplyBrightness()
        {
            float b = Brightness;
            RenderSettings.ambientLight = houseAmbient * b;
            foreach (var l in moons) if (l) l.intensity = moonBrightness * b;
            RenderSettings.fogDensity = fogThickness / Mathf.Max(0.5f, b); // brighter also means a little less fog
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || GameMenu.IsOpen || !kb.lKey.wasPressedThisFrame) return;
            CycleBrightness();
            var gm = GameManager.I;
            if (gm) gm.ShowBanner($"Brightness {Brightness * 100f:0}%   (L to change)", 1.5f);
        }

        [Header("Night sky")]
        public Color skyColor = new Color(0.015f, 0.02f, 0.045f);
        [Tooltip("General light everywhere in the house (raise to make the night brighter).")]
        public Color houseAmbient = new Color(0.17f, 0.17f, 0.22f);
        public Color moonColor = new Color(0.55f, 0.65f, 0.95f);
        public float moonBrightness = 0.35f;
        public bool fog = true;
        public float fogThickness = 0.012f;

        [Header("House lamps")]
        [Tooltip("Hanging lamp model (optional). Without one a small glowing bulb is used.")]
        public GameObject lampPrefab;
        public Color lampColor = new Color(1f, 0.68f, 0.38f);
        public float lampBrightness = 6f;
        public float lampReach = 12f;
        [Tooltip("Lamps are spread out so no two are closer than this.")]
        public float lampGap = 5f;
        public int lampCount = 20;
        [Tooltip("How many lamps cast shadows. Each point-light shadow uses 6 shadow maps; more than 2 overflows URP's shadow atlas.")]
        public int shadowCastingLamps = 2;

        /// <summary>Night sky, moon, ambient and fog. Called before the level is set up.</summary>
        public void ApplySky()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = moonColor;
                l.intensity = moonBrightness;
                l.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
                l.shadows = LightShadows.Soft;
                moons.Add(l);
            }
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = houseAmbient;
            RenderSettings.reflectionIntensity = 0.15f;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = skyColor;
            RenderSettings.fogDensity = fogThickness;
            current = this;
            try { Brightness = PlayerPrefs.GetFloat("SH_Brightness", 1f); } catch { Brightness = 1f; }
            ApplyBrightness();
            // Window panes glow like daylight (an emissive "light" material): make them dark night glass.
            Material night = null;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name.ToLower();
                    if (!n.Contains("windowslight") && !n.Contains("windowglass")) continue;
                    if (night == null) night = Mats.Lit(new Color(0.03f, 0.05f, 0.1f), new Color(0.02f, 0.03f, 0.08f));
                    mats[i] = night;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }

            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = skyColor;
            }
        }

        /// <summary>Hangs lamps under the ceilings, spread across every floor the ghosts and player can reach.</summary>
        public void PlaceLamps(GhostGrid grid, IList<int> floorCells)
        {
            var placed = new List<Vector3>();
            var order = new List<int>(floorCells);
            // Visit cells in a fixed, spread-out order so lamps land in every room.
            order.Sort((a, b) => Hash(a).CompareTo(Hash(b)));
            foreach (int c in order)
            {
                if (placed.Count >= lampCount) break;
                Vector3 floor = grid.PathPoint(c);
                if (!Physics.Raycast(floor + Vector3.up * 0.3f, Vector3.down, out var down, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(down.point + Vector3.up * 0.5f, Vector3.up, out var up, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float height = up.point.y - down.point.y;
                if (height < 2.4f) continue; // too low (under stairs, attic eaves)
                Vector3 at = new Vector3(down.point.x, up.point.y - Mathf.Min(0.9f, height * 0.25f), down.point.z);
                bool tooClose = false;
                foreach (var p in placed)
                    if (Mathf.Abs(p.y - at.y) < 2f && new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < lampGap * lampGap) { tooClose = true; break; }
                if (tooClose) continue;
                CreateLamp(at, up.point.y, placed.Count < shadowCastingLamps);
                placed.Add(at);
            }
            Debug.Log($"[SheetHappens] Night lighting: {placed.Count} lamps in the house.");
        }

        static int Hash(int c) { unchecked { return (c * 73856093) ^ (c >> 3) * 19349663; } }

        void CreateLamp(Vector3 at, float ceilingY, bool shadows)
        {
            var root = new GameObject("HouseLamp");
            root.transform.position = at;
            // a short chain up to the ceiling
            var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(chain.GetComponent<Collider>());
            chain.transform.SetParent(root.transform, false);
            float len = Mathf.Max(0.05f, ceilingY - at.y);
            chain.transform.localPosition = Vector3.up * (len * 0.5f);
            chain.transform.localScale = new Vector3(0.02f, len * 0.5f, 0.02f);
            chain.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.15f, 0.12f, 0.1f));

            if (lampPrefab)
            {
                var lamp = Instantiate(lampPrefab, root.transform);
                lamp.transform.localPosition = Vector3.zero;
                lamp.transform.localRotation = lampPrefab.transform.rotation;
                Mats.FixForUrp(lamp);
                foreach (var col in lamp.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in lamp.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                // hang it from its top
                var rs = lamp.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    lamp.transform.position += Vector3.up * (at.y - b.max.y);
                }
            }
            else
            {
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(bulb.GetComponent<Collider>());
                bulb.transform.SetParent(root.transform, false);
                bulb.transform.localPosition = Vector3.down * 0.1f;
                bulb.transform.localScale = Vector3.one * 0.18f;
                bulb.GetComponent<Renderer>().sharedMaterial = Mats.Lit(lampColor, lampColor * 3f);
            }

            // Use the lamp model's own light if it has one (some carry extra effects that depend on it), else add one.
            var light = root.GetComponentInChildren<Light>();
            if (light == null)
            {
                light = new GameObject("LampLight").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.localPosition = Vector3.down * 0.25f;
            }
            light.type = LightType.Point;
            light.color = lampColor;
            light.intensity = lampBrightness;
            light.range = lampReach;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            root.AddComponent<LampFlicker>().target = light;
            root.AddComponent<GhostPassable>();
        }
    }

    /// <summary>Gentle flicker for oil lamps.</summary>
    public class LampFlicker : MonoBehaviour
    {
        public Light target;
        float baseIntensity, seed;

        void Start()
        {
            if (target) baseIntensity = target.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            if (!target) return;
            float n = Mathf.PerlinNoise(seed, Time.time * 3f);
            target.intensity = baseIntensity * NightLighting.Brightness * (0.85f + 0.3f * n);
        }
    }
}
