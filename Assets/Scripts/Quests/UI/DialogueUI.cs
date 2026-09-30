using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>
    /// Conversation box. E / Space / click = continue (first press finishes the typewriter),
    /// Tab = skip to the choices, 1-4 or click = pick a choice.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        public static DialogueUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.open;

        [SerializeField] private float charsPerSecond = 55f;

        private enum Phase { Lines, Choices, Response }

        private RectTransform panel;
        private Text nameText, bodyText, hintText;
        private Image namePlate;
        private RectTransform choiceRoot;
        private readonly List<Button> choiceButtons = new List<Button>();

        private bool open;
        private int openFrame;
        private int choiceFrame = -1;
        private QuestNpc npc;
        private NpcConversation conversation;
        private List<DialogueLine> lines;
        private int index;
        private Phase phase;
        private string fullText = "";
        private float shown;

        public void Build(RectTransform root)
        {
            Instance = this;

            Image bg = UIKit.Panel9(root, "Dialogue", UIKit.Panel, 0.8f);
            panel = bg.rectTransform;
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(1180, 230);
            panel.anchoredPosition = new Vector2(0, 40);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.8f, 0.4f, 0.35f);

            namePlate = UIKit.Panel9(panel, "NamePlate", new Color(0.2f, 0.14f, 0.05f, 0.97f), 0.5f);
            namePlate.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(300, 48));
            nameText = UIKit.Label(namePlate.transform, "Name", "Merchant", 24, UIKit.Gold, TextAnchor.MiddleCenter, true);
            nameText.rectTransform.Stretch(10, 10, 0, 0);

            bodyText = UIKit.Label(panel, "Body", "", 26, UIKit.Text);
            bodyText.rectTransform.Stretch(48, 48, 44, 52);
            bodyText.lineSpacing = 1.15f;

            hintText = UIKit.Label(panel, "Hint", "", 16, UIKit.Muted, TextAnchor.LowerRight);
            hintText.rectTransform.Stretch(30, 30, 0, 14);

            choiceRoot = UIKit.Rect("Choices", root);
            choiceRoot.anchorMin = choiceRoot.anchorMax = new Vector2(0.5f, 0f);
            choiceRoot.pivot = new Vector2(1f, 0f);
            choiceRoot.anchoredPosition = new Vector2(590, 290);
            choiceRoot.sizeDelta = new Vector2(460, 300);
            var v = UIKit.VStack(choiceRoot.gameObject, 10, null, false);
            v.childAlignment = TextAnchor.LowerRight;
            v.childControlWidth = false;

            panel.gameObject.SetActive(false);
            choiceRoot.gameObject.SetActive(false);
        }

        public void Open(QuestNpc speaker, NpcConversation conv)
        {
            if (open || conv == null) return;
            npc = speaker;
            conversation = conv;
            open = true;
            openFrame = Time.frameCount;
            PlayerControlLock.Lock();
            panel.gameObject.SetActive(true);
            StartLines(conv.lines, Phase.Lines);
        }

        private void StartLines(List<DialogueLine> list, Phase p)
        {
            phase = p;
            lines = list ?? new List<DialogueLine>();
            index = 0;
            choiceRoot.gameObject.SetActive(false);
            if (lines.Count == 0) AfterLines();
            else ShowLine();
        }

        private void ShowLine()
        {
            DialogueLine line = lines[index];
            bool isNpc = string.IsNullOrEmpty(line.speaker);
            nameText.text = isNpc ? npc.NpcName : line.speaker;
            nameText.color = isNpc ? UIKit.Gold : UIKit.Teal;
            fullText = line.text ?? "";
            shown = 0f;
            bodyText.text = "";
            if (isNpc && npc.Anim != null && line.gesture != NpcGesture.None) npc.Anim.Play(line.gesture);
            QuestAudio.Play(QuestSound.DialogueBlip, null, 0.6f);
            hintText.text = "<color=#ffcc61>[E]</color> Continue     <color=#ffcc61>[Tab]</color> Skip";
        }

        private void Update()
        {
            if (!open) return;
            if (QuestPlayer.TryGetPosition(out Vector3 p)) npc?.Anim?.FaceTowards(p);

            bool typing = phase != Phase.Choices && shown < fullText.Length;
            if (typing)
            {
                shown = Mathf.Min(fullText.Length, shown + Time.unscaledDeltaTime * charsPerSecond);
                bodyText.text = fullText.Substring(0, Mathf.FloorToInt(shown));
            }

            if (Time.frameCount == openFrame || Time.frameCount == choiceFrame) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (phase == Phase.Choices)
            {
                for (int i = 0; i < choiceButtons.Count && i < 4; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Choose(i); return; }
                return;
            }

            bool next = kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                        (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            bool skip = kb.tabKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;

            if (skip) { Skip(); return; }
            if (!next) return;

            if (typing)
            {
                shown = fullText.Length;
                bodyText.text = fullText;
                return;
            }
            index++;
            if (index < lines.Count) ShowLine();
            else AfterLines();
        }

        private void Skip()
        {
            if (phase == Phase.Response) Close();
            else AfterLines();
        }

        private void AfterLines()
        {
            if (phase == Phase.Response) { Close(); return; }

            if (conversation.choices != null && conversation.choices.Count > 0)
            {
                // Keep the last line visible above the choices.
                if (lines.Count > 0) { fullText = lines[lines.Count - 1].text; shown = fullText.Length; bodyText.text = fullText; }
                ShowChoices();
                return;
            }

            npc.Execute(conversation.onFinished, conversation);
            Close();
        }

        private void ShowChoices()
        {
            phase = Phase.Choices;
            UIKit.ClearChildren(choiceRoot);
            choiceButtons.Clear();
            for (int i = 0; i < conversation.choices.Count; i++)
            {
                int idx = i;
                DialogueChoice c = conversation.choices[i];
                Button b = UIKit.Button(choiceRoot, $"{i + 1}.  {c.label}", () => Choose(idx), new Vector2(460, 58), i == 0, 23);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                b.GetComponentInChildren<Text>().rectTransform.Stretch(22, 12, 2, 2);
                choiceButtons.Add(b);
            }
            choiceRoot.gameObject.SetActive(true);
            hintText.text = "Choose  <color=#ffcc61>[1-" + conversation.choices.Count + "]</color>";
        }

        private void Choose(int i)
        {
            if (phase != Phase.Choices || i < 0 || i >= conversation.choices.Count) return;
            DialogueChoice choice = conversation.choices[i];
            choiceFrame = Time.frameCount;
            choiceRoot.gameObject.SetActive(false);

            bool closeNow = npc.Execute(choice.action, conversation);
            if (closeNow || choice.response == null || choice.response.Count == 0) { Close(); return; }
            StartLines(choice.response, Phase.Response);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            panel.gameObject.SetActive(false);
            choiceRoot.gameObject.SetActive(false);
            QuestUI.LastMenuCloseFrame = Time.frameCount;
            PlayerControlLock.Unlock();
        }
    }
}
