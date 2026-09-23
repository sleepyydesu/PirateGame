using UnityEngine;

/// <summary>
/// Gives any character the same procedural slash ribbon the player's sword
/// uses, without the player-specific equipment logic.
///
/// SETUP:
///   1. Add this component to the SAME GameObject that has the character's
///      Animator (animation events only reach components on that object).
///   2. Assign Blade Base / Blade Tip — two transforms that span the cutting
///      edge (wrist→claws for unarmed attacks, hilt→tip for a sword).
///      Left empty, they are searched for by child name using the player
///      sword's convention ("SwordTrailPoint" / "SlashSpawnPoint"), so a
///      shared weapon prefab works with zero extra wiring.
///   3. On each attack clip (FBX import settings → Animation tab → Events),
///      add an event calling Begin() where the swing starts and one calling
///      End() where the cut finishes.
///
/// If an attack is interrupted (parry, stagger, death) the clip's End event
/// never fires — <see cref="maxEmissionTime"/> stops the ribbon automatically
/// so it can't get stuck on.
/// </summary>
public class WeaponSlashTrail : MonoBehaviour
{
    [Header("Blade points")]
    [SerializeField] Transform bladeBase;
    [SerializeField] Transform bladeTip;
    [Tooltip("Child names to search when the transforms above are not assigned.")]
    [SerializeField] string bladeBasePointName = "SwordTrailPoint";
    [SerializeField] string bladeTipPointName = "SlashSpawnPoint";

    [Header("Ribbon look")]
    [Tooltip("Optional. A transparent/additive material with vertex colour support looks best. Falls back to URP Particles/Unlit.")]
    [SerializeField] Material ribbonMaterial;
    [SerializeField, ColorUsage(true, true)] Color ribbonColor = new Color(1f, 0.35f, 0.1f, 0.8f);
    [SerializeField, Min(0.01f)] float trailDuration = 0.18f;
    [SerializeField, Min(0.001f)] float sampleDistance = 0.03f;
    [SerializeField, Min(0.01f)] float bladeWidth = 1.1f;
    [SerializeField, Min(2)] int maxSamples = 40;

    [Header("Safety")]
    [Tooltip("Emitting stops automatically after this many seconds, so an interrupted attack never leaves the trail running when no End event fires.")]
    [SerializeField, Min(0.05f)] float maxEmissionTime = 2f;

    SwordSlashRibbon ribbon;
    float emitTimeRemaining;

    void Awake()
    {
        if (bladeBase == null) bladeBase = FindChildByName(bladeBasePointName);
        if (bladeTip == null) bladeTip = FindChildByName(bladeTipPointName);

        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogWarning(
                $"{name}: WeaponSlashTrail found no blade base/tip transforms. " +
                "Assign them in the Inspector or add children named " +
                $"'{bladeBasePointName}' / '{bladeTipPointName}'.",
                this
            );
            return;
        }

        ribbon = gameObject.AddComponent<SwordSlashRibbon>();
        ribbon.Configure(
            bladeBase,
            bladeTip,
            ribbonMaterial,
            ribbonColor,
            trailDuration,
            sampleDistance,
            bladeWidth,
            maxSamples
        );
    }

    /// <summary>Start emitting the ribbon. Call from an animation event at the swing's first frame.</summary>
    public void Begin()
    {
        if (ribbon == null) return;
        emitTimeRemaining = maxEmissionTime;
        ribbon.Begin();
    }

    /// <summary>Stop emitting; the ribbon fades out on its own. Call from an animation event when the cut has finished.</summary>
    public void End()
    {
        emitTimeRemaining = 0f;
        ribbon?.End();
    }

    void Update()
    {
        if (emitTimeRemaining <= 0f) return;

        emitTimeRemaining -= Time.deltaTime;
        if (emitTimeRemaining <= 0f)
            ribbon.End();
    }

    Transform FindChildByName(string childName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == childName)
                return child;

        return null;
    }
}
