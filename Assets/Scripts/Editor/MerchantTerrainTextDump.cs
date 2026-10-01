using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MerchantTerrainTextDump
{
    [MenuItem("Tools/Merchant/Dump TerrainData 7 Refs")]
    public static void Dump()
    {
        const string path = "Assets/MergedTerrains/MergedTerrainData 7.asset";
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (td == null) { Debug.LogError("TerrainData not found"); return; }

        var sb = new StringBuilder();
        var so = new SerializedObject(td);
        var p = so.GetIterator();
        bool enter = true;
        while (p.Next(enter))
        {
            enter = !(p.isArray && p.propertyType != SerializedPropertyType.String && p.arraySize > 1000);
            if (p.propertyType != SerializedPropertyType.ObjectReference) continue;

            var id = p.objectReferenceEntityIdValue;
            string guid = "none";
            string assetPath = "";
            if (!id.Equals(default(EntityId)))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(id, out string g, out long local)) guid = g;
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
            }
            sb.AppendLine(p.propertyPath + " | guid: " + guid + " | path: " + assetPath);
        }

        File.WriteAllText("Temp/TerrainRefs.txt", sb.ToString());
        Debug.Log("Wrote Temp/TerrainRefs.txt (" + sb.Length + " chars)");
    }
}