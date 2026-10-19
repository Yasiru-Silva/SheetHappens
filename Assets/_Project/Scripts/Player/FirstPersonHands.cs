using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// First-person gloved hands holding the Ghost Vacuum: the right hand around the rear handle,
    /// the left hand steadying the barrel from the side. Built from simple shapes at runtime and parented to the vacuum model,
    /// so they follow its recoil and jam shake.
    /// </summary>
    public class FirstPersonHands : MonoBehaviour
    {
        [Header("Look")]
        public Color gloveColor = new Color(0.30f, 0.20f, 0.13f);
        public Color sleeveColor = new Color(0.16f, 0.20f, 0.17f);
        public Color cuffColor = new Color(0.10f, 0.12f, 0.11f);

        [Header("Placement (vacuum model space)")]
        [Tooltip("Centre of the bar the right hand holds (the back of the rear handle loop).")] public Vector3 gripCenter = new Vector3(0f, 0.135f, -0.172f);
        [Tooltip("Direction of that bar, bottom to top.")] public Vector3 gripAxis = new Vector3(0f, 1f, 0.15f);
        public float gripRadius = 0.016f;
        [Tooltip("Where the left palm rests on the left side of the barrel.")] public Vector3 supportPoint = new Vector3(-0.108f, 0.11f, 0.29f);

        [Header("Arms (camera space, metres)")]
        public Vector3 rightElbow = new Vector3(0.42f, -0.62f, 0.22f);
        public Vector3 leftElbow = new Vector3(-0.12f, -0.66f, 0.42f);

        Transform model, cam;
        Light debugLight;

        void Update()
        {
            // F2 (debug): a bright light on the camera, to check the hands and furniture in the dark.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || cam == null || !kb.f2Key.wasPressedThisFrame) return;
            if (debugLight == null)
            {
                debugLight = new GameObject("DebugLight").AddComponent<Light>();
                debugLight.transform.SetParent(cam, false);
                debugLight.type = LightType.Point; debugLight.range = 30f; debugLight.intensity = 6f;
                debugLight.enabled = false;
            }
            debugLight.enabled = !debugLight.enabled;
        }
        Material glove, sleeve, cuff;

        /// <summary>Builds both hands on the held vacuum model.</summary>
        public void Build(Transform vacuumModel, Transform cameraTransform)
        {
            model = vacuumModel; cam = cameraTransform;
            glove = Mats.Lit(gloveColor); glove.SetFloat("_Smoothness", 0.35f);
            sleeve = Mats.Lit(sleeveColor); sleeve.SetFloat("_Smoothness", 0.1f);
            cuff = Mats.Lit(cuffColor);

            // A holder inside the model whose units are metres (the model is scaled down to fit the screen).
            var holder = new GameObject("Hands").transform;
            holder.SetParent(model, false);
            float k = model.localScale.x;
            holder.localScale = Vector3.one / Mathf.Max(0.0001f, k);

            BuildRightHand(holder, k);
            BuildLeftHand(holder, k);

            foreach (var r in holder.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------ the two hands

        void BuildRightHand(Transform holder, float k)
        {
            Vector3 A = gripAxis.normalized;                                     // grip, bottom to top
            Vector3 back = Vector3.ProjectOnPlane(Vector3.back, A).normalized;   // the side of the grip facing the player
            Vector3 side = Vector3.right;
            // Like holding a pistol: the palm on the right side of the grip (a little behind it), fingers around the front,
            // the index finger at the top next to the trigger.
            Vector3 outward = (side * 0.9f + back * 0.35f).normalized;
            Vector3 palmNormal = -outward;                                       // hand +y
            Vector3 fingers = Vector3.Cross(A, palmNormal).normalized;           // hand +z; with this, hand +x (index side) = A
            Vector3 palmCentre = gripCenter + outward * (gripRadius + 0.016f / k);
            // Wrist = palm centre moved back along the fingers' opposite direction (in model units).
            Vector3 wrist = palmCentre - fingers * (0.045f / k);

            var hand = MakeHand(holder, "RightHand", mirror: false,
                curls: new[] { new Vector3(80f, 90f, 60f),    // index .. pinky wrapped round the handle
                               new Vector3(85f, 92f, 60f),
                               new Vector3(88f, 92f, 60f),
                               new Vector3(90f, 92f, 60f) },
                thumbCurl: 45f);
            hand.localPosition = wrist * k;
            hand.localRotation = Quaternion.LookRotation(fingers, palmNormal);

            Arm(holder, k, wrist * k, rightElbow, "RightArm");
        }

        void BuildLeftHand(Transform holder, float k)
        {
            // Palm flat against the left side of the barrel, fingers reaching up and over the top, thumb along the side.
            Vector3 palmNormal = Vector3.right;
            Vector3 fingers = new Vector3(0f, 1f, 0.25f).normalized;
            Vector3 palmCentre = supportPoint + Vector3.left * (0.016f / k);
            Vector3 wrist = palmCentre - fingers * (0.045f / k);
            var hand = MakeHand(holder, "LeftHand", mirror: true,
                curls: new[] { new Vector3(45f, 40f, 25f), new Vector3(48f, 40f, 25f), new Vector3(50f, 40f, 25f), new Vector3(55f, 40f, 25f) },
                thumbCurl: 10f);
            hand.localPosition = wrist * k;
            hand.localRotation = Quaternion.LookRotation(fingers, palmNormal);
            Arm(holder, k, wrist * k, leftElbow, "LeftArm");
        }

        /// <summary>Sleeve from the wrist back to an elbow point near the bottom of the screen.</summary>
        void Arm(Transform holder, float k, Vector3 wristHolder, Vector3 elbowCam, string name)
        {
            Vector3 wristWorld = holder.TransformPoint(wristHolder);
            Vector3 elbowWorld = cam.TransformPoint(elbowCam);
            Vector3 wristLocal = holder.InverseTransformPoint(wristWorld);
            Vector3 elbowLocal = holder.InverseTransformPoint(elbowWorld);
            Vector3 dir = elbowLocal - wristLocal;
            var arm = new GameObject(name).transform;
            arm.SetParent(holder, false);
            arm.localPosition = wristLocal;
            arm.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float len = dir.magnitude;
            // cuff of the glove, then the sleeve (slightly wider towards the elbow)
            Segment(arm, Vector3.forward * 0.005f, 0.075f, 0.052f, cuff);
            Segment(arm, Vector3.forward * 0.06f, len - 0.04f, 0.072f, sleeve);
            Segment(arm, Vector3.forward * (len * 0.55f), len * 0.5f, 0.082f, sleeve);
        }

        // ------------------------------------------------------------ hand model

        static readonly float[] FingerX = { 0.031f, 0.011f, -0.009f, -0.028f };   // index .. pinky across the knuckles
        static readonly float[] KnuckleZ = { 0.088f, 0.092f, 0.089f, 0.082f };
        static readonly Vector3[] Lengths =
        {
            new Vector3(0.044f, 0.027f, 0.022f), // index
            new Vector3(0.049f, 0.030f, 0.024f), // middle
            new Vector3(0.046f, 0.028f, 0.022f), // ring
            new Vector3(0.036f, 0.021f, 0.019f), // pinky
        };
        static readonly float[] Thick = { 0.021f, 0.022f, 0.021f, 0.019f };

        /// <summary>A gloved hand. Local frame: origin at the wrist, +z towards the fingers, +y out of the palm, +x towards the thumb.</summary>
        Transform MakeHand(Transform parent, string name, bool mirror, Vector3[] curls, float thumbCurl)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var basis = new GameObject("Basis").transform; // mirrored for the left hand
            basis.SetParent(root, false);
            if (mirror) basis.localScale = new Vector3(-1f, 1f, 1f);

            // palm: a rounded block
            var palm = Shape(PrimitiveType.Sphere, basis, new Vector3(0f, 0f, 0.048f), Quaternion.identity, new Vector3(0.088f, 0.034f, 0.1f), glove);
            Shape(PrimitiveType.Cube, basis, new Vector3(0f, -0.002f, 0.05f), Quaternion.identity, new Vector3(0.074f, 0.026f, 0.072f), glove);
            Shape(PrimitiveType.Sphere, basis, new Vector3(0f, -0.004f, 0.006f), Quaternion.identity, new Vector3(0.062f, 0.03f, 0.04f), glove); // heel of the wrist

            for (int f = 0; f < 4; f++)
            {
                var joint = new GameObject("Finger" + f).transform;
                joint.SetParent(basis, false);
                joint.localPosition = new Vector3(FingerX[f], 0f, KnuckleZ[f] - 0.008f);
                Transform cur = joint;
                float[] lens = { Lengths[f].x, Lengths[f].y, Lengths[f].z };
                float[] angles = { curls[f].x, curls[f].y, curls[f].z };
                for (int s = 0; s < 3; s++)
                {
                    cur.localRotation = Quaternion.Euler(-angles[s], f == 0 ? 0f : (f - 1.5f) * -2f, 0f); // bend towards the palm
                    float w = Thick[f] * (1f - 0.08f * s);
                    Segment(cur, Vector3.zero, lens[s] + w * 0.4f, w, glove);
                    var next = new GameObject("J" + (s + 1)).transform;
                    next.SetParent(cur, false);
                    next.localPosition = Vector3.forward * lens[s];
                    cur = next;
                }
            }

            // thumb: from the side of the palm near the wrist, angled forwards and across the palm
            var t0 = new GameObject("Thumb").transform;
            t0.SetParent(basis, false);
            t0.localPosition = new Vector3(0.036f, 0.006f, 0.026f);
            t0.localRotation = Quaternion.Euler(-25f, 38f, -30f);
            Transform tc = t0;
            float[] tl = { 0.038f, 0.03f, 0.026f };
            for (int s = 0; s < 3; s++)
            {
                if (s > 0) tc.localRotation = Quaternion.Euler(-thumbCurl * (s == 1 ? 0.6f : 1f), -thumbCurl * 0.5f, 0f);
                Segment(tc, Vector3.zero, tl[s] + 0.01f, 0.025f - s * 0.002f, glove);
                var next = new GameObject("T" + (s + 1)).transform;
                next.SetParent(tc, false);
                next.localPosition = Vector3.forward * tl[s];
                tc = next;
            }
            return root;
        }

        /// <summary>A capsule from 'start' along the parent's +z, 'length' long and 'width' thick.</summary>
        static void Segment(Transform parent, Vector3 start, float length, float width, Material m)
        {
            length = Mathf.Max(length, width);
            Shape(PrimitiveType.Capsule, parent, start + Vector3.forward * (length * 0.5f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(width, length * 0.5f, width), m);
        }

        static Transform Shape(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }
    }
}
