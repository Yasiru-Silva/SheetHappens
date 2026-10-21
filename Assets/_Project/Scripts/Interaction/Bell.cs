using System.Collections;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>The old bell in the attic, hanging from the roof. One ring and the night is lost.</summary>
    public class Bell : MonoBehaviour
    {
        [Tooltip("Rotates when the bell rings (the rope and bell hang from it).")]
        [SerializeField] Transform swing;
        /// <summary>Where a ghost has to get to in order to ring the bell (set at runtime from the floor under it).</summary>
        public Vector3 RingPoint { get; set; }
        [Tooltip("Sound played when a ghost rings the bell. Empty = the default toll in Resources/Audio/BellToll.")]
        public AudioClip ringClip;
        [Range(0f, 1f)] public float ringVolume = 1f;
        bool ringing;
        AudioSource audioSource;

        void Awake()
        {
            RingPoint = transform.position;
            if (ringClip == null) ringClip = Resources.Load<AudioClip>("Audio/BellToll");
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.clip = ringClip;
            audioSource.volume = ringVolume;
            audioSource.spatialBlend = 0.5f;   // you can tell it comes from the attic, but hear it anywhere in the house
            audioSource.minDistance = 8f;
            audioSource.maxDistance = 60f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        /// <summary>Builds a bell hanging on a rope from 'anchorY' (the roof beam) above 'floor'.</summary>
        public static Bell CreateHanging(Vector3 floor, float anchorY, Material rope, Material brass)
        {
            var root = new GameObject("AtticBell");
            root.transform.position = floor;
            var pivot = new GameObject("Swing").transform;
            pivot.SetParent(root.transform, false);
            float drop = Mathf.Max(0.6f, anchorY - floor.y - 2.5f); // bell lip ends ~1.9 m above the floor
            pivot.localPosition = new Vector3(0f, anchorY - floor.y, 0f);
            Part(PrimitiveType.Cylinder, pivot, new Vector3(0f, -drop * 0.5f, 0f), new Vector3(0.04f, drop * 0.5f, 0.04f), rope);
            Part(PrimitiveType.Sphere, pivot, new Vector3(0f, -drop - 0.12f, 0f), new Vector3(0.36f, 0.3f, 0.36f), brass);
            Part(PrimitiveType.Cylinder, pivot, new Vector3(0f, -drop - 0.32f, 0f), new Vector3(0.55f, 0.2f, 0.55f), brass);
            Part(PrimitiveType.Sphere, pivot, new Vector3(0f, -drop - 0.55f, 0f), new Vector3(0.1f, 0.1f, 0.1f), rope);
            var bell = root.AddComponent<Bell>();
            bell.swing = pivot;
            return bell;
        }

        /// <summary>Fallback: a bell on a small standing frame (used only if the scene has no bell).</summary>
        public static Bell Create(Vector3 floor)
        {
            var wood = Mats.Lit(new Color(0.3f, 0.18f, 0.1f));
            var brass = Mats.Lit(new Color(0.85f, 0.65f, 0.2f), new Color(0.25f, 0.18f, 0.05f));
            var bell = CreateHanging(floor, floor.y + 2.6f, wood, brass);
            Part(PrimitiveType.Cube, bell.transform, new Vector3(-0.45f, 1.3f, 0f), new Vector3(0.08f, 2.6f, 0.08f), wood);
            Part(PrimitiveType.Cube, bell.transform, new Vector3(0.45f, 1.3f, 0f), new Vector3(0.08f, 2.6f, 0.08f), wood);
            Part(PrimitiveType.Cube, bell.transform, new Vector3(0f, 2.6f, 0f), new Vector3(1.0f, 0.08f, 0.08f), wood);
            return bell;
        }

        static void Part(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        public void Ring()
        {
            if (ringing) return;
            if (audioSource && ringClip) audioSource.PlayOneShot(ringClip, ringVolume);
            if (swing) StartCoroutine(Swing());
        }

        IEnumerator Swing()
        {
            ringing = true;
            for (float t = 0; t < 3f; t += Time.unscaledDeltaTime)
            {
                swing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 12f) * 30f * (1f - t / 3f));
                yield return null;
            }
            swing.localRotation = Quaternion.identity;
            ringing = false;
        }
    }
}
