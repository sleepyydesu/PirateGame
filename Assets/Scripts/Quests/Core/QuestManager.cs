using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Runtime owner of all quest state, world flags and the event bus.
    ///
    /// Gameplay talks to quests in only three ways:
    ///   • <see cref="Report"/>(eventKey)  — "something happened" (item picked up, zone entered, enemy died…)
    ///   • <see cref="SetFlag"/>(flag)     — persistent world facts ("storm_done")
    ///   • direct calls from dialogue      — Accept / Complete(rewardChoice)
    /// Stages listen for event keys, so new quests are mostly data.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        [SerializeField] private List<QuestDefinition> quests = new List<QuestDefinition>();
        [SerializeField] private List<string> startingFlags = new List<string>();
        [SerializeField] private bool verboseLogging = true;

        private class QuestRuntime
        {
            public QuestDefinition def;
            public QuestStatus status = QuestStatus.Locked;
            public int stageIndex = -1;
            public int progress;
            public int chosenReward = -1;
        }

        private readonly Dictionary<QuestDefinition, QuestRuntime> runtimes = new Dictionary<QuestDefinition, QuestRuntime>();
        private readonly HashSet<string> flags = new HashSet<string>();
        private readonly List<string> eventHistory = new List<string>();

        public QuestDefinition TrackedQuest { get; private set; }
        public IReadOnlyList<QuestDefinition> Quests => quests;
        public IEnumerable<string> Flags => flags;
        public IReadOnlyList<string> RecentEvents => eventHistory;

        // ---- events -------------------------------------------------------------------
        public event Action<QuestDefinition, QuestStatus, QuestStatus> StatusChanged;
        public event Action<QuestDefinition, QuestStage> StageStarted;
        public event Action<QuestDefinition, QuestStage> StageCompleted;
        public event Action<QuestDefinition, QuestStage> StageReverted;
        public event Action<QuestDefinition, int, int> ProgressChanged;
        public event Action<QuestDefinition> QuestDiscovered;
        public event Action<QuestDefinition> QuestAccepted;
        public event Action<QuestDefinition> QuestCompleted;
        public event Action<QuestDefinition, QuestReward> RewardGranted;
        public event Action<string, bool> FlagChanged;
        public event Action<QuestDefinition> TrackedChanged;
        public event Action<string> EventReported;
        /// <summary>Fires after any change. Easiest hook for UI and bindings.</summary>
        public event Action Changed;

        // ---- lifecycle ----------------------------------------------------------------
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Two QuestManagers in the scene — keeping the first.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            foreach (QuestDefinition q in quests.ToList()) Register(q);
            foreach (string f in startingFlags) if (!string.IsNullOrEmpty(f)) flags.Add(f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.Changed -= CheckItemGuards;
        }

        private void Start()
        {
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.Changed += CheckItemGuards;
            EvaluateLocks();
            NotifyChanged();
        }

        public void Register(QuestDefinition q)
        {
            if (q == null || runtimes.ContainsKey(q)) return;
            runtimes[q] = new QuestRuntime { def = q };
            if (!quests.Contains(q)) quests.Add(q);
        }

        private QuestRuntime R(QuestDefinition q)
        {
            if (q == null) return null;
            if (!runtimes.TryGetValue(q, out QuestRuntime r))
            {
                Register(q);
                r = runtimes[q];
            }
            return r;
        }

        // ---- queries ------------------------------------------------------------------
        public QuestStatus GetStatus(QuestDefinition q) => R(q)?.status ?? QuestStatus.Locked;
        public int GetStageIndex(QuestDefinition q) => R(q)?.stageIndex ?? -1;
        public QuestStage GetCurrentStage(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            return r != null && r.status == QuestStatus.Active ? q.GetStage(r.stageIndex) : null;
        }
        public int GetProgress(QuestDefinition q) => R(q)?.progress ?? 0;
        public int GetChosenReward(QuestDefinition q) => R(q)?.chosenReward ?? -1;

        public bool IsStageDone(QuestDefinition q, int index)
        {
            QuestRuntime r = R(q);
            if (r == null) return false;
            return r.status == QuestStatus.Completed || (r.status == QuestStatus.Active && index < r.stageIndex);
        }

        /// <summary>The state name from the design doc, e.g. "Storm_Triggered" or "Active_Return".</summary>
        public string GetStateLabel(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            if (r == null) return "";
            switch (r.status)
            {
                case QuestStatus.Locked:
                    string label = q.lockedLabel;
                    foreach (FlagLabel fl in q.lockedFlagLabels)
                        if (fl != null && HasFlag(fl.flag)) label = fl.label;
                    return label;
                case QuestStatus.Available: return q.availableLabel;
                case QuestStatus.Discovered: return q.discoveredLabel;
                case QuestStatus.Active: return q.GetStage(r.stageIndex)?.Label ?? "Active";
                case QuestStatus.Completed: return q.completedLabel;
            }
            return r.status.ToString();
        }

        public IEnumerable<QuestDefinition> WithStatus(QuestStatus s) => quests.Where(q => GetStatus(q) == s);

        // ---- flags --------------------------------------------------------------------
        public bool HasFlag(string flag) => !string.IsNullOrEmpty(flag) && flags.Contains(flag);

        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !flags.Add(flag)) return;
            Log($"flag + {flag}");
            FlagChanged?.Invoke(flag, true);
            EvaluateLocks();
            NotifyChanged();
        }

        public void ClearFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !flags.Remove(flag)) return;
            Log($"flag - {flag}");
            FlagChanged?.Invoke(flag, false);
            EvaluateLocks();
            NotifyChanged();
        }

        // ---- event bus ----------------------------------------------------------------
        /// <summary>Tell the quest system that something happened in the world.</summary>
        public void Report(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            Log($"event '{eventKey}'");
            eventHistory.Add(eventKey);
            if (eventHistory.Count > 12) eventHistory.RemoveAt(0);
            EventReported?.Invoke(eventKey);

            // ToList: handlers can change quest state while we iterate.
            foreach (QuestRuntime r in runtimes.Values.ToList())
            {
                QuestDefinition q = r.def;
                if (r.status == QuestStatus.Available &&
                    q.startMode == QuestStartMode.DiscoverOnEvent &&
                    q.discoverOnEvent == eventKey)
                {
                    Discover(q);
                    continue;
                }

                if (r.status != QuestStatus.Active) continue;
                QuestStage stage = q.GetStage(r.stageIndex);
                if (stage == null) continue;

                if (!string.IsNullOrEmpty(stage.completeOnEvent) && stage.completeOnEvent == eventKey)
                {
                    r.progress++;
                    int needed = Mathf.Max(1, stage.requiredCount);
                    ProgressChanged?.Invoke(q, r.progress, needed);
                    if (r.progress >= needed) CompleteStage(q);
                    else NotifyChanged();
                }
                else if (!string.IsNullOrEmpty(stage.revertOnEvent) && stage.revertOnEvent == eventKey)
                {
                    RevertStage(r, stage);
                }
            }
        }

        // ---- lifecycle transitions ----------------------------------------------------
        public void EvaluateLocks()
        {
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                foreach (QuestRuntime r in runtimes.Values.ToList())
                {
                    if (r.status != QuestStatus.Locked || !RequirementsMet(r.def)) continue;
                    SetStatus(r, QuestStatus.Available);
                    changed = true;
                    if (r.def.startMode == QuestStartMode.AutoStart) Accept(r.def);
                }
            } while (changed && ++guard < 16);
        }

        public bool RequirementsMet(QuestDefinition q) =>
            q.requirements == null || q.requirements.All(req => req == null || req.IsMet(this));

        public bool Discover(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            if (r == null || r.status != QuestStatus.Available) return false;
            SetStatus(r, QuestStatus.Discovered);
            QuestAudio.Play(QuestSound.QuestDiscovered);
            QuestDiscovered?.Invoke(q);
            NotifyChanged();
            return true;
        }

        public bool CanAccept(QuestDefinition q)
        {
            QuestStatus s = GetStatus(q);
            return s == QuestStatus.Available || s == QuestStatus.Discovered;
        }

        public bool Accept(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            if (r == null || !CanAccept(q)) return false;
            SetStatus(r, QuestStatus.Active);
            QuestAudio.Play(QuestSound.QuestAccepted);
            QuestAccepted?.Invoke(q);
            TrackedQuest = q;
            TrackedChanged?.Invoke(q);

            if (q.stages.Count == 0) Complete(q);
            else EnterStage(r, 0);
            return true;
        }

        /// <summary>"Not Now". The quest keeps its current status so it can be accepted later.</summary>
        public void Decline(QuestDefinition q)
        {
            Log($"'{q.title}' declined for now ({GetStateLabel(q)})");
            NotifyChanged();
        }

        public void CompleteStage(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            if (r == null || r.status != QuestStatus.Active) return;

            QuestStage stage = q.GetStage(r.stageIndex);
            if (stage != null)
            {
                if (!string.IsNullOrEmpty(stage.setFlagOnComplete)) SetFlag(stage.setFlagOnComplete);
                StageCompleted?.Invoke(q, stage);
            }

            if (r.stageIndex + 1 < q.stages.Count) EnterStage(r, r.stageIndex + 1);
            else Complete(q);
        }

        /// <summary>Finish the quest and grant rewards. For choice rewards pass the chosen index.</summary>
        public bool Complete(QuestDefinition q, int rewardChoice = -1)
        {
            QuestRuntime r = R(q);
            if (r == null || r.status == QuestStatus.Completed) return false;

            if (q.rewardIsChoice && q.rewards.Count > 0 && (rewardChoice < 0 || rewardChoice >= q.rewards.Count))
                Debug.LogWarning($"[Quests] '{q.title}' completed without a valid reward choice.", this);

            r.chosenReward = rewardChoice;
            r.stageIndex = q.stages.Count;
            SetStatus(r, QuestStatus.Completed);

            GameObject player = QuestPlayer.GameObject;
            for (int i = 0; i < q.rewards.Count; i++)
            {
                QuestReward reward = q.rewards[i];
                if (reward == null) continue;
                if (q.rewardIsChoice && i != rewardChoice) continue;
                reward.Grant(player);
                Log($"reward '{reward.displayName}'");
                RewardGranted?.Invoke(q, reward);
            }

            foreach (string f in q.clearFlagsOnComplete) ClearFlag(f);
            foreach (string f in q.setFlagsOnComplete) SetFlag(f);
            SetFlag("quest_completed:" + q.Id);

            QuestAudio.Play(QuestSound.QuestComplete);
            QuestCompleted?.Invoke(q);

            if (TrackedQuest == q) Track(WithStatus(QuestStatus.Active).FirstOrDefault());
            EvaluateLocks();
            NotifyChanged();
            return true;
        }

        public void Track(QuestDefinition q)
        {
            if (q != null && GetStatus(q) != QuestStatus.Active) return;
            TrackedQuest = q;
            TrackedChanged?.Invoke(q);
            NotifyChanged();
        }

        // ---- QA / debug helpers ------------------------------------------------------
        public void DebugReset(QuestDefinition q)
        {
            QuestRuntime r = R(q);
            if (r == null) return;
            r.stageIndex = -1; r.progress = 0; r.chosenReward = -1;
            SetStatus(r, QuestStatus.Locked);
            if (TrackedQuest == q) Track(null);
            EvaluateLocks();
            NotifyChanged();
        }

        public void DebugForceStatus(QuestDefinition q, QuestStatus s)
        {
            QuestRuntime r = R(q);
            if (r == null) return;
            if (s == QuestStatus.Active) { DebugSetStage(q, Mathf.Max(0, r.stageIndex)); return; }
            if (s == QuestStatus.Completed) { Complete(q, q.rewardIsChoice ? 0 : -1); return; }
            r.stageIndex = -1;
            SetStatus(r, s);
            NotifyChanged();
        }

        public void DebugSetStage(QuestDefinition q, int index)
        {
            QuestRuntime r = R(q);
            if (r == null || q.stages.Count == 0) return;
            index = Mathf.Clamp(index, 0, q.stages.Count - 1);
            if (r.status != QuestStatus.Active) SetStatus(r, QuestStatus.Active);
            TrackedQuest = q;
            TrackedChanged?.Invoke(q);
            EnterStage(r, index);
        }

        // ---- internals ----------------------------------------------------------------
        private void EnterStage(QuestRuntime r, int index)
        {
            r.stageIndex = index;
            r.progress = 0;
            QuestStage stage = r.def.GetStage(index);
            if (stage != null)
            {
                Log($"'{r.def.title}' -> stage {stage.id}");
                if (!string.IsNullOrEmpty(stage.setFlagOnEnter)) SetFlag(stage.setFlagOnEnter);
                StageStarted?.Invoke(r.def, stage);
            }
            NotifyChanged();
            CheckItemGuards();
        }

        private void RevertStage(QuestRuntime r, QuestStage from)
        {
            int target = string.IsNullOrEmpty(from.revertToStageId) ? r.stageIndex - 1 : r.def.StageIndex(from.revertToStageId);
            target = Mathf.Max(0, target);
            if (target == r.stageIndex) return;
            Log($"'{r.def.title}' reverted {from.id} -> {r.def.GetStage(target)?.id}");
            StageReverted?.Invoke(r.def, from);
            EnterStage(r, target);
        }

        private void CheckItemGuards()
        {
            PlayerInventory inv = PlayerInventory.Instance;
            foreach (QuestRuntime r in runtimes.Values.ToList())
            {
                if (r.status != QuestStatus.Active) continue;
                QuestStage stage = r.def.GetStage(r.stageIndex);
                if (stage?.requiredItem == null) continue;
                if (inv == null || !inv.Has(stage.requiredItem)) RevertStage(r, stage);
            }
        }

        private void SetStatus(QuestRuntime r, QuestStatus s)
        {
            QuestStatus old = r.status;
            if (old == s) return;
            r.status = s;
            Log($"'{r.def.title}' {old} -> {s}");
            StatusChanged?.Invoke(r.def, old, s);
        }

        private void NotifyChanged() => Changed?.Invoke();

        private void Log(string msg)
        {
            if (verboseLogging) Debug.Log("[Quests] " + msg);
        }
    }
}
