using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens.Ghosts
{
    /// <summary>
    /// Bridge between the AI / movement code and the ghost Animator.
    /// The AI decides what to do, this component only reports it to the Animator:
    ///   AI action -> path movement -> GhostAnimatorDriver -> Animator state.
    /// It never moves the ghost, so animation cannot pull a ghost through a wall.
    /// </summary>
    [DisallowMultipleComponent]
    public class GhostAnimatorDriver : MonoBehaviour
    {
        public Animator animator;

        [Tooltip("Derive the Speed parameter from how far this transform moved. Turn off to call SetSpeed yourself.")]
        public bool measureSpeedFromMovement = true;

        [Tooltip("Higher = the Speed value follows the real speed faster.")]
        public float speedSmoothing = 10f;

        float _speed;
        float _targetSpeed;
        Vector3 _lastPosition;
        readonly HashSet<int> _parameters = new HashSet<int>();
        readonly List<string> _triggers = new List<string>();

        /// <summary>Names of every trigger this ghost's controller understands.</summary>
        public IReadOnlyList<string> Triggers => _triggers;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheParameters();
        }

        void OnEnable()
        {
            // Pooled ghosts are teleported to a spawn point; do not read that jump as speed.
            ResetTracking();
        }

        void LateUpdate()
        {
            if (animator == null) return;
            float dt = Time.deltaTime;
            if (dt <= Mathf.Epsilon) return;

            if (measureSpeedFromMovement)
            {
                Vector3 delta = transform.position - _lastPosition;
                delta.y = 0f;
                _targetSpeed = delta.magnitude / dt;
            }
            _lastPosition = transform.position;

            // Frame-rate independent smoothing.
            _speed = Mathf.Lerp(_speed, _targetSpeed, 1f - Mathf.Exp(-speedSmoothing * dt));
            animator.SetFloat(GhostAnimParams.SpeedHash, _speed);
        }

        /// <summary>Call after teleporting or re-using a pooled ghost.</summary>
        public void ResetTracking()
        {
            _lastPosition = transform.position;
            _speed = 0f;
            _targetSpeed = 0f;
            if (animator == null) return;
            foreach (string t in _triggers) animator.ResetTrigger(t);
        }

        /// <summary>Use when measureSpeedFromMovement is off (e.g. the path follower knows its speed).</summary>
        public void SetSpeed(float metresPerSecond) => _targetSpeed = Mathf.Max(0f, metresPerSecond);

        /// <summary>Fires a trigger if this ghost's controller has it. Returns false if it does not.</summary>
        public bool Play(string trigger)
        {
            if (animator == null || !_parameters.Contains(Animator.StringToHash(trigger))) return false;
            animator.SetTrigger(trigger);
            return true;
        }

        // Readable wrappers for the AI code. A ghost that lacks the clip simply ignores the call.
        public void PlaySpawn() => Play(GhostAnimParams.Spawn);
        public void PlayHit() => Play(GhostAnimParams.Hit);
        public void PlayBoo() => Play(GhostAnimParams.Boo);
        public void PlayPeek() => Play(GhostAnimParams.Peek);
        public void PlayDodge() => Play(GhostAnimParams.Dodge);
        public void PlayStartled() => Play(GhostAnimParams.Startled);
        public void PlayFreeze() => Play(GhostAnimParams.Freeze);
        public void PlaySearch() => Play(GhostAnimParams.Search);
        public void PlayAngryTurn() => Play(GhostAnimParams.AngryTurn);
        public void PlayShieldShatter() => Play(GhostAnimParams.ShieldShatter);
        public void PlayCaught() => Play(GhostAnimParams.Caught);
        public void PlayRingBell() => Play(GhostAnimParams.RingBell);

        void CacheParameters()
        {
            _parameters.Clear();
            _triggers.Clear();
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                _parameters.Add(p.nameHash);
                if (p.type == AnimatorControllerParameterType.Trigger) _triggers.Add(p.name);
            }
        }
    }
}
