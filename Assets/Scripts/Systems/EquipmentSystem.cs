using Unity.VisualScripting;
using UnityEngine;

public class EquipmentSystem : MonoBehaviour
{
    DamageDealer damageDealer;

    /// <summary>The DamageDealer on the spawned sword (used by quest upgrades).</summary>
    public DamageDealer WeaponDamageDealer => damageDealer;

    [SerializeField] GameObject weaponHolder;
    [SerializeField] GameObject weaponPrefab;
    [SerializeField] GameObject weaponSheath;
    [SerializeField] GameObject slashVFXPrefab;
    [Tooltip("Optional fallback only. The spawn point on the instantiated sword is used first.")]
    [SerializeField] Transform slashSpawnPoint;
    [SerializeField] string slashSpawnPointName = "SlashSpawnPoint";
    [SerializeField, Min(0.1f)] float slashVFXLifetime = 2f;

    [Header("Slash VFX Timing")]
    [Tooltip("The attack clip that fires PlaySlashVFX. Assigning it makes the VFX automatically last as long as the selected part of that swing.")]
    [SerializeField] AnimationClip slashAnimation;
    [Tooltip("Normalized time in Slash Animation where the visible sword cut begins.")]
    [SerializeField, Range(0f, 1f)] float slashStartNormalizedTime = 0f;
    [Tooltip("Normalized time in Slash Animation where the visible sword cut ends.")]
    [SerializeField, Range(0f, 1f)] float slashEndNormalizedTime = 1f;
    [Tooltip("Used only when Slash Animation is not assigned.")]
    [SerializeField, Min(0.01f)] float fallbackSlashDuration = 1f;

    [Header("Procedural Sword Slash Ribbon")]
    [SerializeField] bool useProceduralSlashRibbon = true;
    [Tooltip("Optional. Use a transparent/additive material that supports vertex colours for the best result.")]
    [SerializeField] Material slashRibbonMaterial;
    [SerializeField] string bladeBasePointName = "SwordTrailPoint";
    [SerializeField, ColorUsage(true, true)] Color slashRibbonColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    [SerializeField, Min(0.01f)] float slashRibbonDuration = 0.16f;
    [SerializeField, Min(0.001f)] float slashRibbonSampleDistance = 0.025f;
    [Tooltip("1 matches the selected blade base/tip. Values above 1 widen the ribbon along the blade.")]
    [SerializeField, Min(0.01f)] float slashRibbonBladeWidth = 1.25f;
    [SerializeField, Min(2)] int slashRibbonMaxSamples = 40;

    GameObject weaponInstance;
    Transform runtimeSlashSpawnPoint;
    Transform runtimeBladeBasePoint;
    ParticleSystem Trail;
    SwordSlashRibbon slashRibbon;

    void Awake()
    {
        weaponInstance = Instantiate(
            weaponPrefab,
            weaponSheath.transform,
            false
        );

        Trail = weaponInstance.GetComponentInChildren<ParticleSystem>();

        damageDealer = weaponInstance.GetComponentInChildren<DamageDealer>(true);

        runtimeSlashSpawnPoint = FindChildByName(
            weaponInstance.transform,
            slashSpawnPointName
        );

        runtimeBladeBasePoint =
            FindChildByName(
                weaponInstance.transform,
                bladeBasePointName
            ) ?? weaponInstance.transform;

        if (useProceduralSlashRibbon && runtimeSlashSpawnPoint != null)
        {
            slashRibbon = weaponInstance.AddComponent<SwordSlashRibbon>();

            slashRibbon.Configure(
                runtimeBladeBasePoint,
                runtimeSlashSpawnPoint,
                slashRibbonMaterial,
                slashRibbonColor,
                slashRibbonDuration,
                slashRibbonSampleDistance,
                slashRibbonBladeWidth,
                slashRibbonMaxSamples
            );
        }

        if (runtimeSlashSpawnPoint == null)
        {
            Debug.LogWarning(
                $"Could not find '{slashSpawnPointName}' on the spawned weapon. " +
                "The slash will use the weapon root instead.",
                this
            );
        }

        weaponInstance.SetActive(true);
    }

    public void DrawWeapon()
    {
        weaponInstance.transform.SetParent(weaponHolder.transform, false);
    }

    public void SheathWeapon()
    {
        weaponInstance.transform.SetParent(weaponSheath.transform, false);
    }

    public void StartTrail()
    {
        if (Trail != null) Trail.Play();
        slashRibbon?.Begin();
    }

    public void StopTrail()
    {
        if (Trail != null) Trail.Stop();
        slashRibbon?.End();
    }

    // Preferred Animation Event names for the custom ribbon. Start this at the
    // first frame of blade movement and stop it when the cut has finished.
    public void StartSlashVFX() => StartTrail();
    public void StopSlashVFX() => StopTrail();

    // called via Animation Event at the moment of the swing/impact
    public void PlaySlashVFX()
    {
        PlaySlashVFXForDuration(GetSlashDuration());
    }

    // Use this from an Animation Event when different attacks have different
    // visible cut lengths. Set the event's Float parameter to that length in seconds.
    public void PlaySlashVFXForDuration(float cutDuration)
    {
        if (slashVFXPrefab == null || weaponInstance == null) return;

        // slashSpawnPoint in this scene currently points to a prefab asset.  That
        // asset never follows the animated sword, so use its clone in weaponInstance.
        Transform spawnPoint = runtimeSlashSpawnPoint != null
            ? runtimeSlashSpawnPoint
            : slashSpawnPoint != null && slashSpawnPoint.gameObject.scene.IsValid()
                ? slashSpawnPoint
                : weaponInstance.transform;

        GameObject vfx = Instantiate(slashVFXPrefab, spawnPoint.position, spawnPoint.rotation);
        float targetDuration = Mathf.Max(cutDuration, 0.01f);

        foreach (ParticleSystem particle in vfx.GetComponentsInChildren<ParticleSystem>(true))
        {
            // The imported particles were authored as one-second systems. Scale their
            // simulation to the same duration as the sword's visible cut.
            ParticleSystem.MainModule main = particle.main;
            main.simulationSpeed = Mathf.Max(main.duration, 0.01f) / targetDuration;
            particle.Clear(true);
            particle.Play(true);
        }

        Destroy(vfx, Mathf.Max(slashVFXLifetime, targetDuration + 0.5f));
    }

    float GetSlashDuration()
    {
        if (slashAnimation == null)
            return fallbackSlashDuration;

        float start = Mathf.Min(slashStartNormalizedTime, slashEndNormalizedTime);
        float end = Mathf.Max(slashStartNormalizedTime, slashEndNormalizedTime);
        float animatorSpeed = GetComponent<Animator>()?.speed ?? 1f;

        return Mathf.Max((end - start) * slashAnimation.length / Mathf.Max(animatorSpeed, 0.01f), 0.01f);
    }

    static Transform FindChildByName(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == childName)
                return child;

        return null;
    }

    public void StartDealDamage()
    {
        if (damageDealer == null)
        {
            Debug.LogError("DamageDealer was not found on the instantiated weapon!");
            return;
        }

        damageDealer.StartDealDamage();
    }

    public void EndDealDamage()
    {
        if (damageDealer == null)
        {
            Debug.LogError("DamageDealer was not found on the instantiated weapon!");
            return;
        }

        damageDealer.EndDealDamage();
    }
}
