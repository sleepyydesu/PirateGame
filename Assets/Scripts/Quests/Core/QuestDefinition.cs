using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// A quest, authored as an asset. To add a new quest:
    ///   1. Create > Pirate Game > Quests > Quest.
    ///   2. Fill requirements, start mode and stages (each stage names the event that completes it
    ///      and the QuestTarget the marker/trail should lead to).
    ///   3. Add it to the QuestManager's list in the scene.
    ///   4. Put QuestTargets / QuestZones / pickups / NPC conversations in the scene that report
    ///      the events your stages listen for. No new code is needed for most quests.
    /// </summary>
    [CreateAssetMenu(fileName = "Quest_New", menuName = "Pirate Game/Quests/Quest")]
    public class QuestDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string questId;
        public string title = "New Quest";
        [Tooltip("Headline used when the quest is discovered. Defaults to the title.")]
        public string discoverHeadline;
        [TextArea(2, 6)] public string description;
        public QuestType questType = QuestType.Side;
        public string giverName;

        [Header("Unlocking (all must pass)")]
        public List<QuestRequirement> requirements = new List<QuestRequirement>();

        [Header("Starting")]
        public QuestStartMode startMode = QuestStartMode.AcceptFromNpc;
        [Tooltip("DiscoverOnEvent only: the event that discovers the quest.")]
        public string discoverOnEvent;
        [Tooltip("Show the Accept / Not Now popup when discovered.")]
        public bool promptOnDiscover = true;

        [Header("Stages (in order)")]
        public List<QuestStage> stages = new List<QuestStage>();

        [Header("Rewards")]
        public List<QuestReward> rewards = new List<QuestReward>();
        [Tooltip("Player picks exactly one reward (the pick is final).")]
        public bool rewardIsChoice;

        [Header("On completion")]
        public List<string> setFlagsOnComplete = new List<string>();
        public List<string> clearFlagsOnComplete = new List<string>();

        [Header("State names (Quest Log / QA)")]
        public string lockedLabel = "Locked";
        public string availableLabel = "Available";
        public string discoveredLabel = "Discovered";
        public string completedLabel = "Completed";
        [Tooltip("While Locked, show a different state name if a flag is set (last match wins). E.g. storm_started -> Storm_Triggered.")]
        public List<FlagLabel> lockedFlagLabels = new List<FlagLabel>();

        public string Id => string.IsNullOrEmpty(questId) ? name : questId;
        public string Headline => string.IsNullOrEmpty(discoverHeadline) ? title : discoverHeadline;

        public int StageIndex(string stageId)
        {
            for (int i = 0; i < stages.Count; i++)
                if (stages[i] != null && stages[i].id == stageId) return i;
            return -1;
        }

        public QuestStage GetStage(int index) => index >= 0 && index < stages.Count ? stages[index] : null;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(questId)) questId = name;
        }
#endif
    }
}
