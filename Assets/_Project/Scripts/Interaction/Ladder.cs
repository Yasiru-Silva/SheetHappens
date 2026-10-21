using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Climbable ladder zone (a trigger box in front of the ladder). Near either end, E climbs the whole ladder
    /// automatically (PlayerController.StartAutoClimb). Inside it, Forward/Back also climb by hand.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Ladder : MonoBehaviour
    {
        public static readonly List<Ladder> All = new List<Ladder>();
        BoxCollider box;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public float TopY => box.bounds.max.y;
        public float BottomY => box.bounds.min.y;
        /// <summary>Centre of the climbing lane (the player is kept here while climbing so they fit through the hatch).</summary>
        public Vector3 LaneCenter => box.bounds.center;

        /// <summary>The ladder whose zone contains this point (the player's body centre), or null.</summary>
        public static Ladder At(Vector3 p)
        {
            foreach (var l in All)
                if (l.box != null && l.box.bounds.Contains(p)) return l;
            return null;
        }
    }
}
