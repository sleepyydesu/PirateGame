using System.Collections;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// The merchant ship. Bobs at anchor, rocks harder as the storm builds, drifts toward the
    /// coast, then snaps in two (bow and stern pivot away from the break and settle, half sunk).
    /// The hull halves are pre-split meshes made by the scene builder.
    /// </summary>
    public class ShipWreck : MonoBehaviour
    {
        [Header("Halves (pivots sit on the break line)")]
        [SerializeField] private Transform bowPivot;
        [SerializeField] private Transform sternPivot;
        [SerializeField] private Vector3 bowWreckEuler = new Vector3(16f, 4f, -12f);
        [SerializeField] private Vector3 bowWreckOffset = new Vector3(0.5f, -3.2f, 2.5f);
        [SerializeField] private Vector3 sternWreckEuler = new Vector3(-12f, -6f, 10f);
        [SerializeField] private Vector3 sternWreckOffset = new Vector3(-0.5f, -2.2f, -2f);

        [Header("Drift")]
        [SerializeField] private Vector3 wreckPosition;
        [SerializeField] private float wreckYaw;

        [Header("Break")]
        [SerializeField] private float breakDuration = 7f;
        [SerializeField] private Material splinterMaterial;
        [SerializeField] private int splinterCount = 26;
        [SerializeField] private string wreckedFlag = "ship_wrecked";

        private Vector3 startPos;
        private Quaternion startRot;
        private Vector3 bowRestPos, sternRestPos;
        private Quaternion bowRestRot, sternRestRot;
        private float rock;
        private float drift;
        private float breakT;
        private bool broken;
        private float settle; // 0 = storm motion, 1 = calm wreck

        public bool IsBroken => broken;

        public void Setup(Transform bow, Transform stern, Vector3 wreckPos, float yaw, Material splinters)
        {
            bowPivot = bow; sternPivot = stern; wreckPosition = wreckPos; wreckYaw = yaw; splinterMaterial = splinters;
        }

        private void Awake()
        {
            startPos = transform.position;
            startRot = transform.rotation;
            if (bowPivot) { bowRestPos = bowPivot.localPosition; bowRestRot = bowPivot.localRotation; }
            if (sternPivot) { sternRestPos = sternPivot.localPosition; sternRestRot = sternPivot.localRotation; }
        }

        /// <summary>Storm drives this every frame: intensity 0..1, drift progress 0..1.</summary>
        public void SetStorm(float intensity, float driftProgress)
        {
            rock = Mathf.Clamp01(intensity);
            drift = Mathf.Max(drift, Mathf.Clamp01(driftProgress));
        }

        private void Update()
        {
            float d = Mathf.SmoothStep(0f, 1f, drift);
            Vector3 basePos = Vector3.Lerp(startPos, wreckPosition, d);
            Quaternion baseRot = Quaternion.Slerp(startRot, Quaternion.Euler(0f, wreckYaw, 0f), d);

            if (broken) settle = Mathf.MoveTowards(settle, 1f, Time.deltaTime / breakDuration);

            float t = Time.time;
            float motion = Mathf.Lerp(1f, 0.15f, settle);
            float amp = (0.2f + rock * 0.7f) * motion;
            float roll = Mathf.Sin(t * 0.8f) * (1.5f + rock * 9f) * motion;
            float pitch = Mathf.Sin(t * 0.63f + 1f) * (0.8f + rock * 4f) * motion;
            transform.SetPositionAndRotation(
                basePos + Vector3.up * Mathf.Sin(t * 0.9f) * amp,
                baseRot * Quaternion.Euler(pitch, 0f, roll));

            if (broken && breakT < 1f)
            {
                breakT = Mathf.MoveTowards(breakT, 1f, Time.deltaTime / breakDuration);
                ApplyBreak(breakT);
            }
        }

        private void ApplyBreak(float t)
        {
            // Fast initial snap, then a slow settle into the water.
            float e = 1f - Mathf.Pow(1f - t, 3f);
            if (bowPivot)
            {
                bowPivot.localPosition = bowRestPos + bowWreckOffset * e;
                bowPivot.localRotation = bowRestRot * Quaternion.Euler(bowWreckEuler * e);
            }
            if (sternPivot)
            {
                sternPivot.localPosition = sternRestPos + sternWreckOffset * e;
                sternPivot.localRotation = sternRestRot * Quaternion.Euler(sternWreckEuler * e);
            }
        }

        public void Break()
        {
            if (broken) return;
            broken = true;
            drift = 1f;
            Vector3 at = bowPivot ? bowPivot.position : transform.position;
            QuestAudio.Play(QuestSound.ShipBreak, at, 1f);
            StartCoroutine(Shake(4, 0.25f));
            StartCoroutine(SplashLater(at, 1.2f));
            SpawnSplinters(at);
            QuestManager.Instance?.SetFlag(wreckedFlag);
        }

        public void SetWreckedInstant()
        {
            broken = true;
            drift = 1f;
            breakT = 1f;
            settle = 1f;
            ApplyBreak(1f);
            QuestManager.Instance?.SetFlag(wreckedFlag);
        }

        private IEnumerator SplashLater(Vector3 at, float delay)
        {
            yield return new WaitForSeconds(delay);
            QuestAudio.Play(QuestSound.Splash, at, 1f);
        }

        private static IEnumerator Shake(int times, float gap)
        {
            var shaker = FindFirstObjectByType<CameraShake>();
            for (int i = 0; i < times && shaker != null; i++)
            {
                shaker.Shake();
                yield return new WaitForSeconds(gap);
            }
        }

        private void SpawnSplinters(Vector3 at)
        {
            for (int i = 0; i < splinterCount; i++)
            {
                GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = "Splinter";
                Destroy(p.GetComponent<Collider>());
                p.transform.position = at + Random.insideUnitSphere * 2.5f + Vector3.up * 2f;
                p.transform.rotation = Random.rotation;
                p.transform.localScale = new Vector3(Random.Range(0.08f, 0.2f), Random.Range(0.05f, 0.1f), Random.Range(0.6f, 1.8f));
                if (splinterMaterial != null) p.GetComponent<Renderer>().sharedMaterial = splinterMaterial;
                var rb = p.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                rb.linearVelocity = (Random.insideUnitSphere + Vector3.up * 1.4f) * Random.Range(4f, 9f);
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                Destroy(p, Random.Range(3f, 5f));
            }
        }
    }
}
