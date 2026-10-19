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
        protected override Color CaptureTint => new Color(1f, 0.85f, 0.3f);

        protected override void OnSpawn()
        {
            Type = GhostType.Wisp; CaptureTime = 0.9f; recoverRate = 0.6f; Speed = gm.wispSpeed;
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
                    visualOffset = transform.right * Mathf.Sin(Time.time * 5f) * 0.18f; // small zig-zag, harder to aim at
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
                    transform.Rotate(0f, 25f * Time.deltaTime, 0f); // slowly peeks around
                    if (CanSeePlayer(11f, 120f)) { s = S.DetectPlayer; timer = 0.7f; gm.ShowBanner("A Blue Boo spotted you!", 1.5f); Scare(); }
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
                            Scare();
                            s = S.Burst;
                            gm.ShowBanner(opened ? "A Blue Boo was hiding in there!" : "A Blue Boo burst out of the cupboard!", 1.6f);
                        }
                        break;
                    }
                case S.Burst:
                    if (MoveDirect(cupboard.FrontPoint + Vector3.up * 0.15f, 5f)) { cupboard = null; s = S.DetectPlayer; timer = 1.0f; } // a moment to react
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

        protected override void OnHurt() => BoostSpeed(1.6f, 3f); // flinches and speeds up for a few seconds

        protected override void OnCaptured()
        {
            if (cupboard) cupboard.Leave(this);
        }
    }

    /// <summary>Green Sneak - The Infiltrator. Takes routes away from the player to the bell; retaliates only when shot.</summary>
    public class GreenSneakAI : GhostAgent
    {
        enum S { HideObserve, ChooseSafeRoute, MoveToBell, Retaliate, RingBell }
        S s;
        float timer, replan;
        Vector3 avoidPos;
        public override string DisplayName => "Green Sneak";

        protected override float StrugglePitch => 0.95f;
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
            s = S.DetectPlayer; timer = 0.5f; lastSeen = PlayerPos;
            gm.ShowBanner("Grandpa Ghost has seen you!", 2f);
            Scare();
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
