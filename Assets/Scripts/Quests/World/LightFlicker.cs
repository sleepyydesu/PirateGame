using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Firelight / torch flicker.</summary>
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        [SerializeField] private float baseIntensity = 2f;
        [SerializeField] private float amount = 0.6f;
        [SerializeField] private float speed = 8f;

        private Light lamp;
        private float seed;

        private void Awake()
        {
            lamp = GetComponent<Light>();
            seed = Random.value * 100f;
            if (baseIntensity <= 0f) baseIntensity = lamp.intensity;
        }

        public void Setup(float intensity, float flicker) { baseIntensity = intensity; amount = flicker; }

        private void Update()
        {
            lamp.intensity = baseIntensity + (Mathf.PerlinNoise(seed, Time.time * speed) - 0.5f) * 2f * amount;
        }
    }
}
