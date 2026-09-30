using UnityEngine;
using UnityEditor;

public static class TerrainLayerTest
{
    [MenuItem("Terrain/Test Merged Terrain Layers")]
    public static void TestMergedTerrainLayers()
    {
        Terrain terrain = Selection.activeGameObject
            ? Selection.activeGameObject.GetComponent<Terrain>()
            : null;

        if (terrain == null)
        {
            Debug.LogError("Select the MERGED Terrain GameObject first.");
            return;
        }

        TerrainData data = terrain.terrainData;

        if (data == null)
        {
            Debug.LogError("Selected Terrain has no TerrainData.");
            return;
        }

        TerrainLayer[] layers = data.terrainLayers;

        if (layers == null || layers.Length < 2)
        {
            Debug.LogError(
                $"Terrain only has {layers?.Length ?? 0} Terrain Layer(s). " +
                "At least 2 are required for this test."
            );
            return;
        }

        int resolution = data.alphamapResolution;

        Debug.Log(
            $"Testing Terrain: {terrain.name}\n" +
            $"Alphamap Resolution: {resolution}\n" +
            $"Terrain Layers: {layers.Length}"
        );

        // Create alphamap.
        float[,,] alphamaps =
            new float[resolution, resolution, layers.Length];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // LEFT HALF = Layer 0
                if (x < resolution / 2)
                {
                    alphamaps[y, x, 0] = 1f;
                }
                // RIGHT HALF = Layer 1
                else
                {
                    alphamaps[y, x, 1] = 1f;
                }
            }
        }

        // Apply the test alphamap.
        data.SetAlphamaps(0, 0, alphamaps);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "TEST COMPLETE.\n" +
            "Expected result: LEFT = Terrain Layer 0, RIGHT = Terrain Layer 1."
        );
    }
}