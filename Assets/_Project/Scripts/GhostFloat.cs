using UnityEngine;

namespace SheetHappens.Ghosts
{
    /// <summary>
    /// Procedural floating: a sine-wave bob plus a lean into movement and a roll when turning.
    /// Lives on the FloatPivot child (between the ghost root and the animated Visual), so it
    /// never fights the path follower on the root or the Animator clips on Visual.
    /// </summary>
    public class GhostFloat : MonoBehaviour
    {
        public float bobAmplitude = 0.05f;
        public float bobFrequency = 0.5f;
        [Tooltip("Max roll (degrees) when the ghost turns quickly.")]
        public float tiltDegrees = 8f;
        [Tooltip("Max forward lean (degrees) at full speed.")]
        public float leanDegrees = 6f;
        public float maxSpeedForLean = 4f;
        public float smoothing = 8f;

        Transform _root;
        Vector3 _basePosition;
        Vector3 _lastRootPosition;
        float _phase;
        float _lastYaw;
        float _roll;
        float _pitch;

        void Awake()
        {
            _root = transform.parent != null ? transform.parent : transform;
            _basePosition = transform.localPosition;
            _phase = Random.value * Mathf.PI * 2f; // so a group of ghosts does not bob in sync
        }

        void OnEnable()
        {
            _lastRootPosition = _root.position;
            _lastYaw = _root.eulerAngles.y;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= Mathf.Epsilon) return;

            float yaw = _root.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt; // degrees per second
            _lastYaw = yaw;

            Vector3 velocity = (_root.position - _lastRootPosition) / dt;
            _lastRootPosition = _root.position;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed > 50f) speed = 0f; // teleport / pool respawn, not real movement

            float targetRoll = Mathf.Clamp(-yawRate / 180f, -1f, 1f) * tiltDegrees;
            float targetPitch = Mathf.Clamp01(speed / Mathf.Max(0.01f, maxSpeedForLean)) * leanDegrees;

            float k = 1f - Mathf.Exp(-smoothing * dt);
            _roll = Mathf.Lerp(_roll, targetRoll, k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);

            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * bobFrequency + _phase) * bobAmplitude;
            transform.localPosition = _basePosition + Vector3.up * bob;
            transform.localRotation = Quaternion.Euler(_pitch, 0f, _roll);
        }
    }
}
