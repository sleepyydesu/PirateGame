using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reusable visual profile for a procedural weapon slash.</summary>
[Serializable]
public sealed class SlashTrailProfile
{
    [ColorUsage(true, true)]
    public Color color = new Color(0.72f, 0.92f, 1f, 1f);

    [Tooltip("Scales the sampled blade span in addition to Blade Width Multiplier.")]
    [Min(0.1f)]
    public float width = 1f;

    [Min(0.01f)]
    public float lifetime = 0.16f;

    [Min(0.001f)]
    public float sampleDistance = 0.018f;

    [Range(8, 96)]
    public int maxSamples = 90;

    [Min(0.001f)]
    public float bladeWidthMultiplier = 1f;

    [Range(0f, 1f)]
    public float midSwingBulge = 0.22f;

    [Range(0f, 0.45f)]
    public float endTaper = 0.2f;

    [Min(0.01f)]
    public float maxEmissionTime = 1.2f;

    public bool useUnscaledTime;

    [Tooltip("Optional shader UV detail scrolling speed.")]
    public float uvScrollSpeed = 0f;

    public static SlashTrailProfile PlayerDefault()
    {
        return new SlashTrailProfile
        {
            color = new Color(0.58f, 0.86f, 1f, 1f),
            width = 1f,
            lifetime = 0.15f,
            sampleDistance = 0.015f,
            maxSamples = 40,
            bladeWidthMultiplier = 1f,
            midSwingBulge = 0.18f,
            endTaper = 0.22f,
            maxEmissionTime = 0.9f,
            useUnscaledTime = false,
            uvScrollSpeed = 0f
        };
    }

    public static SlashTrailProfile BossDefault()
    {
        return new SlashTrailProfile
        {
            color = new Color(1f, 0.28f, 0.12f, 1f),
            width = 1.12f,
            lifetime = 0.22f,
            sampleDistance = 0.025f,
            maxSamples = 48,
            bladeWidthMultiplier = 1.08f,
            midSwingBulge = 0.28f,
            endTaper = 0.18f,
            maxEmissionTime = 1.8f,
            useUnscaledTime = false,
            uvScrollSpeed = 0f
        };
    }
}

/// <summary>
/// Shared player/boss slash renderer.
///
/// Samples blade base and tip in world space and generates a procedural
/// ribbon mesh. Finished slashes remain in world space and fade independently.
///
/// Attach to the weapon or character. Start and stop emission using animation
/// events or the public combat API.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordSlashRibbon : MonoBehaviour
{
    [Header("Shared renderer")]
    [SerializeField] Material ribbonMaterial;
    [SerializeField] SlashTrailProfile defaultProfile;

    [SerializeField, Range(1, 8)]
    int initialPoolSize = 3;

    [SerializeField, Range(8, 96)]
    int poolMaxSamples = 48;

    [SerializeField]
    string shaderName = "PirateGame/Procedural Slash Additive";

    sealed class TrailSlot
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;

        public Vector3[] vertices;
        public Vector2[] uvs;
        public Color[] colors;
        public int[] triangles;

        public Vector3[] bases;
        public Vector3[] tips;
        public float[] sampleTimes;

        public int count;
        public bool emitting;
        public bool occupied;

        public float startedAt;
        public float endedAt;
        public float lastEmitAt;

        public Transform bladeBase;
        public Transform bladeTip;
        public SlashTrailProfile profile;
    }

    TrailSlot[] slots;

    Material runtimeMaterial;
    MaterialPropertyBlock propertyBlock;

    Transform configuredBase;
    Transform configuredTip;
    SlashTrailProfile configuredProfile;

    /// <summary>
    /// Raised at the current blade tip when combat code reports an impact.
    /// The system does not create spark effects itself.
    /// </summary>
    public event Action<Vector3> BladeImpactPoint;

    void Awake()
    {
        if (defaultProfile == null)
            defaultProfile = SlashTrailProfile.PlayerDefault();

        EnsureMaterial();
        EnsurePool(Mathf.Max(1, initialPoolSize));
    }

    /// <summary>
    /// Legacy-compatible configuration used by EquipmentSystem.
    /// Existing calls to Configure(...) can remain unchanged.
    /// </summary>
    public void Configure(
        Transform baseTransform,
        Transform tipTransform,
        Material material,
        Color color,
        float trailDuration,
        float sampleDistance,
        float widthMultiplier,
        int maximumSamples)
    {
        configuredBase = baseTransform;
        configuredTip = tipTransform;

        if (material != null)
            ribbonMaterial = material;

        configuredProfile = new SlashTrailProfile
        {
            color = color,
            width = 1f,
            lifetime = Mathf.Max(0.01f, trailDuration),
            sampleDistance = Mathf.Max(0.001f, sampleDistance),
            maxSamples = Mathf.Clamp(maximumSamples, 8, 96),
            bladeWidthMultiplier = Mathf.Max(0.01f, widthMultiplier),
            midSwingBulge = 0.2f,
            endTaper = 0.2f,
            maxEmissionTime = 1.5f,
            useUnscaledTime = false
        };

        EnsureMaterial();
        EnsurePool(Mathf.Max(1, initialPoolSize));
    }

    /// <summary>Configure the shared renderer with an explicit profile.</summary>
    public void ConfigureProfile(
        Transform baseTransform,
        Transform tipTransform,
        SlashTrailProfile profile,
        Material material = null)
    {
        configuredBase = baseTransform;
        configuredTip = tipTransform;

        configuredProfile = profile != null
            ? profile
            : defaultProfile;

        if (material != null)
            ribbonMaterial = material;

        EnsureMaterial();
        EnsurePool(Mathf.Max(1, initialPoolSize));
    }

    /// <summary>
    /// Starts a new slash. An older fading slash is not cleared.
    /// </summary>
    public void BeginSlash(
        Transform bladeBase,
        Transform bladeTip,
        SlashTrailProfile profile)
    {
        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogWarning(
                $"{name}: BeginSlash requires blade base and tip transforms.",
                this);
            return;
        }

        EnsureMaterial();
        EnsurePool(Mathf.Max(1, initialPoolSize));

        SlashTrailProfile chosen =
            profile != null
                ? profile
                : configuredProfile != null
                    ? configuredProfile
                    : defaultProfile;

        TrailSlot slot = AcquireSlot();

        if (slot == null)
            return;

        slot.bladeBase = bladeBase;
        slot.bladeTip = bladeTip;
        slot.profile = chosen;

        slot.count = 0;
        slot.emitting = true;
        slot.occupied = true;

        slot.startedAt = Now(chosen);
        slot.endedAt = slot.startedAt;
        slot.lastEmitAt = float.NegativeInfinity;

        slot.mesh.Clear(false);
        slot.renderer.enabled = true;
        slot.gameObject.SetActive(true);

        AddSample(slot, slot.startedAt, true);
    }

    /// <summary>
    /// Ends the newest active slash.
    /// Older slashes continue fading independently.
    /// </summary>
    public void EndSlash()
    {
        if (slots == null)
            return;

        TrailSlot newest = null;

        for (int i = 0; i < slots.Length; i++)
        {
            TrailSlot candidate = slots[i];

            if (candidate == null ||
                !candidate.occupied ||
                !candidate.emitting)
            {
                continue;
            }

            if (newest == null ||
                candidate.startedAt >= newest.startedAt)
            {
                newest = candidate;
            }
        }

        if (newest == null)
            return;

        float now = Now(newest.profile);

        if (newest.bladeBase != null &&
            newest.bladeTip != null)
        {
            AddSample(newest, now, true);
        }

        newest.emitting = false;
        newest.endedAt = now;

        // Release the animated transforms. The sampled points are now frozen.
        newest.bladeBase = null;
        newest.bladeTip = null;
    }

    /// <summary>Animation-event-friendly wrappers.</summary>
    public void SlashStart()
    {
        BeginSlash(
            configuredBase,
            configuredTip,
            configuredProfile);
    }

    public void SlashEnd() => EndSlash();

    // Compatibility with the original SwordSlashRibbon API.
    public void Begin() => SlashStart();
    public void End() => SlashEnd();

    /// <summary>
    /// Call from combat code after a parry or impact.
    /// Subscribe to BladeImpactPoint to spawn separate spark VFX.
    /// </summary>
    public void NotifyBladeImpact()
    {
        Transform tip = configuredTip;

        if (tip == null && slots != null)
        {
            for (int i = slots.Length - 1; i >= 0; i--)
            {
                if (slots[i] != null &&
                    slots[i].bladeTip != null)
                {
                    tip = slots[i].bladeTip;
                    break;
                }
            }
        }

        if (tip != null)
            BladeImpactPoint?.Invoke(tip.position);
    }

    /// <summary>
    /// Immediately clears all active and fading trails.
    /// Use on death, disabling, cancellation, or sheathing.
    /// </summary>
    public void CancelAndClear()
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
            ResetSlot(slots[i]);
    }

    void LateUpdate()
    {
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            TrailSlot slot = slots[i];

            if (slot == null || !slot.occupied)
                continue;

            float now = Now(slot.profile);

            if (slot.emitting)
            {
                float emissionAge = now - slot.startedAt;

                // Safety watchdog: an interrupted animation cannot emit forever.
                if (emissionAge >= slot.profile.maxEmissionTime)
                {
                    slot.emitting = false;
                    slot.endedAt = now;
                    slot.bladeBase = null;
                    slot.bladeTip = null;
                }
                else if (slot.bladeBase != null &&
                         slot.bladeTip != null)
                {
                    TryAddSample(slot, now);
                }
                else
                {
                    slot.emitting = false;
                    slot.endedAt = now;
                    slot.bladeBase = null;
                    slot.bladeTip = null;
                }
            }

            RebuildMesh(slot, now);

            if (!slot.emitting &&
                now - slot.endedAt >= slot.profile.lifetime)
            {
                ResetSlot(slot);
            }
        }
    }

    void TryAddSample(TrailSlot slot, float now)
    {
        if (slot.count == 0)
        {
            AddSample(slot, now, true);
            return;
        }

        int last = slot.count - 1;

        float baseMovement = Vector3.Distance(
            slot.bases[last],
            slot.bladeBase.position);

        float tipMovement = Vector3.Distance(
            slot.tips[last],
            slot.bladeTip.position);

        float movement = Mathf.Max(baseMovement, tipMovement);

        // A time-based minimum sampling frequency helps preserve curved paths
        // even when individual frames move the blade only a small distance.
        if (movement >= slot.profile.sampleDistance ||
            now - slot.lastEmitAt >= 1f / 60f)
        {
            AddSample(slot, now, false);
        }
    }

    void AddSample(TrailSlot slot, float now, bool force)
    {
        if (slot.bladeBase == null ||
            slot.bladeTip == null)
        {
            return;
        }

        int capacity = Mathf.Clamp(
            slot.profile.maxSamples,
            8,
            slot.bases.Length);

        if (slot.count >= capacity)
        {
            // Fixed-size history buffer: shift samples without allocating.
            Array.Copy(
                slot.bases, 1,
                slot.bases, 0,
                capacity - 1);

            Array.Copy(
                slot.tips, 1,
                slot.tips, 0,
                capacity - 1);

            Array.Copy(
                slot.sampleTimes, 1,
                slot.sampleTimes, 0,
                capacity - 1);

            slot.count = capacity - 1;
        }

        if (!force && slot.count > 0)
        {
            int last = slot.count - 1;

            if (Vector3.Distance(
                    slot.bases[last],
                    slot.bladeBase.position) < 0.0001f &&
                Vector3.Distance(
                    slot.tips[last],
                    slot.bladeTip.position) < 0.0001f)
            {
                return;
            }
        }

        Vector3 b = slot.bladeBase.position;
        Vector3 t = slot.bladeTip.position;

        Vector3 center = (b + t) * 0.5f;

        // Expand or contract the sampled blade span around its center.
        float bladeScale = Mathf.Max(
            0.01f,
            slot.profile.bladeWidthMultiplier *
            slot.profile.width);

        slot.bases[slot.count] =
            center + (b - center) * bladeScale;

        slot.tips[slot.count] =
            center + (t - center) * bladeScale;

        slot.sampleTimes[slot.count] = now;

        slot.count++;
        slot.lastEmitAt = now;
    }

    void RebuildMesh(TrailSlot slot, float now)
    {
        if (slot.count < 2)
        {
            slot.mesh.Clear(false);
            return;
        }

        int vertexCount = slot.count * 2;
        int triangleCount = (slot.count - 1) * 12;

        float ageAfterEnd = slot.emitting
            ? 0f
            : Mathf.Max(0f, now - slot.endedAt);

        float timeFade = slot.emitting
            ? 1f
            : 1f - Mathf.Clamp01(
                ageAfterEnd /
                Mathf.Max(0.01f, slot.profile.lifetime));

        for (int i = 0; i < slot.count; i++)
        {
            float u = i / (float)(slot.count - 1);

            // Taper the beginning and end of the completed arc.
            float taper = Mathf.Clamp01(
                Mathf.Min(
                    u / Mathf.Max(0.001f, slot.profile.endTaper),
                    (1f - u) /
                    Mathf.Max(0.001f, slot.profile.endTaper)));

            // Smoothstep prevents an abrupt change in ribbon width.
            taper = taper * taper * (3f - 2f * taper);

            // The middle of the arc can be slightly fuller than its ends.
            float bulge = 1f +
                slot.profile.midSwingBulge *
                Mathf.Sin(u * Mathf.PI);

            float sampleFade = 1f;

            if (!slot.emitting)
            {
                // Older sections dim slightly sooner after the swing ends.
                float sampleAge = Mathf.Clamp01(
                    ageAfterEnd /
                    Mathf.Max(0.01f, slot.profile.lifetime)
                    + (1f - u) * 0.2f);

                sampleFade = 1f - sampleAge * 0.35f;
            }

            Color color = slot.profile.color;
            color.a *= timeFade * taper * sampleFade;

            int vertex = i * 2;

            // Each pair represents the sampled blade edge.
            // Connecting pairs forms the swept ribbon surface.
            slot.vertices[vertex] = slot.bases[i];
            slot.vertices[vertex + 1] = slot.tips[i];

            slot.uvs[vertex] = new Vector2(u, 0f);
            slot.uvs[vertex + 1] = new Vector2(u, 1f);

            slot.colors[vertex] = color;
            slot.colors[vertex + 1] = color;
        }

        int triangleIndex = 0;

        for (int i = 0; i < slot.count - 1; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            // Two triangles for the front and two for the back.
            slot.triangles[triangleIndex++] = a;
            slot.triangles[triangleIndex++] = c;
            slot.triangles[triangleIndex++] = b;

            slot.triangles[triangleIndex++] = b;
            slot.triangles[triangleIndex++] = c;
            slot.triangles[triangleIndex++] = d;

            slot.triangles[triangleIndex++] = b;
            slot.triangles[triangleIndex++] = c;
            slot.triangles[triangleIndex++] = a;

            slot.triangles[triangleIndex++] = d;
            slot.triangles[triangleIndex++] = c;
            slot.triangles[triangleIndex++] = b;
        }

        slot.mesh.Clear(false);

        slot.mesh.SetVertices(
            slot.vertices, 0, vertexCount);

        slot.mesh.SetUVs(
            0, slot.uvs, 0, vertexCount);

        slot.mesh.SetColors(
            slot.colors, 0, vertexCount);

        slot.mesh.SetTriangles(
            slot.triangles,
            0,
            triangleCount,
            0,
            false);

        slot.mesh.RecalculateBounds();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        slot.renderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetFloat(
            "_UVScrollSpeed",
            slot.profile.uvScrollSpeed);

        slot.renderer.SetPropertyBlock(propertyBlock);
    }

    TrailSlot AcquireSlot()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].occupied)
                return slots[i];
        }

        // Expand the pool if needed, up to eight simultaneous trails.
        int oldLength = slots.Length;

        EnsurePool(Mathf.Min(8, oldLength + 1));

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].occupied)
                return slots[i];
        }

        // Never overwrite an actively emitting slash.
        // If necessary, reuse the oldest trail that has already ended.
        int oldest = -1;
        float oldestTime = float.PositiveInfinity;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].emitting &&
                slots[i].endedAt < oldestTime)
            {
                oldest = i;
                oldestTime = slots[i].endedAt;
            }
        }

        if (oldest >= 0)
        {
            ResetSlot(slots[oldest]);
            return slots[oldest];
        }

        Debug.LogWarning(
            $"{name}: slash trail pool is full. " +
            "Increase initialPoolSize or reduce overlapping slashes.",
            this);

        return null;
    }

    void EnsurePool(int requestedCount)
    {
        if (slots == null)
            slots = Array.Empty<TrailSlot>();

        requestedCount = Mathf.Clamp(requestedCount, 1, 8);

        if (slots.Length >= requestedCount)
            return;

        TrailSlot[] expanded = new TrailSlot[requestedCount];

        Array.Copy(slots, expanded, slots.Length);

        for (int i = slots.Length; i < expanded.Length; i++)
            expanded[i] = CreateSlot(i);

        slots = expanded;
    }

    TrailSlot CreateSlot(int index)
    {
        int capacity = Mathf.Clamp(poolMaxSamples, 8, 96);

        GameObject go = new GameObject(
            $"Pooled Slash Ribbon {index}");

        // The mesh vertices are world-space coordinates.
        // An identity world transform ensures the trail stays in place
        // when the character moves after the slash has ended.
        go.transform.SetParent(null, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh
        {
            name = $"Slash Ribbon Mesh {index}"
        };

        mesh.MarkDynamic();
        filter.sharedMesh = mesh;

        renderer.sharedMaterial =
            ribbonMaterial != null
                ? ribbonMaterial
                : runtimeMaterial;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;

        go.SetActive(false);

        return new TrailSlot
        {
            gameObject = go,
            mesh = mesh,
            renderer = renderer,

            vertices = new Vector3[capacity * 2],
            uvs = new Vector2[capacity * 2],
            colors = new Color[capacity * 2],
            triangles = new int[(capacity - 1) * 12],

            bases = new Vector3[capacity],
            tips = new Vector3[capacity],
            sampleTimes = new float[capacity]
        };
    }

    void EnsureMaterial()
    {
        if (ribbonMaterial == null &&
            runtimeMaterial == null)
        {
            Shader shader = Shader.Find(shaderName);

            if (shader == null)
                shader = Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogError(
                    $"{name}: no slash shader found. " +
                    $"Assign a material or include '{shaderName}'.",
                    this);

                return;
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Runtime Procedural Slash Additive"
            };

            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", Color.white);

            if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor("_Color", Color.white);
        }

        Material chosen = ribbonMaterial != null
            ? ribbonMaterial
            : runtimeMaterial;

        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null &&
                slots[i].renderer != null)
            {
                slots[i].renderer.sharedMaterial = chosen;
            }
        }
    }

    static float Now(SlashTrailProfile profile)
    {
        return profile != null && profile.useUnscaledTime
            ? Time.unscaledTime
            : Time.time;
    }

    void ResetSlot(TrailSlot slot)
    {
        if (slot == null)
            return;

        slot.count = 0;
        slot.emitting = false;
        slot.occupied = false;

        slot.bladeBase = null;
        slot.bladeTip = null;
        slot.profile = null;

        if (slot.mesh != null)
            slot.mesh.Clear(false);

        if (slot.renderer != null)
            slot.renderer.enabled = false;

        if (slot.gameObject != null)
            slot.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        CancelAndClear();
    }

    void OnDestroy()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    continue;

                if (slots[i].mesh != null)
                    Destroy(slots[i].mesh);

                if (slots[i].gameObject != null)
                    Destroy(slots[i].gameObject);
            }
        }

        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}