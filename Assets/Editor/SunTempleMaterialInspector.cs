using UnityEngine;
using UnityEditor;
using System.Text;

public static class SunTempleMaterialInspector
{
    [MenuItem("Tools/Sun Temple/Inspect Selected Material")]
    public static void InspectSelectedMaterial()
    {
        Material mat = Selection.activeObject as Material;

        if (mat == null)
        {
            EditorUtility.DisplayDialog(
                "Select a Material",
                "In the Project window, select ONE pink Sun Temple material first.",
                "OK"
            );
            return;
        }

        Shader shader = mat.shader;

        if (shader == null)
        {
            Debug.LogError(
                "Material has no shader: " + mat.name
            );
            return;
        }

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("======================================");
        sb.AppendLine("SUN TEMPLE MATERIAL INSPECTOR");
        sb.AppendLine("======================================");
        sb.AppendLine();

        sb.AppendLine("Material: " + mat.name);
        sb.AppendLine("Shader: " + shader.name);
        sb.AppendLine(
            "Path: " + AssetDatabase.GetAssetPath(mat)
        );

        sb.AppendLine();
        sb.AppendLine("===== SHADER PROPERTIES =====");
        sb.AppendLine();

        int count = shader.GetPropertyCount();

        for (int i = 0; i < count; i++)
        {
            string propertyName =
                shader.GetPropertyName(i);

            string description =
                shader.GetPropertyDescription(i);

            UnityEngine.Rendering.ShaderPropertyType type =
                shader.GetPropertyType(i);

            sb.AppendLine("--------------------------------");
            sb.AppendLine("Name: " + propertyName);
            sb.AppendLine("Label: " + description);
            sb.AppendLine("Type: " + type);

            try
            {
                switch (type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    {
                        Texture tex =
                            mat.GetTexture(propertyName);

                        sb.AppendLine(
                            "Texture: " +
                            (tex != null
                                ? tex.name
                                : "NONE")
                        );

                        if (tex != null)
                        {
                            sb.AppendLine(
                                "Texture Path: " +
                                AssetDatabase.GetAssetPath(tex)
                            );
                        }

                        Vector2 scale =
                            mat.GetTextureScale(propertyName);

                        Vector2 offset =
                            mat.GetTextureOffset(propertyName);

                        sb.AppendLine(
                            "Scale: " + scale
                        );

                        sb.AppendLine(
                            "Offset: " + offset
                        );

                        break;
                    }

                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                    {
                        Color color =
                            mat.GetColor(propertyName);

                        sb.AppendLine(
                            "Color: " + color
                        );

                        break;
                    }

                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                    {
                        float value =
                            mat.GetFloat(propertyName);

                        sb.AppendLine(
                            "Value: " + value
                        );

                        break;
                    }

                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    {
                        Vector4 value =
                            mat.GetVector(propertyName);

                        sb.AppendLine(
                            "Vector: " + value
                        );

                        break;
                    }
                }
            }
            catch
            {
                sb.AppendLine(
                    "Could not read value."
                );
            }
        }

        sb.AppendLine();
        sb.AppendLine("======================================");
        sb.AppendLine("END");
        sb.AppendLine("======================================");

        Debug.Log(sb.ToString());

        EditorUtility.DisplayDialog(
            "Inspection Complete",
            "Material:\n" + mat.name +
            "\n\nShader:\n" + shader.name +
            "\n\nThe complete property list is now in the Console.",
            "OK"
        );
    }
}