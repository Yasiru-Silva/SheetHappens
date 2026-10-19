using UnityEngine;

namespace SheetHappens.Ghosts
{
    /// <summary>
    /// Single source of truth for Animator parameter names.
    /// The animation builder and the runtime scripts both use these, so a typo cannot desync them.
    /// </summary>
    public static class GhostAnimParams
    {
        // Shared by ghosts and the vacuum
        public const string Speed = "Speed";
        public const string Spawn = "Spawn";
        public const string Caught = "Caught";
        public const string RingBell = "RingBell";

        // Ghost triggers (each ghost only owns a subset)
        public const string Hit = "Hit";
        public const string Boo = "Boo";
        public const string Peek = "Peek";
        public const string Dodge = "Dodge";
        public const string Startled = "Startled";
        public const string Freeze = "Freeze";
        public const string Search = "Search";
        public const string AngryTurn = "AngryTurn";
        public const string ShieldShatter = "ShieldShatter";

        // Vacuum
        public const string Shoot = "Shoot";
        public const string SwitchMode = "SwitchMode";
        public const string Collect = "Collect";
        public const string Jammed = "Jammed"; // bool

        /// <summary>Speed (m/s) above which the Move state plays.</summary>
        public const float MoveThreshold = 0.1f;

        /// <summary>Vacuum: Speed (m/s) at which Walk turns into Run.</summary>
        public const float RunThreshold = 4.5f;

        public static readonly int SpeedHash = Animator.StringToHash(Speed);
    }
}
