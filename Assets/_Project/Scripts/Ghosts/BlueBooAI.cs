using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Blue Boo - The Ambusher. Hides inside cupboards (the doors rattle now and then), bursts out when the player comes
    /// close or opens the doors, attacks, chases briefly, then floats off to hide in another cupboard.
    /// </summary>
    public class BlueBooAI : GhostAgent
    {
        enum S { Hide, InCupboard, Burst, DetectPlayer, Attack, Chase, FindHidingSpot, GoToCupboard, EnterCupboard }
        S s;
        float timer, repath;
        Cupboard cupboard;
        public override string DisplayName => "Blue Boo";
        protected override string ScareClip => "Boo_Scream";
        [Tooltip("Bursts out of its cupboard when the player comes this close.")]
        public float ambushDistance = 4f;

        protected override float StrugglePitch => 1.1f;
        // Restless: wobbles and swells, leans hard when it charges.
        protected override GhostStyle Style { get { var g = GhostStyle.Default; g.bobAmp = 0.1f; g.bobFreq = 0.45f; g.swayDeg = 5f; g.swayFreq = 0.7f; g.leanPerSpeed = 13f; g.breathe = 0.06f; return g; } }
        protected override Color TrailTint => new Color(0.55f, 0.75f, 1f);
        protected override float TrailDensity => 1f;
        protected override bool FightsBack => true;
        protected override Color CaptureTint => new Color(0.4f, 0.7f, 1f);

        protected override void OnSpawn()
        {
            Type = GhostType.BlueBoo; CaptureTime = 1.7f; recoverRate = 0.35f; Speed = gm.blueBooSpeed;
            s = S.Hide;
            // Start hidden in the cupboard it was spawned next to.
            var c = Cupboard.FindFree(logical, PlayerPos, 4f, 0f);
            if (c != null && c.Reserve(this)) GetInside(c);
        }

        void GetInside(Cupboard c)
        {
            cupboard = c;
            logical = c.Inside;
            ClearPath();
            c.Enter(this);
            c.Close();
            SetHiddenInside(true);
            s = S.InCupboard;
        }

        protected override void Think()
        {
            StateName = s.ToString();
            switch (s)
            {
                case S.Hide:
                    Turn(25f * Time.deltaTime); // slowly peeks around
                    if (CanSeePlayer(11f, 120f)) { s = S.DetectPlayer; timer = 1.3f; gm.ShowBanner("A Blue Boo spotted you!", 1.5f); JumpScare(); }
                    break;
                case S.InCupboard:
                    {
                        var p = gm.Player;
                        bool opened = cupboard.IsOpen;
                        bool close = p && !p.IsHidden && Vector3.Distance(p.transform.position, cupboard.FrontPoint) < ambushDistance
                                     && Mathf.Abs(p.transform.position.y - cupboard.FrontPoint.y) < 2f;
                        if (opened || close)
                        {
                            cupboard.Open(true);
                            cupboard.Leave(this);
                            SetHiddenInside(false);
                            JumpScare();
                            s = S.Burst;
                            gm.ShowBanner(opened ? "A Blue Boo was hiding in there!" : "A Blue Boo burst out of the cupboard!", 1.6f);
                        }
                        break;
                    }
                case S.Burst:
                    if (MoveDirect(cupboard.FrontPoint + Vector3.up * 0.15f, 5f)) { cupboard = null; s = S.DetectPlayer; timer = 1.1f; } // the scare plays out, a moment to react
                    break;
                case S.DetectPlayer:
                    Face(PlayerPos - logical);
                    timer -= Time.deltaTime;
                    if (timer <= 0f) { s = S.Attack; timer = 6f; repath = 0f; }
                    break;
                case S.Attack:
                    FollowPlayer(1.6f);
                    timer -= Time.deltaTime;
                    if (TryAttack()) { PickHidingSpot(); break; } // one good scare, then it slips away to hide again
                    if (timer <= 0f) { s = S.Chase; timer = 4f; }
                    break;
                case S.Chase:
                    FollowPlayer(1.3f);
                    timer -= Time.deltaTime;
                    if (TryAttack() || timer <= 0f) PickHidingSpot();
                    break;
                case S.FindHidingSpot:
                    if (MoveAlong(Speed * 1.2f)) s = S.Hide;
                    break;
                case S.GoToCupboard:
                    if (MoveAlong(Speed * 1.2f) || Near(cupboard.FrontPoint, 0.9f))
                    {
                        cupboard.Open();
                        s = S.EnterCupboard; timer = 0.35f;
                    }
                    break;
                case S.EnterCupboard:
                    timer -= Time.deltaTime;
                    if (timer > 0f) break; // wait for the doors to swing open
                    if (MoveDirect(cupboard.Inside, 2.5f)) GetInside(cupboard);
                    break;
            }
        }

        void FollowPlayer(float mul)
        {
            repath -= Time.deltaTime;
            if (repath <= 0f) { PathTo(PlayerPos); repath = 0.5f; }
            MoveAlong(Speed * mul);
            if (!HasPath) Face(PlayerPos - logical);
        }

        void PickHidingSpot()
        {
            var c = Cupboard.FindFree(logical, PlayerPos, 30f, 7f);
            if (c != null && c.Reserve(this) && PathTo(c.FrontPoint + Vector3.up * 0.15f))
            {
                cupboard = c;
                s = S.GoToCupboard;
                return;
            }
            if (c != null) c.Leave(this);
            int best = gm.Level.FindHidingCell(logical, 8f, 18f);
            if (best >= 0) PathTo(grid.PathPoint(best)); else ClearPath();
            s = S.FindHidingSpot;
        }

        /// <summary>Debug (F8): appears at a point and jump-scares the player straight away.</summary>
        public void DebugAmbush(Vector3 at)
        {
            if (cupboard) { cupboard.Leave(this); cupboard = null; }
            SetHiddenInside(false);
            WarpTo(at);
            s = S.DetectPlayer; timer = 1.4f;
            JumpScare();
        }

        protected override void OnHurt() => BoostSpeed(1.6f, 3f); // flinches and speeds up for a few seconds

        protected override void OnCaptured()
        {
            if (cupboard) cupboard.Leave(this);
        }
    }
}
