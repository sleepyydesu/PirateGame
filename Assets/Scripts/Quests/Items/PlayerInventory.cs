using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Minimal inventory + gold wallet for the player. Lives on the Player object.</summary>
    [DefaultExecutionOrder(-250)]
    public class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Instance { get; private set; }

        [SerializeField] private int startingGold = 150;

        private readonly Dictionary<ItemDefinition, int> items = new Dictionary<ItemDefinition, int>();

        public int Gold { get; private set; }
        public IReadOnlyDictionary<ItemDefinition, int> Items => items;

        public event Action Changed;
        public event Action<ItemDefinition, int> ItemAdded;
        public event Action<ItemDefinition, int> ItemRemoved;
        public event Action<int> GoldChanged;

        private void Awake()
        {
            Instance = this;
            Gold = startingGold;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Has(ItemDefinition item, int count = 1) => item != null && Count(item) >= count;
        public int Count(ItemDefinition item) => item != null && items.TryGetValue(item, out int c) ? c : 0;

        public void Add(ItemDefinition item, int count = 1)
        {
            if (item == null || count <= 0) return;
            int current = Count(item);
            int next = item.unique ? 1 : current + count;
            if (next == current) return;
            items[item] = next;
            ItemAdded?.Invoke(item, next - current);
            Changed?.Invoke();
        }

        public bool Remove(ItemDefinition item, int count = 1)
        {
            int current = Count(item);
            if (current <= 0) return false;
            int next = Mathf.Max(0, current - count);
            if (next == 0) items.Remove(item); else items[item] = next;
            ItemRemoved?.Invoke(item, current - next);
            Changed?.Invoke();
            return true;
        }

        public void AddGold(int amount)
        {
            if (amount == 0) return;
            Gold = Mathf.Max(0, Gold + amount);
            GoldChanged?.Invoke(Gold);
            Changed?.Invoke();
        }

        public bool TrySpend(int amount)
        {
            if (amount > Gold) return false;
            AddGold(-amount);
            return true;
        }
    }
}
