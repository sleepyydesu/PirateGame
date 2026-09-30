using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class MeshCombinerTool : EditorWindow
{
    private bool disableOriginalObjects = true;
    private bool addMeshColliders = false;
    private bool generateLightmapUVs = false;

    [MenuItem("Tools/Mesh Combiner")]
    public static void ShowWindow()
    {
        GetWindow<MeshCombinerTool>("Mesh Combiner");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        EditorGUILayout.LabelField(
            "Mesh Combiner",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Select static Mesh GameObjects and combine them by material. " +
            "The combined objects will keep a movable world-space transform.",
            MessageType.Info
        );

        GUILayout.Space(10);

        disableOriginalObjects = EditorGUILayout.Toggle(
            "Disable Original Objects",
            disableOriginalObjects
        );

        addMeshColliders = EditorGUILayout.Toggle(
            "Add Mesh Colliders",
            addMeshColliders
        );

        generateLightmapUVs = EditorGUILayout.Toggle(
            "Generate Lightmap UVs",
            generateLightmapUVs
        );

        GUILayout.Space(15);

        if (GUILayout.Button(
            "Combine Selected Objects",
            GUILayout.Height(40)))
        {
            CombineSelectedObjects();
        }

        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Objects using the same material are combined together. " +
            "Different materials create separate combined objects.",
            MessageType.None
        );
    }

    private void CombineSelectedObjects()
    {
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Mesh Combiner",
                "Please select some GameObjects first.",
                "OK"
            );

            return;
        }

        // ---------------------------------------------------------
        // Find all valid MeshFilters
        // ---------------------------------------------------------

        List<MeshFilter> meshFilters = new List<MeshFilter>();

        foreach (GameObject selectedObject in selectedObjects)
        {
            MeshFilter[] filters =
                selectedObject.GetComponentsInChildren<MeshFilter>();

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;

                MeshRenderer renderer =
                    filter.GetComponent<MeshRenderer>();

                if (renderer == null)
                    continue;

                if (renderer.sharedMaterials.Length == 0)
                    continue;

                meshFilters.Add(filter);
            }
        }

        if (meshFilters.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Mesh Combiner",
                "No valid MeshFilters were found.",
                "OK"
            );

            return;
        }

        // ---------------------------------------------------------
        // Calculate a pivot from the center of the selected objects
        // ---------------------------------------------------------

        Bounds bounds = new Bounds(
            meshFilters[0].transform.position,
            Vector3.zero
        );

        foreach (MeshFilter filter in meshFilters)
        {
            Bounds worldBounds =
                TransformBounds(
                    filter.sharedMesh.bounds,
                    filter.transform.localToWorldMatrix
                );

            bounds.Encapsulate(worldBounds);
        }

        Vector3 combinedPivot = bounds.center;

        // ---------------------------------------------------------
        // Group meshes by material
        // ---------------------------------------------------------

        Dictionary<Material, List<CombineInstance>> combineGroups =
            new Dictionary<Material, List<CombineInstance>>();

        List<GameObject> originalObjects =
            new List<GameObject>();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            MeshRenderer renderer =
                meshFilter.GetComponent<MeshRenderer>();

            Material[] materials =
                renderer.sharedMaterials;

            Mesh mesh =
                meshFilter.sharedMesh;

            for (int subMeshIndex = 0;
                 subMeshIndex < mesh.subMeshCount;
                 subMeshIndex++)
            {
                if (subMeshIndex >= materials.Length)
                    continue;

                Material material =
                    materials[subMeshIndex];

                if (material == null)
                    continue;

                if (!combineGroups.ContainsKey(material))
                {
                    combineGroups.Add(
                        material,
                        new List<CombineInstance>()
                    );
                }

                CombineInstance combineInstance =
                    new CombineInstance();

                combineInstance.mesh =
                    mesh;

                combineInstance.subMeshIndex =
                    subMeshIndex;

                // -------------------------------------------------
                // IMPORTANT:
                //
                // Convert the original object's world transform
                // into the coordinate space of our new pivot.
                // -------------------------------------------------

                Matrix4x4 pivotMatrix =
                    Matrix4x4.TRS(
                        combinedPivot,
                        Quaternion.identity,
                        Vector3.one
                    );

                combineInstance.transform =
                    pivotMatrix.inverse *
                    meshFilter.transform.localToWorldMatrix;

                combineGroups[material].Add(
                    combineInstance
                );
            }

            GameObject root =
                meshFilter.transform.root.gameObject;

            if (!originalObjects.Contains(root))
            {
                originalObjects.Add(root);
            }
        }

        // ---------------------------------------------------------
        // Create parent
        // ---------------------------------------------------------

        GameObject combinedParent =
            new GameObject(
                "Combined_" +
                DateTime.Now.ToString("HHmmss")
            );

        Undo.RegisterCreatedObjectUndo(
            combinedParent,
            "Create Combined Meshes"
        );

        combinedParent.transform.position =
            combinedPivot;

        combinedParent.transform.rotation =
            Quaternion.identity;

        combinedParent.transform.localScale =
            Vector3.one;

        int combinedIndex = 0;

        // ---------------------------------------------------------
        // Create one mesh per material
        // ---------------------------------------------------------

        foreach (
            KeyValuePair<
                Material,
                List<CombineInstance>
            > group in combineGroups)
        {
            Material material =
                group.Key;

            List<CombineInstance> combines =
                group.Value;

            Mesh combinedMesh =
                new Mesh();

            combinedMesh.name =
                "Combined_" + material.name;

            // Allows large combined meshes.
            combinedMesh.indexFormat =
                UnityEngine.Rendering.IndexFormat.UInt32;

            combinedMesh.CombineMeshes(
                combines.ToArray(),
                true,
                true,
                generateLightmapUVs
            );

            GameObject combinedObject =
                new GameObject(
                    "Combined_" +
                    material.name +
                    "_" +
                    combinedIndex
                );

            Undo.RegisterCreatedObjectUndo(
                combinedObject,
                "Create Combined Mesh"
            );

            combinedObject.transform.SetParent(
                combinedParent.transform,
                false
            );

            // Because the mesh is already expressed relative
            // to the parent pivot, local transform stays zero.
            combinedObject.transform.localPosition =
                Vector3.zero;

            combinedObject.transform.localRotation =
                Quaternion.identity;

            combinedObject.transform.localScale =
                Vector3.one;

            MeshFilter combinedMeshFilter =
                combinedObject.AddComponent<MeshFilter>();

            MeshRenderer combinedRenderer =
                combinedObject.AddComponent<MeshRenderer>();

            combinedMeshFilter.sharedMesh =
                combinedMesh;

            combinedRenderer.sharedMaterial =
                material;

            if (addMeshColliders)
            {
                MeshCollider collider =
                    combinedObject.AddComponent<MeshCollider>();

                collider.sharedMesh =
                    combinedMesh;
            }

            combinedIndex++;
        }

        // ---------------------------------------------------------
        // Disable originals
        // ---------------------------------------------------------

        if (disableOriginalObjects)
        {
            foreach (GameObject original in originalObjects)
            {
                Undo.RecordObject(
                    original,
                    "Disable Original Objects"
                );

                original.SetActive(false);
            }
        }

        // ---------------------------------------------------------
        // Select the combined parent
        // ---------------------------------------------------------

        Selection.activeGameObject =
            combinedParent;

        EditorGUIUtility.PingObject(
            combinedParent
        );

        EditorUtility.DisplayDialog(
            "Mesh Combiner",
            "Successfully combined " +
            meshFilters.Count +
            " meshes into " +
            combinedIndex +
            " material groups.\n\n" +
            "The combined parent can now be moved, rotated, " +
            "and scaled normally.",
            "OK"
        );
    }

    // -------------------------------------------------------------
    // Convert mesh bounds into world-space bounds
    // -------------------------------------------------------------

    private static Bounds TransformBounds(
        Bounds localBounds,
        Matrix4x4 matrix)
    {
        Vector3 center =
            matrix.MultiplyPoint3x4(
                localBounds.center
            );

        Vector3 extents =
            localBounds.extents;

        Vector3 axisX =
            matrix.MultiplyVector(
                new Vector3(extents.x, 0, 0)
            );

        Vector3 axisY =
            matrix.MultiplyVector(
                new Vector3(0, extents.y, 0)
            );

        Vector3 axisZ =
            matrix.MultiplyVector(
                new Vector3(0, 0, extents.z)
            );

        Vector3 worldExtents =
            new Vector3(
                Mathf.Abs(axisX.x) +
                Mathf.Abs(axisY.x) +
                Mathf.Abs(axisZ.x),

                Mathf.Abs(axisX.y) +
                Mathf.Abs(axisY.y) +
                Mathf.Abs(axisZ.y),

                Mathf.Abs(axisX.z) +
                Mathf.Abs(axisY.z) +
                Mathf.Abs(axisZ.z)
            );

        return new Bounds(
            center,
            worldExtents * 2f
        );
    }
}
