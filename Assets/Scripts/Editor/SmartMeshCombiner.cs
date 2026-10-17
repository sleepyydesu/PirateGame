// Smart Mesh Combiner: Unity 6 editor tool. Save in Assets/Scripts/Editor/.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
public class SmartMeshCombiner : EditorWindow
{
    private enum LodMode
    {
        KeepLODs,
        LOD0Only,
        SkipLODGroups
    }
    private GameObject targetRoot;
    private float cellSize = 40f;
    private int maxVertices = 60000;
    private LodMode lodMode = LodMode.KeepLODs;
    private bool includeTransparent = false;
    private bool skipWindShaders = true;
    private bool requireStatic = false;
    private bool skipScriptedObjects = true;
    private Vector2 scroll;
    private bool showDetails;
    private readonly List<string> report = new List<string>();
    private readonly List<string> details = new List<string>();
    private string detailsText = "";
    private const string AssetFolder = "Assets/CombinedMeshes";
    private const string OutputPrefix = "__SmartCombined_";
    // Data-only components that never move or hide their GameObject.
    private static readonly HashSet<string> SafeComponentTypes =
        new HashSet<string> { "ProBuilderMesh", "PolybrushMesh" };
    private readonly Dictionary<Transform, string> hierarchyCache = new Dictionary<Transform, string>();
    private readonly Dictionary<MeshRenderer, string> rendererKeys = new Dictionary<MeshRenderer, string>();
    private readonly Dictionary<LODGroup, string> lodKeys = new Dictionary<LODGroup, string>();
    private readonly Dictionary<LODGroup, int> lodIds = new Dictionary<LODGroup, int>();
    private readonly Dictionary<Mesh, MeshCache> meshCache = new Dictionary<Mesh, MeshCache>();
    private readonly Dictionary<LODGroup, LOD[]> lodCache = new Dictionary<LODGroup, LOD[]>();
    private readonly Dictionary<Shader, bool> windCache = new Dictionary<Shader, bool>();
    private readonly HashSet<Material> windMaterialsUsed = new HashSet<Material>();
    private readonly HashSet<Material> transparentMaterialsUsed = new HashSet<Material>();
    private class Source
    {
        public Mesh mesh;
        public Mesh streams;
        public MeshRenderer renderer;
        public Transform transform;
        public Material material;
        public int submesh;
        public int vertexCount;
        public LODGroup lodGroup;
        public int lodLevel = -1;
    }
    private class Batch
    {
        public Material material;
        public MeshRenderer reference;
        public LODGroup lodReference;
        public int lodLevel = -1;
        public string containerKey;
        public readonly List<Source> sources = new List<Source>();
    }
    private class MeshCache
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Color[] colors;
        public bool colorsAreBytes;
        public readonly Vector4[][] uvs = new Vector4[8][];
        public readonly int[] uvDimensions = new int[8];
        public int[][] indices;
        public int[][] sourceVertices;
        public int[] vertexCount;
    }
    // Every run creates separate assets; previous outputs are never overwritten.
    // This tool does not combine terrain, skinned meshes, particles, or billboards.
    private class LodContainer
    {
        public GameObject gameObject;
        public LODGroup reference;
        public List<MeshRenderer>[] levels;
    }
    [MenuItem("Tools/Optimization/Smart Mesh Combiner")]
    private static void Open()
    {
        GetWindow<SmartMeshCombiner>("Smart Mesh Combiner");
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Non-Destructive Mesh Combiner", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates visible combined copies and disables only the original renderers they replace. " +
            "Skipped objects keep their current visibility. Original GameObjects stay active, " +
            "so colliders and scripts remain active. Meshes, materials and import settings are unchanged. " +
            "Undo restores visibility and removes the generated scene objects. " +
            "Only MeshFilter/MeshRenderer geometry is supported; colliders are not copied.",
            MessageType.Info);
        targetRoot = (GameObject)EditorGUILayout.ObjectField(
            "Source Parent", targetRoot, typeof(GameObject), true);
        cellSize = Mathf.Max(1f, EditorGUILayout.FloatField("Cell Size", cellSize));
        maxVertices = EditorGUILayout.IntSlider("Max Vertices", maxVertices, 1000, 200000);
        lodMode = (LodMode)EditorGUILayout.EnumPopup(new GUIContent("LOD Groups",
            "Keep LODs: combine materials within each original LODGroup, preserving its switches.\n" +
            "LOD0 Only: combine only the highest detail level, without LOD switching.\n" +
            "Skip: leave LODGroup objects untouched."), lodMode);
        includeTransparent = EditorGUILayout.Toggle(new GUIContent("Include Transparent",
            "Combine transparent materials (e.g. leaves). Overlapping transparent " +
            "surfaces inside one cell may sort differently."), includeTransparent);
        skipWindShaders = EditorGUILayout.Toggle(new GUIContent("Skip Wind Shaders",
            "Skip SpeedTree / vegetation shaders with wind properties. Wind that " +
            "depends on object position can look different after combining."), skipWindShaders);
        requireStatic = EditorGUILayout.Toggle("Require Static", requireStatic);
        skipScriptedObjects = EditorGUILayout.Toggle(new GUIContent("Skip Scripted Objects",
            "Skip objects with scripts on themselves or any ancestor (e.g. WindSwaySimple, " +
            "Door, DisableRenderer), since those scripts move or hide them at runtime."),
            skipScriptedObjects);
        if (GUILayout.Button("Analyze"))
            Analyze();
        if (GUILayout.Button("Generate Combined Copies"))
            Generate();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string line in report)
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        if (details.Count > 0)
        {
            showDetails = EditorGUILayout.Foldout(showDetails, "Details (" + details.Count + ")", true);
            if (showDetails)
                EditorGUILayout.TextArea(detailsText, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndScrollView();
    }
    private void Log(string message)
    {
        report.Add(message);
    }
    private void Skip(string objectName, string reason, Dictionary<string, int> counts)
    {
        details.Add("SKIP " + objectName + ": " + reason);
        int colon = reason.IndexOf(':');
        string category = colon > 0 ? reason.Substring(0, colon) : reason;
        counts.TryGetValue(category, out int n);
        counts[category] = n + 1;
    }
    private void FinishReport()
    {
        detailsText = string.Join("\n", details);
        Debug.Log("[Mesh Combiner]\n" + string.Join("\n", report) +
                  (details.Count > 0 ? "\n\n" + detailsText : ""));
        Repaint();
    }
    private static bool IsGeneratedOutput(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name.StartsWith(OutputPrefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
    private LOD[] GetLods(LODGroup group)
    {
        if (!lodCache.TryGetValue(group, out LOD[] lods))
        {
            lods = group.GetLODs();
            lodCache.Add(group, lods);
        }
        return lods;
    }
    private LODGroup FindLodGroup(Renderer renderer, out List<int> levels)
    {
        for (Transform t = renderer.transform; t != null; t = t.parent)
        {
            if (!t.TryGetComponent(out LODGroup group) || !group.enabled)
                continue;
            LOD[] lods = GetLods(group);
            List<int> found = null;
            for (int i = 0; i < lods.Length; i++)
            {
                if (Array.IndexOf(lods[i].renderers, renderer) >= 0)
                    (found ??= new List<int>()).Add(i);
            }
            if (found != null)
            {
                levels = found;
                return group;
            }
        }
        levels = null;
        return null;
    }
    private bool IsWindMaterial(Material material)
    {
        Shader shader = material.shader;
        if (shader == null)
            return false;
        if (windCache.TryGetValue(shader, out bool wind))
            return wind;
        string name = shader.name.ToLowerInvariant();
        wind = name.Contains("speedtree") || name.Contains("tree creator") || name.Contains("nature/tree");
        for (int i = 0; !wind && i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyName(i).IndexOf("wind", StringComparison.OrdinalIgnoreCase) >= 0)
                wind = true;
        }
        windCache.Add(shader, wind);
        return wind;
    }
    private string CheckHierarchy(Transform t)
    {
        if (t == null) return null;
        if (hierarchyCache.TryGetValue(t, out string cached)) return cached;
        string error = null;
        if (t.TryGetComponent<Rigidbody>(out _))
            error = "Physics: Rigidbody on '" + t.name + "'";
        else if (t.TryGetComponent<Animator>(out _))
            error = "Animated: Animator on '" + t.name + "'";
        else if (t.TryGetComponent<Animation>(out _))
            error = "Animated: Animation on '" + t.name + "'";
        else if (skipScriptedObjects)
        {
            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null) { error = "Script: missing script"; break; }
                if (SafeComponentTypes.Contains(behaviour.GetType().Name)) continue;
                error = "Script: '" + behaviour.GetType().Name + "' on '" + t.name + "'";
                break;
            }
        }
        // Inspect ancestors above the selected root too: they can animate this hierarchy.
        error ??= CheckHierarchy(t.parent);
        hierarchyCache[t] = error;
        return error;
    }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static string CheckTransform(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++)
            if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i]))
                return "Transform: non-finite matrix";
        float x = ((Vector3)matrix.GetColumn(0)).magnitude;
        float y = ((Vector3)matrix.GetColumn(1)).magnitude;
        float z = ((Vector3)matrix.GetColumn(2)).magnitude;
        if (x < 1e-6f || y < 1e-6f || z < 1e-6f)
            return "Transform: zero scale";
        if (Mathf.Abs(matrix.determinant) / (x * y * z) < 1e-6f)
            return "Transform: degenerate (flattened) matrix";
        return null;
    }
    private string Validate(MeshFilter filter, MeshRenderer renderer)
    {
        if (!filter.gameObject.activeInHierarchy)
            return "Inactive: GameObject is inactive";
        if (!renderer.enabled || renderer.forceRenderingOff)
            return "Disabled: MeshRenderer is disabled";
        if (requireStatic && !filter.gameObject.isStatic)
            return "Not static: Require Static is on";
        string hierarchy = CheckHierarchy(filter.transform);
        if (hierarchy != null)
            return hierarchy;
        if (renderer.isPartOfStaticBatch)
            return "Static batch: already combined by Unity";
        if (renderer.realtimeLightmapIndex >= 0 && renderer.realtimeLightmapIndex < 0xFFFE)
            return "Realtime GI: Enlighten vertex data cannot be safely merged";
        if (renderer.HasPropertyBlock())
            return "Property block: per-object MaterialPropertyBlock would be lost";
        if (renderer.lightProbeUsage == LightProbeUsage.UseProxyVolume)
            return "Light probes: uses a Light Probe Proxy Volume";
        Mesh mesh = filter.sharedMesh;
        if (mesh == null)
            return "Mesh: missing";
        if (!mesh.HasVertexAttribute(VertexAttribute.Position))
            return "Mesh: missing positions";
        if (mesh.vertexCount == 0 || mesh.subMeshCount == 0)
            return "Mesh: empty";
        string transform = CheckTransform(filter.transform.localToWorldMatrix);
        if (transform != null)
            return transform;
        Mesh streams = renderer.additionalVertexStreams;
        if (streams != null && streams.vertexCount != mesh.vertexCount)
            return "Vertex streams: additional vertex streams don't match the mesh";
        Material[] materials = renderer.sharedMaterials;
        bool anyMaterial = false;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            // Empty slots render nothing in the original either.
            if (material == null)
                continue;
            anyMaterial = true;
            int submesh = Mathf.Min(i, mesh.subMeshCount - 1);
            if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                return "Topology: submesh " + submesh + " is lines/points";
            if (!includeTransparent && material.renderQueue > 2500)
                return "Transparent: '" + material.name + "' (Include Transparent is off)";
            if (skipWindShaders && IsWindMaterial(material))
                return "Wind shader: '" + material.name + "' (Skip Wind Shaders is on)";
        }
        if (!anyMaterial)
            return "Materials: none assigned";
        // Validate data now, so one broken asset is reported instead of aborting generation.
        try
        {
            GetCache(mesh, true);
            if (streams != null) GetCache(streams, false);
        }
        catch (Exception ex) { return "Mesh data: " + ex.Message; }
        return null;
    }
    private string ValidateLodMembers(LODGroup group)
    {
        LOD[] lods = GetLods(group);
        int levels = lodMode == LodMode.LOD0Only ? Mathf.Min(1, lods.Length) : lods.Length;
        for (int i = 0; i < levels; i++)
        {
            foreach (Renderer member in lods[i].renderers)
            {
                if (member == null)
                    continue;
                if (!(member is MeshRenderer) || !member.TryGetComponent<MeshFilter>(out _))
                    return "LOD" + i + " uses a " + member.GetType().Name + " ('" + member.name + "')";
                if (!member.transform.IsChildOf(targetRoot.transform) || IsGeneratedOutput(member.transform))
                    return "LOD member '" + member.name + "' is outside the Source Parent";
            }
        }
        return null;
    }
    private static T[] ReadAttribute<T>(int count, Action<NativeArray<T>> read) where T : struct
    {
        using (var buffer = new NativeArray<T>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
        {
            read(buffer);
            return buffer.ToArray();
        }
    }
    private MeshCache GetCache(Mesh mesh, bool withIndices)
    {
        if (meshCache.TryGetValue(mesh, out MeshCache cache)) return cache;
        cache = new MeshCache();
        // Editor-only read API: no Read/Write importer changes required.
        using (Mesh.MeshDataArray snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            Mesh.MeshData data = snapshot[0];
            int n = data.vertexCount;
            if (data.HasVertexAttribute(VertexAttribute.Position))
                cache.vertices = ReadAttribute<Vector3>(n, a => data.GetVertices(a));
            if (data.HasVertexAttribute(VertexAttribute.Normal))
                cache.normals = ReadAttribute<Vector3>(n, a => data.GetNormals(a));
            if (data.HasVertexAttribute(VertexAttribute.Tangent))
                cache.tangents = ReadAttribute<Vector4>(n, a => data.GetTangents(a));
            if (data.HasVertexAttribute(VertexAttribute.Color))
            {
                cache.colors = ReadAttribute<Color>(n, a => data.GetColors(a));
                cache.colorsAreBytes = data.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8;
            }
            for (int ch = 0; ch < 8; ch++)
            {
                VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                if (!data.HasVertexAttribute(attribute)) continue;
                int channel = ch;
                cache.uvs[ch] = ReadAttribute<Vector4>(n, a => data.GetUVs(channel, a));
                cache.uvDimensions[ch] = data.GetVertexAttributeDimension(attribute);
            }
            // Always cache indices: a mesh may be both a source and an additional stream.
            int submeshes = data.subMeshCount;
            cache.indices = new int[submeshes][];
            cache.sourceVertices = new int[submeshes][];
            cache.vertexCount = new int[submeshes];
            for (int sub = 0; sub < submeshes; sub++)
            {
                var descriptor = data.GetSubMesh(sub);
                if (descriptor.topology != MeshTopology.Triangles)
                {
                    cache.indices[sub] = Array.Empty<int>();
                    cache.sourceVertices[sub] = Array.Empty<int>();
                    continue;
                }
                if (descriptor.indexCount % 3 != 0) throw new InvalidOperationException("Incomplete triangle indices: " + mesh.name);
                int submesh = sub;
                int[] raw = ReadAttribute<int>(descriptor.indexCount, a => data.GetIndices(a, submesh, true));
                var remap = new Dictionary<int, int>();
                var unique = new List<int>();
                for (int i = 0; i < raw.Length; i++)
                {
                    int v = raw[i];
                    if (v < 0 || v >= n) throw new InvalidOperationException("Out-of-range vertex index: " + mesh.name);
                    if (!remap.TryGetValue(v, out int compact))
                    {
                        compact = unique.Count;
                        unique.Add(v);
                        remap.Add(v, compact);
                    }
                    raw[i] = compact;
                }
                cache.indices[sub] = raw;
                cache.sourceVertices[sub] = unique.ToArray();
                cache.vertexCount[sub] = unique.Count;
            }
        }
        meshCache.Add(mesh, cache);
        return cache;
    }
    private void AddSources(MeshFilter filter, MeshRenderer renderer, LODGroup group,
        List<int> levels, List<Source> sources)
    {
        Mesh mesh = filter.sharedMesh;
        MeshCache cache = GetCache(mesh, true);
        details.Add("ELIGIBLE " + AnimationUtility.CalculateTransformPath(filter.transform, targetRoot.transform));
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;
            // Extra material slots re-render the last submesh, same as Unity does.
            int submesh = Mathf.Min(i, mesh.subMeshCount - 1);
            if (cache.vertexCount[submesh] == 0)
                continue;
            if (IsWindMaterial(material))
                windMaterialsUsed.Add(material);
            if (material.renderQueue > 2500)
                transparentMaterialsUsed.Add(material);
            int levelCount = levels?.Count ?? 1;
            for (int l = 0; l < levelCount; l++)
            {
                sources.Add(new Source
                {
                    mesh = mesh,
                    streams = renderer.additionalVertexStreams,
                    renderer = renderer,
                    transform = filter.transform,
                    material = material,
                    submesh = submesh,
                    vertexCount = cache.vertexCount[submesh],
                    lodGroup = group,
                    lodLevel = levels != null ? levels[l] : -1
                });
            }
        }
    }
    private List<Source> Collect()
    {
        meshCache.Clear();
        hierarchyCache.Clear();
        rendererKeys.Clear();
        anchorIds.Clear();
        lodKeys.Clear();
        lodIds.Clear();
        lodCache.Clear();
        windCache.Clear();
        windMaterialsUsed.Clear();
        transparentMaterialsUsed.Clear();
        List<Source> sources = new List<Source>();
        Dictionary<string, int> skipCounts = new Dictionary<string, int>();
        var candidates = new List<(MeshFilter filter, MeshRenderer renderer, LODGroup group, List<int> levels)>();
        Dictionary<LODGroup, string> failedGroups = new Dictionary<LODGroup, string>();
        HashSet<LODGroup> groups = new HashSet<LODGroup>();
        int lodDropped = 0;
        MeshFilter[] filters = targetRoot.GetComponentsInChildren<MeshFilter>(true);
        int scanned = 0;
        foreach (MeshFilter filter in filters)
        {
            if ((scanned++ & 63) == 0 && EditorUtility.DisplayCancelableProgressBar(
                "Smart Mesh Combiner", "Checking source geometry", (float)scanned / Mathf.Max(1, filters.Length)))
                throw new OperationCanceledException();
            if (IsGeneratedOutput(filter.transform) || !filter.TryGetComponent(out MeshRenderer renderer))
                continue;
            LODGroup group = FindLodGroup(renderer, out List<int> levels);
            string error = null;
            if (group != null)
            {
                if (lodMode == LodMode.SkipLODGroups)
                {
                    error = "LODGroup: LOD Groups option is set to Skip";
                }
                else if (lodMode == LodMode.LOD0Only)
                {
                    if (!levels.Contains(0))
                    {
                        lodDropped++;
                        continue;
                    }
                    levels = new List<int> { 0 };
                }
            }
            error ??= Validate(filter, renderer);
            if (error != null)
            {
                if (group != null && !failedGroups.ContainsKey(group))
                    failedGroups[group] = filter.name + " -> " + error;
                Skip(filter.name, error, skipCounts);
                continue;
            }
            if (group != null)
                groups.Add(group);
            candidates.Add((filter, renderer, group, levels));
        }
        foreach (LODGroup group in groups)
        {
            if (failedGroups.ContainsKey(group))
                continue;
            string error = ValidateLodMembers(group);
            if (error != null)
                failedGroups[group] = error;
        }
        int accepted = 0;
        foreach (var candidate in candidates)
        {
            // A LOD object is combined whole or not at all, so no part of it can vanish.
            if (candidate.group != null && failedGroups.TryGetValue(candidate.group, out string reason))
            {
                Skip(candidate.filter.name,
                    "LODGroup: '" + candidate.group.name + "' has an ineligible member (" + reason + ")",
                    skipCounts);
                continue;
            }
            accepted++;
            AddSources(candidate.filter, candidate.renderer, candidate.group, candidate.levels, sources);
        }
        int skipped = 0;
        foreach (int n in skipCounts.Values)
            skipped += n;
        Log("Eligible renderers: " + accepted);
        Log("Skipped renderers: " + skipped);
        foreach (KeyValuePair<string, int> pair in skipCounts)
            Log("    " + pair.Key + ": " + pair.Value);
        if (lodDropped > 0)
            Log("LOD1+ renderers left out (LOD0 Only mode): " + lodDropped);
        Log("Eligible submesh draws: " + sources.Count);
        Log("Keep LODs preserves each object's LODGroup; it does not merge different trees' switches.");
        Log("Probe lighting, object-space shaders and occlusion may change on larger combined bounds; compare visually.");
        if (windMaterialsUsed.Count > 0)
        {
            Log("WARNING: " + windMaterialsUsed.Count + " wind/SpeedTree material(s) included (" +
                JoinNames(windMaterialsUsed) + "). If foliage sways wrongly on the combined copy, " +
                "enable 'Skip Wind Shaders'.");
        }
        if (transparentMaterialsUsed.Count > 0)
        {
            Log("NOTE: " + transparentMaterialsUsed.Count + " transparent material(s) included (" +
                JoinNames(transparentMaterialsUsed) + "). Overlapping transparent surfaces may sort differently.");
        }
        return sources;
    }
    private static string JoinNames(IEnumerable<Material> materials)
    {
        List<string> names = new List<string>();
        foreach (Material material in materials)
            names.Add(material.name);
        return string.Join(", ", names);
    }
    private Vector3Int GetCell(Source source)
    {
        // LOD members share their group's anchor so every level lands in the same cell.
        Vector3 p = source.lodGroup != null
            ? source.lodGroup.transform.TransformPoint(source.lodGroup.localReferencePoint)
            : source.renderer.bounds.center;
        return new Vector3Int(
            Mathf.FloorToInt(p.x / cellSize),
            Mathf.FloorToInt(p.y / cellSize),
            Mathf.FloorToInt(p.z / cellSize));
    }
    private string LodSignature(LODGroup group)
    {
        if (lodKeys.TryGetValue(group, out string key)) return key;
        if (!lodIds.TryGetValue(group, out int id)) { id = lodIds.Count; lodIds.Add(group, id); }
        key = "LODGroup:" + id;
        lodKeys.Add(group, key);
        return key;
    }
    private string RendererKey(MeshRenderer r)
    {
        if (rendererKeys.TryGetValue(r, out string cached)) return cached;
        string key = string.Join("|",
            r.lightmapIndex,
            r.shadowCastingMode,
            r.staticShadowCaster,
            r.receiveShadows,
            r.lightProbeUsage,
            r.reflectionProbeUsage,
            r.motionVectorGenerationMode,
            r.allowOcclusionWhenDynamic,
            r.sortingLayerID,
            r.sortingOrder,
            r.renderingLayerMask,
            r.gameObject.layer,
            GameObjectUtility.GetStaticEditorFlags(r.gameObject),
            r.receiveGI);
        // Object references compare by identity without deprecated GetInstanceID APIs.
        key += "|anchor:" + (r.probeAnchor == null ? -1 : AnchorId(r.probeAnchor));
        rendererKeys.Add(r, key);
        return key;
    }
    private readonly Dictionary<Transform, int> anchorIds = new Dictionary<Transform, int>();
    private int AnchorId(Transform anchor)
    {
        if (!anchorIds.TryGetValue(anchor, out int id)) { id = anchorIds.Count; anchorIds.Add(anchor, id); }
        return id;
    }
    private List<Batch> GroupSources(List<Source> sources)
    {
        var dictionary = new Dictionary<(Material, string), Batch>();
        List<Batch> batches = new List<Batch>();
        foreach (Source source in sources)
        {
            bool keepLod = source.lodLevel >= 0 && lodMode == LodMode.KeepLODs;
            Vector3Int cell = keepLod ? Vector3Int.zero : GetCell(source);
            string lodSignature = keepLod ? LodSignature(source.lodGroup) : null;
            string containerKey = keepLod ? cell + "|" + lodSignature : null;
            string key = cell + "|" +
                         (keepLod ? lodSignature + "|L" + source.lodLevel : "-") + "|" +
                         RendererKey(source.renderer);
            if (!dictionary.TryGetValue((source.material, key), out Batch batch))
            {
                batch = new Batch
                {
                    material = source.material,
                    reference = source.renderer,
                    lodReference = keepLod ? source.lodGroup : null,
                    lodLevel = keepLod ? source.lodLevel : -1,
                    containerKey = containerKey
                };
                dictionary.Add((source.material, key), batch);
                batches.Add(batch);
            }
            batch.sources.Add(source);
        }
        return batches;
    }
    private List<List<Source>> BuildChunks(Batch batch)
    {
        List<List<Source>> chunks = new List<List<Source>>();
        List<Source> current = new List<Source>();
        int vertices = 0;
        foreach (Source source in batch.sources)
        {
            // Oversized meshes still get combined, just in a chunk of their own.
            if (current.Count > 0 && vertices + source.vertexCount > maxVertices)
            {
                chunks.Add(current);
                current = new List<Source>();
                vertices = 0;
            }
            current.Add(source);
            vertices += source.vertexCount;
        }
        if (current.Count > 0)
            chunks.Add(current);
        return chunks;
    }
    private bool ValidateInput(bool forGenerate)
    {
        if (targetRoot == null || cellSize <= 0 || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
        {
            Log("Select a source parent and valid cell size.");
            return false;
        }
        if (!forGenerate)
            return true;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Log("Exit Play Mode before generating meshes.");
            return false;
        }
        if (!targetRoot.scene.IsValid())
        {
            Log("The Source Parent must be an object in an open scene, not a prefab asset.");
            return false;
        }
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.scene == targetRoot.scene)
        {
            Log("Leave Prefab Mode and run the combiner on the scene instead.");
            return false;
        }
        return true;
    }
    private void Analyze()
    {
        report.Clear();
        details.Clear();
        try
        {
            if (!ValidateInput(false))
                return;
            List<Source> sources = Collect();
            List<Batch> batches = GroupSources(sources);
            int chunks = 0;
            foreach (Batch batch in batches)
                chunks += BuildChunks(batch).Count;
            Log("Material/spatial batches: " + batches.Count);
            Log("Combined renderers that would be created: " + chunks);
            Log("Estimated draw reduction (all source draws visible): " + (sources.Count - chunks));
            Log("Analysis changes nothing. Generation will show combined copies and hide only replaced original renderers.");
        }
        catch (OperationCanceledException) { Log("Analysis cancelled. No scene changes."); }
        catch (Exception ex) { Log("Analysis failed: " + ex.Message); Debug.LogException(ex); }
        finally
        {
            EditorUtility.ClearProgressBar();
            meshCache.Clear();
            FinishReport();
        }
    }
    private void Generate()
    {
        report.Clear();
        details.Clear();
        if (!ValidateInput(true))
        {
            FinishReport();
            return;
        }
        GameObject outputRoot = null;
        string runFolder = null;
        int undoGroup = -1;
        var originalVisibility = new Dictionary<Renderer, bool>();
        try
        {
            List<Source> sources = Collect();
            if (sources.Count == 0) { Log("Nothing eligible to combine."); return; }
            List<Batch> batches = GroupSources(sources);
            if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "CombinedMeshes");
            string runFolderName = "Combined_" + Guid.NewGuid().ToString("N");
            runFolder = AssetFolder + "/" + runFolderName;
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(AssetFolder, runFolderName)))
                throw new InvalidOperationException("Cannot create mesh asset folder.");
            var containers = new Dictionary<string, LodContainer>();
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Combined Copies");
            outputRoot = new GameObject(OutputPrefix + targetRoot.name);
            outputRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(outputRoot, targetRoot.scene);
            outputRoot.transform.SetPositionAndRotation(targetRoot.transform.position, Quaternion.identity);
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (Batch batch in batches)
                {
                    Transform parent = outputRoot.transform;
                    LodContainer container = null;
                    if (batch.containerKey != null)
                    {
                        container = GetContainer(containers, batch, outputRoot.transform);
                        parent = container.gameObject.transform;
                    }
                    foreach (List<Source> chunk in BuildChunks(batch))
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Smart Mesh Combiner",
                            "Building " + batch.material.name, (float)count / Mathf.Max(1, sources.Count)))
                            throw new OperationCanceledException();
                        MeshRenderer created = CreateMesh(
                            batch, chunk, parent, runFolder, count++);
                        container?.levels[batch.lodLevel].Add(created);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            foreach (LodContainer container in containers.Values)
                BuildLodGroup(container);
            if (count == 0)
                throw new Exception("No combined meshes generated.");
            AssetDatabase.SaveAssets();
            // Only commit visibility after EVERY mesh and LODGroup succeeded.
            HashSet<Renderer> replaced = GetReplacedRenderers(sources);
            var undoTargets = new UnityEngine.Object[replaced.Count];
            int targetIndex = 0;
            foreach (Renderer renderer in replaced)
            {
                originalVisibility.Add(renderer, renderer.enabled);
                undoTargets[targetIndex++] = renderer;
            }
            Undo.RecordObjects(undoTargets, "Hide Replaced Original Renderers");
            foreach (Renderer renderer in replaced)
            {
                renderer.enabled = false;
                // Persist renderer overrides on prefab instances without changing the prefab asset.
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            outputRoot.SetActive(true);
            // Register the completed hierarchy so Redo recreates its meshes and LODs.
            Undo.RegisterCreatedObjectUndo(outputRoot, "Generate Combined Copies");
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(targetRoot.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = outputRoot;
            Log("Generated combined mesh objects: " + count);
            if (containers.Count > 0)
                Log("Combined LODGroups: " + containers.Count);
            Log("Assets saved to: " + runFolder);
            Log("Combined output is ACTIVE. Replaced original renderers hidden: " + replaced.Count);
            Log("Skipped objects retain their previous visibility. Original GameObjects and colliders stay active.");
            if (lodMode == LodMode.LOD0Only)
                Log("LOD0 Only replaces each accepted object's entire original LOD chain; lower original LODs are hidden too.");
            Log("Colliders remain on originals; moving this output moves visuals only.");
            Log("Undo restores original renderer visibility and removes generated scene objects. Saved mesh assets remain in the run folder.");
        }
        catch (Exception ex)
        {
            Log(ex is OperationCanceledException ? "Generation cancelled." : "Generation failed: " + ex.Message);
            if (!(ex is OperationCanceledException)) Debug.LogException(ex);
            if (undoGroup >= 0)
                Undo.RevertAllDownToGroup(undoGroup);
            foreach (var state in originalVisibility)
            {
                if (state.Key == null) continue;
                state.Key.enabled = state.Value;
                if (PrefabUtility.IsPartOfPrefabInstance(state.Key))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(state.Key);
            }
            if (outputRoot != null)
                DestroyImmediate(outputRoot);
            if (runFolder != null && !AssetDatabase.DeleteAsset(runFolder))
                Log("Could not remove generated asset folder: " + runFolder);
            Log("Cleanup attempted. Original renderer visibility restored.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            meshCache.Clear();
            FinishReport();
        }
    }
    private HashSet<Renderer> GetReplacedRenderers(List<Source> sources)
    {
        var replaced = new HashSet<Renderer>();
        var replacedLodGroups = new HashSet<LODGroup>();
        foreach (Source source in sources)
        {
            replaced.Add(source.renderer);
            if (lodMode == LodMode.LOD0Only && source.lodGroup != null)
                replacedLodGroups.Add(source.lodGroup);
        }
        // LOD0 Only explicitly replaces the entire object's LOD chain. Otherwise its
        // original LOD1+ would become visible at a distance alongside the baked LOD0.
        foreach (LODGroup group in replacedLodGroups)
            foreach (LOD lod in GetLods(group))
                foreach (Renderer renderer in lod.renderers)
                    if (renderer != null && renderer.transform.IsChildOf(targetRoot.transform)
                        && !IsGeneratedOutput(renderer.transform))
                        replaced.Add(renderer);
        return replaced;
    }
    private LodContainer GetContainer(Dictionary<string, LodContainer> containers, Batch batch, Transform root)
    {
        if (containers.TryGetValue(batch.containerKey, out LodContainer container))
            return container;
        GameObject go = new GameObject("CombinedLOD_" + containers.Count);
        go.transform.SetParent(root, false);
        go.transform.localPosition = root.InverseTransformPoint(batch.lodReference.transform.position);
        go.layer = batch.reference.gameObject.layer;
        int levelCount = GetLods(batch.lodReference).Length;
        container = new LodContainer
        {
            gameObject = go,
            reference = batch.lodReference,
            levels = new List<MeshRenderer>[levelCount]
        };
        for (int i = 0; i < levelCount; i++)
            container.levels[i] = new List<MeshRenderer>();
        containers.Add(batch.containerKey, container);
        return container;
    }
    private void BuildLodGroup(LodContainer container)
    {
        LOD[] referenceLods = GetLods(container.reference);
        LODGroup group = container.gameObject.AddComponent<LODGroup>();
        group.fadeMode = container.reference.fadeMode;
        group.animateCrossFading = container.reference.animateCrossFading;
        LOD[] lods = new LOD[referenceLods.Length];
        for (int i = 0; i < lods.Length; i++)
        {
            lods[i] = new LOD(referenceLods[i].screenRelativeTransitionHeight, container.levels[i].ToArray())
            {
                fadeTransitionWidth = referenceLods[i].fadeTransitionWidth
            };
        }
        group.SetLODs(lods);
        Vector3 scale = container.reference.transform.lossyScale;
        group.localReferencePoint = container.gameObject.transform.InverseTransformPoint(
            container.reference.transform.TransformPoint(container.reference.localReferencePoint));
        group.size = container.reference.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }
    private static List<Vector2> ToVector2(List<Vector4> source)
    {
        List<Vector2> result = new List<Vector2>(source.Count);
        foreach (Vector4 v in source)
            result.Add(v);
        return result;
    }
    private static List<Vector3> ToVector3(List<Vector4> source)
    {
        List<Vector3> result = new List<Vector3>(source.Count);
        foreach (Vector4 v in source)
            result.Add(v);
        return result;
    }
    private static bool IsBaked(MeshRenderer renderer)
    {
        int index = renderer.lightmapIndex;
        return index >= 0 && index < 0xFFFE;
    }
    private MeshRenderer CreateMesh(
        Batch batch,
        List<Source> chunk,
        Transform parent,
        string folder,
        int index)
    {
        bool baked = IsBaked(batch.reference);
        bool hasNormals = false;
        bool hasTangents = false;
        bool hasColors = false;
        bool colorsAreBytes = true;
        int[] uvDimensions = new int[8];
        int totalVertices = 0;
        int totalIndices = 0;
        foreach (Source source in chunk)
        {
            MeshCache cache = GetCache(source.mesh, true);
            MeshCache streams = source.streams != null ? GetCache(source.streams, false) : null;
            hasNormals |= cache.normals != null || streams?.normals != null;
            hasTangents |= cache.tangents != null || streams?.tangents != null;
            Color[] colors = streams?.colors ?? cache.colors;
            if (colors != null)
            {
                hasColors = true;
                colorsAreBytes &= streams?.colors != null ? streams.colorsAreBytes : cache.colorsAreBytes;
            }
            for (int ch = 0; ch < 8; ch++)
            {
                int dimension = streams?.uvs[ch] != null ? streams.uvDimensions[ch] : cache.uvDimensions[ch];
                uvDimensions[ch] = Mathf.Max(uvDimensions[ch], dimension);
            }
            totalVertices = checked(totalVertices + source.vertexCount);
            totalIndices = checked(totalIndices + cache.indices[source.submesh].Length);
        }
        if (baked)
            uvDimensions[1] = Mathf.Max(uvDimensions[1], 2);
        for (int ch = 0; ch < 8; ch++)
        {
            if (uvDimensions[ch] == 1)
                uvDimensions[ch] = 2;
        }
        List<Vector3> positions = new List<Vector3>(totalVertices);
        List<Vector3> normals = hasNormals ? new List<Vector3>(totalVertices) : null;
        List<Vector4> tangents = hasTangents ? new List<Vector4>(totalVertices) : null;
        List<Color> colorList = hasColors ? new List<Color>(totalVertices) : null;
        List<Vector4>[] uvLists = new List<Vector4>[8];
        List<int> triangles = new List<int>(totalIndices);
        Matrix4x4 worldToLocal = parent.worldToLocalMatrix;
        for (int ch = 0; ch < 8; ch++)
        {
            if (uvDimensions[ch] > 0)
                uvLists[ch] = new List<Vector4>(totalVertices);
        }
        foreach (Source source in chunk)
        {
            MeshCache cache = GetCache(source.mesh, true);
            MeshCache streams = source.streams != null ? GetCache(source.streams, false) : null;
            Vector3[] srcNormals = streams?.normals ?? cache.normals;
            Vector4[] srcTangents = streams?.tangents ?? cache.tangents;
            Color[] srcColors = streams?.colors ?? cache.colors;
            Vector4[][] srcUvs = new Vector4[8][];
            for (int ch = 0; ch < 8; ch++)
                srcUvs[ch] = streams?.uvs[ch] ?? cache.uvs[ch];
            // Unity falls back to UV0 for lightmapping when UV1 is missing.
            Vector4[] lightmapUvs = srcUvs[1] ?? srcUvs[0];
            Vector4 lightmapST = source.renderer.lightmapScaleOffset;
            Matrix4x4 matrix = worldToLocal * source.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            bool mirrored = matrix.determinant < 0f;
            int[] vertices = cache.sourceVertices[source.submesh];
            int count = cache.vertexCount[source.submesh];
            int baseIndex = positions.Count;
            Vector3[] sourcePositions = streams?.vertices ?? cache.vertices;
            for (int local = 0; local < count; local++)
            {
                if ((local & 16383) == 0 && EditorUtility.DisplayCancelableProgressBar("Smart Mesh Combiner",
                    "Transforming vertices: " + source.renderer.name, (float)local / Mathf.Max(1, count)))
                    throw new OperationCanceledException();
                int v = vertices[local];
                Vector3 position = matrix.MultiplyPoint3x4(sourcePositions[v]);
                if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
                    throw new InvalidOperationException("Non-finite vertex in " + source.renderer.name);
                positions.Add(position);
                if (hasNormals)
                {
                    Vector3 n = srcNormals != null ? srcNormals[v] : Vector3.up;
                    normals.Add(normalMatrix.MultiplyVector(n).normalized);
                }
                if (hasTangents)
                {
                    Vector4 t = srcTangents != null ? srcTangents[v] : new Vector4(1f, 0f, 0f, 1f);
                    Vector3 direction = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z));
                    if (hasNormals)
                    {
                        Vector3 n = normals[normals.Count - 1];
                        direction -= n * Vector3.Dot(n, direction);
                    }
                    direction.Normalize();
                    tangents.Add(new Vector4(direction.x, direction.y, direction.z, mirrored ? -t.w : t.w));
                }
                if (hasColors)
                    colorList.Add(srcColors != null ? srcColors[v] : Color.white);
                for (int ch = 0; ch < 8; ch++)
                {
                    if (uvLists[ch] == null)
                        continue;
                    if (baked && ch == 1)
                    {
                        Vector4 lm = lightmapUvs != null ? lightmapUvs[v] : Vector4.zero;
                        uvLists[ch].Add(new Vector4(
                            lm.x * lightmapST.x + lightmapST.z,
                            lm.y * lightmapST.y + lightmapST.w, 0f, 0f));
                    }
                    else
                    {
                        uvLists[ch].Add(srcUvs[ch] != null ? srcUvs[ch][v] : Vector4.zero);
                    }
                }
            }
            int[] indices = cache.indices[source.submesh];
            int offset = baseIndex;
            // Negative scale flips faces inside-out unless the winding is reversed.
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                triangles.Add(indices[i] + offset);
                if (mirrored)
                {
                    triangles.Add(indices[i + 2] + offset);
                    triangles.Add(indices[i + 1] + offset);
                }
                else
                {
                    triangles.Add(indices[i + 1] + offset);
                    triangles.Add(indices[i + 2] + offset);
                }
            }
        }
        Mesh combined = new Mesh
        {
            name = "CombinedMesh_" + index,
            indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        try
        {
            combined.SetVertices(positions);
            if (hasNormals)
                combined.SetNormals(normals);
            if (hasTangents)
                combined.SetTangents(tangents);
            if (hasColors)
            {
                if (colorsAreBytes)
                {
                    List<Color32> colors32 = new List<Color32>(colorList.Count);
                    foreach (Color c in colorList)
                        colors32.Add(c);
                    combined.SetColors(colors32);
                }
                else
                {
                    combined.SetColors(colorList);
                }
            }
            for (int ch = 0; ch < 8; ch++)
            {
                if (uvLists[ch] == null)
                    continue;
                if (uvDimensions[ch] == 2)
                    combined.SetUVs(ch, ToVector2(uvLists[ch]));
                else if (uvDimensions[ch] == 3)
                    combined.SetUVs(ch, ToVector3(uvLists[ch]));
                else
                    combined.SetUVs(ch, uvLists[ch]);
            }
            combined.SetIndices(triangles, MeshTopology.Triangles, 0, true);
            if (combined.vertexCount == 0 || triangles.Count == 0)
                throw new Exception("Empty combined mesh for " + batch.material.name);
            combined.RecalculateBounds();
            string path = folder + "/Mesh_" + index + ".asset";
            AssetDatabase.CreateAsset(combined, path);
            string lodSuffix = batch.lodLevel >= 0 ? "_LOD" + batch.lodLevel : "";
            GameObject result = new GameObject("Combined_" + batch.material.name + lodSuffix + "_" + index);
            result.transform.SetParent(parent, false);
            MeshRenderer original = batch.reference;
            result.layer = original.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(result,
                GameObjectUtility.GetStaticEditorFlags(original.gameObject));
            MeshFilter filter = result.AddComponent<MeshFilter>();
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            filter.sharedMesh = combined;
            renderer.sharedMaterial = batch.material;
            renderer.probeAnchor = original.probeAnchor;
            renderer.receiveGI = original.receiveGI;
            renderer.scaleInLightmap = original.scaleInLightmap;
            renderer.stitchLightmapSeams = original.stitchLightmapSeams;
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.staticShadowCaster = original.staticShadowCaster;
            renderer.receiveShadows = original.receiveShadows;
            renderer.lightProbeUsage = original.lightProbeUsage;
            renderer.reflectionProbeUsage = original.reflectionProbeUsage;
            renderer.motionVectorGenerationMode = original.motionVectorGenerationMode;
            renderer.allowOcclusionWhenDynamic = original.allowOcclusionWhenDynamic;
            renderer.sortingLayerID = original.sortingLayerID;
            renderer.sortingOrder = original.sortingOrder;
            renderer.renderingLayerMask = original.renderingLayerMask;
            renderer.lightmapIndex = original.lightmapIndex;
            if (baked)
                renderer.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            details.Add("CREATED " + result.name + " (" + chunk.Count + " source parts, " +
                        combined.vertexCount + " verts)");
            return renderer;
        }
        catch
        {
            if (!AssetDatabase.Contains(combined))
                DestroyImmediate(combined);
            throw;
        }
    }
}
#endif
