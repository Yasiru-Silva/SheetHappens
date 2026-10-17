using UnityEngine;

namespace SheetHappens
{
    /// <summary>How a ghost type moves: each ghost has its own personality.</summary>
    public struct GhostStyle
    {
        public float bobAmp, bobFreq;       // floating up and down (m, Hz)
        public float swayDeg, swayFreq;     // rocking side to side
        public float leanPerSpeed;          // degrees of forward lean per m/s
        public float baseHunch;             // constant forward lean (Grandpa)
        public float breathe;               // swelling in and out
        public float hover;                 // extra height (negative = lower)
        public Vector3 posture;             // scale multiplier (Sneak crouches)
        public float twirlEvery;            // playful spins (Wisp), 0 = never
        public float glanceEvery;           // looks over its shoulder when still (Sneak), 0 = never
        public float hop;                   // little hops while moving

        public static GhostStyle Default => new GhostStyle
        {
            bobAmp = 0.08f, bobFreq = 0.35f, swayDeg = 4f, swayFreq = 0.5f, leanPerSpeed = 8f,
            breathe = 0.03f, posture = Vector3.one,
        };
    }

    /// <summary>
    /// Procedural animation for the ghosts (their models have no rigs): floating, leaning into movement, swaying,
    /// breathing, and one-off moves: appearing, lunging at the player, the Blue Boo jump scare and Grandpa looming.
    /// GhostAgent applies the result on top of where its AI puts the ghost.
    /// </summary>
    public class GhostAnimator
    {
        public GhostStyle style;

        // Output for this frame.
        public Vector3 Offset { get; private set; }        // world-space position offset
        public Vector3 Euler { get; private set; }         // pitch (forward lean), extra yaw, roll, in the ghost's facing frame
        public Vector3 Scale { get; private set; } = Vector3.one;
        public float Glow { get; private set; } = 1f;      // eye/emission brightness multiplier
        /// <summary>Playing a move that must face the player.</summary>
        public bool FacingPlayer => Time.time < actionEnd && (action == Act.Lunge || action == Act.Scare || action == Act.Loom);
        public bool Scaring => Time.time < actionEnd && action == Act.Scare;

        enum Act { None, Appear, Lunge, Scare, Loom }
        Act action;
        float actionStart, actionEnd, t, speedSm, nextTwirl, twirlStart = -99f, nextGlance, glanceStart = -99f, glanceSide = 1f;
        Vector3 velSm;

        public GhostAnimator(GhostStyle s)
        {
            style = s;
            t = Random.value * 10f;
            nextTwirl = Time.time + Random.Range(2f, 5f);
            nextGlance = Time.time + Random.Range(2f, 4f);
        }

        public void Appear() => Play(Act.Appear, 0.9f);
        public void Lunge() => Play(Act.Lunge, 0.55f);
        public void Scare() => Play(Act.Scare, 1.35f);
        public void Loom() => Play(Act.Loom, 1.1f);

        void Play(Act a, float seconds)
        {
            action = a;
            actionStart = Time.time;
            actionEnd = Time.time + seconds;
        }

        /// <summary>
        /// Works out this frame's pose. 'velocity' is how the ghost is moving (m/s), 'forward' and 'right' its facing,
        /// 'toPlayer' the vector from its body to the player's eyes.
        /// </summary>
        public void Tick(float dt, Vector3 velocity, Vector3 forward, Vector3 right, Vector3 toPlayer)
        {
            t += dt;
            velSm = Vector3.Lerp(velSm, new Vector3(velocity.x, 0f, velocity.z), 1f - Mathf.Exp(-6f * dt));
            float speed = velSm.magnitude;
            speedSm = Mathf.Lerp(speedSm, speed, 1f - Mathf.Exp(-5f * dt));
            float moving = Mathf.Clamp01(speedSm / 1.2f);
            float vf = Vector3.Dot(velSm, forward), vl = Vector3.Dot(velSm, right);

            // ---- idle and travel
            float bob = Mathf.Sin(t * style.bobFreq * Mathf.PI * 2f) * style.bobAmp * (1f + 0.4f * moving);
            float hop = style.hop > 0f ? Mathf.Abs(Mathf.Sin(t * 7.5f)) * style.hop * moving : 0f;
            float pitch = style.baseHunch + Mathf.Clamp(vf * style.leanPerSpeed, -15f, 28f);
            float roll = -Mathf.Clamp(vl * style.leanPerSpeed * 0.7f, -20f, 20f)
                         + Mathf.Sin(t * style.swayFreq * Mathf.PI * 2f) * style.swayDeg * (1f + moving)
                         + (style.hop > 0f ? Mathf.Sin(t * 7.5f) * 7f * moving : 0f); // tiptoeing
            float yaw = 0f;
            float b = Mathf.Sin(t * 1.6f) * style.breathe;
            Vector3 scale = Vector3.Scale(style.posture, new Vector3(1f + b, 1f - b * 0.6f, 1f + b));
            // stretched a little in the direction of travel
            scale.z *= 1f + Mathf.Min(speedSm, 3f) * 0.05f;
            scale.y *= 1f - Mathf.Min(speedSm, 3f) * 0.02f;
            Vector3 offset = Vector3.up * (style.hover + bob + hop);
            float glow = 1f + 0.15f * Mathf.Sin(t * 3.1f);

            // Wisp: playful twirl now and then.
            if (style.twirlEvery > 0f && Time.time > nextTwirl)
            {
                twirlStart = Time.time;
                nextTwirl = Time.time + style.twirlEvery * Random.Range(0.7f, 1.4f);
            }
            float tw = (Time.time - twirlStart) / 0.7f;
            if (tw >= 0f && tw <= 1f)
            {
                yaw += 360f * Ease(tw);
                offset += Vector3.up * Mathf.Sin(tw * Mathf.PI) * 0.18f;
                scale *= 1f + 0.12f * Mathf.Sin(tw * Mathf.PI);
            }

            // Sneak: looks back over its shoulder when it stops.
            if (style.glanceEvery > 0f && speedSm < 0.3f && Time.time > nextGlance)
            {
                glanceStart = Time.time;
                glanceSide = Random.value < 0.5f ? -1f : 1f;
                nextGlance = Time.time + style.glanceEvery * Random.Range(0.7f, 1.5f);
            }
            float gl = (Time.time - glanceStart) / 1.4f;
            if (gl >= 0f && gl <= 1f) yaw += glanceSide * 45f * Mathf.Sin(gl * Mathf.PI);

            // ---- one-off moves
            if (Time.time < actionEnd)
            {
                float k = Mathf.InverseLerp(actionStart, actionEnd, Time.time);
                float dist = toPlayer.magnitude;
                Vector3 dir = dist > 0.01f ? toPlayer / dist : forward;
                switch (action)
                {
                    case Act.Appear:
                        {
                            // Swirls up out of nothing.
                            float e = Ease(k);
                            scale *= Mathf.Lerp(0.15f, 1f, e) * (1f + 0.15f * Mathf.Sin(k * Mathf.PI));
                            yaw += (1f - e) * 540f;
                            offset += Vector3.down * (1f - e) * 0.4f;
                            glow *= 1f + 2f * (1f - k);
                            break;
                        }
                    case Act.Lunge:
                        {
                            // Rear back, then strike forward stretched out, then recover.
                            float reach = Mathf.Clamp(dist - 0.6f, 0.2f, 1.1f);
                            if (k < 0.25f) { float a = k / 0.25f; offset -= dir * 0.18f * a; scale = Vector3.Scale(scale, new Vector3(1f + 0.12f * a, 1f + 0.1f * a, 1f - 0.15f * a)); pitch -= 12f * a; }
                            else if (k < 0.5f) { float a = (k - 0.25f) / 0.25f; offset += dir * Mathf.Lerp(-0.18f, reach, Ease(a)); scale = Vector3.Scale(scale, new Vector3(0.88f, 0.92f, 1.45f)); pitch += 30f; glow *= 2f; }
                            else { float a = (k - 0.5f) / 0.5f; offset += dir * reach * (1f - Ease(a)); pitch += 30f * (1f - a); }
                            break;
                        }
                    case Act.Scare:
                        {
                            // Blue Boo jump scare: shrinks back, then rushes right up to your face, swells huge with
                            // blazing eyes and shakes violently, then drifts back.
                            float reach = Mathf.Max(0f, dist - 0.8f);
                            Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 25f) - 0.5f, Mathf.PerlinNoise(t * 25f, t * 25f) - 0.5f);
                            if (k < 0.15f)
                            {
                                float a = k / 0.15f;
                                scale *= Mathf.Lerp(1f, 0.75f, a);
                                offset -= dir * 0.3f * a;
                                glow *= 1f - 0.6f * a; // eyes go dark...
                            }
                            else if (k < 0.32f)
                            {
                                float a = Ease((k - 0.15f) / 0.17f);
                                scale *= Mathf.Lerp(0.75f, 1.85f, a);
                                offset += dir * Mathf.Lerp(-0.3f, reach, a);
                                pitch -= 15f * a;
                                glow *= Mathf.Lerp(0.4f, 4f, a); // ...then blaze
                            }
                            else if (k < 0.78f)
                            {
                                float a = (k - 0.32f) / 0.46f;
                                scale *= 1.85f + 0.15f * Mathf.Sin(t * 30f);
                                offset += dir * reach + jitter * 0.14f;
                                roll += Mathf.Sin(t * 47f) * 14f;
                                pitch += Mathf.Sin(t * 31f) * 8f - 10f;
                                glow *= 3.5f + Mathf.Sin(t * 40f) * 1.2f;
                                scale.z *= 1f + 0.1f * Mathf.Sin(a * 30f);
                            }
                            else
                            {
                                float a = Ease((k - 0.78f) / 0.22f);
                                scale *= Mathf.Lerp(1.85f, 1f, a);
                                offset += dir * reach * (1f - a);
                                glow *= Mathf.Lerp(3f, 1f, a);
                            }
                            break;
                        }
                    case Act.Loom:
                        {
                            // Grandpa rises up tall and swells, leaning back, then hunches forward at you.
                            float up = Mathf.Sin(Mathf.Min(1f, k * 1.4f) * Mathf.PI * 0.5f) * (1f - Mathf.Max(0f, k - 0.75f) * 2f);
                            offset += Vector3.up * 0.45f * up;
                            scale *= 1f + 0.4f * up;
                            pitch += k < 0.5f ? -18f * up : 22f * up;
                            if (k > 0.35f && k < 0.75f) offset += Random.insideUnitSphere * 0.03f;
                            glow *= 1f + 2.5f * up;
                            break;
                        }
                }
            }

            Offset = offset;
            Euler = new Vector3(pitch, yaw, roll);
            Scale = scale;
            Glow = glow;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
