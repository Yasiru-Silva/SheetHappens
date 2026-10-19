using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>First-person movement: arrow keys (or WASD), mouse look, Shift to run, C/Ctrl to crouch and hide.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Transform cameraRoot;
        [Tooltip("Move the player to a good start point on the ground floor when the game starts.")]
        public bool autoPlace = true;
        public float walkSpeed = 3.2f, runSpeed = 5.5f, crouchSpeed = 1.6f;
        public float lookSensitivity = 0.12f;
        public float gravity = -20f;
        public float standHeight = 1.75f, crouchHeight = 0.95f;

        public float climbSpeed = 2.2f;

        public bool Crouching { get; private set; }
        public bool OnLadder { get; private set; }
        public bool IsHidden => Crouching && HideSpot.Contains(transform.position);
        public Vector3 EyePosition => cameraRoot ? cameraRoot.position : transform.position + Vector3.up * 1.6f;

        CharacterController cc;
        float pitch, vy, shakeUntil, shakeAmount, ladderGraceUntil;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (!cameraRoot) { var cam = GetComponentInChildren<Camera>(); if (cam) cameraRoot = cam.transform; }
        }

        void Start() => LockCursor(true);

        public void Teleport(Vector3 feet)
        {
            cc.enabled = false;
            transform.position = feet;
            cc.enabled = true;
            vy = 0f;
        }

        public void Shake(float seconds, float amount)
        {
            shakeUntil = Time.time + seconds;
            shakeAmount = amount;
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm != null && gm.State != GameState.Playing) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, d.x, 0f);
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            }

            bool wantCrouch = kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
            if (!wantCrouch && Crouching)
            {
                // Only stand up if there is headroom.
                Vector3 bottom = transform.position + Vector3.up * (cc.radius + 0.05f);
                if (Physics.SphereCast(bottom, cc.radius * 0.9f, Vector3.up, out _, standHeight - cc.radius, ~0, QueryTriggerInteraction.Ignore)) wantCrouch = true;
            }
            Crouching = wantCrouch;
            cc.height = Mathf.MoveTowards(cc.height, Crouching ? crouchHeight : standHeight, Time.deltaTime * 4f);
            cc.center = Vector3.up * cc.height * 0.5f;

            Vector2 mv = Vector2.zero;
            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) mv.y += 1f;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) mv.y -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) mv.x += 1f;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) mv.x -= 1f;
            float speed = Crouching ? crouchSpeed : (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed);
            Vector3 move = transform.forward * mv.y + transform.right * mv.x;
            if (move.sqrMagnitude > 1f) move.Normalize();
            move *= speed;

            // Ladder: Forward climbs up, Back climbs down; at the top, step left/right onto the floor.
            var ladder = Ladder.At(transform.position + Vector3.up * 0.1f);
            OnLadder = ladder != null;
            if (OnLadder)
            {
                vy = 0f;
                float up = mv.y * climbSpeed;
                if (up > 0f && transform.position.y > ladder.TopY - 0.05f) up = 0f; // top reached: step off sideways
                if (transform.position.y > ladder.TopY - 0.6f) ladderGraceUntil = Time.time + 0.5f; // don't drop back through the hatch while stepping off
                move = transform.right * mv.x * walkSpeed * 0.6f + transform.forward * Mathf.Min(0f, mv.y) * walkSpeed * 0.5f;
                if (Mathf.Abs(mv.x) < 0.1f && mv.y >= 0f)
                {
                    // keep the body centred on the ladder so it fits up through the hatch
                    Vector3 toLane = ladder.LaneCenter - transform.position; toLane.y = 0f;
                    move += Vector3.ClampMagnitude(toLane * 6f, 2.5f);
                }
                move.y = up;
                cc.Move(move * Time.deltaTime);
            }
            else
            {
                if (cc.isGrounded && vy < 0f) vy = -2f;
                if (Time.time < ladderGraceUntil && !cc.isGrounded) vy = 0f;
                else vy += gravity * Time.deltaTime;
                move.y = vy;
                cc.Move(move * Time.deltaTime);
            }

            if (transform.position.y < -60f && gm != null && gm.Level != null) gm.Level.RespawnPlayer();

            if (cameraRoot)
            {
                Vector3 shake = Time.time < shakeUntil ? Random.insideUnitSphere * shakeAmount : Vector3.zero;
                cameraRoot.localPosition = new Vector3(0f, cc.height - 0.15f, 0f) + shake;
                cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Push barricades (sofas, trunks, baskets) by walking into them.
            var rb = hit.collider.attachedRigidbody;
            if (rb == null || rb.isKinematic || hit.moveDirection.y < -0.3f) return;
            Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            rb.linearVelocity = new Vector3(push.x * 1.6f, rb.linearVelocity.y, push.z * 1.6f);
        }
    }
}
