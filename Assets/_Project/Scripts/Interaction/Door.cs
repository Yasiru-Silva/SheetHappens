using UnityEngine;

namespace SheetHappens
{
    /// <summary>A door that swings open/closed around its edge (E). Ghosts float through doors.
    /// A locked door (the front door) only rattles in its frame.</summary>
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
        public void Toggle()
        {
            if (!ready) return;
            if (locked) { Rattle(); return; }
            target = IsOpen ? 0f : -openAngle * (reverseSwing ? -1f : 1f);
        }

        [Tooltip("Locked doors don't open (the front door: there's no escaping the house tonight).")]
        public bool locked;
        float rattleUntil, nextRattle;

        void Rattle()
        {
            if (Time.time < nextRattle) return;
            nextRattle = Time.time + 0.9f;
            rattleUntil = Time.time + 0.6f;
            Sfx.Play("Door_Locked", transform.position, 0.9f, 0.6f, Random.Range(0.95f, 1.05f));
            var gm = GameManager.I;
            if (gm) gm.ShowBanner("The front door is locked. There's no way out tonight...", 2f);
        }

        void Update()
        {
            // a locked door jiggles a few degrees in its frame
            float shake = Time.time < rattleUntil ? Mathf.Sin(Time.time * 55f) * 1.2f * (rattleUntil - Time.time) / 0.6f : 0f;
            float want = target + shake;
            if (!ready || Mathf.Approximately(angle, want)) return;
            if (shake != 0f || locked)
            {
                transform.RotateAround(hinge, Vector3.up, want - angle);
                angle = want;
                return;
            }
            float next = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            transform.RotateAround(hinge, Vector3.up, next - angle);
            angle = next;
        }
    }
}
