using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    public enum NpcGesture { Talk, Thank, Freed, None }

    [Serializable]
    public class DialogueLine
    {
        [Tooltip("Empty = the NPC. Use 'You' for the player.")]
        public string speaker;
        [TextArea(1, 4)] public string text;
        public NpcGesture gesture = NpcGesture.Talk;

        public DialogueLine() { }
        public DialogueLine(string text, string speaker = null, NpcGesture gesture = NpcGesture.Talk)
        {
            this.text = text; this.speaker = speaker; this.gesture = gesture;
        }
    }

    public enum DialogueAction
    {
        None,
        AcceptQuest,
        DeclineQuest,
        ReportEvent,
        CompleteCurrentStage,
        CompleteQuestWithReward,
        /// <summary>Takes the item from the player, then reports the event.</summary>
        GiveItemAndReport,
        OpenShop,
        SetFlag,
    }

    [Serializable]
    public class DialogueActionData
    {
        public DialogueAction action;
        [Tooltip("Defaults to the conversation's quest.")]
        public QuestDefinition quest;
        [Tooltip("Event key / flag name, depending on the action.")]
        public string key;
        public int rewardIndex;
        public ItemDefinition item;

        public static DialogueActionData Of(DialogueAction a, string key = null, int reward = 0, ItemDefinition item = null)
            => new DialogueActionData { action = a, key = key, rewardIndex = reward, item = item };
    }

    [Serializable]
    public class DialogueChoice
    {
        public string label = "OK";
        public DialogueActionData action = new DialogueActionData();
        [Tooltip("Lines the NPC says after this choice, before the dialogue closes.")]
        public List<DialogueLine> response = new List<DialogueLine>();

        public DialogueChoice() { }
        public DialogueChoice(string label, DialogueActionData action, params DialogueLine[] response)
        {
            this.label = label; this.action = action; this.response = new List<DialogueLine>(response);
        }
    }

    /// <summary>
    /// One thing an NPC can say. An NPC picks the FIRST conversation in its list whose
    /// condition passes, so order them most-specific first.
    /// </summary>
    [Serializable]
    public class NpcConversation
    {
        public string label = "Conversation";
        public QuestStateCondition condition = new QuestStateCondition();
        [Tooltip("Only valid while the player carries this item.")]
        public ItemDefinition requiresItem;
        public List<DialogueLine> lines = new List<DialogueLine>();
        public List<DialogueChoice> choices = new List<DialogueChoice>();
        [Tooltip("Runs when the lines finish and there are no choices.")]
        public DialogueActionData onFinished = new DialogueActionData();

        public bool IsValid(QuestManager m, PlayerInventory inv)
        {
            if (!condition.Evaluate(m)) return false;
            if (requiresItem != null && (inv == null || !inv.Has(requiresItem))) return false;
            return true;
        }

        /// <summary>True if this conversation hands out a quest (drives the "!" icon).</summary>
        public bool OffersQuest
        {
            get
            {
                foreach (DialogueChoice c in choices) if (c.action.action == DialogueAction.AcceptQuest) return true;
                return false;
            }
        }

        public bool IsTurnIn =>
            onFinished.action == DialogueAction.GiveItemAndReport ||
            onFinished.action == DialogueAction.CompleteCurrentStage ||
            choices.Exists(c => c.action.action == DialogueAction.CompleteQuestWithReward);
    }
}
