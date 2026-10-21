using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SheetHappens
{
    /// <summary>
    /// Title screen and in-game Settings.
    /// - Title screen (shown when the game starts, and after "Exit to Menu"): Start Game, Sound, Brightness, Quit.
    /// - Settings (Esc, or the Settings button): Resume, New Game, Exit to Menu, Sound on/off, Brightness.
    /// The game is paused while either is open. Sound and brightness are remembered between games.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        public enum Mode { None, Title, Settings }
        public static Mode Current { get; private set; } = Mode.None;
        public static bool IsOpen => Current != Mode.None;
        public static bool Muted { get; private set; }

        /// <summary>Show the title screen when the scene (re)loads. False after "New Game".</summary>
        static bool showTitleOnLoad = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { showTitleOnLoad = true; Current = Mode.None; }

        GUIStyle title, sub, button, label, small;
        Texture2D white;

        void Start()
        {
            try { Muted = PlayerPrefs.GetInt("SH_Muted", 0) == 1; } catch { Muted = false; }
            AudioListener.volume = Muted ? 0f : 1f;
            if (showTitleOnLoad) Open(Mode.Title);
            else Close();
        }

        void OnDestroy()
        {
            // leaving the scene: never stay paused
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Current = Mode.None;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var gm = GameManager.I;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (Current == Mode.Settings) Close();
                else if (Current == Mode.None && (gm == null || gm.State == GameState.Playing)) Open(Mode.Settings);
            }
            if (kb.mKey.wasPressedThisFrame) SetMuted(!Muted);
        }

        // ------------------------------------------------------------ actions

        public static void SetMuted(bool m)
        {
            Muted = m;
            AudioListener.volume = m ? 0f : 1f;
            try { PlayerPrefs.SetInt("SH_Muted", m ? 1 : 0); } catch { }
        }

        void Open(Mode m)
        {
            Current = m;
            Time.timeScale = 0f;
            AudioListener.pause = m == Mode.Settings; // the night keeps whispering behind the title screen
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Close()
        {
            Current = Mode.None;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void NewGame()
        {
            showTitleOnLoad = false;
            Reload();
        }

        static void ExitToMenu()
        {
            showTitleOnLoad = true;
            Reload();
        }

        static void Reload()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Current = Mode.None;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------ drawing

        void Styles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            title = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sub = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        void OnGUI()
        {
            GUI.depth = -10; // above the HUD
            Styles();
            float w = Screen.width, h = Screen.height;

            if (Current == Mode.None)
            {
                // Settings button (top right, under the controls hint). Press Esc while playing (the mouse is captured).
                var gm = GameManager.I;
                if (gm != null && gm.State == GameState.Playing)
                {
                    if (GUI.Button(new Rect(w - 170, 82, 150, 36), "Settings (Esc)", new GUIStyle(GUI.skin.button) { fontSize = 16 })) Open(Mode.Settings);
                }
                return;
            }

            GUI.color = new Color(0f, 0f, 0.02f, Current == Mode.Title ? 0.82f : 0.7f);
            GUI.DrawTexture(new Rect(0, 0, w, h), white);
            GUI.color = Color.white;

            float pw = 440f, x = (w - pw) / 2f, y;
            if (Current == Mode.Title)
            {
                Shadow(new Rect(0, h * 0.12f, w, 90), "SHEET HAPPENS", title, new Color(0.85f, 0.75f, 1f));
                Shadow(new Rect(w * 0.2f, h * 0.12f + 90, w * 0.6f, 50), "Keep the ghosts away from the attic bell until 6 AM.", sub, new Color(0.85f, 0.85f, 0.9f));
                y = h * 0.12f + 170;
                if (GUI.Button(new Rect(x, y, pw, 54), "Start Game", button)) Close();
                y += 70;
            }
            else
            {
                Shadow(new Rect(0, h * 0.12f, w, 70), "Settings", title, Color.white);
                y = h * 0.12f + 100;
                if (GUI.Button(new Rect(x, y, pw, 50), "Resume", button)) Close();
                y += 62;
                if (GUI.Button(new Rect(x, y, pw, 50), "New Game", button)) NewGame();
                y += 62;
                if (GUI.Button(new Rect(x, y, pw, 50), "Exit to Main Menu", button)) ExitToMenu();
                y += 74;
            }

            // Sound
            if (GUI.Button(new Rect(x, y, pw, 46), Muted ? "Sound: OFF  (M)" : "Sound: ON  (M)", button)) SetMuted(!Muted);
            y += 62;

            // Brightness
            Shadow(new Rect(x, y, pw, 30), $"Brightness: {NightLighting.Brightness * 100f:0}%   (L in game)", label, Color.white);
            y += 34;
            float b = GUI.HorizontalSlider(new Rect(x, y + 6, pw, 24), NightLighting.Brightness, 0.5f, 2.2f);
            if (!Mathf.Approximately(b, NightLighting.Brightness)) NightLighting.SetBrightness(b);
            y += 46;

            if (Current == Mode.Title)
            {
                if (GUI.Button(new Rect(x, y, pw, 46), "Quit", button)) Quit();
                y += 64;
                Shadow(new Rect(x - 60, y, pw + 120, 80),
                    "WASD move · Mouse look · Hold left-click to suck up ghosts · E doors, cupboards, ladders\nC crouch / hide · Shift run · Esc settings · L brightness · M mute",
                    small, new Color(0.8f, 0.8f, 0.85f));
            }
        }

        void Shadow(Rect r, string text, GUIStyle s, Color c)
        {
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
            GUI.color = c;
            GUI.Label(r, text, s);
            GUI.color = Color.white;
        }
    }
}
