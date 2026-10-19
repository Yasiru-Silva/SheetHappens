using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>Testing shortcuts: F3 opens/closes every cupboard, F4 jumps the player in front of the next cupboard, F5 spawns a Blue Boo.</summary>
    public class DebugKeys : MonoBehaviour
    {
        int next;
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
            if (kb.f5Key.wasPressedThisFrame) { gm.Level.SpawnGhost(GhostType.BlueBoo); gm.ShowBanner("Debug: a Blue Boo is hiding in a cupboard", 2f); }
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
