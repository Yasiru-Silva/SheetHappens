using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// A cupboard whose doors open and close (E). Blue Boos hide inside; an occupied cupboard rattles now and then.
    /// Works with any model that has child parts named "...door..." pivoting at their hinges (e.g. WoodCupboard03).
    /// </summary>
    public class Cupboard : MonoBehaviour
    {
        public static readonly List<Cupboard> All = new List<Cupboard>();

        public float openAngle = 105f;
        public float speed = 260f;

        public bool IsOpen { get; private set; }
        public GhostAgent Occupant { get; private set; }
        public bool Free => Occupant == null && reservedBy == null;
        /// <summary>Where a hiding ghost's base sits.</summary>
        public Vector3 Inside { get; private set; }
        /// <summary>A floor point just in front of the doors.</summary>
        public Vector3 FrontPoint { get; private set; }
        public Vector3 Forward { get; private set; }

        class Leaf { public Transform t; public Quaternion closed; public float sign; public float angle; }
        readonly List<Leaf> leaves = new List<Leaf>();
        GhostAgent reservedBy;
        float target, nextRattle;
        AudioClip creak;
        AudioSource audioSource;
        bool ready;
        static Material glassMat;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Start() => Setup();

        public void Setup()
        {
            if (ready) return;
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds all = rs[0].bounds;
            foreach (var r in rs) all.Encapsulate(r.bounds);

            var doors = new List<Transform>();
            Bounds body = default; bool hasBody = false;
            foreach (var r in rs)
            {
                string n = r.name.ToLower();
                if (n.Contains("door") && !n.Contains("glass")) doors.Add(r.transform);
                else if (!n.Contains("door") && !n.Contains("glass")) { if (!hasBody) { body = r.bounds; hasBody = true; } else body.Encapsulate(r.bounds); }
                // Inner glass shelves would hide a ghost even with the doors open.
                if (n.Contains("glass") && !n.Contains("door")) r.enabled = false;
                // Door panes: dark, glossy old glass (you can't see what is inside).
                if (n.Contains("glass") && n.Contains("door"))
                {
                    if (glassMat == null) { glassMat = Mats.Lit(new Color(0.06f, 0.08f, 0.09f)); glassMat.SetFloat("_Smoothness", 0.92f); glassMat.SetFloat("_Metallic", 0.2f); }
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = glassMat;
                    r.sharedMaterials = ms;
                }
            }
            if (!hasBody) body = all;
            if (doors.Count == 0) { enabled = false; return; }

            // Front = from the body's centre towards the doors.
            Vector3 dc = Vector3.zero;
            foreach (var d in doors) dc += PartBounds(d).center;
            dc /= doors.Count;
            Vector3 f = dc - body.center; f.y = 0f;
            Forward = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(f.z));
            if (f.sqrMagnitude < 1e-6f) Forward = transform.forward;

            foreach (var d in doors)
            {
                // Pick the swing direction that moves the door outwards (towards the front).
                Vector3 c = PartBounds(d).center;
                Quaternion q0 = d.rotation;
                Vector3 testPlus = RotateAbout(c, d.position, 60f), testMinus = RotateAbout(c, d.position, -60f);
                float sign = Vector3.Dot(testPlus - c, Forward) > Vector3.Dot(testMinus - c, Forward) ? 1f : -1f;
                leaves.Add(new Leaf { t = d, closed = q0, sign = sign });
                // A collider on each door so closed doors block shots and the player.
                var col = d.gameObject.GetComponent<BoxCollider>();
                if (col == null)
                {
                    Bounds b = PartBounds(d);
                    col = d.gameObject.AddComponent<BoxCollider>();
                    col.center = d.InverseTransformPoint(b.center);
                    Vector3 s = d.InverseTransformVector(b.size);
                    col.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Max(Mathf.Abs(s.z), 0.01f));
                }
            }
            float depth = Mathf.Abs(Vector3.Dot(body.size, Forward));
            Inside = new Vector3(body.center.x, body.min.y + body.size.y * 0.12f, body.center.z) - Forward * depth * 0.05f;
            FrontPoint = new Vector3(body.center.x, body.min.y, body.center.z) + Forward * (depth * 0.5f + 1.1f);

            creak = Resources.Load<AudioClip>("Audio/Night/Creak_Door");
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; audioSource.minDistance = 2f; audioSource.maxDistance = 25f; audioSource.playOnAwake = false;
            nextRattle = Time.time + Random.Range(3f, 8f);
            ready = true;
        }

        static Vector3 RotateAbout(Vector3 p, Vector3 pivot, float deg) => pivot + Quaternion.AngleAxis(deg, Vector3.up) * (p - pivot);

        static Bounds PartBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ doors

        public void Open(bool fast = false) { Setup(); if (!IsOpen) PlaySound(fast ? 1.3f : 1f); IsOpen = true; target = openAngle; if (fast) SnapTowards(0.6f); }
        public void Close() { Setup(); if (IsOpen) PlaySound(1.1f); IsOpen = false; target = 0f; }
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        void SnapTowards(float k) { foreach (var l in leaves) l.angle = Mathf.Lerp(l.angle, target, k); }

        void PlaySound(float pitch)
        {
            if (audioSource && creak) { audioSource.pitch = pitch * Random.Range(0.9f, 1.1f); audioSource.PlayOneShot(creak, 0.5f); }
        }

        void Update()
        {
            if (!ready) return;
            foreach (var l in leaves)
            {
                l.angle = Mathf.MoveTowards(l.angle, target, speed * Time.deltaTime);
                l.t.rotation = Quaternion.AngleAxis(l.angle * l.sign, Vector3.up) * l.closed;
            }
            // A ghost inside makes the closed doors rattle from time to time: a clue for the player.
            if (Occupant && !IsOpen && Time.time > nextRattle)
            {
                nextRattle = Time.time + Random.Range(4f, 9f);
                StartCoroutine(Rattle());
            }
        }

        IEnumerator Rattle()
        {
            if (audioSource && creak) { audioSource.pitch = Random.Range(1.6f, 2.0f); audioSource.PlayOneShot(creak, 0.25f); }
            for (float t = 0; t < 0.45f && !IsOpen; t += Time.deltaTime)
            {
                foreach (var l in leaves) l.angle = Mathf.Abs(Mathf.Sin(t * 45f)) * 4f;
                yield return null;
            }
            if (!IsOpen) foreach (var l in leaves) l.angle = 0f;
        }

        // ------------------------------------------------------------ ghosts

        public bool Reserve(GhostAgent g) { if (!Free && reservedBy != g) return false; reservedBy = g; return true; }
        public void Enter(GhostAgent g) { Occupant = g; reservedBy = null; }
        public void Leave(GhostAgent g) { if (Occupant == g) Occupant = null; if (reservedBy == g) reservedBy = null; }

        /// <summary>A free cupboard near 'from' that the player is not standing next to.</summary>
        public static Cupboard FindFree(Vector3 from, Vector3 player, float maxDist, float minFromPlayer)
        {
            Cupboard best = null; float bestD = float.MaxValue;
            foreach (var c in All)
            {
                if (!c.ready || !c.Free) continue;
                float d = (c.FrontPoint - from).sqrMagnitude;
                if (d > maxDist * maxDist || (c.FrontPoint - player).sqrMagnitude < minFromPlayer * minFromPlayer) continue;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }
    }
}
