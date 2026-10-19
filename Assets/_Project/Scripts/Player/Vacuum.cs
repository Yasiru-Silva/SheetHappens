using UnityEngine;
using UnityEngine.InputSystem;

namespace SheetHappens
{
    /// <summary>The Ghost Vacuum: left-click suction shot, 3 power levels from purple energy balls, jams for 3 s when the player is hit.</summary>
    public class Vacuum : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("The Ghost_Vacuum_V2 model held in front of the camera.")]
        public Transform model;
        public float[] cooldown = { 0.55f, 0.35f, 0.35f };
        public float[] range = { 14f, 20f, 20f };
        public float[] radius = { 0.3f, 0.4f, 0.65f };

        public int Energy { get; private set; }
        public int Level => Energy >= 10 ? 3 : Energy >= 5 ? 2 : 1;
        public bool Jammed => Time.time < jamUntil;
        public float JamRemaining => Mathf.Max(0f, jamUntil - Time.time);
        public Transform Nozzle => model ? model : (cam ? cam.transform : transform);

        float nextShot, jamUntil, beamUntil, recoil;
        Vector3 modelBase;
        LineRenderer beam;

        void Awake()
        {
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (model) { FitModel(); modelBase = model.localPosition; }
            beam = new GameObject("VacuumBeam").AddComponent<LineRenderer>();
            beam.transform.SetParent(transform, false);
            beam.positionCount = 2;
            beam.startWidth = 0.06f; beam.endWidth = 0.25f;
            beam.material = Mats.Unlit(new Color(0.75f, 0.55f, 1f, 0.8f));
            beam.enabled = false;
        }

        void FitModel()
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (size > 0.01f) model.localScale *= 0.45f / size; // fit the held vacuum to ~45 cm on screen
            foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Start()
        {
            // Gloved hands holding the vacuum (built after the game has set up its materials).
            if (model && cam)
            {
                var hands = GetComponent<FirstPersonHands>();
                if (hands == null) hands = gameObject.AddComponent<FirstPersonHands>();
                hands.Build(model, cam.transform);
            }
        }

        public void Jam(float seconds) => jamUntil = Mathf.Max(jamUntil, Time.time + seconds);
        public void AddEnergy(int amount) => Energy += amount;

        void Update()
        {
            var gm = GameManager.I;
            if (gm != null && gm.State != GameState.Playing) return;
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked) TryFire();

            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 5f);
            if (model) model.localPosition = modelBase + Vector3.back * recoil * 0.1f + (Jammed ? Random.insideUnitSphere * 0.01f : Vector3.zero);
            if (beam.enabled && Time.time > beamUntil) beam.enabled = false;
        }

        void TryFire()
        {
            if (Jammed || Time.time < nextShot || cam == null) return;
            int L = Level - 1;
            nextShot = Time.time + cooldown[L];
            recoil = 1f;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var hits = Physics.SphereCastAll(ray, radius[L], range[L], ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Vector3 end = ray.GetPoint(range[L]);
            GhostAgent target = null;
            foreach (var h in hits)
            {
                if (h.distance <= 0f) continue;
                if (h.collider.GetComponentInParent<PlayerController>()) continue;
                var ghost = h.collider.GetComponentInParent<GhostAgent>();
                if (ghost != null)
                {
                    if (ghost.IsCaptured) continue;
                    target = ghost; end = h.collider.bounds.center; break;
                }
                if (h.collider.isTrigger) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic)
                {
                    h.collider.attachedRigidbody.AddForce(-ray.direction * 3f, ForceMode.Impulse); // suction tugs loose objects
                    continue;
                }
                end = h.point; break; // a wall, door or furniture blocks the shot
            }
            if (target) target.TakeHit();

            Vector3 from = model ? model.position + model.forward * 0.3f : ray.origin + cam.transform.right * 0.2f - cam.transform.up * 0.2f;
            beam.SetPosition(0, from);
            beam.SetPosition(1, end);
            beam.enabled = true;
            beamUntil = Time.time + 0.08f;
        }
    }
}
