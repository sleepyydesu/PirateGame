using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Lifecycle every quest goes through. Quest-specific states from the design docs
    /// (e.g. "Active_Search", "Active_Return") are the *stages* inside Active — see
    /// <see cref="QuestStage.stateLabel"/> and <see cref="QuestManager.GetStateLabel"/>.
    /// </summary>
    public enum QuestStatus
    {
        Locked = 0,      // requirements not met yet
        Available = 1,   // can be offered / discovered
        Discovered = 2,  // player knows about it but hasn't accepted (e.g. "Not Now" from a popup)
        Active = 3,      // accepted, working through stages
        Completed = 4,
    }

    [Flags]
    public enum QuestStatusMask
    {
        None = 0,
        Locked = 1 << 0,
        Available = 1 << 1,
        Discovered = 1 << 2,
        Active = 1 << 3,
        Completed = 1 << 4,
        Any = Locked | Available | Discovered | Active | Completed,
    }

    public static class QuestStatusExtensions
    {
        public static QuestStatusMask ToMask(this QuestStatus s) => (QuestStatusMask)(1 << (int)s);
    }

    public enum QuestType { Main, Side }

    public enum QuestStartMode
    {
        /// <summary>An NPC offers it in dialogue with an Accept / Not Now choice.</summary>
        AcceptFromNpc,
        /// <summary>Becomes Discovered when <see cref="QuestDefinition.discoverOnEvent"/> is reported; accepted from a popup or the Quest Log.</summary>
        DiscoverOnEvent,
        /// <summary>Goes straight to Active as soon as its requirements are met.</summary>
        AutoStart,
    }

    /// <summary>One unlock requirement. All requirements on a quest must pass for it to leave Locked.</summary>
    [Serializable]
    public class QuestRequirement
    {
        public enum Kind { QuestCompleted, FlagSet, FlagNotSet }

        public Kind kind;
        public QuestDefinition quest;
        public string flag;

        public bool IsMet(QuestManager m)
        {
            switch (kind)
            {
                case Kind.QuestCompleted: return quest != null && m.GetStatus(quest) == QuestStatus.Completed;
                case Kind.FlagSet: return m.HasFlag(flag);
                case Kind.FlagNotSet: return !m.HasFlag(flag);
                default: return true;
            }
        }

        public string Describe()
        {
            switch (kind)
            {
                case Kind.QuestCompleted: return quest != null ? $"Complete \"{quest.title}\"" : "(missing quest)";
                case Kind.FlagSet: return $"Flag '{flag}' set";
                case Kind.FlagNotSet: return $"Flag '{flag}' not set";
                default: return "";
            }
        }
    }

    /// <summary>
    /// Reusable "is the world in this state?" test used by NPC dialogue, bindings,
    /// pickups, encounters and zones. Leave <see cref="quest"/> empty to test flags only.
    /// </summary>
    [Serializable]
    public class QuestStateCondition
    {
        public QuestDefinition quest;
        public QuestStatusMask statuses = QuestStatusMask.Any;
        [Tooltip("Only checked while the quest is Active. Empty = any stage.")]
        public List<string> stageIds = new List<string>();
        public List<string> requiredFlags = new List<string>();
        public List<string> forbiddenFlags = new List<string>();

        public bool Evaluate(QuestManager m)
        {
            if (m == null) return false;

            if (quest != null)
            {
                QuestStatus status = m.GetStatus(quest);
                if ((statuses & status.ToMask()) == 0) return false;

                if (status == QuestStatus.Active && stageIds != null && stageIds.Count > 0)
                {
                    QuestStage stage = m.GetCurrentStage(quest);
                    if (stage == null || !stageIds.Contains(stage.id)) return false;
                }
            }

            if (requiredFlags != null)
                foreach (string f in requiredFlags)
                    if (!string.IsNullOrEmpty(f) && !m.HasFlag(f)) return false;

            if (forbiddenFlags != null)
                foreach (string f in forbiddenFlags)
                    if (!string.IsNullOrEmpty(f) && m.HasFlag(f)) return false;

            return true;
        }

        // ---- fluent helpers (used by code / the scene builder) ----
        public static QuestStateCondition For(QuestDefinition q, QuestStatusMask statuses, params string[] stageIds)
            => new QuestStateCondition { quest = q, statuses = statuses, stageIds = new List<string>(stageIds) };

        public QuestStateCondition Require(params string[] flags) { requiredFlags.AddRange(flags); return this; }
        public QuestStateCondition Forbid(params string[] flags) { forbiddenFlags.AddRange(flags); return this; }
    }

    [Serializable]
    public class FlagLabel
    {
        public string flag;
        public string label;
    }

    /// <summary>
    /// One step of a quest. Stages advance when <see cref="completeOnEvent"/> has been
    /// reported <see cref="requiredCount"/> times, or when code/dialogue completes them.
    /// </summary>
    [Serializable]
    public class QuestStage
    {
        [Tooltip("Unique id inside this quest, e.g. 'Active_Search'.")]
        public string id = "Stage";
        [Tooltip("State name shown in the Quest Log / QA panel. Defaults to the id. Several stages may share a label.")]
        public string stateLabel;
        [TextArea(1, 3)] public string objective = "Do the thing.";

        [Header("Guidance")]
        [Tooltip("Id of a QuestTarget in the scene. The marker and trail lead here.")]
        public string targetId;
        public bool showMarker = true;
        public bool showTrail = true;

        [Header("Completion")]
        [Tooltip("Event key that progresses this stage, e.g. 'item_collected:merchant_item'. Leave empty if dialogue/code completes it.")]
        public string completeOnEvent;
        [Min(1)] public int requiredCount = 1;

        [Header("Falling back")]
        [Tooltip("If this event is reported while in this stage, go back to 'Revert To Stage'.")]
        public string revertOnEvent;
        [Tooltip("If set, the stage reverts whenever the player no longer carries this item.")]
        public ItemDefinition requiredItem;
        [Tooltip("Stage id to revert to. Empty = previous stage.")]
        public string revertToStageId;

        [Header("Flags")]
        public string setFlagOnEnter;
        public string setFlagOnComplete;

        public string Label => string.IsNullOrEmpty(stateLabel) ? id : stateLabel;
    }
}
