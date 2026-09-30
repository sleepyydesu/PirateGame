using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PirateGame.Quests
{
    /// <summary>
    /// Genshin-style guide to the tracked objective:
    ///   • sparkle motes drifting up along the NavMesh path,
    ///   • a wisp that repeatedly flies ahead of the player along the route,
    ///   • an optional glowing gold ribbon on the ground (off by default — see Show Ribbon),
    ///   • a pulsing ring on the ground for "search this area" objectives.
    /// It follows whatever <see cref="QuestGuidance"/> resolves (tracked quest -> current stage -> QuestTarget).
    /// Toggle with T (see QuestUI).
    /// </summary>
    public class QuestTrail : MonoBehaviour
    {
        public static QuestTrail Instance { get; private set; }

        [Header("Look")]
        [SerializeField] private Material ribbonMaterial;
        [SerializeField] private Material glowMaterial;
        [SerializeField, ColorUsage(false, true)] private Color color = new Color(1f, 0.78f, 0.32f) * 2.2f;
        [SerializeField] private float width = 0.7f;
        [SerializeField] private float groundOffset = 0.07f;
        [Tooltip("Glowing gold ribbon laid on the ground along the route.")]
        [SerializeField] private bool showRibbon = false;
        [Tooltip("Sparkle motes drifting up along the route.")]
        [SerializeField] private bool showMotes = true;
        [Tooltip("A glowing wisp that flies ahead of the player along the route.")]
        [SerializeField] private bool showWisp = true;

        [Header("Path")]
        [SerializeField] private float maxLength = 55f;
        [SerializeField] private float startOffset = 1.2f;
        [SerializeField] private float sampleSpacing = 0.6f;
        [SerializeField] private float repathInterval = 0.35f;
        [SerializeField] private float hideWithin = 3.5f;
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("The trail never dips below this height (keep it on top of water surfaces).")]
        [SerializeField] private float waterLevel = -1000f;

        [Header("Wisp")]
        [SerializeField] private float wispSpeed = 9f;
        [SerializeField] private float wispRange = 28f;
        [SerializeField] private float wispHeight = 1.1f;

        [Header("Motes")]
        [SerializeField] private float motesPerMetre = 0.9f;

        public bool PlayerEnabled { get; set; } = true;

        private LineRenderer ribbon, ring;
        private ParticleSystem motes;
        private Transform wisp;
        private TrailRenderer wispTrail;
        private Light wispLight;
        private Material ribbonInstance, ringInstance;

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<float> cumulative = new List<float>();
        private float length;
        private NavMeshPath navPath;
        private float repathTimer;
        private float alpha;
        private float wispDistance;
        private QuestTarget ringTarget;
        private float moteAccumulator;

        private void Awake()
        {
            Instance = this;
            navPath = new NavMeshPath();
            if (groundMask == ~0)
                groundMask = ~LayerMask.GetMask("Player", "Enemy", "Water", "UI", "Ignore Raycast", "TransparentFX");
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Setup(Material ribbonMat, Material glowMat)
        {
            ribbonMaterial = ribbonMat;
            glowMaterial = glowMat;
        }

        // ================================================================ build

        private void Build()
        {
            Texture2D band = MakeRibbonTexture();
            Texture2D dash = MakeDashTexture();

            ribbonInstance = ribbonMaterial != null ? new Material(ribbonMaterial) : QuestMaterials.Additive(Color.white);
            SetTexture(ribbonInstance, band);
            ringInstance = ribbonMaterial != null ? new Material(ribbonMaterial) : QuestMaterials.Additive(Color.white);
            SetTexture(ringInstance, dash);
            Material glow = glowMaterial != null ? glowMaterial : QuestMaterials.Additive(Color.white, MakeSoftDot());

            ribbon = MakeLine("GuideRibbon", ribbonInstance);
            ribbon.alignment = LineAlignment.TransformZ; // lie flat on the ground
            ribbon.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ribbon.textureMode = LineTextureMode.Stretch;
            ribbon.widthMultiplier = width;
            ribbon.numCornerVertices = 3;

            ring = MakeLine("AreaRing", ringInstance);
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ring.textureMode = LineTextureMode.Tile;
            ring.textureScale = new Vector2(0.5f, 1f);
            ring.loop = true;
            ring.widthMultiplier = 0.35f;

            // Motes
            var motesGo = new GameObject("GuideMotes");
            motesGo.transform.SetParent(transform, false);
            motes = motesGo.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = motes.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startSpeed = 0f;
            var emission = motes.emission; emission.enabled = false;
            var shape = motes.shape; shape.enabled = false;
            var col = motes.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = motes.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.8f;
            var mr = motesGo.GetComponent<ParticleSystemRenderer>();
            mr.sharedMaterial = glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            motes.Play();

            // Wisp
            var wispGo = new GameObject("GuideWisp");
            wispGo.transform.SetParent(transform, false);
            wisp = wispGo.transform;
            wispTrail = wispGo.AddComponent<TrailRenderer>();
            wispTrail.sharedMaterial = glow;
            wispTrail.time = 0.55f;
            wispTrail.minVertexDistance = 0.08f;
            wispTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 0f));
            wispTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wispTrail.emitting = false;
            wispLight = wispGo.AddComponent<Light>();
            wispLight.type = LightType.Point;
            wispLight.range = 4f;
            wispLight.intensity = 0f;
            wispLight.shadows = LightShadows.None;
        }

        private LineRenderer MakeLine(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        // ================================================================ update

        private void Update()
        {
            bool haveTarget = QuestGuidance.TryGetCurrent(out _, out QuestStage stage, out QuestTarget target);
            bool havePlayer = QuestPlayer.TryGetPosition(out Vector3 player);
            bool insideArea = haveTarget && havePlayer && target.Contains(player);
            float dist = haveTarget && havePlayer ? Flat(target.transform.position - player) : 0f;

            bool show = PlayerEnabled && haveTarget && havePlayer && stage.showTrail && !insideArea && dist > hideWithin;
            alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.deltaTime * (show ? 2f : 3f));

            // Area ring
            QuestTarget wantRing = PlayerEnabled && haveTarget && target.IsArea ? target : null;
            if (wantRing != ringTarget) { ringTarget = wantRing; BuildRing(); }
            if (ringTarget != null)
            {
                float a = (0.55f + 0.25f * Mathf.Sin(Time.time * 2.5f)) * (insideArea ? 0.6f : 1f);
                ring.startColor = ring.endColor = new Color(1f, 1f, 1f, a);
                ringInstance.color = color;
                Scroll(ringInstance, -0.2f);
            }

            if (show)
            {
                repathTimer -= Time.deltaTime;
                if (repathTimer <= 0f || points.Count == 0)
                {
                    repathTimer = repathInterval;
                    Rebuild(player, target);
                }
                else if (points.Count > 0)
                {
                    // Keep the first point glued to the player between repaths.
                    TrimToPlayer(player);
                }
            }

            if (alpha <= 0.001f)
            {
                ribbon.positionCount = 0;
                wispTrail.emitting = false;
                wispLight.intensity = 0f;
                wisp.gameObject.SetActive(false);
                return;
            }

            if (showRibbon) ApplyRibbon();
            else ribbon.positionCount = 0;
            if (showMotes) EmitMotes();
            if (showWisp) UpdateWisp();
            else if (wisp.gameObject.activeSelf) { wispTrail.emitting = false; wispLight.intensity = 0f; wisp.gameObject.SetActive(false); }
        }

        private void Rebuild(Vector3 player, QuestTarget target)
        {
            points.Clear();
            cumulative.Clear();
            length = 0f;

            Vector3 goal = target.transform.position;
            var corners = new List<Vector3>();
            bool ok = NavMesh.SamplePosition(player, out NavMeshHit a, 4f, NavMesh.AllAreas) &
                      NavMesh.SamplePosition(goal, out NavMeshHit b, target.IsArea ? target.areaRadius : 8f, NavMesh.AllAreas);
            if (ok && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, navPath) && navPath.status != NavMeshPathStatus.PathInvalid)
            {
                corners.Add(player);
                corners.AddRange(navPath.corners);
                if (navPath.status == NavMeshPathStatus.PathPartial) corners.Add(goal);
            }
            else
            {
                corners.Add(player);
                corners.Add(goal);
            }

            // Resample into evenly spaced, ground-snapped points.
            float travelled = 0f;
            float next = startOffset;
            for (int i = 0; i < corners.Count - 1; i++)
            {
                Vector3 p0 = corners[i], p1 = corners[i + 1];
                float seg = Vector3.Distance(p0, p1);
                while (next <= travelled + seg)
                {
                    Vector3 p = Vector3.Lerp(p0, p1, (next - travelled) / Mathf.Max(seg, 0.0001f));
                    if (target.IsArea && target.Contains(p)) { Finish(); return; }
                    AddPoint(Ground(p));
                    if (next >= maxLength) { Finish(); return; }
                    next += sampleSpacing;
                }
                travelled += seg;
            }
            Finish();

            void Finish()
            {
                if (points.Count == 1) AddPoint(Ground(goal));
            }
        }

        private void AddPoint(Vector3 p)
        {
            if (points.Count > 0) length += Vector3.Distance(points[points.Count - 1], p);
            points.Add(p);
            cumulative.Add(length);
        }

        private void TrimToPlayer(Vector3 player)
        {
            // Drop points the player has walked past so the ribbon doesn't trail behind them.
            while (points.Count > 2 && Flat(points[1] - player) < Flat(points[0] - player) && Flat(points[0] - player) < startOffset + 1f)
            {
                points.RemoveAt(0);
                cumulative.RemoveAt(0);
            }
        }

        private Vector3 Ground(Vector3 p)
        {
            Vector3 g = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point : p;
            g.y = Mathf.Max(g.y, waterLevel) + groundOffset;
            return g;
        }

        private void ApplyRibbon()
        {
            ribbon.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) ribbon.SetPosition(i, points[i]);

            float fadeIn = length > 0 ? Mathf.Clamp01(1.5f / length) : 0.1f;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(alpha, fadeIn),
                    new GradientAlphaKey(alpha * 0.9f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            ribbon.colorGradient = g;
            ribbonInstance.color = color;
        }

        private void EmitMotes()
        {
            if (points.Count < 2 || length < 0.5f) return;
            moteAccumulator += motesPerMetre * Mathf.Min(length, maxLength) * Time.deltaTime * alpha;
            var ep = new ParticleSystem.EmitParams();
            Color c = new Color(1f, 0.86f, 0.5f, 1f);
            while (moteAccumulator >= 1f)
            {
                moteAccumulator -= 1f;
                float d = Random.Range(0f, length);
                ep.position = Sample(d) + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.05f, 0.4f), Random.Range(-0.35f, 0.35f));
                ep.velocity = new Vector3(0f, Random.Range(0.25f, 0.8f), 0f);
                ep.startColor = c;
                motes.Emit(ep, 1);
            }
        }

        private void UpdateWisp()
        {
            if (points.Count < 2)
            {
                wisp.gameObject.SetActive(false);
                return;
            }
            wisp.gameObject.SetActive(true);
            float range = Mathf.Min(length, wispRange);
            wispDistance += wispSpeed * Time.deltaTime;
            if (wispDistance > range + 2f)
            {
                wispDistance = 0f;
                wispTrail.emitting = false;
                wispTrail.Clear();
            }

            float d = Mathf.Min(wispDistance, range);
            float fade = Mathf.Clamp01(wispDistance / 1.5f) * Mathf.Clamp01((range + 2f - wispDistance) / 2.5f);
            Vector3 pos = Sample(d) + Vector3.up * (wispHeight + 0.15f * Mathf.Sin(Time.time * 6f));
            wisp.position = pos;
            if (wispDistance > 0.05f) wispTrail.emitting = true;
            wispTrail.startColor = new Color(1f, 0.9f, 0.6f, fade * alpha);
            wispTrail.endColor = new Color(1f, 0.7f, 0.3f, 0f);
            wispLight.color = new Color(1f, 0.8f, 0.45f);
            wispLight.intensity = 2.2f * fade * alpha;

            // Sparkle around the wisp head.
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + Random.insideUnitSphere * 0.12f,
                velocity = Random.insideUnitSphere * 0.3f,
                startColor = new Color(1f, 0.95f, 0.75f, fade * alpha),
                startSize = Random.Range(0.08f, 0.2f),
                startLifetime = 0.5f,
            };
            motes.Emit(ep, 1);
        }

        private Vector3 Sample(float d)
        {
            if (points.Count == 0) return transform.position;
            if (d <= 0f) return points[0];
            for (int i = 1; i < points.Count; i++)
            {
                if (cumulative[i] >= d)
                {
                    float seg = cumulative[i] - cumulative[i - 1];
                    return Vector3.Lerp(points[i - 1], points[i], seg > 0 ? (d - cumulative[i - 1]) / seg : 0f);
                }
            }
            return points[points.Count - 1];
        }

        private void BuildRing()
        {
            if (ringTarget == null) { ring.positionCount = 0; return; }
            const int seg = 96;
            ring.positionCount = seg;
            Vector3 c = ringTarget.transform.position;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                ring.SetPosition(i, Ground(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringTarget.areaRadius));
            }
        }

        // ================================================================ helpers

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private static void Scroll(Material m, float speed)
        {
            Vector2 o = m.mainTextureOffset;
            o.x = Mathf.Repeat(o.x + speed * Time.deltaTime, 1f);
            m.mainTextureOffset = o;
        }

        private static void SetTexture(Material m, Texture2D tex)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }

        /// <summary>Soft glowing gold band: bright core fading out to the edges (v runs across the ribbon).</summary>
        private static Texture2D MakeRibbonTexture()
        {
            const int w = 8, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;               // -1..1 across the ribbon
                float core = Mathf.Exp(-v * v * 18f);              // thin bright centre line
                float glow = Mathf.Exp(-v * v * 3.5f) * 0.45f;     // soft halo
                float a = Mathf.Clamp01(core + glow);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D MakeDashTexture()
        {
            const int w = 64, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float dash = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) / 0.3f) > 0f ? 1f : 0.15f;
                    px[y * w + x] = new Color(1f, 1f, 1f, dash * Mathf.Exp(-v * v * 2.5f));
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D MakeSoftDot()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * s + x] = new Color(1f, 1f, 1f, a * a * a);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
