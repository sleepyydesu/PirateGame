using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Base class for anything a quest can give. Make a new reward type by subclassing this
    /// and implementing <see cref="Grant"/>; it then shows up in Create > Pirate Game > Quests > Rewards.
    /// </summary>
    public abstract class QuestReward : ScriptableObject
    {
        public string displayName = "Reward";
        [TextArea(1, 3)] public string description;

        public abstract void Grant(GameObject player);

        public virtual string Describe() =>
            string.IsNullOrEmpty(description) ? displayName : $"{displayName} — {description}";
    }
}
