using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Cached lookup of the object tagged "Player" (the same contract the enemy AI uses).</summary>
    public static class QuestPlayer
    {
        private static GameObject cached;

        public static GameObject GameObject
        {
            get
            {
                if (cached == null) cached = UnityEngine.GameObject.FindGameObjectWithTag("Player");
                return cached;
            }
        }

        public static Transform Transform => GameObject != null ? GameObject.transform : null;

        public static bool TryGetPosition(out Vector3 position)
        {
            Transform t = Transform;
            position = t != null ? t.position : Vector3.zero;
            return t != null;
        }

        /// <summary>Move the player safely (CharacterController ignores direct position writes).</summary>
        public static void Teleport(Vector3 position, Quaternion? rotation = null)
        {
            GameObject go = GameObject;
            if (go == null) return;
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            go.transform.position = position;
            if (rotation.HasValue) go.transform.rotation = rotation.Value;
            if (cc != null) cc.enabled = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => cached = null;
    }
}
