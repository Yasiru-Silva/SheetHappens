using UnityEngine;

namespace SheetHappens
{
    /// <summary>Grandpa Ghost - The Hunter. Wakes at 5 AM, searches, then hunts the player and never retreats.</summary>
    public class GrandpaAI : GhostAgent
    {
        enum S { Hidden, Search, DetectPlayer, Chase, Attack }
        S s;
        float timer, repath, lostSight;
        Vector3 lastSeen;
        public override string DisplayName => "Grandpa Ghost";
        protected override string ScareClip => "Grandpa_Growl";
        // After landing a hit he laughs at you.
        protected override void OnAttackSound() => Sfx.Play("Grandpa_Laugh", Eye, 1f, 0.75f);

        protected override float StrugglePitch => 0.6f;
        // Heavy and menacing: slow deep float, always hunched forward, leans into the hunt.
        protected override GhostStyle Style { get { var g = GhostStyle.Default; g.bobAmp = 0.12f; g.bobFreq = 0.25f; g.swayDeg = 3f; g.swayFreq = 0.3f; g.leanPerSpeed = 9f; g.baseHunch = 8f; g.breathe = 0.045f; return g; } }
        protected override Color TrailTint => new Color(0.75f, 0.75f, 0.8f);
        protected override float TrailDensity => 1.4f;
        protected override bool FightsBack => true;
        protected override Color CaptureTint => new Color(0.9f, 0.9f, 1f);

        protected override void OnSpawn()
        {
            Type = GhostType.Grandpa; CaptureTime = Mathf.Max(1f, gm.grandpaCaptureSeconds); recoverRate = 0.12f; Speed = gm.grandpaSpeed;
            s = S.Hidden; timer = 3f;
        }

        protected override void Think()
        {
            StateName = s == S.Chase ? "Chase (continue)" : s.ToString();
            switch (s)
            {
                case S.Hidden:
                    timer -= Time.deltaTime;
                    if (CanSeePlayer(8f, 120f)) { Detect(); break; }
                    if (timer <= 0f) { s = S.Search; NewSearchTarget(); }
                    break;
                case S.Search:
                    if (CanSeePlayer(15f, 150f)) { Detect(); break; }
                    if (MoveAlong(Speed)) NewSearchTarget();
                    break;
                case S.DetectPlayer:
                    Face(PlayerPos - logical);
                    timer -= Time.deltaTime;
                    if (timer <= 0f) { s = S.Chase; repath = 0f; }
                    break;
                case S.Chase:
                    if (CanSeePlayer(25f, 360f)) { lastSeen = PlayerPos; lostSight = 0f; }
                    else lostSight += Time.deltaTime;
                    repath -= Time.deltaTime;
                    if (repath <= 0f) { PathTo(lastSeen); repath = 0.4f; }
                    bool arrived = MoveAlong(Speed * 1.15f);
                    if (TryAttack(1.7f, 1.2f)) { s = S.Attack; timer = 0.6f; break; }
                    if (lostSight > 5f && arrived) { s = S.Search; NewSearchTarget(); } // still hunting, just lost you
                    break;
                case S.Attack:
                    Face(PlayerPos - logical);
                    timer -= Time.deltaTime;
                    if (timer <= 0f) s = S.Chase; // continue the chase, never retreat
                    break;
            }
        }

        void Detect()
        {
            s = S.DetectPlayer; timer = 1.0f; lastSeen = PlayerPos; // rises up and looms before the hunt
            gm.ShowBanner("Grandpa Ghost has seen you!", 2f);
            Scare();
            Loom();
        }

        void NewSearchTarget()
        {
            int c = gm.Level.RandomGroundCell(logical, 6f);
            if (c >= 0) PathTo(grid.PathPoint(c));
        }

        protected override void OnHurt()
        {
            if (s == S.Hidden || s == S.Search) { lastSeen = PlayerPos; s = S.Chase; repath = 0f; }
        }
    }
}
