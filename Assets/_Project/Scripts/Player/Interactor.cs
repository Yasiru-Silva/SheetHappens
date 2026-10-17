using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>E climbs ladders, opens/closes doors and picks up objects; right-click throws the held object.</summary>
    public class Interactor : MonoBehaviour
    {
        public float reach = 3.2f;
        public float throwSpeed = 11f;
        public string Prompt { get; private set; }

        Camera cam;
        Throwable held;
        PlayerController player;

        void Awake() => cam = GetComponentInChildren<Camera>();

        void Update()
        {
            Prompt = null;
            var gm = GameManager.I;
            if (gm != null && gm.State != GameState.Playing) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null || cam == null) return;
            bool e = kb.eKey.wasPressedThisFrame;

            if (held)
            {
                Prompt = "Right-click: throw    E: drop";
                held.transform.position = cam.transform.position + cam.transform.forward * 0.8f - cam.transform.up * 0.25f + cam.transform.right * 0.15f;
                held.transform.rotation = cam.transform.rotation;
                if (mouse.rightButton.wasPressedThisFrame) Release(cam.transform.forward * throwSpeed, true);
                else if (e) Release(Vector3.zero, false);
                return;
            }

            if (player == null) player = GetComponent<PlayerController>();
            // Sitting in the chair at the start: W or E gets up (PlayerController handles it).
            if (player && player.SittingOrRising) { Prompt = player.Seated ? "W or E: get up" : null; return; }
            // At a ladder, E climbs it (up or down) before anything else.
            if (player && player.LadderPrompt != null)
            {
                Prompt = player.LadderPrompt;
                if (e) player.UseLadder();
                return;
            }

            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)) return;
            var door = hit.collider.GetComponentInParent<Door>();
            if (door)
            {
                Prompt = door.locked ? "E: try the front door (locked)" : door.IsOpen ? "E: close door" : "E: open door";
                if (e) door.Toggle();
                return;
            }
            var cupboard = hit.collider.GetComponentInParent<Cupboard>();
            if (cupboard && cupboard.enabled)
            {
                Prompt = cupboard.IsOpen ? "E: close cupboard" : "E: open cupboard";
                if (e) cupboard.Toggle();
                return;
            }
            var t = hit.collider.GetComponentInParent<Throwable>();
            if (t)
            {
                Prompt = "E: pick up";
                if (e) Grab(t);
            }
        }

        void Grab(Throwable t)
        {
            held = t;
            t.Rb.isKinematic = true;
            foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void Release(Vector3 velocity, bool thrown)
        {
            var t = held; held = null;
            foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = true;
            t.Rb.isKinematic = false;
            t.Rb.linearVelocity = velocity;
            if (thrown) t.MarkThrown();
        }
    }
}
