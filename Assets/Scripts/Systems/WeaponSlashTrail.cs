using UnityEngine;

/// <summary>
/// Boss/enemy connector for the shared procedural SwordSlashRibbon renderer.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponSlashTrail : MonoBehaviour
{
    [Header("Blade points")]
    [SerializeField] Transform bladeBase;
    [SerializeField] Transform bladeTip;

    [SerializeField]
    string bladeBasePointName = "SwordTrailPoint";

    [SerializeField]
    string bladeTipPointName = "SlashSpawnPoint";

    [Header("Look (used when Profile is empty)")]
    [SerializeField] Material ribbonMaterial;

    [SerializeField, ColorUsage(true, true)]
    Color ribbonColor = new Color(1f, 0.28f, 0.12f, 1f);

    [SerializeField, Min(0.01f)]
    float trailDuration = 0.22f;

    [SerializeField, Min(0.001f)]
    float sampleDistance = 0.025f;

    [SerializeField, Min(0.01f)]
    float bladeWidth = 1.1f;

    [SerializeField, Min(8)]
    int maxSamples = 48;

    [SerializeField, Min(0.05f)]
    float maxEmissionTime = 1.8f;

    [Header("Optional shared profile")]
    [SerializeField] SlashTrailProfile profile;

    SwordSlashRibbon ribbon;
    Transform resolvedBase;
    Transform resolvedTip;

    void Awake()
    {
        if (bladeBase == null)
            bladeBase = FindChildByName(bladeBasePointName);

        if (bladeTip == null)
            bladeTip = FindChildByName(bladeTipPointName);

        resolvedBase = bladeBase;
        resolvedTip = bladeTip;

        if (resolvedBase == null || resolvedTip == null)
        {
            Debug.LogWarning(
                $"{name}: WeaponSlashTrail needs blade base/tip transforms. " +
                $"Assign them or add children named '{bladeBasePointName}' " +
                $"and '{bladeTipPointName}'.",
                this);

            return;
        }

        ribbon = GetComponent<SwordSlashRibbon>();

        if (ribbon == null)
            ribbon = gameObject.AddComponent<SwordSlashRibbon>();

        if (profile != null)
        {
            ribbon.ConfigureProfile(
                resolvedBase,
                resolvedTip,
                profile,
                ribbonMaterial);
        }
        else
        {
            ribbon.Configure(
                resolvedBase,
                resolvedTip,
                ribbonMaterial,
                ribbonColor,
                trailDuration,
                sampleDistance,
                bladeWidth,
                maxSamples);
        }
    }

    /// <summary>Call from an Animation Event at the start of the visible cut.</summary>
    public void Begin()
    {
        if (ribbon == null)
            return;

        if (profile != null)
        {
            ribbon.BeginSlash(
                resolvedBase,
                resolvedTip,
                profile);
        }
        else
        {
            ribbon.BeginSlash(
                resolvedBase,
                resolvedTip,
                BuildLegacyProfile());
        }
    }

    /// <summary>Call from an Animation Event at the end of the visible cut.</summary>
    public void End()
    {
        ribbon?.EndSlash();
    }

    // Animation Event-compatible alternatives.
    public void SlashStart() => Begin();
    public void SlashEnd() => End();

    public void BeginSlash() => Begin();
    public void EndSlash() => End();

    /// <summary>Optional combat hook for parry or impact feedback.</summary>
    public void NotifyParryOrImpact()
    {
        ribbon?.NotifyBladeImpact();
    }

    /// <summary>Immediately removes all trails, including fading ones.</summary>
    public void CancelTrail()
    {
        ribbon?.CancelAndClear();
    }

    SlashTrailProfile BuildLegacyProfile()
    {
        return new SlashTrailProfile
        {
            color = ribbonColor,
            width = 1f,
            lifetime = Mathf.Max(0.01f, trailDuration),
            sampleDistance = Mathf.Max(0.001f, sampleDistance),
            maxSamples = Mathf.Clamp(maxSamples, 8, 96),
            bladeWidthMultiplier = Mathf.Max(0.01f, bladeWidth),
            midSwingBulge = 0.28f,
            endTaper = 0.18f,
            maxEmissionTime = Mathf.Max(0.05f, maxEmissionTime),
            useUnscaledTime = false
        };
    }

    Transform FindChildByName(string childName)
    {
        Transform[] all =
            GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == childName)
                return all[i];
        }

        return null;
    }

    void OnDisable()
    {
        ribbon?.CancelAndClear();
    }
}