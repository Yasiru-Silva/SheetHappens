using UnityEngine;

namespace SheetHappens
{
    /// <summary>A door that swings open/closed around its edge (E). Ghosts float through doors.</summary>
    public class Door : MonoBehaviour
    {
        public float openAngle = 95f;
        public float speed = 240f;
        public bool IsOpen => target != 0f;

        Vector3 hinge;
        float angle, target;
        bool ready;

        void Start()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            hinge = b.size.x >= b.size.z ? new Vector3(b.min.x, b.center.y, b.center.z) : new Vector3(b.center.x, b.center.y, b.min.z);
            ready = true;
        }

        [Tooltip("Swing the door the other way (into the other room).")]
        public bool reverseSwing;

        // The house's doors swing into the rooms when the opening angle is negative around their hinge.
        public void Toggle() { if (ready) target = IsOpen ? 0f : -openAngle * (reverseSwing ? -1f : 1f); }

        void Update()
        {
            if (!ready || Mathf.Approximately(angle, target)) return;
            float next = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            transform.RotateAround(hinge, Vector3.up, next - angle);
            angle = next;
        }
    }
}
