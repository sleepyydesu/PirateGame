using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Text;

public static class SunTempleShaderScanner
{
    private const string FOLDER =
        "Assets/Sun_Temple/Content/Meshes/Main/Materials";

    [MenuItem("Tools/Sun Temple/Scan ALL Custom Shaders")]
    public static void ScanAll()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:Material",
            new[] { FOLDER }
        );

        // One example material for every unique shader
        Dictionary<Shader, Material> shaders =
            new Dictionary<Shader, Material>();

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(guid);

            Material mat =
                AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null || mat.shader == null)
                continue;

            // We only care about old Sun Temple shaders
            if (!mat.shader.name.StartsWith("Sun_Temple/"))
                continue;

            if (!shaders.ContainsKey(mat.shader))
                shaders.Add(mat.shader, mat);
        }

        StringBuilder sb = new StringBuilder();

        sb.AppendLine(
            "========================================"
        );
        sb.AppendLine(
            "SUN TEMPLE ALL SHADERS REPORT"
        );
        sb.AppendLine(
            "========================================"
        );

        sb.AppendLine(
            "Unique custom shaders: " + shaders.Count
        );

        sb.AppendLine();

        foreach (var pair in shaders)
        {
            Shader shader = pair.Key;
            Material mat = pair.Value;

            sb.AppendLine();
            sb.AppendLine(
                "########################################"
            );

            sb.AppendLine(
                "SHADER: " + shader.name
            );

            sb.AppendLine(
                "EXAMPLE MATERIAL: " + mat.name
            );

            sb.AppendLine(
                "PATH: " +
                AssetDatabase.GetAssetPath(mat)
            );

            sb.AppendLine(
                "########################################"
            );

            int count = shader.GetPropertyCount();

            for (int i = 0; i < count; i++)
            {
                string name =
                    shader.GetPropertyName(i);

                string label =
                    shader.GetPropertyDescription(i);

                ShaderPropertyType type =
                    shader.GetPropertyType(i);

                sb.AppendLine();

                sb.AppendLine(
                    "Name: " + name
                );

                sb.AppendLine(
                    "Label: " + label
                );

                sb.AppendLine(
                    "Type: " + type
                );

                try
                {
                    switch (type)
                    {
                        case ShaderPropertyType.Texture:
                        {
                            Texture tex =
                                mat.GetTexture(name);

                            sb.AppendLine(
                                "Texture: " +
                                (tex != null
                                    ? tex.name
                                    : "NONE")
                            );

                            if (tex != null)
                            {
                                sb.AppendLine(
                                    "TexturePath: " +
                                    AssetDatabase.GetAssetPath(tex)
                                );
                            }

                            sb.AppendLine(
                                "Scale: " +
                                mat.GetTextureScale(name)
                            );

                            sb.AppendLine(
                                "Offset: " +
                                mat.GetTextureOffset(name)
                            );

                            break;
                        }

                        case ShaderPropertyType.Color:
                            sb.AppendLine(
                                "Value: " +
                                mat.GetColor(name)
                            );
                            break;

                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            sb.AppendLine(
                                "Value: " +
                                mat.GetFloat(name)
                            );
                            break;

                        case ShaderPropertyType.Vector:
                            sb.AppendLine(
                                "Value: " +
                                mat.GetVector(name)
                            );
                            break;
                    }
                }
                catch
                {
                    sb.AppendLine(
                        "Value: <could not read>"
                    );
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine(
            "========================================"
        );
        sb.AppendLine(
            "END REPORT"
        );
        sb.AppendLine(
            "========================================"
        );

        Debug.Log(sb.ToString());

        EditorUtility.DisplayDialog(
            "Scan Complete",
            "Found " +
            shaders.Count +
            " unique Sun_Temple shaders.\n\n" +
            "Copy the report from the Console.",
            "OK"
        );
    }
}