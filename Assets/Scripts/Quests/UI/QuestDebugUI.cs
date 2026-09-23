using System.Linq;
using PirateGame.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>
    /// QA panel (F1). Force any quest into any state, jump stages, skip the storm, give/drop
    /// quest items, clear encounters and teleport to the current objective — so every state
    /// and transition in the specs can be reproduced quickly.
    /// </summary>
    public class QuestDebugUI : MonoBehaviour
    {
        public static QuestDebugUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.panel != null && Instance.panel.gameObject.activeSelf;

        private RectTransform panel, content;
        private Text info;
        private float refresh;

        public void Build(RectTransform root)
        {
            Instance = this;
            Image bg = UIKit.Panel9(root, "QuestDebug", new Color(0.03f, 0.04f, 0.06f, 0.94f), 0.6f);
            panel = bg.rectTransform;
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(16, 16); panel.offsetMax = new Vector2(596, -16);

            Text header = UIKit.Label(panel, "Header", "QUEST QA PANEL  <size=15><color=#9aa0a8>[F1] close</color></size>", 26, UIKit.Gold, TextAnchor.MiddleLeft, true);
            header.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -12), new Vector2(540, 40));

            // Scroll view
            RectTransform view = UIKit.Rect("View", panel);
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(12, 12); view.offsetMax = new Vector2(-12, -60);
            view.gameObject.AddComponent<RectMask2D>();
            var viewImg = view.gameObject.AddComponent<Image>();
            viewImg.color = new Color(0, 0, 0, 0.01f);

            content = UIKit.Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            UIKit.VStack(content.gameObject, 8, new RectOffset(8, 8, 4, 12), true);
            UIKit.FitHeight(content.gameObject);

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.viewport = view;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            panel.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                panel.gameObject.SetActive(false);
                QuestUI.LastMenuCloseFrame = Time.frameCount;
                PlayerControlLock.Unlock();
            }
            else
            {
                panel.gameObject.SetActive(true);
                PlayerControlLock.Lock();
                Rebuild();
            }
        }

        private void Update()
        {
            if (!IsOpen) return;
            refresh -= Time.unscaledDeltaTime;
            if (refresh <= 0f) { refresh = 0.25f; RefreshInfo(); }
        }

        private void Rebuild()
        {
            UIKit.ClearChildren(content);
            QuestManager m = QuestManager.Instance;
            if (m == null) return;

            foreach (QuestDefinition q in m.Quests)
            {
                if (q == null) continue;
                QuestDefinition quest = q;
                Section($"{q.title}");
                Row(("Reset", () => m.DebugReset(quest)),
                    ("Available", () => m.DebugForceStatus(quest, QuestStatus.Available)),
                    ("Discover", () => { m.DebugForceStatus(quest, QuestStatus.Available); m.Discover(quest); }),
                    ("Accept", () => { if (!m.CanAccept(quest)) m.DebugForceStatus(quest, QuestStatus.Available); m.Accept(quest); }));
                Row(("◀ Stage", () => m.DebugSetStage(quest, m.GetStatus(quest) == QuestStatus.Active ? m.GetStageIndex(quest) - 1 : 0)),
                    ("Stage ▶", () => { if (m.GetStatus(quest) == QuestStatus.Active) m.CompleteStage(quest); else m.DebugSetStage(quest, 0); }),
                    (q.rewardIsChoice ? "Complete (A)" : "Complete", () => m.Complete(quest, q.rewardIsChoice ? 0 : -1)),
                    (q.rewardIsChoice ? "Complete (B)" : "Track", () => { if (quest.rewardIsChoice) m.Complete(quest, 1); else m.Track(quest); }));
            }

            Section("World");
            Row(("Start storm", () => FindFirstObjectByType<StormEvent>()?.Begin()),
                ("Skip storm", () => FindFirstObjectByType<StormEvent>()?.CompleteInstantly()),
                ("Kill encounter", () => { foreach (var e in FindObjectsByType<CombatEncounter>()) e.DebugKillAll(); }),
                ("Go to objective", TeleportToObjective));

            foreach (QuestItemPickup pickup in FindObjectsByType<QuestItemPickup>().Where(p => p.Item != null))
            {
                ItemDefinition item = pickup.Item;
                Row(($"Give {Short(item.displayName)}", () => PlayerInventory.Instance?.Add(item)),
                    ($"Drop {Short(item.displayName)}", () => PlayerInventory.Instance?.Remove(item)));
            }

            Row(("Heal player", () => { var h = QuestPlayer.GameObject?.GetComponent<Health>(); if (h != null) { if (h.IsDead) h.Revive(); else h.Heal(h.MaxHealth); } }),
                ("+100 gold", () => PlayerInventory.Instance?.AddGold(100)),
                ("Toggle trail", () => { var t = QuestTrail.Instance; if (t) t.PlayerEnabled = !t.PlayerEnabled; }),
                ("Refresh", Rebuild));

            Section("Live state");
            info = UIKit.Label(content, "Info", "", 16, UIKit.Text);
            RefreshInfo();
        }

        private void RefreshInfo()
        {
            if (info == null) return;
            QuestManager m = QuestManager.Instance;
            if (m == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (QuestDefinition q in m.Quests)
            {
                if (q == null) continue;
                string tracked = m.TrackedQuest == q ? " ◆" : "";
                sb.Append($"<color=#ffcc61>{q.title}</color>{tracked}\n   {m.GetStatus(q)}  ·  state <color=#73d9e6>{m.GetStateLabel(q)}</color>");
                QuestStage s = m.GetCurrentStage(q);
                if (s != null && s.requiredCount > 1) sb.Append($"  ({m.GetProgress(q)}/{s.requiredCount})");
                sb.Append('\n');
            }
            sb.Append($"\n<color=#9aa0a8>Flags:</color> {string.Join(", ", m.Flags.OrderBy(f => f))}\n");
            PlayerInventory inv = PlayerInventory.Instance;
            if (inv != null)
                sb.Append($"<color=#9aa0a8>Gold:</color> {inv.Gold}   <color=#9aa0a8>Items:</color> {string.Join(", ", inv.Items.Select(kv => $"{kv.Key.displayName} x{kv.Value}"))}\n");
            PlayerUpgrades up = PlayerUpgrades.Instance;
            if (up != null)
                sb.Append($"<color=#9aa0a8>Weapon dmg x</color>{up.WeaponDamageMultiplier:0.00}   <color=#9aa0a8>Merchant discount</color> {Mathf.RoundToInt(up.GetShopDiscount("merchant") * 100)}%\n");
            foreach (CombatEncounter e in FindObjectsByType<CombatEncounter>())
                sb.Append($"<color=#9aa0a8>Encounter '{e.Id}':</color> {e.Current} ({e.Alive} alive)\n");
            StormEvent storm = FindFirstObjectByType<StormEvent>();
            if (storm != null) sb.Append($"<color=#9aa0a8>Storm:</color> {storm.Current} {(storm.Current == StormEvent.Phase.Running ? $"{storm.Elapsed:0}s" : "")}\n");
            sb.Append($"\n<color=#9aa0a8>Recent events:</color>\n  {string.Join("\n  ", m.RecentEvents.Reverse().Take(8))}");
            info.text = sb.ToString();
        }

        private static string Short(string s) => s.Length > 14 ? s.Substring(0, 13) + "…" : s;

        private void TeleportToObjective()
        {
            if (!QuestGuidance.TryGetCurrent(out _, out _, out QuestTarget t)) { QuestUI.Toast("No tracked objective"); return; }
            Vector3 pos = t.transform.position;
            if (QuestPlayer.TryGetPosition(out Vector3 p))
            {
                Vector3 dir = p - pos; dir.y = 0f;
                float back = t.IsArea ? t.areaRadius * 0.5f : 3f;
                pos += (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back) * back;
            }
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            QuestPlayer.Teleport(pos + Vector3.up * 0.1f);
        }

        private void Section(string title)
        {
            Text t = UIKit.Label(content, "Section", title, 19, UIKit.Gold, TextAnchor.LowerLeft, true);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
        }

        private void Row(params (string label, UnityEngine.Events.UnityAction action)[] buttons)
        {
            RectTransform row = UIKit.Rect("Row", content);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            var h = UIKit.HStack(row.gameObject, 6);
            h.childControlWidth = true; h.childForceExpandWidth = true;
            foreach (var (label, action) in buttons)
            {
                UnityEngine.Events.UnityAction a = action;
                Button b = UIKit.Button(row, label, () => { a(); Rebuild(); }, new Vector2(128, 38), false, 15);
                b.GetComponent<LayoutElement>().flexibleWidth = 1;
            }
        }
    }
}
