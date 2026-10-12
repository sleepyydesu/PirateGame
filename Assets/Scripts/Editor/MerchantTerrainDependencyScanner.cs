using UnityEditor;
using UnityEngine;
using System.Linq;

public static class MerchantTerrainDependencyScanner
{
    const string Path = "Assets/MergedTerrains/MergedTerrainData 7.asset";

    [MenuItem("Tools/Merchant/Scan TerrainData 7 Dependencies")]
    public static void Scan()
    {
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(Path);
        if (td == null) { Debug.LogError("TerrainData not found: " + Path); return; }

        Debug.Log("== Terrain layers ==");
        foreach (var l in td.terrainLayers)
            Log(l);

        Debug.Log("== Tree prototypes ==");
        foreach (var t in td.treePrototypes)
            Log(t.prefab);

        Debug.Log("== Detail prototypes ==");
        foreach (var d in td.detailPrototypes)
        {
            Log(d.prototype);
            Log(d.prototypeTexture);
        }

        Debug.Log("== All dependencies ==");
        foreach (var dep in AssetDatabase.GetDependencies(Path, true).Distinct())
            Debug.Log(dep + "  " + AssetDatabase.AssetPathToGUID(dep));
    }

    static void Log(Object o)
    {
        if (o == null) { Debug.LogWarning("MISSING (null reference)"); return; }
        var p = AssetDatabase.GetAssetPath(o);
        Debug.Log(p + "  " + AssetDatabase.AssetPathToGUID(p));
    }
}