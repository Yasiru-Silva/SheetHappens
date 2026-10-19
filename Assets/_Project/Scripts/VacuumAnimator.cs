using UnityEngine;

namespace SheetHappens.Ghosts
{
    /// <summary>
    /// Drives the first-person vacuum animations. The player/vacuum code calls these methods;
    /// nothing here reads input, so the Systems Engineer's scripts stay in charge of gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public class VacuumAnimator : MonoBehaviour
    {
        public Animator animator;
        public float speedSmoothing = 10f;

        [Header("Special-mode nozzle glow (optional)")]
        public Renderer[] glowRenderers;
        [ColorUsage(false, true)] public Color normalGlow = new Color(0f, 1.2f, 1.4f);
        [ColorUsage(false, true)] public Color specialGlow = new Color(2.2f, 1.6f, 0.2f);

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        float _speed;
        float _targetSpeed;
        MaterialPropertyBlock _block;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            ApplyGlow(false);
        }

        void Update()
        {
            if (animator == null) return;
            _speed = Mathf.Lerp(_speed, _targetSpeed, 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
            animator.SetFloat(GhostAnimParams.SpeedHash, _speed);
        }

        /// <summary>Player walking speed in m/s. At or above GhostAnimParams.RunThreshold the Run clip plays.</summary>
        public void SetMoveSpeed(float metresPerSecond) => _targetSpeed = Mathf.Max(0f, metresPerSecond);

        public void Shoot() => Fire(GhostAnimParams.Shoot);
        public void Collect() => Fire(GhostAnimParams.Collect);
        public void SetJammed(bool jammed) { if (animator != null) animator.SetBool(GhostAnimParams.Jammed, jammed); }

        public void SwitchMode(bool special)
        {
            Fire(GhostAnimParams.SwitchMode);
            ApplyGlow(special);
        }

        void Fire(string trigger)
        {
            if (animator != null) animator.SetTrigger(trigger);
        }

        void ApplyGlow(bool special)
        {
            if (glowRenderers == null || glowRenderers.Length == 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            Color c = special ? specialGlow : normalGlow;
            foreach (Renderer r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionColor, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
