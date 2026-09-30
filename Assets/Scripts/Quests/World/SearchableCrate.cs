using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>Wreck debris the player can rummage through (E). Gives a little gold or nothing.</summary>
    public class SearchableCrate : Interactable
    {
        [SerializeField] private Transform lid;
        [SerializeField] private Vector2Int goldRange = new Vector2Int(0, 12);
        [SerializeField] private string searchedEvent = "searched:debris";

        private bool searched;
        private float lidT;

        private void Reset()
        {
            promptText = "Search debris";
            range = 2.2f;
        }

        public override bool CanInteract(GameObject player) => !searched;

        public override void Interact(GameObject player)
        {
            if (searched) return;
            searched = true;
            int gold = Random.Range(goldRange.x, goldRange.y + 1);
            if (gold >= 3)
            {
                PlayerInventory.Instance?.AddGold(gold);
                QuestAudio.Play(QuestSound.CoinPurchase);
                QuestUI.Toast("Searched debris", $"Found {gold} gold", ToastKind.Item);
            }
            else
            {
                QuestAudio.Play(QuestSound.UIClick);
                QuestUI.Toast("Searched debris", "Nothing but soggy rope…", ToastKind.Info);
            }
            QuestManager.Instance?.Report(searchedEvent);
        }

        private void Update()
        {
            if (!searched || lid == null || lidT >= 1f) return;
            lidT = Mathf.MoveTowards(lidT, 1f, Time.deltaTime * 3f);
            lid.localRotation = Quaternion.Euler(-110f * Mathf.SmoothStep(0, 1, lidT), 0f, 0f);
        }

        public void Setup(Transform lidTransform) { lid = lidTransform; promptText = "Search debris"; range = 2.2f; }
    }
}
