using UnityEngine;
using PirateGame.Combat;
using System;
using Unity.InferenceEngine;

[RequireComponent(typeof(Health))]
public class BloodSplatVFX : MonoBehaviour
{
    [SerializeField] GameObject bloodSplatPrefab;
    [SerializeField] float vfxLifeTime = 2f;
    [SerializeField] private CameraShake cameraShake;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
    }

    void HandleDamaged(DamageInfo info)
    {
        if (health.IsDead)
        {
            return;
        }
        if (bloodSplatPrefab == null)
        {
            return;
        }

        cameraShake.Shake();

        Vector3 spawnPosition = info.HitPoint != Vector3.zero ? info.HitPoint : transform.position + Vector3.up * 1.2f;

        GameObject vfx = Instantiate(bloodSplatPrefab, spawnPosition, Quaternion.identity);
        Destroy(vfx, vfxLifeTime);
    }
}
