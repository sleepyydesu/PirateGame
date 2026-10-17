using System;
using UnityEngine;

namespace PirateGame.Combat
{
    public enum HitReactionType
    {
        Default,
        Light,
        Heavy,
        Thrust
    }

    public enum HitReactionMode
    {
        Stagger = 0,
        Flinch = 1
    }

    [Serializable]
    public sealed class HitReactionAnimation
    {
        public HitReactionType type;

        [Tooltip("Example: Damage Layer.Player_HurtHeavy")]
        public string fullStatePath;

        [Tooltip("Safety timeout if the completion event is missing.")]
        [Min(0.2f)]
        public float watchdogSeconds = 2f;

        [Tooltip("How long a non-interrupting flinch remains visible.")]
        [Min(0.05f)]
        public float flinchSeconds = 0.3f;
    }
}