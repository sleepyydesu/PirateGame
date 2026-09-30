using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEditor;
using System.IO;

namespace UnityEditor.PostProcessing
{
    public static class PostProcessingFactory
    {
        [MenuItem("Assets/Create/Post-Processing Profile", priority = 201)]
        private static void MenuCreatePostProcessingProfile()
        {
            // Determine currently selected folder.
            string folderPath = GetSelectedFolderPath();

            // Generate a unique asset path.
            string path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folderPath, "New Post-Processing Profile.asset")
            );

            // Create profile.
            PostProcessingProfile profile =
                ScriptableObject.CreateInstance<PostProcessingProfile>();

            profile.name = Path.GetFileNameWithoutExtension(path);

            // Preserve original Sun Temple behavior.
            if (profile.fog != null)
            {
                profile.fog.enabled = true;
            }

            // Save asset.
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Select and highlight newly created profile.
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        internal static PostProcessingProfile CreatePostProcessingProfileAtPath(
            string path
        )
        {
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            PostProcessingProfile profile =
                ScriptableObject.CreateInstance<PostProcessingProfile>();

            profile.name = Path.GetFileNameWithoutExtension(path);

            if (profile.fog != null)
            {
                profile.fog.enabled = true;
            }

            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            return profile;
        }

        private static string GetSelectedFolderPath()
        {
            string path = "Assets";

            Object selectedObject = Selection.activeObject;

            if (selectedObject == null)
                return path;

            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);

            if (string.IsNullOrEmpty(selectedPath))
                return path;

            // Selected object itself is a folder.
            if (AssetDatabase.IsValidFolder(selectedPath))
            {
                return selectedPath;
            }

            // Selected object is a file, so use its containing folder.
            string directory = Path.GetDirectoryName(selectedPath);

            if (!string.IsNullOrEmpty(directory))
            {
                return directory.Replace("\\", "/");
            }

            return path;
        }
    }
}