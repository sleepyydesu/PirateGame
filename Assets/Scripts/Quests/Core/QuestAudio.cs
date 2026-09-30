using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    public enum QuestSound
    {
        Pickup, QuestAccepted, QuestComplete, QuestDiscovered, Reward, DialogueBlip, UIClick,
        ObjectiveUpdated, Thunder, ShipBreak, EmptyShop, MerchantFreed, CoinPurchase, Splash,
    }

    public enum QuestLoop { Wind, Rain }

    /// <summary>
    /// One place for every quest sound. Drop real clips into the overrides list when the
    /// audio team has them; until then each sound is synthesised procedurally so the
    /// feature is testable end to end.
    /// </summary>
    public class QuestAudio : MonoBehaviour
    {
        [Serializable]
        public class SoundOverride
        {
            public QuestSound sound;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
        }

        [Serializable]
        public class LoopOverride
        {
            public QuestLoop loop;
            public AudioClip clip;
        }

        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.7f;
        [SerializeField] private List<SoundOverride> soundOverrides = new List<SoundOverride>();
        [SerializeField] private List<LoopOverride> loopOverrides = new List<LoopOverride>();

        private static QuestAudio instance;
        private AudioSource uiSource;
        private readonly Dictionary<QuestSound, AudioClip> cache = new Dictionary<QuestSound, AudioClip>();
        private readonly Dictionary<QuestLoop, AudioClip> loopCache = new Dictionary<QuestLoop, AudioClip>();

        private const int Rate = 44100;

        private static QuestAudio Get()
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<QuestAudio>();
                if (instance == null && Application.isPlaying)
                    instance = new GameObject("QuestAudio").AddComponent<QuestAudio>();
            }
            return instance;
        }

        private void Awake()
        {
            instance = this;
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
        }

        /// <summary>Play a one-shot. Pass a position for a 3D sound, otherwise it plays as UI audio.</summary>
        public static void Play(QuestSound sound, Vector3? at = null, float volume = 1f)
        {
            QuestAudio a = Get();
            if (a == null) return;
            AudioClip clip = a.ClipFor(sound, out float v);
            if (clip == null) return;
            volume *= v * a.masterVolume;

            if (at.HasValue)
            {
                var go = new GameObject("QuestSfx_" + sound);
                go.transform.position = at.Value;
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.volume = volume;
                src.spatialBlend = 1f;
                src.minDistance = 8f;
                src.maxDistance = 250f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.Play();
                Destroy(go, clip.length + 0.1f);
            }
            else
            {
                a.uiSource.PlayOneShot(clip, volume);
            }
        }

        /// <summary>Create a looping 2D ambience source (volume starts at 0 — fade it yourself).</summary>
        public static AudioSource CreateLoop(QuestLoop loop, Transform parent)
        {
            QuestAudio a = Get();
            if (a == null) return null;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = a.LoopClip(loop);
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = 0f;
            src.Play();
            return src;
        }

        public static float MasterVolume => Get() != null ? Get().masterVolume : 1f;

        private AudioClip ClipFor(QuestSound s, out float volume)
        {
            volume = 1f;
            foreach (SoundOverride o in soundOverrides)
                if (o != null && o.sound == s && o.clip != null) { volume = o.volume; return o.clip; }

            if (!cache.TryGetValue(s, out AudioClip clip))
            {
                clip = Generate(s);
                cache[s] = clip;
            }
            return clip;
        }

        private AudioClip LoopClip(QuestLoop l)
        {
            foreach (LoopOverride o in loopOverrides)
                if (o != null && o.loop == l && o.clip != null) return o.clip;
            if (!loopCache.TryGetValue(l, out AudioClip clip))
            {
                clip = l == QuestLoop.Wind ? MakeWind() : MakeRain();
                loopCache[l] = clip;
            }
            return clip;
        }

        // =============================================================== synthesis

        private static AudioClip Generate(QuestSound s)
        {
            switch (s)
            {
                case QuestSound.Pickup: return Notes("pickup", new[] { 880f, 1318.5f }, 0.09f, 0.35f, 0.5f);
                case QuestSound.QuestAccepted: return Notes("accepted", new[] { 587.3f, 880f }, 0.12f, 0.5f, 0.45f);
                case QuestSound.QuestComplete: return Notes("complete", new[] { 523.3f, 659.3f, 784f, 1046.5f }, 0.13f, 0.9f, 0.45f);
                case QuestSound.QuestDiscovered: return Notes("discovered", new[] { 392f, 466.2f, 587.3f }, 0.18f, 0.8f, 0.4f);
                case QuestSound.Reward: return Notes("reward", new[] { 1046.5f, 1318.5f, 1568f, 2093f, 2637f }, 0.06f, 0.6f, 0.3f);
                case QuestSound.ObjectiveUpdated: return Notes("objective", new[] { 659.3f, 987.8f }, 0.1f, 0.4f, 0.35f);
                case QuestSound.MerchantFreed: return Notes("freed", new[] { 440f, 554.4f, 659.3f }, 0.1f, 0.7f, 0.35f);
                case QuestSound.CoinPurchase: return Notes("coin", new[] { 2093f, 2637f }, 0.05f, 0.25f, 0.3f);
                case QuestSound.DialogueBlip: return Blip();
                case QuestSound.UIClick: return Click();
                case QuestSound.Thunder: return Thunder();
                case QuestSound.ShipBreak: return ShipBreak();
                case QuestSound.EmptyShop: return Creak();
                case QuestSound.Splash: return Splash();
            }
            return null;
        }

        /// <summary>Bell-ish arpeggio: each note a decaying sine with a soft overtone.</summary>
        private static AudioClip Notes(string name, float[] freqs, float step, float tail, float gain)
        {
            float total = step * (freqs.Length - 1) + tail;
            int n = Mathf.CeilToInt(total * Rate);
            var data = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = Mathf.RoundToInt(k * step * Rate);
                float dur = (k == freqs.Length - 1) ? tail : tail * 0.7f;
                int len = Mathf.Min(n - start, Mathf.RoundToInt(dur * Rate));
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 5f / dur);
                    float w = Mathf.Sin(2 * Mathf.PI * freqs[k] * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * freqs[k] * 2.01f * t);
                    data[start + i] += w * env * gain;
                }
            }
            return Make(name, data);
        }

        private static AudioClip Blip()
        {
            int n = (int)(0.045f * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 70f);
                d[i] = (Mathf.Sin(2 * Mathf.PI * 520f * t) > 0 ? 0.18f : -0.18f) * env * 0.5f + Mathf.Sin(2 * Mathf.PI * 520f * t) * env * 0.2f;
            }
            return Make("blip", d);
        }

        private static AudioClip Click()
        {
            int n = (int)(0.03f * Rate);
            var d = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 180f) * 0.4f + Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 120f) * 0.2f;
            }
            return Make("click", d);
        }

        private static AudioClip Thunder()
        {
            int n = (int)(3.5f * Rate);
            var d = new float[n];
            var rng = new System.Random(7);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.04f, -1f, 1f) * 0.998f;
                lp += (white - lp) * 0.25f;
                float crack = lp * Mathf.Exp(-t * 9f) * 0.9f;
                float rumble = brown * (0.4f + 0.6f * Mathf.PerlinNoise(t * 3f, 0.3f)) * Mathf.Exp(-t * 0.9f) * 2.2f;
                d[i] = Mathf.Clamp(crack + rumble, -1f, 1f);
            }
            return Make("thunder", d);
        }

        private static AudioClip ShipBreak()
        {
            int n = (int)(3f * Rate);
            var d = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.35f;
                // crackles: random gated bursts, denser early
                float gate = Mathf.PerlinNoise(t * 40f, 1.7f) > 0.62f - t * 0.05f ? 1f : 0.05f;
                float crack = lp * gate * Mathf.Exp(-t * 0.8f) * 0.8f;
                // wooden groan: falling low tone with wobble
                float f = 95f - t * 22f + Mathf.Sin(t * 13f) * 6f;
                float groan = Mathf.Sin(2 * Mathf.PI * f * t) * 0.35f * Mathf.Clamp01(t * 3f) * Mathf.Exp(-t * 0.6f);
                d[i] = Mathf.Clamp(crack + groan, -1f, 1f);
            }
            return Make("shipbreak", d);
        }

        private static AudioClip Creak()
        {
            int n = (int)(2.2f * Rate);
            var d = new float[n];
            var rng = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.02f;
                float f = 170f - t * 25f + Mathf.Sin(t * 30f) * 8f;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 2.2f));
                float saw = ((f * t) % 1f) * 2f - 1f;
                d[i] = (saw * 0.12f * (0.5f + 0.5f * Mathf.PerlinNoise(t * 25f, 0f)) + lp * 1.5f) * env;
            }
            return Make("creak", d);
        }

        private static AudioClip Splash()
        {
            int n = (int)(1.2f * Rate);
            var d = new float[n];
            var rng = new System.Random(9);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.5f;
                d[i] = lp * Mathf.Exp(-t * 3.5f) * 0.6f;
            }
            return Make("splash", d);
        }

        private static AudioClip MakeWind()
        {
            int n = 8 * Rate;
            var d = new float[n];
            var rng = new System.Random(13);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.999f;
                float cutoff = 0.02f + 0.06f * Mathf.PerlinNoise(t * 0.35f, 0.5f);
                lp += (white - lp) * cutoff;
                float gust = 0.5f + 0.5f * Mathf.PerlinNoise(t * 0.5f, 2.2f);
                d[i] = (brown * 0.6f + lp * 2.5f) * gust;
            }
            return MakeLoop("wind", d);
        }

        private static AudioClip MakeRain()
        {
            int n = 5 * Rate;
            var d = new float[n];
            var rng = new System.Random(17);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.45f;
                hp = 0.95f * (hp + lp - prev);
                prev = lp;
                float drop = rng.NextDouble() < 0.0009 ? 0.6f : 0f;
                d[i] = hp * 0.35f + drop * ((float)rng.NextDouble() - 0.5f);
            }
            return MakeLoop("rain", d);
        }

        private static AudioClip MakeLoop(string name, float[] d)
        {
            // Crossfade the tail into the head so the loop point is seamless.
            int fade = Rate / 2;
            int n = d.Length - fade;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < fade; i++)
            {
                float a = i / (float)fade;
                o[i] = d[i] * a + d[n + i] * (1f - a);
            }
            return Make(name, o);
        }

        private static AudioClip Make(string name, float[] data)
        {
            float peak = 0.0001f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0.95f) for (int i = 0; i < data.Length; i++) data[i] *= 0.95f / peak;
            AudioClip clip = AudioClip.Create("Quest_" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
