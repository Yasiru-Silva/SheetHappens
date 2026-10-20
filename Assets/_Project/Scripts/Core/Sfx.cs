using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>One-shot sound effects from Resources/Audio/SFX (capture, energy pickup, ghost screams).</summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static AudioClip Clip(string name)
        {
            if (!cache.TryGetValue(name, out var c) || c == null)
            {
                c = Resources.Load<AudioClip>("Audio/SFX/" + name);
                cache[name] = c;
            }
            return c;
        }

        /// <summary>Plays a clip at a point. spatial 0 = same loudness everywhere, 1 = fully 3D.</summary>
        public static AudioSource Play(string name, Vector3 at, float volume = 1f, float spatial = 0.6f, float pitch = 1f)
        {
            var clip = Clip(name);
            if (clip == null) return null;
            var go = new GameObject("Sfx_" + name);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.volume = volume; src.pitch = pitch;
            src.spatialBlend = spatial; src.minDistance = 4f; src.maxDistance = 40f;
            src.rolloffMode = AudioRolloffMode.Linear; src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
            return src;
        }
    }
}
