using UnityEngine;

namespace SheetHappens
{
    /// <summary>Green Sneak - The Infiltrator. Takes routes away from the player to the bell; retaliates only when shot.</summary>
    public class GreenSneakAI : GhostAgent
    {
        enum S { HideObserve, ChooseSafeRoute, MoveToBell, Retaliate, RingBell }
        S s;
        float timer, replan;
        Vector3 avoidPos;
        public override string DisplayName => "Green Sneak";

        protected override float StrugglePitch => 0.95f;
        // Sneaky: crouched low, tiptoes with little hops, glances over its shoulder when it stops.
        protected override GhostStyle Style { get { var g = GhostStyle.Default; g.bobAmp = 0.04f; g.bobFreq = 0.3f; g.swayDeg = 3f; g.leanPerSpeed = 6f; g.hover = -0.12f; g.posture = new Vector3(1.08f, 0.82f, 1.08f); g.glanceEvery = 4f; g.hop = 0.06f; return g; } }
        protected override Color TrailTint => new Color(0.55f, 0.95f, 0.55f);
        protected override float TrailDensity => 0.8f;
        protected override bool FightsBack => true;
        protected override Color CaptureTint => new Color(0.45f, 1f, 0.45f);

        protected override void OnSpawn()
        {
            Type = GhostType.GreenSneak; CaptureTime = 1.9f; recoverRate = 0.3f; Speed = gm.greenSneakSpeed;
            s = S.HideObserve; timer = 3f;
        }

        float AvoidCost(int cell)
        {
            float d = Vector3.Distance(grid.Center(cell), avoidPos);
            return d < 7f ? (7f - d) * 3f : 0f; // routes near the player cost more
        }

        protected override void Think()
        {
            StateName = s == S.MoveToBell ? "MoveToBell (avoiding player)" : s.ToString();
            Vector3 bell = gm.Bell.RingPoint;
            switch (s)
            {
                case S.HideObserve:
                    Face(PlayerPos - logical);
                    timer -= Time.deltaTime;
                    if (timer <= 0f) s = S.ChooseSafeRoute;
                    break;
                case S.ChooseSafeRoute:
                    avoidPos = PlayerPos;
                    if (PathTo(bell, AvoidCost)) { s = S.MoveToBell; replan = 1.5f; }
                    break;
                case S.MoveToBell:
                    replan -= Time.deltaTime;
                    if (replan <= 0f || PathBlockedAhead()) { s = S.ChooseSafeRoute; break; }
                    if (MoveAlong(Speed))
                    {
                        if (AtBell(bell)) s = S.RingBell; else s = S.ChooseSafeRoute;
                    }
                    break;
                case S.Retaliate:
                    replan -= Time.deltaTime;
                    if (replan <= 0f) { PathTo(PlayerPos); replan = 0.5f; }
                    MoveAlong(Speed * 1.4f);
                    timer -= Time.deltaTime;
                    if (TryAttack() || timer <= 0f) s = S.ChooseSafeRoute; // hits back once, then continues to the bell
                    break;
                case S.RingBell:
                    gm.BellRung(this);
                    break;
            }
        }

        protected override void OnHurt()
        {
            s = S.Retaliate; timer = 6f; replan = 0f;
        }
    }
}
