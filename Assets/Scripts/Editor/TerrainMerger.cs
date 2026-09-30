using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Merges multiple Terrain GameObjects into one Terrain.
/// Source terrains are never modified.
///
/// Stages implemented:
/// 1. Heightmap
/// 2. Holes
/// 3. Terrain Layers + Alphamaps
/// 4. Trees
/// 5. Grass / Terrain Details
/// </summary>
public static class TerrainMerger
{
    // ============================================================
    // SETTINGS
    // ============================================================

    private const int OUTPUT_HEIGHTMAP_RESOLUTION = 1025;

    // Alphamap resolution is chosen automatically from source density.
    // Unity accepts powers of two from 16 to 2048.
    private const int MIN_ALPHAMAP_RESOLUTION = 16;
    private const int MAX_ALPHAMAP_RESOLUTION = 2048;

    // Detail / grass settings.
    // If merging is too slow or uses too much memory, lower MAX_DETAIL_RESOLUTION.
    private const int MIN_DETAIL_RESOLUTION = 16;
    private const int MAX_DETAIL_RESOLUTION = 2048;

    // Unity detail values are usually small density values.
    // Raise this if your source terrains use higher detail density values.
    private const int MAX_DETAIL_VALUE = 64;

    // When true, detail density is adjusted when source and merged detail
    // cell sizes differ. This helps preserve visual density.
    private const bool SCALE_DETAIL_DENSITY = true;

    private const string OUTPUT_FOLDER = "Assets/MergedTerrains";
    private const string OUTPUT_DATA_PATH =
        OUTPUT_FOLDER + "/MergedTerrainData.asset";

    // ============================================================
    // DATA TYPES
    // ============================================================

    private class TerrainInfo
    {
        public Terrain terrain;
        public TerrainData data;
        public Vector3 position;
        public Vector3 size;

        // Holes
        public bool[,] holes;
        public int holesResolution;

        // Terrain Layers
        public TerrainLayer[] terrainLayers;

        // Alphamap [z, x, layer]
        public float[,,] alphamaps;
        public int alphamapResolution;
        public int alphamapLayerCount;

        // Trees
        public TreePrototype[] treePrototypes;
        public TreeInstance[] treeInstances;

        // Details / Grass
        public DetailPrototype[] detailPrototypes;
        public int detailResolution;
        public int detailResolutionPerPatch;
        public int detailWidth;
        public int detailHeight;
        public DetailScatterMode detailScatterMode;
    }

    private struct MergeBounds
    {
        public float minX, maxX;
        public float minY, maxY;
        public float minZ, maxZ;

        public float Width => maxX - minX;
        public float Length => maxZ - minZ;
        public float Height => maxY - minY;
    }

    // ============================================================
    // MENU ENTRY
    // ============================================================

    [MenuItem("Terrain/Merge Selected Terrains (Height + Holes + Textures + Trees + Details)")]
    public static void MergeSelectedTerrains()
    {
        Terrain[] selectedTerrains = GetSelectedTerrains();

        if (selectedTerrains.Length < 2)
        {
            ShowDialog("Please select at least TWO Terrain GameObjects.");
            return;
        }

        if (!ValidateTransforms(selectedTerrains))
            return;

        List<TerrainInfo> terrainInfos = CacheTerrainInfos(selectedTerrains);

        if (terrainInfos.Count < 2)
        {
            ShowDialog("Not enough valid terrains to merge.");
            return;
        }

        MergeBounds bounds = CalculateBounds(terrainInfos);

        if (bounds.Width <= 0 || bounds.Length <= 0 || bounds.Height <= 0)
        {
            ShowDialog("Invalid combined Terrain bounds.");
            return;
        }

        Debug.Log(
            $"Merged bounds:\n" +
            $"X: {bounds.minX} to {bounds.maxX}\n" +
            $"Y: {bounds.minY} to {bounds.maxY}\n" +
            $"Z: {bounds.minZ} to {bounds.maxZ}\n" +
            $"Size: {bounds.Width} x {bounds.Height} x {bounds.Length}"
        );

        EnsureFolderExists();

        // ---------------- Create TerrainData ----------------

        TerrainData mergedData = new TerrainData();

        mergedData.heightmapResolution = OUTPUT_HEIGHTMAP_RESOLUTION;
        mergedData.alphamapResolution =
            ChooseAlphamapResolution(terrainInfos, bounds);

        mergedData.size = new Vector3(
            bounds.Width,
            bounds.Height,
            bounds.Length
        );

        // Create the asset BEFORE writing data. Writing alphamaps to an
        // unsaved TerrainData caused only layer 0 to render.
        string assetPath =
            AssetDatabase.GenerateUniqueAssetPath(OUTPUT_DATA_PATH);

        AssetDatabase.CreateAsset(mergedData, assetPath);

        // ---------------- Stages ----------------

        MergeHeightmap(mergedData, terrainInfos, bounds);
        MergeHoles(mergedData, terrainInfos, bounds);

        List<TerrainLayer> mergedLayers = CollectTerrainLayers(terrainInfos);

        // Unity 6 requires the undo name argument.
        mergedData.SetTerrainLayersRegisterUndo(
            mergedLayers.ToArray(),
            "Merge Terrain Layers"
        );

        MergeAlphamaps(mergedData, terrainInfos, mergedLayers, bounds);
        VerifyAlphamaps(mergedData, mergedLayers);

        int mergedTreeCount = MergeTrees(mergedData, terrainInfos, bounds);
        int mergedDetailPrototypeCount = MergeDetails(mergedData, terrainInfos, bounds);

        // ---------------- Create object + save ----------------

        GameObject mergedObject = CreateTerrainObject(
            mergedData,
            bounds,
            terrainInfos[0].terrain
        );

        mergedObject.GetComponent<Terrain>().Flush();

        EditorUtility.SetDirty(mergedData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = mergedObject;
        EditorGUIUtility.PingObject(mergedObject);

        Debug.Log(
            "========================================\n" +
            "TERRAIN MERGE COMPLETE\n" +
            "========================================\n" +
            $"Terrain Layers: {mergedLayers.Count}\n" +
            $"Heightmap: {OUTPUT_HEIGHTMAP_RESOLUTION}\n" +
            $"Alphamap: {mergedData.alphamapResolution}\n" +
            $"Trees: {mergedTreeCount}\n" +
            $"Details: {mergedDetailPrototypeCount} prototype(s)\n" +
            $"Output: {assetPath}\n" +
            "========================================"
        );

        ShowDialog(
            "Terrain merge completed successfully.\n\n" +
            "Height + Holes + Terrain Layers + Textures + Trees + Details were merged."
        );
    }

    // ============================================================
    // SELECTION + VALIDATION
    // ============================================================

    private static Terrain[] GetSelectedTerrains()
    {
        List<Terrain> terrains = new List<Terrain>();

        foreach (GameObject obj in Selection.gameObjects)
        {
            Terrain terrain = obj.GetComponent<Terrain>();

            if (terrain != null)
                terrains.Add(terrain);
        }

        return terrains.ToArray();
    }

    private static bool ValidateTransforms(Terrain[] terrains)
    {
        foreach (Terrain terrain in terrains)
        {
            if (Quaternion.Angle(
                    terrain.transform.rotation,
                    Quaternion.identity) > 0.01f)
            {
                ShowDialog(
                    $"Terrain '{terrain.name}' has rotation.\n\n" +
                    "Terrain rotation must be 0,0,0."
                );

                return false;
            }

            if (Vector3.Distance(
                    terrain.transform.lossyScale,
                    Vector3.one) > 0.001f)
            {
                ShowDialog(
                    $"Terrain '{terrain.name}' has non-1 scale.\n\n" +
                    "Terrain scale must be 1,1,1."
                );

                return false;
            }
        }

        return true;
    }

    // ============================================================
    // CACHE SOURCE DATA
    // Read expensive TerrainData once.
    // ============================================================

    private static List<TerrainInfo> CacheTerrainInfos(Terrain[] terrains)
    {
        List<TerrainInfo> infos = new List<TerrainInfo>();

        foreach (Terrain terrain in terrains)
        {
            TerrainData data = terrain.terrainData;

            if (data == null)
            {
                Debug.LogWarning(
                    $"Skipping '{terrain.name}' because it has no TerrainData."
                );

                continue;
            }

            TerrainInfo info = new TerrainInfo
            {
                terrain = terrain,
                data = data,
                position = terrain.transform.position,
                size = data.size,

                terrainLayers = data.terrainLayers,
                holesResolution = data.holesResolution,
                alphamapResolution = data.alphamapResolution,
                alphamapLayerCount = data.alphamapLayers,

                detailPrototypes = data.detailPrototypes,
                detailResolution = data.detailResolution,
                detailResolutionPerPatch = data.detailResolutionPerPatch,
                detailWidth = data.detailWidth,
                detailHeight = data.detailHeight,
                detailScatterMode = data.detailScatterMode
            };

            if (info.holesResolution > 0)
            {
                info.holes = data.GetHoles(
                    0,
                    0,
                    info.holesResolution,
                    info.holesResolution
                );
            }

            if (info.alphamapLayerCount > 0)
            {
                info.alphamaps = data.GetAlphamaps(
                    0,
                    0,
                    info.alphamapResolution,
                    info.alphamapResolution
                );
            }

            info.treePrototypes = data.treePrototypes;
            info.treeInstances = data.treeInstances;

            infos.Add(info);
        }

        return infos;
    }

    // ============================================================
    // BOUNDS
    // ============================================================

    private static MergeBounds CalculateBounds(List<TerrainInfo> infos)
    {
        MergeBounds b = new MergeBounds
        {
            minX = float.PositiveInfinity,
            maxX = float.NegativeInfinity,
            minY = float.PositiveInfinity,
            maxY = float.NegativeInfinity,
            minZ = float.PositiveInfinity,
            maxZ = float.NegativeInfinity
        };

        foreach (TerrainInfo info in infos)
        {
            b.minX = Mathf.Min(b.minX, info.position.x);
            b.maxX = Mathf.Max(b.maxX, info.position.x + info.size.x);

            b.minZ = Mathf.Min(b.minZ, info.position.z);
            b.maxZ = Mathf.Max(b.maxZ, info.position.z + info.size.z);

            // Terrain height is stored relative to its Y position.
            b.minY = Mathf.Min(b.minY, info.position.y);
            b.maxY = Mathf.Max(b.maxY, info.position.y + info.size.y);
        }

        return b;
    }

    // ============================================================
    // ALPHAMAP RESOLUTION
    // Keep the densest source texel density, so merging two
    // terrains does not halve texture detail.
    // ============================================================

    private static int ChooseAlphamapResolution(
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        float maxTexelsPerUnit = 0f;

        foreach (TerrainInfo info in infos)
        {
            if (info.alphamapResolution <= 0)
                continue;

            float densityX = info.alphamapResolution / info.size.x;
            float densityZ = info.alphamapResolution / info.size.z;

            maxTexelsPerUnit = Mathf.Max(
                maxTexelsPerUnit,
                densityX,
                densityZ
            );
        }

        int needed = Mathf.CeilToInt(
            maxTexelsPerUnit * Mathf.Max(bounds.Width, bounds.Length)
        );

        int resolution = Mathf.NextPowerOfTwo(Mathf.Max(needed, 1));

        resolution = Mathf.Clamp(
            resolution,
            MIN_ALPHAMAP_RESOLUTION,
            MAX_ALPHAMAP_RESOLUTION
        );

        Debug.Log($"Chosen alphamap resolution: {resolution}");

        return resolution;
    }

    // ============================================================
    // STAGE 1 - HEIGHTMAP
    // ============================================================

    private static void MergeHeightmap(
        TerrainData mergedData,
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        Debug.Log("Merging heightmaps...");

        const int res = OUTPUT_HEIGHTMAP_RESOLUTION;

        float[,] mergedHeights = new float[res, res];

        for (int z = 0; z < res; z++)
        {
            // Heightmaps are vertex-based, so corner-aligned mapping is correct.
            float worldZ = bounds.minZ + ((float)z / (res - 1)) * bounds.Length;

            for (int x = 0; x < res; x++)
            {
                float worldX =
                    bounds.minX + ((float)x / (res - 1)) * bounds.Width;

                TerrainInfo source = FindTerrainAtPosition(infos, worldX, worldZ);

                if (source == null)
                {
                    // Outside all source terrains.
                    mergedHeights[z, x] = 0f;
                    continue;
                }

                WorldToSourceNormalized(
                    source,
                    worldX,
                    worldZ,
                    out float nx,
                    out float nz
                );

                float localHeight =
                    source.data.GetInterpolatedHeight(nx, nz);

                float worldHeight = source.position.y + localHeight;

                mergedHeights[z, x] =
                    Mathf.InverseLerp(bounds.minY, bounds.maxY, worldHeight);
            }

            if (z % 128 == 0)
                Debug.Log($"Heightmap: {z}/{res}");
        }

        mergedData.SetHeights(0, 0, mergedHeights);
    }

    // ============================================================
    // STAGE 2 - HOLES
    // ============================================================

    private static void MergeHoles(
        TerrainData mergedData,
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        Debug.Log("Merging terrain holes...");

        // holesResolution is read-only in Unity 6.
        int res = mergedData.holesResolution;

        bool[,] mergedHoles = new bool[res, res];

        for (int z = 0; z < res; z++)
        {
            float worldZ = bounds.minZ + ((float)z / (res - 1)) * bounds.Length;

            for (int x = 0; x < res; x++)
            {
                float worldX =
                    bounds.minX + ((float)x / (res - 1)) * bounds.Width;

                TerrainInfo source = FindTerrainAtPosition(infos, worldX, worldZ);

                if (source != null &&
                    source.holes != null &&
                    source.holesResolution > 0)
                {
                    WorldToSourceNormalized(
                        source,
                        worldX,
                        worldZ,
                        out float nx,
                        out float nz
                    );

                    int sourceX = Mathf.Clamp(
                        Mathf.RoundToInt(nx * (source.holesResolution - 1)),
                        0,
                        source.holesResolution - 1
                    );

                    int sourceZ = Mathf.Clamp(
                        Mathf.RoundToInt(nz * (source.holesResolution - 1)),
                        0,
                        source.holesResolution - 1
                    );

                    mergedHoles[z, x] = source.holes[sourceZ, sourceX];
                }
                else
                {
                    // Outside all sources: false = HOLE (Unity: true = surface, false = hole).
                    // This punches holes where no source terrain existed, so an irregular
                    // footprint is preserved inside the rectangular bounding box.
                    mergedHoles[z, x] = false;
                }
            }
        }

        mergedData.SetHoles(0, 0, mergedHoles);
    }

    // ============================================================
    // STAGE 3 - TERRAIN LAYERS
    // ============================================================

    private static List<TerrainLayer> CollectTerrainLayers(
        List<TerrainInfo> infos)
    {
        Debug.Log("Collecting Terrain Layers...");

        List<TerrainLayer> mergedLayers = new List<TerrainLayer>();

        foreach (TerrainInfo info in infos)
        {
            if (info.terrainLayers == null)
                continue;

            foreach (TerrainLayer layer in info.terrainLayers)
            {
                if (layer == null)
                    continue;

                if (!mergedLayers.Contains(layer))
                    mergedLayers.Add(layer);
            }
        }

        if (mergedLayers.Count == 0)
            Debug.LogWarning("No Terrain Layers were found.");
        else
            Debug.Log($"Found {mergedLayers.Count} unique Terrain Layer(s).");

        return mergedLayers;
    }

    // ============================================================
    // STAGE 3 - ALPHAMAPS
    // ============================================================

    private static void MergeAlphamaps(
        TerrainData mergedData,
        List<TerrainInfo> infos,
        List<TerrainLayer> mergedLayers,
        MergeBounds bounds)
    {
        int layerCount = mergedLayers.Count;

        if (layerCount == 0)
            return;

        Debug.Log("Merging Terrain texture alphamaps...");

        int res = mergedData.alphamapResolution;

        float[,,] merged = new float[res, res, layerCount];

        for (int z = 0; z < res; z++)
        {
            // Alphamap texels are cell-based, so sample texel CENTERS.
            float worldZ = bounds.minZ + ((z + 0.5f) / res) * bounds.Length;

            for (int x = 0; x < res; x++)
            {
                float worldX = bounds.minX + ((x + 0.5f) / res) * bounds.Width;

                TerrainInfo source = FindTerrainAtPosition(infos, worldX, worldZ);

                if (source != null &&
                    source.alphamaps != null &&
                    source.alphamapLayerCount > 0)
                {
                    WorldToSourceNormalized(
                        source,
                        worldX,
                        worldZ,
                        out float nx,
                        out float nz
                    );

                    // Source texel-center coordinates.
                    // SampleAlphamap clamps to the valid range.
                    float sourcePixelX = nx * source.alphamapResolution - 0.5f;
                    float sourcePixelZ = nz * source.alphamapResolution - 0.5f;

                    int usableLayers = Mathf.Min(
                        source.alphamapLayerCount,
                        source.terrainLayers.Length
                    );

                    for (int sl = 0; sl < usableLayers; sl++)
                    {
                        TerrainLayer sourceLayer = source.terrainLayers[sl];

                        if (sourceLayer == null)
                            continue;

                        int mergedIndex = mergedLayers.IndexOf(sourceLayer);

                        if (mergedIndex < 0)
                            continue;

                        merged[z, x, mergedIndex] += SampleAlphamap(
                            source.alphamaps,
                            sourcePixelX,
                            sourcePixelZ,
                            sl
                        );
                    }
                }

                // Normalize. Empty pixels fall back to layer 0 so they are
                // never left with all-zero weights.
                float total = 0f;

                for (int l = 0; l < layerCount; l++)
                    total += merged[z, x, l];

                if (total > 0.0001f)
                {
                    for (int l = 0; l < layerCount; l++)
                        merged[z, x, l] /= total;
                }
                else
                {
                    merged[z, x, 0] = 1f;
                }
            }

            if (z % 128 == 0)
                Debug.Log($"Alphamap: {z}/{res}");
        }

        Debug.Log("Writing merged alphamap...");

        mergedData.SetAlphamaps(0, 0, merged);
    }

    /// <summary>
    /// Reads the alphamap BACK from the merged TerrainData, so the
    /// diagnostic proves what Unity actually stored.
    /// </summary>
    private static void VerifyAlphamaps(
        TerrainData mergedData,
        List<TerrainLayer> mergedLayers)
    {
        int layerCount = mergedLayers.Count;

        if (layerCount == 0)
            return;

        Debug.Log("Verifying merged Terrain Layer data...");

        int res = mergedData.alphamapResolution;

        float[,,] stored = mergedData.GetAlphamaps(0, 0, res, res);

        int storedLayers = stored.GetLength(2);

        for (int layer = 0; layer < storedLayers; layer++)
        {
            float min = 1f;
            float max = 0f;
            float sum = 0f;
            int count = 0;

            for (int z = 0; z < res; z += 16)
            {
                for (int x = 0; x < res; x += 16)
                {
                    float value = stored[z, x, layer];

                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                    sum += value;
                    count++;
                }
            }

            string layerName =
                layer < layerCount && mergedLayers[layer] != null
                    ? mergedLayers[layer].name
                    : "NULL";

            Debug.Log(
                $"Layer {layer} | Name: {layerName} | " +
                $"Min: {min:F3} | Max: {max:F3} | " +
                $"Average: {(count > 0 ? sum / count : 0f):F3}"
            );
        }
    }

    // ============================================================
    // STAGE 4 - TREES
    // ============================================================

    private static int MergeTrees(
        TerrainData mergedData,
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        Debug.Log("Merging trees...");

        // ---------------- Combined prototype list ----------------
        // Prototypes are matched by prefab reference.
        // prototypeMaps[terrainIndex][sourcePrototype] = mergedPrototype

        List<TreePrototype> mergedPrototypes = new List<TreePrototype>();
        int[][] prototypeMaps = new int[infos.Count][];

        for (int i = 0; i < infos.Count; i++)
        {
            TreePrototype[] sourcePrototypes = infos[i].treePrototypes;
            int length = sourcePrototypes != null ? sourcePrototypes.Length : 0;

            int[] map = new int[length];

            for (int p = 0; p < length; p++)
            {
                map[p] = -1;

                TreePrototype sp = sourcePrototypes[p];

                if (sp == null || sp.prefab == null)
                    continue;

                int mergedIndex = -1;

                for (int m = 0; m < mergedPrototypes.Count; m++)
                {
                    if (mergedPrototypes[m].prefab == sp.prefab)
                    {
                        mergedIndex = m;
                        break;
                    }
                }

                if (mergedIndex < 0)
                {
                    mergedPrototypes.Add(new TreePrototype
                    {
                        prefab = sp.prefab,
                        bendFactor = sp.bendFactor,
                        navMeshLod = sp.navMeshLod
                    });

                    mergedIndex = mergedPrototypes.Count - 1;
                }

                map[p] = mergedIndex;
            }

            prototypeMaps[i] = map;
        }

        if (mergedPrototypes.Count == 0)
        {
            Debug.Log("No tree prototypes found. Skipping trees.");
            return 0;
        }

        // Prototypes MUST be set before instances.
        mergedData.treePrototypes = mergedPrototypes.ToArray();

        Debug.Log($"Combined tree prototypes: {mergedPrototypes.Count}");

        // ---------------- Instances ----------------

        List<TreeInstance> mergedTrees = new List<TreeInstance>();

        int skippedInvalid = 0;
        int skippedOverlap = 0;

        for (int i = 0; i < infos.Count; i++)
        {
            TerrainInfo info = infos[i];

            if (info.treeInstances == null)
                continue;

            int[] map = prototypeMaps[i];

            foreach (TreeInstance tree in info.treeInstances)
            {
                if (tree.prototypeIndex < 0 ||
                    tree.prototypeIndex >= map.Length ||
                    map[tree.prototypeIndex] < 0)
                {
                    skippedInvalid++;
                    continue;
                }

                // Source normalized to world.
                float worldX = info.position.x + tree.position.x * info.size.x;
                float worldY = info.position.y + tree.position.y * info.size.y;
                float worldZ = info.position.z + tree.position.z * info.size.z;

                // In overlaps only the terrain that owns the position
                // keeps the tree. Prevents duplicates.
                if (FindTerrainAtPosition(infos, worldX, worldZ) != info)
                {
                    skippedOverlap++;
                    continue;
                }

                // World to merged normalized.
                TreeInstance merged = tree;

                merged.prototypeIndex = map[tree.prototypeIndex];

                merged.position = new Vector3(
                    Mathf.Clamp01((worldX - bounds.minX) / bounds.Width),
                    Mathf.Clamp01(
                        Mathf.InverseLerp(bounds.minY, bounds.maxY, worldY)
                    ),
                    Mathf.Clamp01((worldZ - bounds.minZ) / bounds.Length)
                );

                // rotation, widthScale, heightScale, color,
                // lightmapColor are copied from the source tree.
                mergedTrees.Add(merged);
            }
        }

        // snapToHeightmap = true keeps trees on the resampled ground.
        mergedData.SetTreeInstances(mergedTrees.ToArray(), true);

        Debug.Log(
            $"Trees merged: {mergedTrees.Count} | " +
            $"Skipped invalid: {skippedInvalid} | " +
            $"Skipped overlap: {skippedOverlap}"
        );

        return mergedTrees.Count;
    }

    // ============================================================
    // STAGE 5 - DETAILS / GRASS
    // ============================================================

    private static int MergeDetails(
        TerrainData mergedData,
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        Debug.Log("Merging terrain details / grass...");

        // ---------------- Combined prototype list ----------------
        // prototypeMaps[terrainIndex][sourcePrototype] = mergedPrototype

        List<DetailPrototype> mergedPrototypes = new List<DetailPrototype>();
        int[][] prototypeMaps = new int[infos.Count][];
        TerrainInfo modelSource = null; // first source that has usable details

        for (int i = 0; i < infos.Count; i++)
        {
            DetailPrototype[] sourcePrototypes = infos[i].detailPrototypes;
            int length = sourcePrototypes != null ? sourcePrototypes.Length : 0;

            int[] map = new int[length];

            for (int p = 0; p < length; p++)
            {
                map[p] = -1;

                DetailPrototype sp = sourcePrototypes[p];

                if (sp == null ||
                    (sp.prototype == null && sp.prototypeTexture == null))
                {
                    continue;
                }

                int mergedIndex = FindDetailPrototype(mergedPrototypes, sp);

                if (mergedIndex < 0)
                {
                    mergedPrototypes.Add(CopyDetailPrototype(sp));
                    mergedIndex = mergedPrototypes.Count - 1;
                }

                map[p] = mergedIndex;

                if (modelSource == null)
                    modelSource = infos[i];
            }

            prototypeMaps[i] = map;
        }

        if (modelSource == null || mergedPrototypes.Count == 0)
        {
            Debug.Log("No detail prototypes found. Skipping details.");
            return 0;
        }

        // ---------------- Scatter mode ----------------
        // Coverage and Instance Count mode interpret layer values differently,
        // so the merged terrain follows the first source with details.

        DetailScatterMode scatterMode = modelSource.detailScatterMode;

        foreach (TerrainInfo info in infos)
        {
            if (info.detailPrototypes != null &&
                info.detailPrototypes.Length > 0 &&
                info.detailScatterMode != scatterMode)
            {
                Debug.LogWarning(
                    $"Source '{info.terrain.name}' uses detail scatter mode " +
                    $"{info.detailScatterMode}, but merged terrain uses " +
                    $"{scatterMode}. Detail density may look different."
                );
            }
        }

        mergedData.SetDetailScatterMode(scatterMode);

        // ---------------- Resolution ----------------
        // SetDetailResolution clears existing detail data, so it is called
        // before prototypes and layers are assigned.

        int desiredRes = ChooseDetailResolution(infos, bounds);

        int perPatch = modelSource.detailResolutionPerPatch > 0
            ? modelSource.detailResolutionPerPatch
            : 16;

        if (perPatch > desiredRes || desiredRes % perPatch != 0)
            perPatch = 16; // MIN_DETAIL_RESOLUTION is 16, so 16 always divides

        mergedData.SetDetailResolution(desiredRes, perPatch);

        int res = mergedData.detailResolution;
        mergedData.detailPrototypes = mergedPrototypes.ToArray();

        Debug.Log(
            $"Combined detail prototypes: {mergedPrototypes.Count} | " +
            $"Detail resolution: {res} | Per patch: {perPatch} | " +
            $"Scatter mode: {scatterMode}"
        );

        // ---------------- Accumulate merged detail maps ----------------
        // Floats keep fractional density from scaling. Conversion back to
        // ints uses dithering so sparse grass does not round down to zero.

        List<float[,]> accumulated = new List<float[,]>();

        for (int i = 0; i < mergedPrototypes.Count; i++)
        {
            accumulated.Add(new float[res, res]);
        }

        float mergedCellArea = (bounds.Width / res) * (bounds.Length / res);
        double sourceDensityTotal = 0.0; // sum(value * cellArea) over sources
        int sourceMaxValue = 0;          // highest raw value found in any source

        for (int i = 0; i < infos.Count; i++)
        {
            TerrainInfo info = infos[i];

            if (info.detailPrototypes == null || info.detailPrototypes.Length == 0)
                continue;

            if (info.size.x <= 0f || info.size.z <= 0f)
                continue;

            int sourceW = info.detailWidth > 0 ? info.detailWidth : info.detailResolution;
            int sourceH = info.detailHeight > 0 ? info.detailHeight : info.detailResolution;

            if (sourceW <= 0 || sourceH <= 0)
                continue;

            int layerCount = Mathf.Min(
                info.detailPrototypes.Length,
                prototypeMaps[i].Length
            );

            if (layerCount <= 0)
                continue;

            float sourceCellArea = (info.size.x / sourceW) * (info.size.z / sourceH);

            // Read each source detail layer once.
            int[][,] sourceLayers = new int[layerCount][,];
            bool hasReadableLayer = false;

            for (int p = 0; p < layerCount; p++)
            {
                if (prototypeMaps[i][p] < 0)
                    continue;

                int[,] layerData = info.data.GetDetailLayer(
                    0,
                    0,
                    sourceW,
                    sourceH,
                    p
                );

                sourceLayers[p] = layerData;

                if (layerData == null)
                    continue;

                hasReadableLayer = true;

                long layerSum = 0;

                for (int z = 0; z < sourceH; z++)
                {
                    for (int x = 0; x < sourceW; x++)
                    {
                        int v = layerData[z, x];
                        layerSum += v;

                        if (v > sourceMaxValue)
                            sourceMaxValue = v;
                    }
                }

                sourceDensityTotal += (double)layerSum * sourceCellArea;
            }

            if (!hasReadableLayer)
                continue;

            // Only loop over output cells that could belong to this terrain.
            int startX = Mathf.Clamp(
                Mathf.FloorToInt(((info.position.x - bounds.minX) / bounds.Width) * res),
                0,
                res
            );

            int endX = Mathf.Clamp(
                Mathf.CeilToInt(((info.position.x + info.size.x - bounds.minX) / bounds.Width) * res),
                0,
                res
            );

            int startZ = Mathf.Clamp(
                Mathf.FloorToInt(((info.position.z - bounds.minZ) / bounds.Length) * res),
                0,
                res
            );

            int endZ = Mathf.Clamp(
                Mathf.CeilToInt(((info.position.z + info.size.z - bounds.minZ) / bounds.Length) * res),
                0,
                res
            );

            // Density scaling only makes sense when values are instance
            // counts per cell, not coverage weights.
            float densityScale = 1f;

            if (SCALE_DETAIL_DENSITY &&
                info.detailScatterMode == DetailScatterMode.InstanceCountMode &&
                sourceCellArea > 0f)
            {
                densityScale = mergedCellArea / sourceCellArea;
            }

            Debug.Log(
                $"Detail source '{info.terrain.name}': {sourceW}x{sourceH} cells, " +
                $"cell area {sourceCellArea:F3} m2, merged cell area " +
                $"{mergedCellArea:F3} m2, density scale {densityScale:F2}, " +
                $"mode {info.detailScatterMode}"
            );

            for (int z = startZ; z < endZ; z++)
            {
                // Detail texels are cell-based, so sample texel centers.
                float worldZ = bounds.minZ + ((z + 0.5f) / res) * bounds.Length;

                for (int x = startX; x < endX; x++)
                {
                    float worldX = bounds.minX + ((x + 0.5f) / res) * bounds.Width;

                    // Overlap rule: first terrain owning this position wins.
                    if (FindTerrainAtPosition(infos, worldX, worldZ) != info)
                        continue;

                    WorldToSourceNormalized(
                        info,
                        worldX,
                        worldZ,
                        out float nx,
                        out float nz
                    );

                    // Cell-based source index is a plain floor.
                    int sourceX = Mathf.Clamp(
                        Mathf.FloorToInt(nx * sourceW),
                        0,
                        sourceW - 1
                    );

                    int sourceZ = Mathf.Clamp(
                        Mathf.FloorToInt(nz * sourceH),
                        0,
                        sourceH - 1
                    );

                    for (int p = 0; p < layerCount; p++)
                    {
                        int mergedIndex = prototypeMaps[i][p];

                        if (mergedIndex < 0 || sourceLayers[p] == null)
                            continue;

                        int value = sourceLayers[p][sourceZ, sourceX];

                        if (value <= 0)
                            continue;

                        accumulated[mergedIndex][z, x] += value * densityScale;
                    }
                }
            }
        }

        // ---------------- Write merged detail layers ----------------

        // Coverage mode stores weights that can be far above 64, so the clamp
        // never goes below the highest value present in the sources.
        int maxDetailValue = Mathf.Max(MAX_DETAIL_VALUE, sourceMaxValue);

        Debug.Log(
            $"Source max detail value: {sourceMaxValue} | " +
            $"Output clamp: {maxDetailValue}"
        );

        int nonZeroCells = 0;
        long clampedCells = 0;
        double clampedLoss = 0.0; // density (value * cellArea) cut by clamp
        double mergedDensityTotal = 0.0; // sum(value * cellArea)

        for (int layer = 0; layer < mergedPrototypes.Count; layer++)
        {
            int[,] outputLayer = new int[res, res];
            float[,] layerAccum = accumulated[layer];
            long layerSum = 0;

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float f = layerAccum[z, x];

                    if (f <= 0f)
                        continue;

                    // Dither: keeps average density when f is fractional.
                    int value = Mathf.FloorToInt(f + Hash01(x, z, layer));

                    if (value > maxDetailValue)
                    {
                        clampedCells++;
                        clampedLoss += (double)(value - maxDetailValue) * mergedCellArea;
                        value = maxDetailValue;
                    }

                    if (value <= 0)
                        continue;

                    outputLayer[z, x] = value;
                    layerSum += value;
                    nonZeroCells++;
                }
            }

            mergedDensityTotal += (double)layerSum * mergedCellArea;

            mergedData.SetDetailLayer(0, 0, layer, outputLayer);
        }

        double ratio = sourceDensityTotal > 0.0
            ? mergedDensityTotal / sourceDensityTotal
            : 0.0;

        Debug.Log(
            $"Details merged: {mergedPrototypes.Count} prototype(s) | " +
            $"Non-zero detail cells: {nonZeroCells}"
        );

        double lostPct = sourceDensityTotal > 0.0
            ? clampedLoss / sourceDensityTotal
            : 0.0;

        Debug.Log(
            $"Detail clamp: {clampedCells} cell(s) exceeded {maxDetailValue}; " +
            $"density lost to clamp: {lostPct:P1} of source."
        );

        Debug.Log(
            $"Detail density check (merged / source): {ratio:P1}. " +
            "Expect about 100% (a bit less if source terrains overlap, " +
            "or if MAX_DETAIL_VALUE clamps dense cells)."
        );

        return mergedPrototypes.Count;
    }

    private static int ChooseDetailResolution(
        List<TerrainInfo> infos,
        MergeBounds bounds)
    {
        float maxDetailsPerUnit = 0f;
        int highestSourceRes = 0;

        foreach (TerrainInfo info in infos)
        {
            if (info.size.x <= 0f || info.size.z <= 0f)
                continue;

            int sourceRes = info.detailResolution > 0
                ? info.detailResolution
                : Mathf.Max(info.detailWidth, info.detailHeight);

            if (sourceRes <= 0)
                continue;

            highestSourceRes = Mathf.Max(highestSourceRes, sourceRes);

            // Only count terrains that actually contain detail prototypes.
            if (info.detailPrototypes == null || info.detailPrototypes.Length == 0)
                continue;

            float densityX = sourceRes / info.size.x;
            float densityZ = sourceRes / info.size.z;

            maxDetailsPerUnit = Mathf.Max(
                maxDetailsPerUnit,
                densityX,
                densityZ
            );
        }

        int needed;

        if (maxDetailsPerUnit > 0f)
        {
            needed = Mathf.CeilToInt(
                maxDetailsPerUnit * Mathf.Max(bounds.Width, bounds.Length)
            );
        }
        else
        {
            needed = highestSourceRes > 0 ? highestSourceRes : 512;
        }

        int resolution = Mathf.NextPowerOfTwo(Mathf.Max(needed, 1));

        resolution = Mathf.Clamp(
            resolution,
            MIN_DETAIL_RESOLUTION,
            MAX_DETAIL_RESOLUTION
        );

        Debug.Log(
            $"Chosen detail resolution: {resolution} (needed {needed}, " +
            $"max allowed {MAX_DETAIL_RESOLUTION})"
        );

        return resolution;
    }

    private static int FindDetailPrototype(
        List<DetailPrototype> prototypes,
        DetailPrototype candidate)
    {
        for (int i = 0; i < prototypes.Count; i++)
        {
            if (DetailPrototypesMatch(prototypes[i], candidate))
                return i;
        }

        return -1;
    }

    private static bool DetailPrototypesMatch(
        DetailPrototype a,
        DetailPrototype b)
    {
        if (a == null || b == null)
            return false;

        return
            a.prototype == b.prototype &&
            a.prototypeTexture == b.prototypeTexture &&
            a.usePrototypeMesh == b.usePrototypeMesh &&
            a.useInstancing == b.useInstancing &&
            a.renderMode == b.renderMode &&
            Mathf.Approximately(a.minWidth, b.minWidth) &&
            Mathf.Approximately(a.maxWidth, b.maxWidth) &&
            Mathf.Approximately(a.minHeight, b.minHeight) &&
            Mathf.Approximately(a.maxHeight, b.maxHeight) &&
            Mathf.Approximately(a.noiseSpread, b.noiseSpread) &&
            a.noiseSeed == b.noiseSeed &&
            a.healthyColor == b.healthyColor &&
            a.dryColor == b.dryColor &&
            Mathf.Approximately(a.alignToGround, b.alignToGround) &&
            Mathf.Approximately(a.positionJitter, b.positionJitter) &&
            Mathf.Approximately(a.density, b.density) &&
            Mathf.Approximately(a.holeEdgePadding, b.holeEdgePadding) &&
            a.useDensityScaling == b.useDensityScaling &&
            Mathf.Approximately(a.targetCoverage, b.targetCoverage);
    }

    private static DetailPrototype CopyDetailPrototype(DetailPrototype source)
    {
        // Copy constructor carries every field, including Unity 6 scatter
        // settings (instancing, alignment, jitter, density, coverage).
        return new DetailPrototype(source);
    }

    // ============================================================
    // CREATE RESULT OBJECT
    // ============================================================

    private static GameObject CreateTerrainObject(
        TerrainData mergedData,
        MergeBounds bounds,
        Terrain visualSource)
    {
        GameObject mergedObject =
            Terrain.CreateTerrainGameObject(mergedData);

        mergedObject.name = "Merged Terrain";

        mergedObject.transform.position = new Vector3(
            bounds.minX,
            bounds.minY,
            bounds.minZ
        );

        Undo.RegisterCreatedObjectUndo(mergedObject, "Merge Terrains");

        Terrain mergedTerrain = mergedObject.GetComponent<Terrain>();

        if (visualSource != null)
        {
            mergedTerrain.drawInstanced = visualSource.drawInstanced;

            // materialTemplate copy disabled: default URP Terrain Lit
            // rendered all layers correctly. Re-enable only if needed.
            // mergedTerrain.materialTemplate = visualSource.materialTemplate;

            mergedTerrain.heightmapPixelError = visualSource.heightmapPixelError;
            mergedTerrain.basemapDistance = visualSource.basemapDistance;

            // Tree render settings.
            mergedTerrain.treeDistance = visualSource.treeDistance;
            mergedTerrain.treeBillboardDistance = visualSource.treeBillboardDistance;
            mergedTerrain.treeCrossFadeLength = visualSource.treeCrossFadeLength;
            mergedTerrain.treeMaximumFullLODCount =
                visualSource.treeMaximumFullLODCount;

            // Detail / grass render settings.
            mergedTerrain.detailObjectDistance = visualSource.detailObjectDistance;
            mergedTerrain.detailObjectDensity = visualSource.detailObjectDensity;

            TerrainData sourceData = visualSource.terrainData;

            if (sourceData != null)
            {
                mergedData.wavingGrassStrength = sourceData.wavingGrassStrength;
                mergedData.wavingGrassSpeed = sourceData.wavingGrassSpeed;
                mergedData.wavingGrassAmount = sourceData.wavingGrassAmount;
                mergedData.wavingGrassTint = sourceData.wavingGrassTint;
            }
        }

        EditorUtility.SetDirty(mergedTerrain);

        return mergedObject;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    /// <summary>
    /// Deterministic pseudo-random value in [0, 1) for detail dithering.
    /// </summary>
    private static float Hash01(int x, int z, int layer)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^
                     (uint)(z * 19349663) ^
                     (uint)(layer * 83492791);

            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;

            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static void WorldToSourceNormalized(
        TerrainInfo source,
        float worldX,
        float worldZ,
        out float normalizedX,
        out float normalizedZ)
    {
        normalizedX = Mathf.Clamp01(
            Mathf.InverseLerp(
                source.position.x,
                source.position.x + source.size.x,
                worldX
            )
        );

        normalizedZ = Mathf.Clamp01(
            Mathf.InverseLerp(
                source.position.z,
                source.position.z + source.size.z,
                worldZ
            )
        );
    }

    /// <summary>
    /// Returns the first source terrain containing the world position.
    /// Overlap priority/blending is not defined yet.
    /// </summary>
    private static TerrainInfo FindTerrainAtPosition(
        List<TerrainInfo> terrains,
        float worldX,
        float worldZ)
    {
        const float epsilon = 0.001f;

        foreach (TerrainInfo info in terrains)
        {
            float minX = info.position.x;
            float maxX = info.position.x + info.size.x;
            float minZ = info.position.z;
            float maxZ = info.position.z + info.size.z;

            if (worldX >= minX - epsilon && worldX <= maxX + epsilon &&
                worldZ >= minZ - epsilon && worldZ <= maxZ + epsilon)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Bilinear alphamap sample. Array layout is [z, x, layer].
    /// pixelX/pixelZ are clamped to the valid range.
    /// </summary>
    private static float SampleAlphamap(
        float[,,] alphamaps,
        float pixelX,
        float pixelZ,
        int layer)
    {
        int resX = alphamaps.GetLength(1);
        int resZ = alphamaps.GetLength(0);

        pixelX = Mathf.Clamp(pixelX, 0, resX - 1);
        pixelZ = Mathf.Clamp(pixelZ, 0, resZ - 1);

        int x0 = Mathf.FloorToInt(pixelX);
        int z0 = Mathf.FloorToInt(pixelZ);

        int x1 = Mathf.Min(x0 + 1, resX - 1);
        int z1 = Mathf.Min(z0 + 1, resZ - 1);

        float tx = pixelX - x0;
        float tz = pixelZ - z0;

        float top = Mathf.Lerp(
            alphamaps[z0, x0, layer],
            alphamaps[z0, x1, layer],
            tx
        );

        float bottom = Mathf.Lerp(
            alphamaps[z1, x0, layer],
            alphamaps[z1, x1, layer],
            tx
        );

        return Mathf.Lerp(top, bottom, tz);
    }

    private static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
            AssetDatabase.CreateFolder("Assets", "MergedTerrains");
    }

    private static void ShowDialog(string message)
    {
        EditorUtility.DisplayDialog("Terrain Merger", message, "OK");
    }
}