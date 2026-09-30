using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>
    /// Quest Log (J). Lists Active, Discovered (ready to accept) and Completed quests; the
    /// detail pane shows the design-doc state name, objectives with progress, and rewards.
    /// Discovered quests can be accepted here; active ones can be tracked.
    /// </summary>
    public class QuestLogUI : MonoBehaviour
    {
        public static QuestLogUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.panel != null && Instance.panel.gameObject.activeSelf;

        private RectTransform panel, list, detail;
        private Text dTitle, dMeta, dState, dDesc, dObjectives, dRewards, emptyText;
        private Button trackButton, acceptButton;
        private QuestDefinition selected;
        private bool dirty;

        public void Build(RectTransform root)
        {
            Instance = this;
            Image bg = UIKit.Panel9(root, "QuestLog", UIKit.Panel, 0.9f);
            panel = bg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 700));
            bg.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.8f, 0.4f, 0.35f);

            Text header = UIKit.Label(panel, "Header", "QUEST LOG", 34, UIKit.Gold, TextAnchor.MiddleLeft, true);
            header.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -22), new Vector2(500, 50));
            Text close = UIKit.Label(panel, "CloseHint", "<color=#ffcc61>[J]</color> / <color=#ffcc61>[Esc]</color> Close", 17, UIKit.Muted, TextAnchor.MiddleRight);
            close.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -30), new Vector2(400, 36));

            // Left list
            Image listBg = UIKit.Panel9(panel, "ListBg", new Color(0f, 0f, 0f, 0.25f), 0.6f);
            listBg.rectTransform.anchorMin = new Vector2(0, 0); listBg.rectTransform.anchorMax = new Vector2(0, 1);
            listBg.rectTransform.pivot = new Vector2(0, 1);
            listBg.rectTransform.offsetMin = new Vector2(30, 30); listBg.rectTransform.offsetMax = new Vector2(430, -90);
            list = UIKit.Rect("List", listBg.transform).Stretch(12, 12, 12, 12);
            var lv = UIKit.VStack(list.gameObject, 6, null, false);
            lv.childAlignment = TextAnchor.UpperLeft;

            // Right detail
            detail = UIKit.Rect("Detail", panel);
            detail.anchorMin = new Vector2(0, 0); detail.anchorMax = new Vector2(1, 1);
            detail.offsetMin = new Vector2(470, 30); detail.offsetMax = new Vector2(-40, -90);
            UIKit.VStack(detail.gameObject, 10, null, true).childAlignment = TextAnchor.UpperLeft;

            dTitle = UIKit.Label(detail, "Title", "", 32, UIKit.Gold, TextAnchor.UpperLeft, true);
            dMeta = UIKit.Label(detail, "Meta", "", 17, UIKit.Muted);
            dState = UIKit.Label(detail, "State", "", 17, UIKit.Teal);
            dDesc = UIKit.Label(detail, "Desc", "", 20, UIKit.Text);
            UIKit.Label(detail, "ObjHeader", "OBJECTIVES", 15, UIKit.Muted);
            dObjectives = UIKit.Label(detail, "Objectives", "", 20, UIKit.Text);
            dObjectives.lineSpacing = 1.2f;
            UIKit.Label(detail, "RewardHeader", "REWARDS", 15, UIKit.Muted);
            dRewards = UIKit.Label(detail, "Rewards", "", 19, UIKit.Text);

            RectTransform buttons = UIKit.Rect("Buttons", detail);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            UIKit.HStack(buttons.gameObject, 16);
            acceptButton = UIKit.Button(buttons, "Accept Quest", AcceptSelected, new Vector2(220, 52), true);
            trackButton = UIKit.Button(buttons, "Track", TrackSelected, new Vector2(180, 52));

            emptyText = UIKit.Label(panel, "Empty", "No quests yet.\nExplore the coast — someone may need your help.", 22, UIKit.Muted, TextAnchor.MiddleCenter);
            emptyText.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(200, 0), new Vector2(700, 120));

            panel.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed += MarkDirty;
        }

        private void OnDestroy()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed -= MarkDirty;
        }

        private void MarkDirty() => dirty = true;

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            panel.gameObject.SetActive(true);
            PlayerControlLock.Lock();
            QuestManager m = QuestManager.Instance;
            if (m != null && (selected == null || m.GetStatus(selected) == QuestStatus.Completed))
                selected = m.TrackedQuest
                           ?? m.WithStatus(QuestStatus.Active).FirstOrDefault()
                           ?? m.WithStatus(QuestStatus.Discovered).FirstOrDefault()
                           ?? Visible().FirstOrDefault();
            Rebuild();
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.gameObject.SetActive(false);
            QuestUI.LastMenuCloseFrame = Time.frameCount;
            PlayerControlLock.Unlock();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (dirty) Rebuild();
        }

        private System.Collections.Generic.IEnumerable<QuestDefinition> Visible()
        {
            QuestManager m = QuestManager.Instance;
            if (m == null) return Enumerable.Empty<QuestDefinition>();
            return m.Quests.Where(q => q != null && m.GetStatus(q) >= QuestStatus.Discovered);
        }

        private void Rebuild()
        {
            dirty = false;
            QuestManager m = QuestManager.Instance;
            UIKit.ClearChildren(list);
            if (m == null) return;

            var active = m.WithStatus(QuestStatus.Active).ToList();
            var discovered = m.WithStatus(QuestStatus.Discovered).ToList();
            var done = m.WithStatus(QuestStatus.Completed).ToList();

            Section("ACTIVE", active);
            Section("READY TO ACCEPT", discovered);
            Section("COMPLETED", done);

            bool any = active.Count + discovered.Count + done.Count > 0;
            emptyText.gameObject.SetActive(!any);
            detail.gameObject.SetActive(any);
            if (!any) return;
            if (selected == null || m.GetStatus(selected) < QuestStatus.Discovered) selected = Visible().FirstOrDefault();
            ShowDetail(selected);
        }

        private void Section(string title, System.Collections.Generic.List<QuestDefinition> quests)
        {
            if (quests.Count == 0) return;
            Text t = UIKit.Label(list, "Section", title, 15, UIKit.Muted);
            t.rectTransform.sizeDelta = new Vector2(370, 30);
            t.alignment = TextAnchor.LowerLeft;
            QuestManager m = QuestManager.Instance;
            foreach (QuestDefinition q in quests)
            {
                QuestDefinition captured = q;
                bool tracked = m.TrackedQuest == q;
                string label = (tracked ? "◆ " : "") + q.title + $"\n<size=14><color=#9aa0a8>{m.GetStateLabel(q)}</color></size>";
                Button b = UIKit.Button(list, label, () => { selected = captured; Rebuild(); }, new Vector2(376, 64), q == selected, 19);
                Text bt = b.GetComponentInChildren<Text>();
                bt.alignment = TextAnchor.MiddleLeft;
                bt.rectTransform.Stretch(16, 10, 4, 4);
            }
        }

        private void ShowDetail(QuestDefinition q)
        {
            QuestManager m = QuestManager.Instance;
            if (q == null || m == null) return;
            QuestStatus status = m.GetStatus(q);

            dTitle.text = q.title;
            dMeta.text = $"{q.questType} Quest" + (string.IsNullOrEmpty(q.giverName) ? "" : $"  ·  Given by {q.giverName}");
            dState.text = $"State: {m.GetStateLabel(q)}";
            dDesc.text = q.description;

            var sb = new System.Text.StringBuilder();
            if (status == QuestStatus.Discovered) sb.Append("<color=#9aa0a8>Accept this quest to begin.</color>");
            for (int i = 0; i < q.stages.Count; i++)
            {
                QuestStage s = q.stages[i];
                bool doneStage = m.IsStageDone(q, i);
                bool current = status == QuestStatus.Active && m.GetStageIndex(q) == i;
                if (!doneStage && !current) continue;
                if (doneStage) sb.Append($"<color=#7f8690>✓  {s.objective}</color>\n");
                else
                {
                    sb.Append($"<color=#ffcc61>▸</color>  {s.objective}");
                    if (s.requiredCount > 1) sb.Append($"  <color=#ffcc61>({m.GetProgress(q)}/{s.requiredCount})</color>");
                    sb.Append('\n');
                }
            }
            dObjectives.text = sb.ToString().TrimEnd();

            var rb = new System.Text.StringBuilder();
            if (q.rewardIsChoice && q.rewards.Count > 1) rb.Append("<color=#9aa0a8>Choose one (final):</color>\n");
            int chosen = m.GetChosenReward(q);
            for (int i = 0; i < q.rewards.Count; i++)
            {
                QuestReward r = q.rewards[i];
                if (r == null) continue;
                bool got = status == QuestStatus.Completed && (!q.rewardIsChoice || chosen == i);
                string mark = status == QuestStatus.Completed ? (got ? "<color=#8ce68c>✓</color>" : "<color=#7f8690>✗</color>") : "•";
                rb.Append($"{mark}  <color=#ffcc61>{r.displayName}</color> — {r.description}\n");
            }
            dRewards.text = rb.ToString().TrimEnd();

            acceptButton.gameObject.SetActive(status == QuestStatus.Discovered);
            trackButton.gameObject.SetActive(status == QuestStatus.Active);
            UIKit.SetButtonLabel(trackButton, m.TrackedQuest == q ? "Tracking ◆" : "Track");
            trackButton.interactable = m.TrackedQuest != q;
        }

        private void AcceptSelected()
        {
            if (selected != null) QuestManager.Instance?.Accept(selected);
        }

        private void TrackSelected()
        {
            if (selected != null) QuestManager.Instance?.Track(selected);
        }
    }
}
