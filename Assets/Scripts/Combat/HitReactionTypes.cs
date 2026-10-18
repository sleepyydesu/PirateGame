using System;
using UnityEngine;

namespace PirateGame.Combat
{
    public enum HitReactionType
    {
        Default,
        Light,
        Heavy,
        Thrust,
        GroundSlam
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
        public bool fullBody;
        [Header("Knockdown")]
        public bool knockdown;

        [Tooltip("Example: Base Layer.Player_GetUp")]
        public string getUpStatePath = "Base Layer.Player_GetUp";

        [Min(0f)]
        public float downSeconds = 0.3f;

        [Tooltip("Set slightly longer than the getting-up clip.")]
        [Min(0.2f)]
        public float getUpWatchdogSeconds = 3f;

        [Tooltip("Safety timeout if the completion event is missing.")]
        [Min(0.2f)]
        public float watchdogSeconds = 2f;

        [Tooltip("How long a non-interrupting flinch remains visible.")]
        [Min(0.05f)]
        public float flinchSeconds = 0.3f;
    }
}