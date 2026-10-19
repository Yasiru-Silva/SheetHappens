using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>Simple on-screen HUD: clock, hearts, vacuum power, jam and hide state, prompts, banners, win/lose screen. F1 shows ghost AI states.</summary>
    public class GameHUD : MonoBehaviour
    {
        GUIStyle big, mid, small, center;
        bool showDebug;
        Texture2D white;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) showDebug = !showDebug;
        }

        void Styles()
        {
            if (big != null) return;
            white = Texture2D.whiteTexture;
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            center = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        void Shadowed(Rect r, string text, GUIStyle s, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
            GUI.color = c;
            GUI.Label(r, text, s);
            GUI.color = old;
        }

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            Styles();
            float w = Screen.width, h = Screen.height;

            if (gm.State != GameState.Playing)
            {
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(0, 0, w, h), white);
                GUI.color = Color.white;
                bool won = gm.State == GameState.Won;
                Shadowed(new Rect(0, h * 0.3f, w, 60), won ? "YOU WIN!" : "GAME OVER", big, won ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.4f));
                Shadowed(new Rect(w * 0.15f, h * 0.3f + 70, w * 0.7f, 60), gm.EndReason, center, Color.white);
                Shadowed(new Rect(0, h * 0.3f + 140, w, 30), "Press R to play again", center, new Color(0.8f, 0.8f, 0.8f));
                return;
            }

            // Crosshair
            Shadowed(new Rect(w / 2 - 10, h / 2 - 14, 20, 28), "+", center, Color.white);

            // Top-left: clock, wave, hearts
            Shadowed(new Rect(20, 16, 400, 30), $"{gm.ClockText}   Wave {gm.Wave}", mid, Color.white);
            string hearts = new string('♥', gm.Hearts) + new string('♡', Mathf.Max(0, gm.maxHearts - gm.Hearts));
            Shadowed(new Rect(20, 46, 400, 30), "Hearts  " + hearts, mid, new Color(1f, 0.45f, 0.5f));

            // Vacuum
            var v = gm.Vacuum;
            if (v)
            {
                int toNext = v.Level == 1 ? 5 - v.Energy : v.Level == 2 ? 10 - v.Energy : 0;
                Shadowed(new Rect(20, h - 70, 500, 26), $"Vacuum power: Level {v.Level}" + (toNext > 0 ? $"  ({toNext} energy balls to next level)" : "  (max)"), small, new Color(0.85f, 0.65f, 1f));
                GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(new Rect(20, h - 42, 200, 12), white);
                GUI.color = new Color(0.65f, 0.3f, 1f); GUI.DrawTexture(new Rect(20, h - 42, 200 * Mathf.Clamp01(v.Energy / 10f), 12), white);
                GUI.color = Color.white;
                if (v.Jammed) Shadowed(new Rect(0, h * 0.62f, w, 30), $"VACUUM JAMMED  {v.JamRemaining:0.0}s", center, new Color(1f, 0.4f, 0.3f));
            }

            var p = gm.Player;
            if (p && p.IsHidden) Shadowed(new Rect(w - 220, h - 70, 200, 26), "Hidden", mid, new Color(0.6f, 1f, 0.6f));
            else if (p && p.Crouching) Shadowed(new Rect(w - 220, h - 70, 200, 26), "Crouching", small, Color.white);

            if (p && p.OnLadder) Shadowed(new Rect(0, h / 2 + 56, w, 26), "Ladder: Forward climbs up, Back climbs down, step sideways at the top", center, new Color(0.9f, 1f, 0.9f));

            var it = p ? p.GetComponent<Interactor>() : null;
            if (it && !string.IsNullOrEmpty(it.Prompt)) Shadowed(new Rect(0, h / 2 + 30, w, 26), it.Prompt, center, Color.white);

            // Bell danger warning
            if (gm.Bell)
            {
                foreach (var g in gm.Ghosts)
                {
                    if (g == null || (g.Type != GhostType.Wisp && g.Type != GhostType.GreenSneak)) continue;
                    if ((g.Logical - gm.Bell.RingPoint).sqrMagnitude < 64f && Mathf.Repeat(Time.time, 0.6f) < 0.4f)
                    {
                        Shadowed(new Rect(0, 90, w, 30), "A ghost is near the attic bell!", center, new Color(1f, 0.3f, 0.3f));
                        break;
                    }
                }
            }

            if (gm.BannerVisible) Shadowed(new Rect(w * 0.1f, 130, w * 0.8f, 60), gm.Banner, center, new Color(1f, 0.95f, 0.8f));

            if (!gm.Ready) Shadowed(new Rect(0, h * 0.4f, w, 30), "Preparing the house...", center, Color.white);
            if (Cursor.lockState != CursorLockMode.Locked) Shadowed(new Rect(0, h - 36, w, 26), "Click to play   (Esc frees the mouse)", center, new Color(1, 1, 1, 0.8f));
            Shadowed(new Rect(w - 420, 16, 400, 60), "Arrows/WASD move · Mouse aim · Left-click shoot\nC crouch/hide · E doors & pick up · Right-click throw · Shift run", small, new Color(1, 1, 1, 0.7f));

            if (showDebug) DrawDebug(gm);
        }

        void DrawDebug(GameManager gm)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var g in gm.Ghosts)
            {
                if (g == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(g.Eye + Vector3.up * 0.8f);
                if (sp.z < 0) continue;
                Shadowed(new Rect(sp.x - 110, Screen.height - sp.y, 220, 22), $"{g.DisplayName}: {g.StateName} ({g.MaxHits - g.HitsTaken} hp)", center, Color.yellow);
            }
        }
    }
}
