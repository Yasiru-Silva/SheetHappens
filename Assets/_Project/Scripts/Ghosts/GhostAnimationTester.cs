using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens.Ghosts
{
    /// <summary>
    /// Play-mode preview tool for the S4_Agents scene. Drop it on any object, assign a ghost
    /// and/or the vacuum, press Play:
    ///   Number keys 1-9 : fire the ghost's triggers (listed on screen)
    ///   Arrow keys      : move the ghost (shows Move) / set vacuum speed (Shift = run)
    ///   V + 1..4        : vacuum Shoot / SwitchMode / Collect / Jam toggle
    /// Remove it before the final build; it is only a testing aid.
    /// </summary>
    public class GhostAnimationTester : MonoBehaviour
    {
        public GhostAnimatorDriver ghost;
        public VacuumAnimator vacuum;
        public float ghostSpeed = 2.5f;
        public float vacuumWalkSpeed = 2.5f;
        public float vacuumRunSpeed = 6f;

        bool _special;
        bool _jammed;

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            float h = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
            float v = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
            bool vacuumKeys = kb.vKey.isPressed;

            if (ghost != null)
            {
                Vector3 move = new Vector3(h, 0f, v);
                if (move.sqrMagnitude > 1f) move.Normalize();
                ghost.transform.position += move * (ghostSpeed * Time.deltaTime);
                if (move.sqrMagnitude > 0.01f)
                {
                    Quaternion face = Quaternion.LookRotation(move, Vector3.up);
                    ghost.transform.rotation = Quaternion.Slerp(ghost.transform.rotation, face, 1f - Mathf.Exp(-8f * Time.deltaTime));
                }

                if (!vacuumKeys)
                {
                    for (int i = 0; i < 9 && i < ghost.Triggers.Count; i++)
                    {
                        if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) ghost.Play(ghost.Triggers[i]);
                    }
                }
            }

            if (vacuum != null)
            {
                bool moving = Mathf.Abs(h) + Mathf.Abs(v) > 0.01f;
                bool run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                vacuum.SetMoveSpeed(moving ? (run ? vacuumRunSpeed : vacuumWalkSpeed) : 0f);

                if (vacuumKeys)
                {
                    if (kb.digit1Key.wasPressedThisFrame) vacuum.Shoot();
                    if (kb.digit2Key.wasPressedThisFrame) { _special = !_special; vacuum.SwitchMode(_special); }
                    if (kb.digit3Key.wasPressedThisFrame) vacuum.Collect();
                    if (kb.digit4Key.wasPressedThisFrame) { _jammed = !_jammed; vacuum.SetJammed(_jammed); }
                }
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 360, 400));
            GUILayout.Label("Animation tester (arrow keys move)");
            if (ghost != null)
            {
                for (int i = 0; i < 9 && i < ghost.Triggers.Count; i++)
                    GUILayout.Label($"  [{i + 1}] {ghost.Triggers[i]}");
            }
            if (vacuum != null)
                GUILayout.Label("  Vacuum: hold V + [1] Shoot  [2] Mode  [3] Collect  [4] Jam   Shift = run");
            GUILayout.EndArea();
        }
    }
}
