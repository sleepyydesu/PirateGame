using System.Collections;
using UnityEngine;
using PirateGame.Combat;

[RequireComponent(typeof(Health))]
public class HitFlash : MonoBehaviour
{
    [SerializeField] Renderer[] renderers; // auto-filled if left empty
    [SerializeField] Color flashColor = Color.white;
    [SerializeField] float flashDuration = 0.08f;

    Health health;
    MaterialPropertyBlock propBlock;
    static readonly int ColorProp = Shader.PropertyToID("_BaseColor"); // URP Lit default; use "_Color" for Built-in RP

    Coroutine flashRoutine;

    void Awake()
    {
        health = GetComponent<Health>();
        propBlock = new MaterialPropertyBlock();

        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>();
        }
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
        if (health.IsDead) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        SetFlash(1f);
        yield return new WaitForSeconds(flashDuration);
        SetFlash(0f);
    }

    void SetFlash(float amount)
    {
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(propBlock);
            propBlock.SetColor("_EmissionColor", flashColor * amount);
            r.SetPropertyBlock(propBlock);
        }
    }
}