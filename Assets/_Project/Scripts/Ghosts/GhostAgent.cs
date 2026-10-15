using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Shared ghost body: A* path following, floating, vision, attacks, the vacuum struggle and capture,
    /// procedural animation (GhostAnimator), the smoky dissolving bottom (GhostDissolve) and the smoke (GhostTrail).
    /// Added at runtime to the existing ghost prefabs; their meshes are not changed.
    /// </summary>
    public abstract class GhostAgent : MonoBehaviour
    {
        public GhostType Type { get; protected set; }
        /// <summary>Seconds of steady suction (vacuum level 1) needed to capture this ghost.</summary>
        public float CaptureTime { get; protected set; } = 1f;
        public float Speed { get; protected set; } = 2f;
        public string StateName { get; protected set; } = "";
        /// <summary>The capture meter, 0..1. Drops back slowly when the beam lets go.</summary>
        public float SuckProgress { get; private set; }
        /// <summary>Caught in the beam right now.</summary>
        public bool BeingSucked => Time.time - lastSuck < 0.12f;
        public bool CanBeSucked => !IsCaptured && !HiddenInside && gm != null && gm.State == GameState.Playing;
        /// <summary>The middle of the ghost's body (where the beam aims).</summary>
        public Vector3 SuckPoint => body ? body.bounds.center : Eye;
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
        /// <summary>How fast the capture meter drains (per second) after the beam lets go.</summary>
        protected float recoverRate = 0.4f;
        protected float radius = 0.4f, height = 1f;

        float bottomOffset, bob, distractedUntil, attackReadyAt, speedBoostUntil;
        float lastSuck = -99f, nextStruggleSound;
        Vector3 distractPoint, suckFrom, struggleOffset, baseScale;
        int pathFailures;
        CapsuleCollider body;
        GhostTrail trail;
        GhostDissolve dissolve;
        protected GhostAnimator anim;
        Quaternion yaw = Quaternion.identity;
        Vector3 lastLogical;
        Bounds bodyBounds = new Bounds(Vector3.zero, Vector3.one * 0.6f);
        Vector3 correction;
        static readonly Collider[] Nearby = new Collider[32];
        static bool loggedPush;

        public void Init(GameManager manager, Vector3 at)
        {
            gm = manager;
            grid = manager.Grid;
            transform.position = at;
            baseScale = transform.localScale;
            MeasureBody();
            logical = at;
            lastLogical = at;
            yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (bodyBounds.center == Vector3.zero) bodyBounds.center = at + Vector3.up * 0.5f; // no meshes measured
            dissolve = GhostDissolve.Apply(gameObject, Color.Lerp(TrailTint, Color.white, 0.2f));
            trail = GhostTrail.Attach(this, bodyBounds, TrailTint, TrailDensity);
            bob = Random.value * 6f;
            anim = new GhostAnimator(Style);
            anim.Appear();
            CollectEyes();
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
            bodyBounds = b;
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
            if (Time.time > speedBoostUntil) speedMul = 1f;

            if (BeingSucked) Struggle();
            else
            {
                // Out of the beam: the meter drains and the body springs back.
                if (Time.time - lastSuck > 0.4f) SuckProgress = Mathf.MoveTowards(SuckProgress, 0f, recoverRate * Time.deltaTime);
                struggleOffset = Vector3.Lerp(struggleOffset, Vector3.zero, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }

            if (BeingSucked) { /* held in the beam: no thinking, just fighting it */ }
            else if (Time.time < distractedUntil)
            {
                StateName = "Distracted";
                Face(distractPoint - logical);
            }
            else Think();
            Place();
        }

        void Place()
        {
            float dt = Time.deltaTime;
            bob += dt * 2.2f;
            // Animation on top of where the AI put the ghost.
            Vector3 vel = dt > 0f ? Vector3.ClampMagnitude((logical - lastLogical) / dt, 6f) : Vector3.zero;
            lastLogical = logical;
            Vector3 toPlayer = gm != null && gm.Player ? gm.Player.EyePosition - SuckPoint : Vector3.zero;
            bool held = BeingSucked;
            if (anim != null)
            {
                anim.Tick(dt, vel, Forward, Right, toPlayer);
                if (anim.FacingPlayer && !held) Face(toPlayer);
            }
            Vector3 animOffset = anim != null && !held ? anim.Offset : Vector3.zero;
            Vector3 want = logical + Vector3.up * bottomOffset + visualOffset + struggleOffset + animOffset;
            // Keep the whole body inside the room: push it out of any wall, floor or ceiling it overlaps.
            Vector3 push = body ? PushOutOfWalls(want) : Vector3.zero;
            if (!loggedPush && push.sqrMagnitude > 0.0025f) { loggedPush = true; Debug.Log($"[SheetHappens] {DisplayName} body kept out of a wall/floor (pushed {push.magnitude:0.00} m)."); }
            correction = Vector3.Lerp(correction, push, 1f - Mathf.Exp(-15f * Time.deltaTime));
            if ((push - correction).sqrMagnitude > 0.04f) correction = push; // never lag far behind into a wall
            transform.position = want + correction;
            transform.rotation = held || anim == null ? yaw : yaw * Quaternion.Euler(anim.Euler);
            if (!held && anim != null)
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.Scale(baseScale, anim.Scale), 1f - Mathf.Exp(-25f * dt));
            if (dissolve) dissolve.SetGlow(anim != null && !held ? anim.Glow : 1.6f);
            Blink(held);
        }

        // ------------------------------------------------------------ blinking

        struct EyePart { public Transform t; public Vector3 scale; public int axis; }
        readonly List<EyePart> eyes = new List<EyePart>();
        float nextBlink, blinkStart = -99f, secondBlink = -99f, nextFlicker, flickerStart = -99f, flickerLength;

        /// <summary>Finds the glowing eye parts of the model (EyeGlow, Glint, EyeShine) so they can blink.</summary>
        void CollectEyes()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLower();
                if (!(n.Contains("eyeglow") || n.Contains("glint") || n.Contains("eyeshine"))) continue;
                // Squash along whichever local axis points up.
                float ux = Mathf.Abs(Vector3.Dot(t.right, Vector3.up)), uy = Mathf.Abs(Vector3.Dot(t.up, Vector3.up)), uz = Mathf.Abs(Vector3.Dot(t.forward, Vector3.up));
                int axis = uy >= ux && uy >= uz ? 1 : ux >= uz ? 0 : 2;
                eyes.Add(new EyePart { t = t, scale = t.localScale, axis = axis });
            }
            nextBlink = Time.time + Random.Range(1f, 4f);
            nextFlicker = Time.time + Random.Range(5f, 12f);
        }

        /// <summary>
        /// Eyes blink every few seconds (sometimes twice), go wide during a jump scare and squint in the vacuum beam.
        /// Now and then the whole ghost flickers like a bad signal.
        /// </summary>
        void Blink(bool held)
        {
            float now = Time.time;
            if (now > nextBlink)
            {
                blinkStart = now;
                secondBlink = Random.value < 0.25f ? now + 0.28f : -99f;
                nextBlink = now + Random.Range(2f, 5.5f);
            }
            float open = 1f;
            float b1 = (now - blinkStart) / 0.16f, b2 = (now - secondBlink) / 0.16f;
            if (b1 >= 0f && b1 <= 1f) open = 1f - Mathf.Sin(b1 * Mathf.PI) * 0.92f;
            if (b2 >= 0f && b2 <= 1f) open = Mathf.Min(open, 1f - Mathf.Sin(b2 * Mathf.PI) * 0.92f);
            if (held) open = 0.35f + 0.1f * Mathf.Sin(now * 25f);   // squeezed shut, fighting the beam
            else if (anim != null && anim.Scaring) open = 1.3f;     // eyes wide open
            foreach (var e in eyes)
            {
                if (e.t == null) continue;
                Vector3 sc = e.scale;
                sc[e.axis] *= open;
                e.t.localScale = sc;
            }

            // Ghostly flicker: a few rapid fades in and out.
            if (now > nextFlicker && !HiddenInside)
            {
                flickerStart = now;
                flickerLength = Random.Range(0.3f, 0.6f);
                nextFlicker = now + Random.Range(6f, 14f);
            }
            float f = (now - flickerStart) / flickerLength;
            float opacity = 0.95f;
            if (f >= 0f && f <= 1f) opacity *= Mathf.Lerp(0.25f, 1f, Mathf.Abs(Mathf.Sin(f * Mathf.PI * 3.5f)));
            if (dissolve) dissolve.SetOpacity(opacity);
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
            yaw = Quaternion.Slerp(yaw, Quaternion.LookRotation(dir), Time.deltaTime * 6f);
        }

        /// <summary>Turns on the spot (degrees).</summary>
        protected void Turn(float degrees) => yaw *= Quaternion.Euler(0f, degrees, 0f);
        /// <summary>The way the ghost faces (ignoring its animated lean and sway).</summary>
        protected Vector3 Forward => yaw * Vector3.forward;
        protected Vector3 Right => yaw * Vector3.right;

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
            if (d.magnitude > 2.5f && Vector3.Angle(Forward, new Vector3(d.x, 0f, d.z)) > fov * 0.5f) return false;
            if (!Physics.Linecast(eye, pe, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<PlayerController>() != null;
        }

        protected bool InReach(float reach) => gm.Player && Vector3.Distance(Eye, gm.Player.EyePosition) <= reach + radius;

        protected bool TryAttack(float reach = 1.6f, float cooldown = 1.5f)
        {
            var p = gm.Player;
            if (p == null || p.IsHidden || Time.time < attackReadyAt || !InReach(reach)) return false;
            attackReadyAt = Time.time + cooldown;
            if (anim != null && !anim.Scaring) anim.Lunge();
            OnAttackSound();
            gm.DamagePlayer(this);
            return true;
        }

        // ------------------------------------------------------------ scary noises

        /// <summary>Scary noise this ghost makes when it goes for the player (none for Wisps and Green Sneaks).</summary>
        protected virtual string ScareClip => null;
        float nextScare;

        protected void Scare(float volume = 1f)
        {
            if (ScareClip == null || Time.time < nextScare) return;
            nextScare = Time.time + 2.5f;
            Sfx.Play(ScareClip, Eye, volume, 0.75f, Random.Range(0.92f, 1.08f));
        }

        protected virtual void OnAttackSound() => Scare();

        // ------------------------------------------------------------ animation

        /// <summary>How this ghost type floats and moves.</summary>
        protected virtual GhostStyle Style => GhostStyle.Default;

        /// <summary>
        /// Jump scare: the ghost rushes up into the player's face, swells huge with blazing eyes and shakes, with a
        /// scream, a camera jolt and a dark flash on the screen.
        /// </summary>
        protected void JumpScare()
        {
            if (anim != null) anim.Scare();
            Scare(1f);
            var p = gm.Player;
            if (p && (p.EyePosition - SuckPoint).sqrMagnitude < 64f)
            {
                p.Shake(0.7f, 0.07f);
                gm.Flash(new Color(0.05f, 0.1f, 0.35f), 0.75f);
            }
        }

        /// <summary>Rises up tall and swells (Grandpa noticing the player).</summary>
        protected void Loom() { if (anim != null) anim.Loom(); }

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
        public bool IsHiddenInside => HiddenInside;

        /// <summary>Colour of the smoke this ghost leaves behind as it moves.</summary>
        protected virtual Color TrailTint => new Color(0.8f, 0.8f, 0.9f);
        /// <summary>How much smoke (1 = normal).</summary>
        protected virtual float TrailDensity => 1f;

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

        /// <summary>Debug: moves the ghost to a floor point.</summary>
        public void WarpTo(Vector3 p) { logical = p; ClearPath(); }

        protected virtual void OnCaptured() { }

        // ------------------------------------------------------------ vacuum

        /// <summary>Called every frame the beam holds this ghost. 'amount' is seconds of suction (scaled by vacuum power).</summary>
        public void Suck(float amount, Vector3 from)
        {
            if (!CanBeSucked) return;
            if (!BeingSucked)
            {
                OnHurt(); // each ghost reacts in its own way when the beam first grabs it
                if (Time.time > nextStruggleSound)
                {
                    nextStruggleSound = Time.time + 1.6f;
                    Sfx.Play("Ghost_Struggle", Eye, 0.8f, 0.7f, StrugglePitch * Random.Range(0.95f, 1.05f));
                }
            }
            lastSuck = Time.time;
            suckFrom = from;
            SuckProgress = Mathf.Clamp01(SuckProgress + amount / Mathf.Max(0.1f, CaptureTime));
            if (SuckProgress >= 1f) BeginCapture();
        }

        /// <summary>The beam let go (trigger released, lost aim or vacuum jammed).</summary>
        public void ReleaseSuction() => lastSuck = Mathf.Min(lastSuck, Time.time - 0.13f);

        /// <summary>Voice of the struggle squeal: small ghosts squeak higher, Grandpa groans low.</summary>
        protected virtual float StrugglePitch => 1f;
        /// <summary>The bigger ghosts can still lash out at a player standing too close while caught in the beam.</summary>
        protected virtual bool FightsBack => false;
        /// <summary>Colour of the sparks when this ghost is captured.</summary>
        protected virtual Color CaptureTint => new Color(0.8f, 0.6f, 1f);

        void Struggle()
        {
            StateName = $"Struggling {SuckProgress * 100f:0}%";
            float p = SuckProgress;
            Vector3 toVac = suckFrom - SuckPoint;
            Face(toVac); // screams at the player
            Vector3 dir = toVac.sqrMagnitude > 1e-4f ? toVac.normalized : Forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            // Pulled towards the vacuum more and more, while thrashing side to side trying to break free.
            float resist = 1f - p;
            Vector3 want = dir * (0.12f + 0.75f * p * p)
                         + side * Mathf.Sin(Time.time * 6.5f + bob) * 0.28f * resist
                         + Vector3.up * Mathf.Sin(Time.time * 9f) * 0.06f
                         + Random.insideUnitSphere * (0.02f + 0.05f * p);
            struggleOffset = Vector3.Lerp(struggleOffset, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
            // Stretched towards the nozzle, squeezed thin, wobbling.
            float wob = Mathf.Sin(Time.time * 32f) * 0.05f;
            transform.localScale = new Vector3(baseScale.x * (1f - 0.25f * p + wob), baseScale.y * (1f - 0.2f * p - wob), baseScale.z * (1f + 0.7f * p));
            if (FightsBack) TryAttack(1.4f, 1.6f);
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
            Destroy(gameObject, 2.2f); // safety net: the ghost is always removed, even if the animation is interrupted
        }

        IEnumerator Capture()
        {
            // Struggle for a moment, then get stretched, spun and slurped into the vacuum nozzle.
            Sfx.Play("Capture_Suck", transform.position, 1f, 0.3f, Random.Range(0.95f, 1.05f));
            Vector3 start = transform.position;
            Vector3 s0 = transform.localScale;
            var renderers = GetComponentsInChildren<Renderer>();
            Vector3 centreOffset = Vector3.up * (height * 0.5f);
            var vac = gm.Vacuum;
            for (float t = 0; t < 0.15f; t += Time.deltaTime)
            {
                transform.position = start + Random.insideUnitSphere * 0.07f;
                transform.localScale = s0 * (1f + 0.12f * Mathf.Sin(t * 70f));
                if (vac) vac.HoldBeam(transform.position + centreOffset);
                yield return null;
            }
            start = transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, (vac ? vac.Nozzle.position : start + Vector3.forward) - start).normalized;
            const float duration = 0.75f;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                Vector3 nozzle = vac ? vac.Nozzle.position + vac.Nozzle.forward * 0.35f : start;
                // spiral in towards the nozzle, speeding up
                float swirl = (1f - k) * 0.35f;
                Vector3 p = Vector3.Lerp(start, nozzle, k * k) + (side * Mathf.Cos(k * 22f) + Vector3.up * Mathf.Sin(k * 22f)) * swirl;
                transform.position = p;
                Vector3 to = nozzle - p;
                if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to) * Quaternion.Euler(0f, 0f, k * 900f);
                // stretched long and thin like it is being sucked up
                float shrink = Mathf.Lerp(1f, 0.05f, k * k);
                transform.localScale = new Vector3(s0.x * shrink * (1f - 0.45f * k), s0.y * shrink * (1f - 0.45f * k), s0.z * shrink * (1f + 2.5f * k));
                if (vac) vac.HoldBeam(p);
                if (k > 0.9f) foreach (var r in renderers) if (r) r.enabled = false; // gone before it reaches the camera
                yield return null;
            }
            if (vac) vac.CaptureBurst(CaptureTint);
            if (trail) trail.Release();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
