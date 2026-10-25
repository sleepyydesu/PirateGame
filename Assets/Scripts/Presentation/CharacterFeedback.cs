using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using PirateGame.Combat;

namespace PirateGame.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CharacterFeedback : MonoBehaviour
    {
        [Serializable]
        public sealed class Cue
        {
            public string id;
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 1f)] public float volume = 0.6f;
            public Vector2 pitch = new Vector2(0.96f, 1.04f);
            [Min(0f)] public float cooldown = 0.04f;
            [Range(0, 4)] public int importance = 1;
            public Transform origin;
            public GameObject vfxPrefab;
            [Min(0.1f)] public float vfxLifetime = 3f;
            public Vector3 vfxRotationOffset;
            public UnityEvent onPlayed = new UnityEvent();
            public Cue(string id, float volume, int importance = 1, float cooldown = 0.04f)
            { this.id = id; this.volume = volume; this.importance = importance; this.cooldown = cooldown; }
        }

        [Header("Sound output; generated AudioSources use these settings")]
        public AudioMixerGroup output;
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Min(0.1f)] public float minDistance = 3f;
        [Min(0.2f)] public float maxDistance = 25f;
        [Range(2, 24)] public int voices = 8;
        [Header("Automatic health feedback")]
        public bool automaticDamage = true;
        public bool automaticDeath = true;
        public bool warnOnMissingCue = true;
        [Header("IDs are case-sensitive; each ID must be unique")]
        public Cue[] cues =
        {
            new Cue("Swing", .45f), new Cue("SwingHeavy", .6f),
            new Cue("HitBody", .65f, 2, 0f), new Cue("Hurt", .45f, 2, .3f),
            new Cue("Block", .7f, 2, .04f), new Cue("GuardBreak", .8f, 3),
            new Cue("Roll", .35f), new Cue("Backstep", .35f),
            new Cue("Counter", .4f, 2, .2f), new Cue("Jump", .3f),
            new Cue("Land", .45f, 1, .12f), new Cue("JumpTakeoff", .5f),
            new Cue("Slam", .8f, 3), new Cue("Projectile", .6f, 2),
            new Cue("Draw", .45f), new Cue("Sheath", .4f),
            new Cue("PotionGrab", .25f), new Cue("Sip", .3f),
            new Cue("Heal", .4f, 2), new Cue("Roar", .75f, 3),
            new Cue("Taunt", .5f, 2, .3f), new Cue("Death", .7f, 4, .2f),
            new Cue("PhaseEnd", .5f, 2),
            new Cue("Footstep_Dirt", .25f, 0, .05f),
            new Cue("Footstep_Grass", .25f, 0, .05f),
            new Cue("Footstep_Wood", .3f, 0, .05f),
            new Cue("Footstep_Stone", .3f, 0, .05f),
            new Cue("Footstep_Sand", .25f, 0, .05f)
        };

        private sealed class RuntimeCue
        { public Cue cue; public int last = -1; public float ready = float.NegativeInfinity; }
        private readonly Dictionary<string, RuntimeCue> lookup = new Dictionary<string, RuntimeCue>();
        private readonly HashSet<string> warned = new HashSet<string>();
        private AudioSource[] sources;
        private int[] priorities;
        private float[] started;
        private Health health;

        private void Awake()
        {
            health = GetComponent<Health>();
            foreach (Cue c in cues ?? new Cue[0])
            {
                if (c == null || string.IsNullOrWhiteSpace(c.id)) continue;
                if (lookup.ContainsKey(c.id))
                { Debug.LogWarning("Feedback: duplicate cue ID: " + c.id, this); continue; }
                lookup.Add(c.id, new RuntimeCue { cue = c });
            }
            int count = Mathf.Clamp(voices, 2, 24);
            sources = new AudioSource[count]; priorities = new int[count]; started = new float[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject("Feedback voice " + i);
                child.transform.SetParent(transform, false);
                sources[i] = child.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].loop = false;
            }
        }
        private void OnEnable()
        {
            if (health == null) return;
            health.OnDamaged += Damaged;
            health.OnDeath += Died;
        }
        private void OnDisable()
        {
            if (health != null) { health.OnDamaged -= Damaged; health.OnDeath -= Died; }
            if (sources != null) foreach (AudioSource s in sources) if (s != null) s.Stop();
            foreach (RuntimeCue c in lookup.Values) c.ready = float.NegativeInfinity;
        }
        public void Play(string id)
        {
            Vector3 point = transform.position;
            if (!string.IsNullOrEmpty(id) && lookup.TryGetValue(id, out RuntimeCue r) && r.cue.origin != null)
                point = r.cue.origin.position;
            PlayAt(id, point, transform.forward);
        }
        public void PlayLanding(float impactSpeed)
        {
            if (impactSpeed >= 3.5f) Play("Land");
        }
        public void PlaySlamAt(Vector3 point) { PlayAt("Slam", point, Vector3.up); }
        public void PlayAt(string id, Vector3 point, Vector3 normal)
        {
            if (!isActiveAndEnabled || sources == null || string.IsNullOrEmpty(id)) return;
            if (!lookup.TryGetValue(id, out RuntimeCue r))
            {
                if (warnOnMissingCue && warned.Add(id)) Debug.LogWarning("Feedback: add cue ID: " + id, this);
                return;
            }
            if (Time.unscaledTime < r.ready) return;
            r.ready = Time.unscaledTime + Mathf.Max(0f, r.cue.cooldown);
            Cue c = r.cue;
            int clip = PickClip(c.clips, r.last);
            if (clip >= 0)
            {
                int voice = FindVoice(c.importance);
                if (voice >= 0)
                {
                    r.last = clip;
                    AudioSource s = sources[voice];
                    s.Stop(); s.transform.position = point;
                    s.outputAudioMixerGroup = output;
                    s.spatialBlend = Mathf.Clamp01(spatialBlend);
                    s.rolloffMode = AudioRolloffMode.Linear;
                    s.minDistance = Mathf.Max(.1f, minDistance);
                    s.maxDistance = Mathf.Max(s.minDistance + .1f, maxDistance);
                    s.dopplerLevel = 0f;
                    s.priority = 180 - Mathf.Clamp(c.importance, 0, 4) * 30;
                    s.volume = Mathf.Clamp01(c.volume);
                    float low = Mathf.Clamp(Mathf.Min(c.pitch.x, c.pitch.y), .1f, 3f);
                    float high = Mathf.Clamp(Mathf.Max(c.pitch.x, c.pitch.y), low, 3f);
                    s.pitch = UnityEngine.Random.Range(low, high);
                    s.clip = c.clips[clip];
                    priorities[voice] = c.importance; started[voice] = Time.unscaledTime;
                    s.Play();
                }
            }
            if (c.vfxPrefab != null)
            {
                if (normal.sqrMagnitude < .0001f) normal = Vector3.up;
                Quaternion rotation = Quaternion.LookRotation(normal.normalized) * Quaternion.Euler(c.vfxRotationOffset);
                GameObject fx = Instantiate(c.vfxPrefab, point, rotation);
                // The prefab's Play On Awake owns particle playback. Do not play it a second time here.
                Destroy(fx, Mathf.Max(.1f, c.vfxLifetime));
            }
            c.onPlayed?.Invoke();
        }
        private int FindVoice(int importance)
        {
            int victim = -1;
            for (int i = 0; i < sources.Length; i++)
            {
                if (!sources[i].isPlaying) return i;
                if (victim < 0 || priorities[i] < priorities[victim] ||
                    (priorities[i] == priorities[victim] && started[i] < started[victim])) victim = i;
            }
            return victim >= 0 && priorities[victim] <= importance ? victim : -1;
        }
        private static int PickClip(AudioClip[] clips, int last)
        {
            if (clips == null) return -1;
            int count = 0;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null && i != last) count++;
            if (count == 0) return last >= 0 && last < clips.Length && clips[last] != null ? last : -1;
            int pick = UnityEngine.Random.Range(0, count);
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && i != last && pick-- == 0) return i;
            return -1;
        }
        private void Damaged(DamageInfo info)
        {
            if (!automaticDamage || info.WasBlocked || info.Amount <= 0f) return;
            Vector3 normal = info.Attacker != null ? info.Attacker.transform.position - info.HitPoint : transform.forward;
            if (info.Reaction != HitReactionType.GroundSlam)
                PlayAt("HitBody", info.HitPoint, normal);
            if (health != null && !health.IsDead) Play("Hurt");
        }
        private void Died() { if (automaticDeath) Play("Death"); }
    }
}
