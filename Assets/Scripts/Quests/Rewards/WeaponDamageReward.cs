using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Permanently increases the player's weapon damage.</summary>
    [CreateAssetMenu(fileName = "Reward_WeaponDamage", menuName = "Pirate Game/Quests/Rewards/Weapon Damage")]
    public class WeaponDamageReward : QuestReward
    {
        [Tooltip("0.25 = +25% damage")]
        [Min(0f)] public float bonus = 0.25f;

        public override void Grant(GameObject player)
        {
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades == null) { Debug.LogWarning("No PlayerUpgrades on the player — upgrade not applied."); return; }
            upgrades.AddWeaponDamageBonus(bonus);
        }
    }
}
