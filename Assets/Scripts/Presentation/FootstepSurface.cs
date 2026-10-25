using System;
using UnityEngine;

namespace PirateGame.Presentation
{
    [DisallowMultipleComponent]
    public sealed class FootstepSurface : MonoBehaviour
    {
        [Serializable]
        public sealed class TerrainSound
        {
            public TerrainLayer layer;
            public string cue = "Footstep_Dirt";
        }
        public string defaultCue = "Footstep_Dirt";
        [Tooltip("Optional. Assign the same TerrainLayer assets used to paint this Terrain.")]
        public TerrainSound[] terrainLayers = new TerrainSound[0];
        private Terrain terrain;
        private void Awake() { terrain = GetComponent<Terrain>(); }
        public string CueAt(Vector3 worldPoint)
        {
            if (terrain == null || terrain.terrainData == null || terrainLayers == null || terrainLayers.Length == 0)
                return defaultCue;
            TerrainData data = terrain.terrainData;
            Vector3 p = worldPoint - terrain.transform.position;
            if (data.size.x <= 0f || data.size.z <= 0f) return defaultCue;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            TerrainLayer[] layers = data.terrainLayers;
            int best = -1; float highest = -1f;
            for (int i = 0; i < layers.Length && i < weights.GetLength(2); i++)
                if (weights[0, 0, i] > highest) { best = i; highest = weights[0, 0, i]; }
            if (best >= 0)
                foreach (TerrainSound entry in terrainLayers)
                    if (entry != null && entry.layer == layers[best] && !string.IsNullOrWhiteSpace(entry.cue))
                        return entry.cue;
            return defaultCue;
        }
    }
}
