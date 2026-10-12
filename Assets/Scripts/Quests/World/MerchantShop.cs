using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>A shop's stock and pricing. Quest discounts (<see cref="ShopDiscountReward"/>) apply automatically.</summary>
    public class MerchantShop : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public ItemDefinition item;
            [Min(0)] public int basePrice = 10;
        }

        [SerializeField] private string shopId = "merchant";
        [SerializeField] private string shopName = "Merchant's Wares";
        [SerializeField] private List<Entry> stock = new List<Entry>();

        public string ShopId => shopId;
        public string ShopName => shopName;
        public IReadOnlyList<Entry> Stock => stock;

        public float Discount => PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.GetShopDiscount(shopId) : 0f;

        /// <summary>Final price after discounts (rounded down in the player's favour).</summary>
        public int PriceOf(Entry e) => Mathf.FloorToInt(e.basePrice * (1f - Discount));

        public bool TryBuy(Entry e)
        {
            PlayerInventory inv = PlayerInventory.Instance;
            if (e == null || inv == null) return false;
            if (e.item != null && e.item.unique && inv.Has(e.item)) return false;
            if (!inv.TrySpend(PriceOf(e))) return false;
            inv.Add(e.item);
            QuestManager.Instance?.Report("bought:" + (e.item != null ? e.item.Id : "item"));
            return true;
        }

        public void Setup(string id, string displayName, List<Entry> entries)
        {
            shopId = id; shopName = displayName; stock = entries;
        }
    }
}
