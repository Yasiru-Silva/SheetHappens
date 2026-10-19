using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>Testing shortcuts: F3 opens/closes every cupboard, F4 jumps the player in front of the next cupboard, F5 spawns a Blue Boo,
    /// F7 puts a Wisp right in front of the player (to try the vacuum).</summary>
    public class DebugKeys : MonoBehaviour
    {
        int next, nextOwn;
        bool open;

        void Update()
        {
            var kb = Keyboard.current;
            var gm = GameManager.I;
            if (kb == null || gm == null || !gm.Ready) return;
            if (kb.f3Key.wasPressedThisFrame)
            {
                open = !open;
                foreach (var c in Cupboard.All) if (open) c.Open(); else c.Close();
            }
            if (kb.f6Key.wasPressedThisFrame && gm.Player)
            {
                // Jump next to the next piece of our own furniture (bed, dining table, pantry).
                gm.Level.OwnPieces.RemoveAll(x => x == null);
                var list = gm.Level.OwnPieces;
                if (list.Count > 0)
                {
                    var go = list[nextOwn++ % list.Count];
                    var rs = go.GetComponentsInChildren<Renderer>();
                    Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        Vector3 p = b.center + dir * (Mathf.Abs(Vector3.Dot(b.extents, dir)) + 2.5f);
                        if (!Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var h, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.CheckCapsule(h.point + Vector3.up * 0.4f, h.point + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        gm.Player.Teleport(h.point + Vector3.up * 0.05f);
                        gm.Player.transform.rotation = Quaternion.LookRotation(-dir);
                        gm.ShowBanner("Debug: " + go.name, 2f);
                        break;
                    }
                }
            }
            if (kb.f5Key.wasPressedThisFrame) { gm.TrySpawn(GhostType.BlueBoo); gm.ShowBanner("Debug: a Blue Boo is hiding in a cupboard", 2f); }
            if (kb.f7Key.wasPressedThisFrame && gm.Player)
            {
                var cam = Camera.main ? Camera.main.transform : gm.Player.transform;
                Vector3 fwd = cam.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 at = gm.Player.transform.position + fwd * 3.5f;
                foreach (float dist in new[] { 3.5f, 2.5f, 1.8f })
                {
                    Vector3 c = gm.Player.transform.position + fwd * dist;
                    if (!Physics.Linecast(gm.Player.EyePosition, c + Vector3.up * 0.6f, ~0, QueryTriggerInteraction.Ignore)) { at = c; break; }
                }
                if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out var fh, 4f, ~0, QueryTriggerInteraction.Ignore)) at = fh.point;
                var g = gm.TrySpawn(GhostType.Wisp);
                if (g) { g.WarpTo(at); gm.Player.LookAt(at + Vector3.up * 0.6f); gm.ShowBanner("Debug: a Wisp appeared in front of you", 2f); }
            }
            if (kb.f4Key.wasPressedThisFrame && Cupboard.All.Count > 0 && gm.Player)
            {
                var c = Cupboard.All[next++ % Cupboard.All.Count];
                Vector3 at = c.FrontPoint + c.Forward * 1.5f;
                gm.Player.Teleport(at);
                gm.Player.transform.rotation = Quaternion.LookRotation(-c.Forward);
            }
        }
    }
}
