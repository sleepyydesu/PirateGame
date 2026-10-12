using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// An NPC the player can talk to (E within 3 m). Holds an ordered list of conversations;
    /// the first whose condition passes is used. Choices/finish actions drive the quest system
    /// (accept, decline, hand in item, pick reward, open shop…).
    /// </summary>
    public class QuestNpc : Interactable
    {
        [SerializeField] private string npcName = "Merchant";
        [SerializeField] private NpcAnimator npcAnimator;
        [SerializeField] private MerchantShop shop;
        [SerializeField] private List<NpcConversation> conversations = new List<NpcConversation>();

        public string NpcName => npcName;
        public NpcAnimator Anim => npcAnimator;
        public MerchantShop Shop => shop;
        public List<NpcConversation> Conversations => conversations;

        private void Reset()
        {
            promptText = "Talk";
            range = 3f;
        }

        private void Awake()
        {
            if (npcAnimator == null) npcAnimator = GetComponent<NpcAnimator>();
        }

        public override string PromptText => $"Talk to {npcName}";

        public override bool CanInteract(GameObject player) => !DialogueUI.IsOpen && Current != null;

        public NpcConversation Current
        {
            get
            {
                QuestManager m = QuestManager.Instance;
                if (m == null) return null;
                foreach (NpcConversation c in conversations)
                    if (c != null && c.IsValid(m, PlayerInventory.Instance)) return c;
                return null;
            }
        }

        public bool HasQuestToOffer => Current?.OffersQuest ?? false;
        public bool HasTurnIn => Current?.IsTurnIn ?? false;

        public override void Interact(GameObject player)
        {
            NpcConversation c = Current;
            if (c == null || DialogueUI.Instance == null) return;
            npcAnimator?.FaceTowards(player.transform.position);
            DialogueUI.Instance.Open(this, c);
        }

        /// <summary>Executes a dialogue action. Returns true if the dialogue should close immediately.</summary>
        public bool Execute(DialogueActionData a, NpcConversation conversation)
        {
            if (a == null) return false;
            QuestManager m = QuestManager.Instance;
            QuestDefinition q = a.quest != null ? a.quest : conversation?.condition.quest;

            switch (a.action)
            {
                case DialogueAction.AcceptQuest:
                    if (m != null && q != null) m.Accept(q);
                    break;
                case DialogueAction.DeclineQuest:
                    if (m != null && q != null) m.Decline(q);
                    break;
                case DialogueAction.ReportEvent:
                    m?.Report(a.key);
                    break;
                case DialogueAction.CompleteCurrentStage:
                    if (m != null && q != null) m.CompleteStage(q);
                    break;
                case DialogueAction.CompleteQuestWithReward:
                    if (m != null && q != null)
                    {
                        QuestAudio.Play(QuestSound.Reward);
                        m.Complete(q, a.rewardIndex);
                    }
                    break;
                case DialogueAction.GiveItemAndReport:
                    // Report first so the stage completes before the item guard notices it's gone.
                    m?.Report(a.key);
                    if (a.item != null) PlayerInventory.Instance?.Remove(a.item);
                    break;
                case DialogueAction.OpenShop:
                    if (shop != null) { ShopUI.Instance?.Open(shop, this); return true; }
                    break;
                case DialogueAction.SetFlag:
                    m?.SetFlag(a.key);
                    break;
            }
            return false;
        }

        public void AddConversation(NpcConversation c) => conversations.Add(c);
        public void Setup(string name, NpcAnimator anim, MerchantShop merchantShop)
        {
            npcName = name; npcAnimator = anim; shop = merchantShop; promptText = "Talk"; range = 3f;
        }
    }
}
