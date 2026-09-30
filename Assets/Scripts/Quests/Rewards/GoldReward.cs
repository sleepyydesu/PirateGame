using UnityEngine;

namespace PirateGame.Quests
{
    [CreateAssetMenu(fileName = "Reward_Gold", menuName = "Pirate Game/Quests/Rewards/Gold")]
    public class GoldReward : QuestReward
    {
        [Min(0)] public int amount = 50;

        public override void Grant(GameObject player)
        {
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.AddGold(amount);
        }
    }
}
