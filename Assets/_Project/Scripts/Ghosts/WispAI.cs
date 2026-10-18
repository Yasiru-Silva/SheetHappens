using UnityEngine;

namespace SheetHappens
{
    /// <summary>Wisp - The Runner. Ignores the player and takes the shortest A* path to the bell, re-pathing around barricades.</summary>
    public class WispAI : GhostAgent
    {
        enum S { Spawn, MoveToBell, Repath, RingBell }
        S s;
        float timer;

        protected override float StrugglePitch => 1.35f;
        // Light and playful: bouncy float, leans into its run, twirls now and then.
        protected override GhostStyle Style { get { var g = GhostStyle.Default; g.bobAmp = 0.1f; g.bobFreq = 0.55f; g.swayDeg = 6f; g.swayFreq = 0.8f; g.leanPerSpeed = 10f; g.breathe = 0.04f; g.twirlEvery = 5f; return g; } }
        protected override Color TrailTint => new Color(1f, 0.9f, 0.55f);
        protected override float TrailDensity => 0.9f;
        protected override Color CaptureTint => new Color(1f, 0.85f, 0.3f);

        protected override void OnSpawn()
        {
            Type = GhostType.Wisp; CaptureTime = 0.9f; recoverRate = 0.6f; Speed = gm.wispMoveSpeed;
            s = S.Spawn; timer = 3f; // rises out of the furniture
        }

        protected override void Think()
        {
            StateName = s.ToString();
            Vector3 bell = gm.Bell.RingPoint;
            switch (s)
            {
                case S.Spawn:
                    timer -= Time.deltaTime;
                    if (timer <= 0f) s = S.Repath;
                    break;
                case S.Repath:
                    timer -= Time.deltaTime;
                    if (timer > 0f) break;
                    if (PathTo(bell)) s = S.MoveToBell; else timer = 0.5f;
                    break;
                case S.MoveToBell:
                    visualOffset = Right * Mathf.Sin(Time.time * 5f) * 0.18f; // small zig-zag, harder to aim at
                    if (PathBlockedAhead()) { s = S.Repath; timer = 0f; break; }
                    if (MoveAlong(Speed))
                    {
                        if (AtBell(bell)) s = S.RingBell; else { s = S.Repath; timer = 0f; }
                    }
                    break;
                case S.RingBell:
                    gm.BellRung(this);
                    break;
            }
        }
    }
}
