using UnityEngine;

namespace PirateGame.Quests
{
    [CreateAssetMenu(fileName = "Item_New", menuName = "Pirate Game/Items/Item")]
    public class ItemDefinition : ScriptableObject
    {
        public string itemId;
        public string displayName = "Item";
        [TextArea(1, 3)] public string description;
        public Sprite icon;
        public Color uiColor = new Color(1f, 0.82f, 0.4f);
        [Tooltip("Quest items are shown separately and can't be sold.")]
        public bool isQuestItem;
        [Tooltip("Only one can ever be carried.")]
        public bool unique;

        public string Id => string.IsNullOrEmpty(itemId) ? name : itemId;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(itemId)) itemId = name;
        }
#endif
    }
}
