using System.Collections;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Quest A, step 1. First time the player enters the storm area a 30 s storm rolls in over
    /// the sea, the merchant ship is driven toward the coast and breaks apart, then the storm
    /// clears. Sets storm_started / storm_done so it can never re-trigger and so Quest A
    /// (which requires storm_done) becomes available afterwards.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StormEvent : MonoBehaviour
    {
        public enum Phase { Waiting, Running, Done }

        [Header("Flow")]
        [SerializeField] private float duration = 30f;
        [SerializeField] private float fadeIn = 4f;
        [SerializeField] private float fadeOut = 4f;
        [SerializeField] private float breakAt = 15f;
        [SerializeField] private string startedFlag = "storm_started";
        [SerializeField] private string doneFlag = "storm_done";

        [Header("Scene refs")]
        [SerializeField] private ShipWreck ship;
        [SerializeField] private Light sun;
        [Tooltip("Where the clouds gather (over the ship, across the water from the player).")]
        [SerializeField] private Transform stormCenter;

        [Header("Look")]
        [SerializeField] private Material rainMaterial;
        [SerializeField] private Material cloudMaterial;
        [SerializeField] private Material boltMaterial;
        [SerializeField] private Color stormSunColor = new Color(0.55f, 0.6f, 0.72f);
        [SerializeField, Range(0f, 1f)] private float stormSunFactor = 0.18f;
        [SerializeField] private Color fogColor = new Color(0.28f, 0.31f, 0.36f);
        [SerializeField] private float fogDensity = 0.016f;

        private Collider area;
        private float intensity;
        private ParticleSystem cameraRain, distantRain, clouds;
        private LineRenderer bolt;
        private Light flash;
        private AudioSource wind, rain;

        private float sunIntensity0;
        private Color sunColor0, ambient0, fogColor0;
        private bool fog0;
        private float fogDensity0;
        private FogMode fogMode0;

        public Phase Current { get; private set; } = Phase.Waiting;
        public float Elapsed { get; private set; }

        public void Setup(ShipWreck wreck, Light sunLight, Transform center, Material rainMat, Material cloudMat, Material boltMat)
        {
            ship = wreck; sun = sunLight; stormCenter = center; rainMaterial = rainMat; cloudMaterial = cloudMat; boltMaterial = boltMat;
        }

        private void Awake()
        {
            area = GetComponent<Collider>();
            area.isTrigger = true;
        }

        private void Start()
        {
            if (QuestManager.Instance != null && QuestManager.Instance.HasFlag(doneFlag))
            {
                Current = Phase.Done;
                ship?.SetWreckedInstant();
            }
        }

        private void Update()
        {
            if (Current != Phase.Waiting) return;
            if (QuestManager.Instance != null && QuestManager.Instance.HasFlag(doneFlag)) { Current = Phase.Done; return; }
            if (!QuestPlayer.TryGetPosition(out Vector3 p)) return;
            Vector3 probe = p + Vector3.up * 0.5f;
            if ((area.ClosestPoint(probe) - probe).sqrMagnitude < 0.0001f) Begin();
        }

        private void LateUpdate()
        {
            if (cameraRain != null && Camera.main != null)
                cameraRain.transform.position = Camera.main.transform.position + Vector3.up * 14f;
        }

        public void Begin()
        {
            if (Current != Phase.Waiting) return;
            Current = Phase.Running;
            BuildEffects();
            CaptureLighting();
            QuestManager.Instance?.SetFlag(startedFlag);
            QuestManager.Instance?.Report("storm_started");
            QuestUI.Toast("A storm rolls in!", "Dark clouds gather over the merchant ship…", ToastKind.Warning);
            StartCoroutine(Run());
        }

        /// <summary>QA: jump straight to the aftermath.</summary>
        public void CompleteInstantly()
        {
            if (Current == Phase.Done) return;
            StopAllCoroutines();
            if (Current == Phase.Running) { SetIntensity(0f); StopEffects(); }
            QuestManager.Instance?.SetFlag(startedFlag);
            ship?.SetWreckedInstant();
            Finish();
        }

        private IEnumerator Run()
        {
            Elapsed = 0f;
            float nextBolt = 2.5f;
            bool broke = false;
            wind = QuestAudio.CreateLoop(QuestLoop.Wind, transform);
            rain = QuestAudio.CreateLoop(QuestLoop.Rain, transform);
            cameraRain.Play(); distantRain.Play(); clouds.Play();

            while (Elapsed < duration)
            {
                Elapsed += Time.deltaTime;
                float i = Mathf.Min(Mathf.Clamp01(Elapsed / fadeIn), Mathf.Clamp01((duration - Elapsed) / fadeOut));
                SetIntensity(i);
                ship?.SetStorm(i, Elapsed / breakAt);

                if (!broke && Elapsed >= breakAt)
                {
                    broke = true;
                    ship?.Break();
                    StartCoroutine(Lightning(true));
                }

                if (Elapsed >= nextBolt && Elapsed < duration - fadeOut)
                {
                    nextBolt = Elapsed + Random.Range(2.2f, 4.5f);
                    StartCoroutine(Lightning(false));
                }

                if (Elapsed > duration - fadeOut && cameraRain.isEmitting)
                {
                    cameraRain.Stop(); distantRain.Stop(); clouds.Stop();
                }
                yield return null;
            }

            SetIntensity(0f);
            ship?.SetStorm(0f, 1f);
            if (!broke) ship?.Break();
            yield return new WaitForSeconds(1f);
            StopEffects();
            Finish();
        }

        private void Finish()
        {
            Current = Phase.Done;
            QuestManager.Instance?.SetFlag(doneFlag);
            QuestManager.Instance?.Report("storm_finished");
            QuestUI.Toast("The storm has passed", "The merchant ship was wrecked on the coast. Someone may need help.", ToastKind.Quest);
        }

        // ----------------------------------------------------------------- lighting

        private void CaptureLighting()
        {
            if (sun != null) { sunIntensity0 = sun.intensity; sunColor0 = sun.color; }
            ambient0 = RenderSettings.ambientLight;
            fog0 = RenderSettings.fog;
            fogColor0 = RenderSettings.fogColor;
            fogDensity0 = RenderSettings.fogDensity;
            fogMode0 = RenderSettings.fogMode;
        }

        private void SetIntensity(float i)
        {
            intensity = i;
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sunIntensity0, sunIntensity0 * stormSunFactor, i);
                sun.color = Color.Lerp(sunColor0, stormSunColor, i);
            }
            RenderSettings.ambientLight = Color.Lerp(ambient0, ambient0 * 0.45f, i);
            if (i > 0.001f || fog0)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(fogColor0, fogColor, i);
                RenderSettings.fogDensity = Mathf.Lerp(fog0 ? fogDensity0 : 0f, fogDensity, i);
            }
            if (i <= 0.001f)
            {
                RenderSettings.fog = fog0;
                RenderSettings.fogMode = fogMode0;
                RenderSettings.fogDensity = fogDensity0;
                RenderSettings.fogColor = fogColor0;
            }

            float vol = QuestAudio.MasterVolume;
            if (wind) wind.volume = i * 0.55f * vol;
            if (rain) rain.volume = i * 0.45f * vol;

            if (cameraRain != null)
            {
                var em = cameraRain.emission;
                em.rateOverTime = 1800f * i;
            }
        }

        private IEnumerator Lightning(bool big)
        {
            Vector3 c = stormCenter != null ? stormCenter.position : transform.position;
            Vector3 top = c + new Vector3(Random.Range(-35f, 35f), 38f, Random.Range(-20f, 30f));
            Vector3 bottom = new Vector3(top.x + Random.Range(-8f, 8f), 0f, top.z + Random.Range(-8f, 8f));
            if (big && ship != null) bottom = ship.transform.position + Vector3.up * 6f;

            const int segs = 14;
            bolt.positionCount = segs + 1;
            for (int k = 0; k <= segs; k++)
            {
                float t = k / (float)segs;
                Vector3 p = Vector3.Lerp(top, bottom, t);
                if (k > 0 && k < segs) p += new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
                bolt.SetPosition(k, p);
            }

            for (int flicker = 0; flicker < 2; flicker++)
            {
                bolt.enabled = true;
                flash.intensity = big ? 1.3f : 0.8f;
                yield return new WaitForSeconds(0.07f);
                bolt.enabled = false;
                flash.intensity = 0.15f;
                yield return new WaitForSeconds(0.06f);
            }
            flash.intensity = 0f;

            float dist = QuestPlayer.TryGetPosition(out Vector3 pp) ? Vector3.Distance(pp, bottom) : 60f;
            yield return new WaitForSeconds(Mathf.Clamp(dist / 340f, 0.1f, 1.5f));
            QuestAudio.Play(QuestSound.Thunder, null, big ? 1f : 0.7f);
            var shaker = FindFirstObjectByType<CameraShake>();
            if (shaker != null) shaker.Shake();
        }

        // ----------------------------------------------------------------- particles

        private void BuildEffects()
        {
            Vector3 c = stormCenter != null ? stormCenter.position : transform.position;

            cameraRain = MakeSystem("Rain_Camera", Camera.main != null ? Camera.main.transform.position : c, rainMaterial);
            ConfigureRain(cameraRain, 45f, 1800f, 0.035f, new Color(0.78f, 0.82f, 0.9f, 0.45f), 26f);

            distantRain = MakeSystem("Rain_Storm", c + Vector3.up * 34f, rainMaterial);
            ConfigureRain(distantRain, 110f, 2500f, 0.12f, new Color(0.6f, 0.65f, 0.72f, 0.35f), 30f);

            clouds = MakeSystem("StormClouds", c + Vector3.up * 40f, cloudMaterial);
            var main = clouds.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(28f, 48f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.16f, 0.17f, 0.2f, 0.9f), new Color(0.27f, 0.28f, 0.32f, 0.9f));
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = clouds.emission; em.rateOverTime = 22f;
            var sh = clouds.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(140f, 8f, 110f);
            var col = clouds.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = clouds.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = clouds.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = 50f;

            var boltGo = new GameObject("LightningBolt");
            boltGo.transform.SetParent(transform, false);
            bolt = boltGo.AddComponent<LineRenderer>();
            bolt.sharedMaterial = boltMaterial;
            bolt.widthMultiplier = 0.7f;
            bolt.startColor = bolt.endColor = new Color(0.85f, 0.9f, 1f, 1f);
            bolt.useWorldSpace = true;
            bolt.enabled = false;
            bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var flashGo = new GameObject("LightningFlash");
            flashGo.transform.SetParent(transform, false);
            flashGo.transform.rotation = Quaternion.Euler(60f, 200f, 0f);
            flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Directional;
            flash.color = new Color(0.9f, 0.92f, 1f);
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
        }

        private ParticleSystem MakeSystem(string name, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void ConfigureRain(ParticleSystem ps, float area, float rate, float size, Color color, float speed)
        {
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // emit straight down
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.9f, speed * 1.1f);
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(area, area, 1f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(3f, 5f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2f;
        }

        private void StopEffects()
        {
            if (wind) Destroy(wind);
            if (rain) Destroy(rain);
            foreach (var ps in new[] { cameraRain, distantRain, clouds })
                if (ps != null) Destroy(ps.gameObject, 15f);
            if (bolt) Destroy(bolt.gameObject);
            if (flash) Destroy(flash.gameObject);
        }
    }
}
