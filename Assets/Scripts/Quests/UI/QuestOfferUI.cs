using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>
    /// "New quest" popup with Accept / Not Now, shown when a DiscoverOnEvent quest is discovered
    /// (Quest B's "Merchant Has Been Kidnapped"). Not Now leaves it Discovered in the Quest Log.
    /// </summary>
    public class QuestOfferUI : MonoBehaviour
    {
        public static QuestOfferUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.current != null;

        private RectTransform panel;
        private Text headline, body, giver;
        private QuestDefinition current;
        private readonly Queue<QuestDefinition> queue = new Queue<QuestDefinition>();
        private float openedAt;

        public void Build(RectTransform root)
        {
            Instance = this;
            Image bg = UIKit.Panel9(root, "QuestOffer", UIKit.Panel, 0.9f);
            panel = bg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(720, 380));
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.8f, 0.4f, 0.6f);
            outline.effectDistance = new Vector2(2, -2);

            var v = UIKit.VStack(bg.gameObject, 12, new RectOffset(44, 44, 34, 30));
            v.childAlignment = TextAnchor.UpperCenter;
            UIKit.Label(panel, "Kicker", "NEW QUEST", 16, UIKit.Muted, TextAnchor.MiddleCenter);
            headline = UIKit.Label(panel, "Headline", "", 36, UIKit.Gold, TextAnchor.MiddleCenter, true);
            giver = UIKit.Label(panel, "Giver", "", 17, UIKit.Teal, TextAnchor.MiddleCenter);
            body = UIKit.Label(panel, "Body", "", 21, UIKit.Text, TextAnchor.UpperCenter);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            RectTransform row = UIKit.Rect("Buttons", panel);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58;
            UIKit.HStack(row.gameObject, 20, TextAnchor.MiddleCenter);
            UIKit.Button(row, "[1] Accept", Accept, new Vector2(230, 56), true, 22);
            UIKit.Button(row, "[2] Not Now", NotNow, new Vector2(230, 56), false, 22);

            panel.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.QuestDiscovered += OnDiscovered;
        }

        private void OnDestroy()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.QuestDiscovered -= OnDiscovered;
        }

        private void OnDiscovered(QuestDefinition q)
        {
            if (!q.promptOnDiscover) return;
            queue.Enqueue(q);
        }

        private void Update()
        {
            if (current == null)
            {
                if (queue.Count > 0 && !DialogueUI.IsOpen && !ShopUI.IsOpen) Show(queue.Dequeue());
                return;
            }

            // Discovered-but-accepted-elsewhere (e.g. via QA panel) — just close.
            if (QuestManager.Instance != null && QuestManager.Instance.GetStatus(current) != QuestStatus.Discovered) { Hide(); return; }

            Keyboard kb = Keyboard.current;
            if (kb == null || Time.unscaledTime - openedAt < 0.4f) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Accept();
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) NotNow();
        }

        private void Show(QuestDefinition q)
        {
            current = q;
            openedAt = Time.unscaledTime;
            headline.text = q.Headline;
            giver.text = q.questType == QuestType.Side ? "Side Quest" : "Main Quest";
            body.text = q.description;
            panel.gameObject.SetActive(true);
            PlayerControlLock.Lock();
        }

        private void Accept()
        {
            if (current == null) return;
            QuestDefinition q = current;
            Hide();
            QuestManager.Instance?.Accept(q);
        }

        private void NotNow()
        {
            if (current == null) return;
            QuestDefinition q = current;
            Hide();
            QuestManager.Instance?.Decline(q);
            QuestUI.Toast("Added to Quest Log", "Accept it any time from the Quest Log [J].", ToastKind.Info);
        }

        private void Hide()
        {
            current = null;
            panel.gameObject.SetActive(false);
            QuestUI.LastMenuCloseFrame = Time.frameCount;
            PlayerControlLock.Unlock();
        }
    }
}
