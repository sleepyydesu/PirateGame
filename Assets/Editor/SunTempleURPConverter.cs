using UnityEngine;
using UnityEditor;

public static class SunTempleURPConverter
{
    private const string FOLDER =
        "Assets/Sun_Temple/Content/Meshes/Main/Materials";

    [MenuItem("Tools/Sun Temple/Convert Custom Materials To URP")]
    public static void Convert()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");

        if (lit == null)
        {
            Debug.LogError("URP/Lit shader not found.");
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "Convert Sun Temple?",
            "This converts Sun_Temple custom materials to URP/Lit.\n\n" +
            "Original texture references are read BEFORE changing shaders.\n\n" +
            "Make a backup first.",
            "Convert",
            "Cancel"))
            return;

        string[] guids = AssetDatabase.FindAssets(
            "t:Material",
            new[] { FOLDER }
        );

        int converted = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[i]);

                Material mat =
                    AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null || mat.shader == null)
                    continue;

                string oldShader = mat.shader.name;

                if (!oldShader.StartsWith("Sun_Temple/"))
                    continue;

                EditorUtility.DisplayProgressBar(
                    "Converting Sun Temple",
                    mat.name,
                    (float)i / guids.Length
                );

                // ---------------------------------------------
                // Leave the special skybox alone.
                // ---------------------------------------------

                if (oldShader == "Sun_Temple/Skybox_Rotating")
                {
                    skipped++;
                    continue;
                }

                // ---------------------------------------------
                // SAVE EVERYTHING WE NEED BEFORE SHADER CHANGE
                // ---------------------------------------------

                Texture mainTex =
                    GetTexture(mat, "_MainTex");

                Texture normal =
                    GetTexture(mat, "_BumpMap");

                Texture terrainNormal =
                    GetTexture(mat, "_TerrainNormal");

                Texture detail =
                    GetTexture(mat, "_DetailAlbedo");

                Texture detailNormal =
                    GetTexture(mat, "_DetailNormal");

                Texture emission =
                    GetTexture(mat, "_Emission");

                Texture mask =
                    GetTexture(mat, "_Mask");

                Color color =
                    GetColor(mat, "_Color", Color.white);

                float roughness =
                    GetFloat(mat, "_Roughness", 0.5f);

                float cutoff =
                    GetFloat(mat, "_Cutoff", 0.5f);

                Vector2 mainScale = Vector2.one;
                Vector2 mainOffset = Vector2.zero;

                if (mat.HasProperty("_MainTex"))
                {
                    mainScale =
                        mat.GetTextureScale("_MainTex");

                    mainOffset =
                        mat.GetTextureOffset("_MainTex");
                }

                Vector2 normalScale = Vector2.one;
                Vector2 normalOffset = Vector2.zero;

                if (mat.HasProperty("_BumpMap"))
                {
                    normalScale =
                        mat.GetTextureScale("_BumpMap");

                    normalOffset =
                        mat.GetTextureOffset("_BumpMap");
                }

                // Mountains may have overall normal but
                // _BumpMap is still preferable when available.
                if (normal == null)
                    normal = terrainNormal;

                // Puddle doesn't have _MainTex.
                // Use its mask so it isn't plain white.
                if (mainTex == null &&
                    oldShader == "Sun_Temple/Decal_Puddle")
                {
                    mainTex = mask;
                }

                // ---------------------------------------------
                // CHANGE SHADER
                // ---------------------------------------------

                Undo.RecordObject(
                    mat,
                    "Convert Sun Temple Material"
                );

                mat.shader = lit;

                // ---------------------------------------------
                // BASE MAP
                // ---------------------------------------------

                if (mainTex != null)
                {
                    mat.SetTexture("_BaseMap", mainTex);

                    mat.SetTextureScale(
                        "_BaseMap",
                        mainScale
                    );

                    mat.SetTextureOffset(
                        "_BaseMap",
                        mainOffset
                    );
                }

                mat.SetColor("_BaseColor", color);

                // ---------------------------------------------
                // NORMAL
                // ---------------------------------------------

                if (normal != null)
                {
                    mat.SetTexture(
                        "_BumpMap",
                        normal
                    );

                    mat.SetTextureScale(
                        "_BumpMap",
                        normalScale
                    );

                    mat.SetTextureOffset(
                        "_BumpMap",
                        normalOffset
                    );

                    mat.EnableKeyword("_NORMALMAP");
                }

                // ---------------------------------------------
                // ROUGHNESS -> SMOOTHNESS
                // ---------------------------------------------

                if (mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat(
                        "_Smoothness",
                        Mathf.Clamp01(1f - roughness)
                    );
                }

                // ---------------------------------------------
                // DETAIL
                // ---------------------------------------------

                if (detail != null &&
                    mat.HasProperty("_DetailAlbedoMap"))
                {
                    mat.SetTexture(
                        "_DetailAlbedoMap",
                        detail
                    );
                }

                if (detailNormal != null &&
                    mat.HasProperty("_DetailNormalMap"))
                {
                    mat.SetTexture(
                        "_DetailNormalMap",
                        detailNormal
                    );
                }

                // ---------------------------------------------
                // EMISSION
                // ---------------------------------------------

                if (emission != null)
                {
                    mat.SetTexture(
                        "_EmissionMap",
                        emission
                    );

                    mat.SetColor(
                        "_EmissionColor",
                        Color.white
                    );

                    mat.EnableKeyword("_EMISSION");
                }

                // ---------------------------------------------
                // FOLIAGE + DECALS
                // ---------------------------------------------

                if (oldShader == "Sun_Temple/Foliage" ||
                    oldShader == "Sun_Temple/Decal")
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", cutoff);

                    mat.EnableKeyword(
                        "_ALPHATEST_ON"
                    );
                }

                // ---------------------------------------------
                // GLASS
                // ---------------------------------------------

                if (oldShader == "Sun_Temple/WindowGlass")
                {
                    // Transparent URP/Lit
                    mat.SetFloat("_Surface", 1f);

                    mat.SetFloat(
                        "_SrcBlend",
                        (float)UnityEngine.Rendering.BlendMode.SrcAlpha
                    );

                    mat.SetFloat(
                        "_DstBlend",
                        (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                    );

                    mat.SetFloat("_ZWrite", 0f);

                    mat.EnableKeyword(
                        "_SURFACE_TYPE_TRANSPARENT"
                    );

                    mat.renderQueue = 3000;
                }

                EditorUtility.SetDirty(mat);

                converted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "===== SUN TEMPLE CONVERSION COMPLETE =====\n" +
            "Converted: " + converted + "\n" +
            "Skipped special: " + skipped
        );

        EditorUtility.DisplayDialog(
            "Finished",
            "Converted: " + converted +
            "\nSkipped special: " + skipped +
            "\n\nCheck DemoScene.",
            "OK"
        );
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private static Texture GetTexture(
        Material mat,
        string property)
    {
        if (!mat.HasProperty(property))
            return null;

        return mat.GetTexture(property);
    }

    private static Color GetColor(
        Material mat,
        string property,
        Color fallback)
    {
        if (!mat.HasProperty(property))
            return fallback;

        return mat.GetColor(property);
    }

    private static float GetFloat(
        Material mat,
        string property,
        float fallback)
    {
        if (!mat.HasProperty(property))
            return fallback;

        return mat.GetFloat(property);
    }
}