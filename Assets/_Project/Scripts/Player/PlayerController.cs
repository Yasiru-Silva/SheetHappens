using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>First-person movement: arrow keys (or WASD), mouse look, Shift to run, C/Ctrl to crouch and hide,
    /// E at a ladder to climb it up or down. The night starts with the player sitting in a chair (W or E to get up).</summary>
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

        // ---- E to climb a ladder
        /// <summary>"E: climb up the ladder" etc. when standing at one end of a ladder (null otherwise).</summary>
        public string LadderPrompt { get; private set; }
        /// <summary>Climbing a ladder by itself after pressing E.</summary>
        public bool AutoClimbing => climbLadder != null;
        Ladder nearLadder, climbLadder;
        bool nearGoesUp, climbUp, hasLanding;
        int climbPhase;
        float climbTimer, climbLastY, climbBob;
        Vector3 landing;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (!cameraRoot) { var cam = GetComponentInChildren<Camera>(); if (cam) cameraRoot = cam.transform; }
        }

        void Start() => LockCursor(true);

        public void Teleport(Vector3 feet)
        {
            seated = false; standT = -1f;
            cc.enabled = false;
            transform.position = feet;
            cc.enabled = true;
            vy = 0f;
        }

        /// <summary>Turns the view to look at a point (used by the debug keys).</summary>
        public void LookAt(Vector3 point)
        {
            Vector3 d = point - EyePosition;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            if (flat.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(flat);
            pitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg, -85f, 85f);
            if (cameraRoot) cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
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

            if (GameMenu.IsOpen) return; // paused: the menu has the mouse
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, d.x, 0f);
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            }

            if (seated || standT >= 0f) { SeatedUpdate(kb); UpdateCamera(); return; }

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

            FindNearLadder();

            // Ladder: Forward climbs up, Back climbs down; at the top, step left/right onto the floor.
            var ladder = Ladder.At(transform.position + Vector3.up * 0.1f);
            OnLadder = ladder != null || AutoClimbing;
            if (AutoClimbing) AutoClimbStep();
            else if (ladder != null)
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
            UpdateCamera();
        }

        void UpdateCamera()
        {
            if (cameraRoot)
            {
                Vector3 shake = Time.time < shakeUntil ? Random.insideUnitSphere * shakeAmount : Vector3.zero;
                // hand-over-hand bob while climbing
                Vector3 climbSway = AutoClimbing && climbPhase == 1 ? new Vector3(Mathf.Sin(climbBob * 0.5f) * 0.02f, Mathf.Abs(Mathf.Sin(climbBob)) * 0.035f, 0f) : Vector3.zero;
                cameraRoot.localPosition = new Vector3(0f, cc.height - 0.15f, 0f) + shake + climbSway;
                cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        // ------------------------------------------------------------ sitting in a chair

        /// <summary>Sitting down (the start of the night). Any movement key or E gets up.</summary>
        public bool Seated => seated;
        public bool SittingOrRising => seated || standT >= 0f;
        bool seated;
        float standT = -1f, seatedEyeDrop;
        Vector3 seatFeet, standFeet;

        /// <summary>Sits the player on a chair: 'seat' is the top of the seat cushion, 'facing' the way the chair faces,
        /// 'stand' a clear floor point in front of it to get up onto.</summary>
        public void SitAt(Vector3 seat, Vector3 facing, Vector3 stand)
        {
            cc.enabled = false;
            seated = true; standT = -1f;
            Crouching = false;
            cc.height = standHeight; cc.center = Vector3.up * cc.height * 0.5f;
            // The camera sits standHeight-0.15 above the "feet": put the eyes ~0.75 m above the cushion.
            float eye = standHeight - 0.15f;
            seatFeet = seat + Vector3.up * 0.75f - Vector3.up * eye;
            standFeet = stand;
            transform.position = seatFeet;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(facing.normalized);
            pitch = 8f;
            OnLadder = false;
        }

        void SeatedUpdate(Keyboard kb)
        {
            vy = 0f;
            if (seated)
            {
                bool getUp = kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame
                             || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame
                             || kb.rightArrowKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                if (getUp) { seated = false; standT = 0f; }
                transform.position = seatFeet;
                return;
            }
            // Getting up: lean forward and rise onto the floor in front of the chair.
            standT += Time.deltaTime / 0.8f;
            float k = Mathf.Clamp01(standT);
            float e = k * k * (3f - 2f * k);
            Vector3 p = Vector3.Lerp(seatFeet, standFeet, e);
            p.y = Mathf.Lerp(seatFeet.y, standFeet.y, Mathf.Clamp01(e * 1.3f)) + Mathf.Sin(k * Mathf.PI) * 0.06f;
            transform.position = p;
            if (k >= 1f)
            {
                standT = -1f;
                transform.position = standFeet;
                cc.enabled = true;
            }
        }

        // ------------------------------------------------------------ E to climb

        /// <summary>Is the player standing at the bottom or the top of a ladder?</summary>
        void FindNearLadder()
        {
            nearLadder = null;
            LadderPrompt = null;
            if (AutoClimbing) { LadderPrompt = "E: let go of the ladder"; return; }
            Vector3 pos = transform.position;
            float best = float.MaxValue;
            foreach (var l in Ladder.All)
            {
                if (l == null) continue;
                Vector3 c = l.LaneCenter;
                float d = new Vector2(pos.x - c.x, pos.z - c.z).magnitude;
                if (d > 1.7f || d > best) continue;
                bool atTop = pos.y > l.TopY - 0.7f && pos.y < l.TopY + 1.2f;
                bool atBottom = !atTop && pos.y > l.BottomY - 0.8f && pos.y < l.TopY - 0.7f;
                if (!atTop && !atBottom) continue;
                best = d; nearLadder = l; nearGoesUp = atBottom;
            }
            if (nearLadder) LadderPrompt = nearGoesUp ? "E: climb up the ladder" : "E: climb down the ladder";
        }

        /// <summary>E pressed: climb the nearby ladder all the way (or let go if already climbing).</summary>
        public bool UseLadder()
        {
            if (AutoClimbing) { climbLadder = null; return true; }
            if (nearLadder == null) return false;
            climbLadder = nearLadder;
            climbUp = nearGoesUp;
            climbPhase = 0;
            climbTimer = 0f;
            Crouching = false;
            return true;
        }

        void AutoClimbStep()
        {
            float dt = Time.deltaTime;
            vy = 0f;
            ladderGraceUntil = Time.time + 0.5f;
            climbTimer += dt;
            Vector3 pos = transform.position;
            Vector3 toLane = climbLadder.LaneCenter - pos; toLane.y = 0f;
            Vector3 centre = Vector3.ClampMagnitude(toLane * 6f, 2.5f);
            switch (climbPhase)
            {
                case 0: // step onto the ladder
                    cc.Move(centre * dt);
                    if (toLane.magnitude < 0.12f || climbTimer > 1.2f) { climbPhase = 1; climbTimer = 0f; climbLastY = pos.y; }
                    break;
                case 1: // climb
                    {
                        float targetY = climbUp ? climbLadder.TopY + 0.02f : climbLadder.BottomY;
                        Vector3 m = centre;
                        m.y = Mathf.Sign(targetY - pos.y) * climbSpeed;
                        cc.Move(m * dt);
                        climbBob += dt * climbSpeed * 4f;
                        float y = transform.position.y;
                        bool done = climbUp ? y >= climbLadder.TopY - 0.03f
                                            : y <= targetY + 0.03f || (cc.isGrounded && y < climbLadder.TopY - 1f);
                        if (Mathf.Abs(y - climbLastY) > 0.05f) { climbLastY = y; climbTimer = 0f; }
                        else if (climbTimer > 0.6f) done = true; // can't go further (reached the floor or something is in the way)
                        if (done)
                        {
                            if (climbUp && FindLanding()) { climbPhase = 2; climbTimer = 0f; }
                            else climbLadder = null;
                        }
                        break;
                    }
                case 2: // step off at the top onto the floor
                    {
                        Vector3 to = landing - pos; to.y = 0f;
                        Vector3 m = Vector3.ClampMagnitude(to * 5f, 2.2f);
                        m.y = landing.y > pos.y + 0.02f ? 1.5f : 0f;
                        cc.Move(m * dt);
                        if (to.magnitude < 0.15f || climbTimer > 1.5f) climbLadder = null;
                        break;
                    }
            }
        }

        /// <summary>A clear spot on the floor next to the top of the ladder (preferring the way the player faces).</summary>
        bool FindLanding()
        {
            Vector3 lane = climbLadder.LaneCenter;
            float top = climbLadder.TopY;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float[] angles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * fwd;
                foreach (float d in new[] { 0.9f, 1.3f, 1.7f })
                {
                    Vector3 p = lane + dir * d;
                    if (!Physics.Raycast(new Vector3(p.x, top + 1.5f, p.z), Vector3.down, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.point.y - top) > 0.6f) continue; // fell through the hatch or hit something tall
                    Vector3 lo = hit.point + Vector3.up * (cc.radius + 0.08f), hi = hit.point + Vector3.up * (standHeight - cc.radius);
                    if (Physics.CheckCapsule(lo, hi, cc.radius * 0.9f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    landing = hit.point;
                    return hasLanding = true;
                }
            }
            return hasLanding = false;
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
