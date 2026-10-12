using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Permanent perks earned from quests (weapon damage, shop discounts). Lives on the Player.
    /// Pushes the damage multiplier into the sword's DamageDealer via EquipmentSystem.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public class PlayerUpgrades : MonoBehaviour
    {
        public static PlayerUpgrades Instance { get; private set; }

        [SerializeField] private float weaponDamageBonus;

        private readonly Dictionary<string, float> shopDiscounts = new Dictionary<string, float>();

        public float WeaponDamageMultiplier => 1f + weaponDamageBonus;
        public event Action Changed;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // EquipmentSystem spawns the sword in Awake, so Start is safe.
        private void Start() => ApplyWeapon();

        public void AddWeaponDamageBonus(float bonus)
        {
            weaponDamageBonus += bonus;
            ApplyWeapon();
            Changed?.Invoke();
        }

        public void AddShopDiscount(string shopId, float discount)
        {
            if (string.IsNullOrEmpty(shopId)) return;
            shopDiscounts.TryGetValue(shopId, out float current);
            shopDiscounts[shopId] = Mathf.Clamp(current + discount, 0f, 0.9f);
            Changed?.Invoke();
        }

        public float GetShopDiscount(string shopId) =>
            !string.IsNullOrEmpty(shopId) && shopDiscounts.TryGetValue(shopId, out float d) ? d : 0f;

        private void ApplyWeapon()
        {
            var equipment = GetComponent<EquipmentSystem>();
            if (equipment != null && equipment.WeaponDamageDealer != null)
                equipment.WeaponDamageDealer.damageMultiplier = WeaponDamageMultiplier;
        }
    }
}
