using UnityEngine;

namespace PirateGame.Quests
{
    [CreateAssetMenu(fileName = "Reward_Item", menuName = "Pirate Game/Quests/Rewards/Item")]
    public class ItemReward : QuestReward
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;

        public override void Grant(GameObject player)
        {
            if (item != null && PlayerInventory.Instance != null) PlayerInventory.Instance.Add(item, count);
        }
    }
}
