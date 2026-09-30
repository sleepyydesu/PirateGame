using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Permanent percentage off everything at one shop.</summary>
    [CreateAssetMenu(fileName = "Reward_ShopDiscount", menuName = "Pirate Game/Quests/Rewards/Shop Discount")]
    public class ShopDiscountReward : QuestReward
    {
        public string shopId = "merchant";
        [Range(0f, 0.9f)] public float discount = 0.1f;

        public override void Grant(GameObject player)
        {
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades == null) { Debug.LogWarning("No PlayerUpgrades on the player — discount not applied."); return; }
            upgrades.AddShopDiscount(shopId, discount);
        }
    }
}
