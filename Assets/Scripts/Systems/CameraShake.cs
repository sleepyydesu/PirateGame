using UnityEngine;
using Unity.Cinemachine;

public class CameraShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [SerializeField] private float shakeAmplitude = 1.5f;
    [SerializeField] private float shakeFrequency = 2f;
    [SerializeField] private float shakeDuration = 0.15f;

    private CinemachineBasicMultiChannelPerlin noise;
    private float shakeTimer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        noise = GetComponent<CinemachineBasicMultiChannelPerlin>();

        if (noise == null)
        {
            Debug.LogError(
                "CinemachineBasicMultiChannelPerlin not found.",
                this);
        }

        StopShake();
    }

    // Update is called once per frame
    void Update()
    {
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;

            if (shakeTimer <= 0f)
            {
                StopShake();
            }
        }
    }

    public void Shake()
    {
        Shake(shakeAmplitude, shakeFrequency, shakeDuration);
    }

    public void Shake(float amplitude, float frequency, float duration)
    {
        if (noise == null ||
            !isActiveAndEnabled ||
            duration <= 0f)
        {
            return;
        }

        amplitude = Mathf.Max(0f, amplitude);
        frequency = Mathf.Max(0f, frequency);

        if (shakeTimer <= 0f)
        {
            noise.AmplitudeGain = amplitude;
            noise.FrequencyGain = frequency;
        }
        else
        {
            noise.AmplitudeGain =
                Mathf.Max(noise.AmplitudeGain, amplitude);

            noise.FrequencyGain =
                Mathf.Max(noise.FrequencyGain, frequency);
        }

        shakeTimer = Mathf.Max(shakeTimer, duration);
    }

    private void StopShake()
    {
        shakeTimer = 0f;
        if (noise == null)
        {
            return;
        }

        noise.AmplitudeGain = 0f;
        noise.FrequencyGain = 0f;
    }

    private void OnDisable()
    {
        StopShake();
    }
}
