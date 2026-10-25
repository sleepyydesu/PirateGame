using UnityEngine;

namespace PirateGame.Enemies
{
    // Put on the root of a combined prefab when only the body should fall.
    public sealed class MeteorParts : MonoBehaviour
    {
        public Transform body;
        public GameObject marker;
        public ParticleSystem[] flightEffects;
        public GameObject[] impactEffects;
        public ParticleSystem[] impactParticleSystems;
    }
}