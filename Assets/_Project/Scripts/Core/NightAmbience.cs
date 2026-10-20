using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Night-time sound: a wind and cricket bed that loops all night, plus owls, wolves, a distant dog and frogs calling
    /// from the dark outside the house, and the old house creaking around the player.
    /// Sounds come from Resources/Audio/Night: files starting with "Loop_" loop, "Creak_" play inside the house,
    /// everything else plays outside.
    /// </summary>
    public class NightAmbience : MonoBehaviour
    {
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Range(0f, 1f)] public float windVolume = 0.35f;
        [Range(0f, 1f)] public float cricketVolume = 0.22f;
        [Range(0f, 1f)] public float animalVolume = 0.9f;
        [Range(0f, 1f)] public float creakVolume = 0.6f;
        [Tooltip("Seconds between random calls (min, max).")]
        public Vector2 callInterval = new Vector2(7f, 16f);
        [Tooltip("How far outside the house the animals are (metres).")]
        public Vector2 animalDistance = new Vector2(18f, 45f);

        readonly List<AudioClip> outside = new List<AudioClip>(), creaks = new List<AudioClip>();
        readonly List<AudioSource> loops = new List<AudioSource>();
        Bounds house;
        float nextCall;
        AudioClip last;

        /// <summary>Starts the night sounds. 'houseCells' are floor points inside the house.</summary>
        public void Begin(IEnumerable<Vector3> houseFloor)
        {
            bool any = false;
            foreach (var p in houseFloor) { if (!any) { house = new Bounds(p, Vector3.one); any = true; } else house.Encapsulate(p); }
            if (!any) house = new Bounds(transform.position, Vector3.one * 20f);

            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Night"))
            {
                if (clip.name.StartsWith("Loop_"))
                {
                    var src = gameObject.AddComponent<AudioSource>();
                    src.clip = clip; src.loop = true; src.spatialBlend = 0f; src.playOnAwake = false;
                    src.volume = masterVolume * (clip.name.Contains("Wind") ? windVolume : cricketVolume);
                    src.time = Random.Range(0f, clip.length * 0.9f);
                    src.Play();
                    loops.Add(src);
                }
                else if (clip.name.StartsWith("Creak_")) creaks.Add(clip);
                else outside.Add(clip);
            }
            nextCall = Time.time + Random.Range(3f, 6f); // the first owl comes soon after midnight
            Debug.Log($"[SheetHappens] Night sounds: {loops.Count} loops, {outside.Count} animal calls, {creaks.Count} creaks.");
        }

        void Update()
        {
            if (Time.time < nextCall || (outside.Count == 0 && creaks.Count == 0)) return;
            nextCall = Time.time + Random.Range(callInterval.x, callInterval.y);
            bool creak = creaks.Count > 0 && (outside.Count == 0 || Random.value < 0.3f);
            var pool = creak ? creaks : outside;
            var clip = pool[Random.Range(0, pool.Count)];
            if (clip == last && pool.Count > 1) clip = pool[(pool.IndexOf(clip) + 1) % pool.Count];
            last = clip;
            if (creak) PlayCreak(clip); else PlayOutside(clip);
        }

        void PlayOutside(AudioClip clip)
        {
            float radius = Mathf.Max(house.extents.x, house.extents.z);
            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = radius + Random.Range(animalDistance.x, animalDistance.y);
            Vector3 at = new Vector3(house.center.x + dir.x * dist, house.min.y + 1f, house.center.z + dir.y * dist);
            OneShot(clip, at, masterVolume * animalVolume * Random.Range(0.6f, 1f), 0.8f, 12f, 150f, Random.Range(0.92f, 1.06f));
        }

        void PlayCreak(AudioClip clip)
        {
            var gm = GameManager.I;
            Vector3 center = gm && gm.Player ? gm.Player.transform.position : house.center;
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(3f, 8f);
            Vector3 at = center + new Vector3(d.x, Random.Range(-1f, 3f), d.y);
            OneShot(clip, at, masterVolume * creakVolume * Random.Range(0.5f, 1f), 0.9f, 2f, 25f, Random.Range(0.85f, 1.15f));
        }

        void OneShot(AudioClip clip, Vector3 at, float volume, float spatial, float minD, float maxD, float pitch)
        {
            var go = new GameObject("NightSound_" + clip.name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.volume = volume; src.pitch = pitch;
            src.spatialBlend = spatial; src.minDistance = minD; src.maxDistance = maxD;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }
    }
}
