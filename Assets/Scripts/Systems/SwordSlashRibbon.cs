using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the surface swept out by a weapon's blade. Because it samples the
/// blade's real transforms, it works with any authored attack animation.
/// </summary>
public sealed class SwordSlashRibbon : MonoBehaviour
{
    struct BladeSample
    {
        public Vector3 bladeBase;
        public Vector3 bladeTip;
        public float time;
    }

    readonly List<BladeSample> samples = new();

    Transform bladeBase;
    Transform bladeTip;
    Material ribbonMaterial;
    Material fallbackMaterial;
    Color ribbonColor;
    float duration;
    float minimumSampleDistance;
    float bladeWidthMultiplier;
    int maxSamples;
    bool emitting;

    GameObject ribbonObject;
    Mesh ribbonMesh;

    public void Configure(Transform baseTransform, Transform tipTransform, Material material,
        Color color, float trailDuration, float sampleDistance, float widthMultiplier, int maximumSamples)
    {
        bladeBase = baseTransform;
        bladeTip = tipTransform;
        ribbonMaterial = material;
        ribbonColor = color;
        duration = Mathf.Max(trailDuration, 0.01f);
        minimumSampleDistance = Mathf.Max(sampleDistance, 0.001f);
        bladeWidthMultiplier = Mathf.Max(widthMultiplier, 0.01f);
        maxSamples = Mathf.Max(maximumSamples, 2);

        CreateRendererIfNeeded();
    }

    public void Begin()
    {
        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogWarning("Sword slash ribbon needs both a blade base and blade tip.", this);
            return;
        }

        Clear();
        emitting = true;
        AddSample();
    }

    public void End()
    {
        emitting = false;
    }

    public void Clear()
    {
        samples.Clear();
        ribbonMesh?.Clear();
    }

    void LateUpdate()
    {
        if (emitting)
            TryAddSample();

        RemoveExpiredSamples();
        RebuildMesh();
    }

    void TryAddSample()
    {
        if (samples.Count == 0)
        {
            AddSample();
            return;
        }

        BladeSample lastSample = samples[^1];
        float baseMovement = Vector3.Distance(lastSample.bladeBase, bladeBase.position);
        float tipMovement = Vector3.Distance(lastSample.bladeTip, bladeTip.position);

        if (baseMovement >= minimumSampleDistance || tipMovement >= minimumSampleDistance)
            AddSample();
    }

    void AddSample()
    {
        // Expand from the middle of the blade so the slash stays centred on it.
        Vector3 centre = (bladeBase.position + bladeTip.position) * 0.5f;
        Vector3 scaledBase = centre + (bladeBase.position - centre) * bladeWidthMultiplier;
        Vector3 scaledTip = centre + (bladeTip.position - centre) * bladeWidthMultiplier;

        samples.Add(new BladeSample
        {
            bladeBase = scaledBase,
            bladeTip = scaledTip,
            time = Time.time
        });

        if (samples.Count > maxSamples)
            samples.RemoveAt(0);
    }

    void RemoveExpiredSamples()
    {
        float oldestAllowedTime = Time.time - duration;
        while (samples.Count > 0 && samples[0].time < oldestAllowedTime)
            samples.RemoveAt(0);
    }

    void RebuildMesh()
    {
        if (ribbonMesh == null) return;

        if (samples.Count < 2)
        {
            ribbonMesh.Clear();
            return;
        }

        int vertexCount = samples.Count * 2;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Color[] colors = new Color[vertexCount];
        // Add both winding orders so the ribbon is visible from either side.
        int[] triangles = new int[(samples.Count - 1) * 12];

        for (int i = 0; i < samples.Count; i++)
        {
            BladeSample sample = samples[i];
            int vertexIndex = i * 2;
            float age01 = Mathf.Clamp01((Time.time - sample.time) / duration);
            Color color = ribbonColor;
            color.a *= 1f - age01;

            vertices[vertexIndex] = sample.bladeBase;
            vertices[vertexIndex + 1] = sample.bladeTip;
            uvs[vertexIndex] = new Vector2(age01, 0f);
            uvs[vertexIndex + 1] = new Vector2(age01, 1f);
            colors[vertexIndex] = color;
            colors[vertexIndex + 1] = color;

            if (i == samples.Count - 1) continue;

            int triangleIndex = i * 12;
            triangles[triangleIndex] = vertexIndex;
            triangles[triangleIndex + 1] = vertexIndex + 2;
            triangles[triangleIndex + 2] = vertexIndex + 1;
            triangles[triangleIndex + 3] = vertexIndex + 1;
            triangles[triangleIndex + 4] = vertexIndex + 2;
            triangles[triangleIndex + 5] = vertexIndex + 3;
            triangles[triangleIndex + 6] = vertexIndex + 1;
            triangles[triangleIndex + 7] = vertexIndex + 2;
            triangles[triangleIndex + 8] = vertexIndex;
            triangles[triangleIndex + 9] = vertexIndex + 3;
            triangles[triangleIndex + 10] = vertexIndex + 2;
            triangles[triangleIndex + 11] = vertexIndex + 1;
        }

        ribbonMesh.Clear();
        ribbonMesh.vertices = vertices;
        ribbonMesh.uv = uvs;
        ribbonMesh.colors = colors;
        ribbonMesh.triangles = triangles;
        ribbonMesh.RecalculateBounds();
    }

    void CreateRendererIfNeeded()
    {
        if (ribbonObject == null)
        {
            ribbonObject = new GameObject("Sword Slash Ribbon");
            ribbonObject.layer = gameObject.layer;
            MeshFilter filter = ribbonObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = ribbonObject.AddComponent<MeshRenderer>();

            ribbonMesh = new Mesh { name = "Sword Slash Ribbon Mesh" };
            ribbonMesh.MarkDynamic();
            filter.sharedMesh = ribbonMesh;

            if (ribbonMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Sprites/Default");
                fallbackMaterial = new Material(shader) { name = "Runtime Sword Slash Ribbon Material" };
                if (fallbackMaterial.HasProperty("_BaseColor"))
                    fallbackMaterial.SetColor("_BaseColor", ribbonColor);
                else if (fallbackMaterial.HasProperty("_Color"))
                    fallbackMaterial.SetColor("_Color", ribbonColor);
            }

            renderer.sharedMaterial = ribbonMaterial != null ? ribbonMaterial : fallbackMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    void OnDestroy()
    {
        if (ribbonObject != null) Destroy(ribbonObject);
        if (ribbonMesh != null) Destroy(ribbonMesh);
        if (fallbackMaterial != null) Destroy(fallbackMaterial);
    }
}
