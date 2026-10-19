using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Shared ghost body: A* path following, floating, vision, attacks, hits and capture.
    /// Added at runtime to the existing ghost prefabs; their meshes and materials are not changed.
    /// </summary>
    public abstract class GhostAgent : MonoBehaviour
    {
        public GhostType Type { get; protected set; }
        public int MaxHits { get; protected set; } = 1;
        public float Speed { get; protected set; } = 2f;
        public string StateName { get; protected set; } = "";
        public int HitsTaken { get; private set; }
        public bool IsCaptured { get; private set; }
        public Vector3 Logical => logical;
        public Vector3 Eye => logical + Vector3.up * (height * 0.6f);
        public virtual string DisplayName => Type.ToString();

        protected GameManager gm;
        protected GhostGrid grid;
        protected readonly List<int> path = new List<int>();
        protected int pathIndex;
        protected Vector3 logical;
        protected Vector3 visualOffset;
        protected float speedMul = 1f;
        protected float healAfter;
        protected float radius = 0.4f, height = 1f;

        float bottomOffset, bob, lastHitTime = -99f, distractedUntil, attackReadyAt, speedBoostUntil;
        Vector3 distractPoint;
        int pathFailures;
        CapsuleCollider body;
        Vector3 correction;
        static readonly Collider[] Nearby = new Collider[32];
        static bool loggedPush;

        public void Init(GameManager manager, Vector3 at)
        {
            gm = manager;
            grid = manager.Grid;
            transform.position = at;
            MeasureBody();
            logical = at;
            bob = Random.value * 6f;
            OnSpawn();
            Place();
            gm.Register(this);
        }

        void MeasureBody()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            height = b.size.y;
            radius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            bottomOffset = transform.position.y - b.min.y;
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            Vector3 s = transform.lossyScale;
            col.center = transform.InverseTransformPoint(b.center);
            col.height = b.size.y / Mathf.Max(0.001f, s.y);
            col.radius = radius / Mathf.Max(0.001f, Mathf.Max(s.x, s.z));
            body = col;
        }

        protected abstract void OnSpawn();
        protected abstract void Think();
        protected virtual void OnHurt() { }

        void Update()
        {
            if (IsCaptured) return;
            if (gm == null || gm.State != GameState.Playing) { Place(); return; }
            if (healAfter > 0f && HitsTaken > 0 && Time.time - lastHitTime > healAfter) HitsTaken = 0;
            if (Time.time > speedBoostUntil) speedMul = 1f;

            if (Time.time < distractedUntil)
            {
                StateName = "Distracted";
                Face(distractPoint - logical);
            }
            else Think();
            Place();
        }

        void Place()
        {
            bob += Time.deltaTime * 2.2f;
            Vector3 want = logical + Vector3.up * (bottomOffset + Mathf.Sin(bob) * 0.08f) + visualOffset;
            // Keep the whole body inside the room: push it out of any wall, floor or ceiling it overlaps.
            Vector3 push = body ? PushOutOfWalls(want) : Vector3.zero;
            if (!loggedPush && push.sqrMagnitude > 0.0025f) { loggedPush = true; Debug.Log($"[SheetHappens] {DisplayName} body kept out of a wall/floor (pushed {push.magnitude:0.00} m)."); }
            correction = Vector3.Lerp(correction, push, 1f - Mathf.Exp(-15f * Time.deltaTime));
            if ((push - correction).sqrMagnitude > 0.04f) correction = push; // never lag far behind into a wall
            transform.position = want + correction;
        }

        /// <summary>How far the ghost's body must move so it no longer overlaps solid level geometry (doors excepted).</summary>
        Vector3 PushOutOfWalls(Vector3 pos)
        {
            Vector3 total = Vector3.zero;
            Quaternion rot = transform.rotation;
            Vector3 ls = transform.lossyScale;
            float r = body.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z)) * 0.9f;
            float h = Mathf.Max(body.height * Mathf.Abs(ls.y), 2f * r);
            for (int iter = 0; iter < 3; iter++)
            {
                Vector3 c = pos + total + rot * Vector3.Scale(body.center, ls);
                Vector3 a = c + Vector3.up * (h * 0.5f - r), b = c - Vector3.up * (h * 0.5f - r);
                int n = Physics.OverlapCapsuleNonAlloc(a, b, r, Nearby, ~0, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int k = 0; k < n; k++)
                {
                    var other = Nearby[k];
                    if (other == body || other is CharacterController || GhostGrid.IsPassable(other)) continue;
                    if (Physics.ComputePenetration(body, pos + total, rot, other, other.transform.position, other.transform.rotation, out Vector3 dir, out float dist))
                    {
                        total += dir * dist;
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            return Vector3.ClampMagnitude(total, 1.2f);
        }

        // ------------------------------------------------------------ movement

        protected bool PathTo(Vector3 target, System.Func<int, float> extraCost = null)
        {
            bool allowBlocked = pathFailures >= 4; // after ~2 s stuck, push through barricades
            bool ok = grid.FindPath(logical, target, extraCost, path, allowBlocked);
            pathIndex = path.Count > 1 ? 1 : 0;
            pathFailures = ok ? 0 : pathFailures + 1;
            return ok;
        }

        /// <summary>Moves along the current path. Returns true when the end is reached.</summary>
        protected bool MoveAlong(float spd)
        {
            if (pathIndex >= path.Count) return true;
            Vector3 target = grid.PathPoint(path[pathIndex]);
            Vector3 to = target - logical;
            float step = spd * speedMul * Time.deltaTime;
            Face(to);
            if (to.magnitude <= step + 0.02f)
            {
                logical = target;
                pathIndex++;
                return pathIndex >= path.Count;
            }
            logical += to.normalized * step;
            return false;
        }

        protected bool PathBlockedAhead() => grid.AnyBlocked(path, pathIndex, 8);
        protected bool HasPath => pathIndex < path.Count;

        protected void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 6f);
        }

        protected bool Near(Vector3 p, float r) => (logical - p).sqrMagnitude < r * r;

        /// <summary>Close to the bell AND in the same room as it (no floor, ceiling or wall in between).</summary>
        protected bool AtBell(Vector3 ringPoint) =>
            Near(ringPoint, 1.8f) && !GhostGrid.SolidBetween(logical + Vector3.up * 0.3f, ringPoint + Vector3.up * 0.3f);

        protected Vector3 PlayerPos => gm.Player ? gm.Player.transform.position + Vector3.up * 0.4f : logical;

        // ------------------------------------------------------------ senses / combat

        protected bool CanSeePlayer(float range, float fov)
        {
            var p = gm.Player;
            if (p == null || p.IsHidden) return false;
            Vector3 eye = Eye, pe = p.EyePosition, d = pe - eye;
            if (d.magnitude > range) return false;
            if (d.magnitude > 2.5f && Vector3.Angle(transform.forward, new Vector3(d.x, 0f, d.z)) > fov * 0.5f) return false;
            if (!Physics.Linecast(eye, pe, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<PlayerController>() != null;
        }

        protected bool InReach(float reach) => gm.Player && Vector3.Distance(Eye, gm.Player.EyePosition) <= reach + radius;

        protected bool TryAttack(float reach = 1.6f, float cooldown = 1.5f)
        {
            var p = gm.Player;
            if (p == null || p.IsHidden || Time.time < attackReadyAt || !InReach(reach)) return false;
            attackReadyAt = Time.time + cooldown;
            gm.DamagePlayer(this);
            return true;
        }

        public void Distract(Vector3 point, float seconds)
        {
            distractPoint = point;
            distractedUntil = Time.time + seconds;
        }

        protected void BoostSpeed(float mul, float seconds)
        {
            speedMul = mul;
            speedBoostUntil = Time.time + seconds;
        }

        /// <summary>Hidden inside closed furniture: not drawn and cannot be hit.</summary>
        protected bool HiddenInside { get; private set; }

        protected void SetHiddenInside(bool hidden)
        {
            HiddenInside = hidden;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
        }

        /// <summary>Moves straight towards a point (no path): used for the last step into or out of a cupboard.</summary>
        protected bool MoveDirect(Vector3 p, float spd)
        {
            Face(p - logical);
            logical = Vector3.MoveTowards(logical, p, spd * Time.deltaTime);
            return (logical - p).sqrMagnitude < 0.0025f;
        }

        protected void ClearPath() { path.Clear(); pathIndex = 0; }

        protected virtual void OnCaptured() { }

        public void TakeHit()
        {
            if (IsCaptured || HiddenInside) return;
            HitsTaken++;
            lastHitTime = Time.time;
            if (HitsTaken >= MaxHits) { BeginCapture(); return; }
            StartCoroutine(Flinch());
            OnHurt();
        }

        void BeginCapture()
        {
            IsCaptured = true;
            StateName = "Captured";
            StopAllCoroutines();
            OnCaptured();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            gm.OnGhostCaptured(this); // removed from the game straight away (no more pathing, attacks, HUD tracking)
            StartCoroutine(Capture());
            Destroy(gameObject, 1.5f); // safety net: the ghost is always removed, even if the animation is interrupted
        }

        IEnumerator Flinch()
        {
            Quaternion r0 = transform.rotation;
            for (float t = 0; t < 0.25f; t += Time.deltaTime)
            {
                transform.rotation = r0 * Quaternion.Euler(0f, Mathf.Sin(t * 60f) * 12f, 0f);
                yield return null;
            }
        }

        IEnumerator Capture()
        {
            // Spin, shrink and get pulled into the nozzle; then vanish for good.
            Vector3 start = transform.position;
            Vector3 s0 = transform.localScale;
            var renderers = GetComponentsInChildren<Renderer>();
            const float duration = 0.6f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                Vector3 target = gm.Vacuum ? gm.Vacuum.Nozzle.position + gm.Vacuum.Nozzle.forward * 0.4f : start;
                transform.position = Vector3.Lerp(start, target, k * k);
                transform.localScale = s0 * Mathf.Lerp(1f, 0.05f, k);
                transform.Rotate(0f, 900f * Time.deltaTime, 0f);
                if (k > 0.75f) foreach (var r in renderers) if (r) r.enabled = false; // gone before it reaches the camera
                yield return null;
            }
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
