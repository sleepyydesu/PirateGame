using UnityEngine;
using Unity.Cinemachine;
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;

public class CameraShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [SerializeField] private float shakeAmplitude = 1.5f;
    [SerializeField] private float shakeFrequency = 2f;
    [SerializeField] private float shakeDuration = 0.15f;

    private CinemachineBasicMultiChannelPerlin noise;
    private float shakeTimer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        noise =  GetComponent<CinemachineBasicMultiChannelPerlin>();

        if (noise == null)
        {
            Debug.LogError("CinemachineBasicMulriChanelPerlin not found.");
        }
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
        if (noise == null)
        {
            return;
        }

        shakeTimer = shakeDuration;

        noise.AmplitudeGain = shakeAmplitude;
        noise.FrequencyGain = shakeFrequency;
    }

    private void StopShake()
    {
        if (noise == null)
        {
            return;
        }

        noise.AmplitudeGain = 0f;
        noise.FrequencyGain = 0f;
    }
}
