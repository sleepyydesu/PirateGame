using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>
    /// Root of all quest UI. Builds its own overlay canvas and owns the HUD pieces
    /// (tracker, toasts, interaction prompts, quest marker, NPC "!" icons). The menus
    /// (dialogue, quest log, offer popup, shop, QA panel) are separate components it creates.
    ///
    /// Keys: J quest log · T toggle guide trail · F1 QA panel.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class QuestUI : MonoBehaviour
    {
        public static QuestUI Instance { get; private set; }
        /// <summary>Frame a menu last closed — stops the closing key press from also triggering gameplay.</summary>
        public static int LastMenuCloseFrame = -10;

        [Header("Fonts")]
        [SerializeField] private Font headerFont;
        [SerializeField] private Font bodyFont;

        [Header("Keys")]
        [SerializeField] private Key questLogKey = Key.J;
        [SerializeField] private Key trailToggleKey = Key.T;
        [SerializeField] private Key debugKey = Key.F1;

        [Header("HUD")]
        [SerializeField] private float npcIconRange = 45f;
        [SerializeField] private float toastSeconds = 3.8f;

        private RectTransform root;
        private Camera cam;

        // tracker
        private RectTransform tracker;
        private Text trackerTitle, trackerObjective, trackerDistance;
        private RectTransform trackerIcon;
        private Image trackerIconGlow;
        private CanvasGroup trackerGroup;
        private string lastObjective;
        private float trackerAnim;
        [Tooltip("Top-left offset of the objective tracker (leave room above for a minimap).")]
        [SerializeField] private Vector2 trackerOffset = new Vector2(40f, -250f);
        // prompts
        private RectTransform promptRoot;
        private readonly List<(RectTransform row, Text key, Text label)> promptRows = new List<(RectTransform, Text, Text)>();
        // marker
        private RectTransform marker;
        private Image markerIcon, markerGlow, markerArrow;
        private Text markerDistance;
        // npc icons
        private readonly List<(RectTransform rt, Text glyph, Image bg)> npcIcons = new List<(RectTransform, Text, Image)>();
        // toasts
        private RectTransform toastRoot;
        private readonly Queue<(string title, string body, ToastKind kind)> toastQueue = new Queue<(string, string, ToastKind)>();
        private readonly List<ToastView> activeToasts = new List<ToastView>();
        private int acceptFrame = -1;

        private class ToastView
        {
            public RectTransform rt;
            public CanvasGroup group;
            public float age;
        }

        public static bool AnyMenuOpen =>
            DialogueUI.IsOpen || QuestLogUI.IsOpen || ShopUI.IsOpen || QuestOfferUI.IsOpen || QuestDebugUI.IsOpen;

        public RectTransform Root => root;

        // ================================================================ build

        private void Awake()
        {
            Instance = this;
            UIKit.HeaderFont = headerFont;
            UIKit.BodyFont = bodyFont;

            var canvasGo = new GameObject("QuestUICanvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)canvasGo.transform;

            BuildMarker();
            BuildTracker();
            BuildPrompts();
            BuildToasts();

            // Menus
            gameObject.AddComponent<DialogueUI>().Build(root);
            gameObject.AddComponent<QuestOfferUI>().Build(root);
            gameObject.AddComponent<QuestLogUI>().Build(root);
            gameObject.AddComponent<ShopUI>().Build(root);
            gameObject.AddComponent<QuestDebugUI>().Build(root);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            QuestManager m = QuestManager.Instance;
            if (m == null) return;
            m.QuestAccepted -= OnAccepted;
            m.StageStarted -= OnStageStarted;
            m.StageReverted -= OnStageReverted;
            m.QuestCompleted -= OnCompleted;
            m.RewardGranted -= OnReward;
            m.StatusChanged -= OnStatusChanged;
            m.ProgressChanged -= OnProgress;
        }

        private void Start()
        {
            QuestManager m = QuestManager.Instance;
            if (m == null) return;
            m.QuestAccepted += OnAccepted;
            m.StageStarted += OnStageStarted;
            m.StageReverted += OnStageReverted;
            m.QuestCompleted += OnCompleted;
            m.RewardGranted += OnReward;
            m.StatusChanged += OnStatusChanged;
            m.ProgressChanged += OnProgress;
        }

        /// <summary>
        /// Genshin-style objective tracker on the left: gold diamond quest icon, outlined white
        /// objective text with no panel behind it, and the distance underneath.
        /// </summary>
        private void BuildTracker()
        {
            tracker = UIKit.Rect("QuestTracker", root).Place(new Vector2(0, 1), new Vector2(0, 1), trackerOffset, new Vector2(620, 80));
            trackerGroup = tracker.gameObject.AddComponent<CanvasGroup>();
            trackerGroup.blocksRaycasts = false;

            // Icon: soft glow + gold diamond + dark inner diamond + gold core
            RectTransform icon = UIKit.Rect("Icon", tracker).Place(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(20, -20), new Vector2(34, 34));
            trackerIconGlow = UIKit.Image(icon, "Glow", UIKit.Soft, new Color(1f, 0.78f, 0.3f, 0.45f));
            trackerIconGlow.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78, 78));
            Image outer = UIKit.Image(icon, "Outer", UIKit.Diamond, new Color(1f, 0.82f, 0.4f));
            outer.rectTransform.Stretch();
            Image inner = UIKit.Image(outer.transform, "Inner", UIKit.Diamond, new Color(0.35f, 0.22f, 0.05f, 0.95f));
            inner.rectTransform.Stretch(6, 6, 6, 6);
            Image core = UIKit.Image(inner.transform, "Core", UIKit.Diamond, new Color(1f, 0.85f, 0.45f));
            core.rectTransform.Stretch(6, 6, 6, 6);
            trackerIcon = icon;

            trackerObjective = UIKit.Label(tracker, "Objective", "", 28, Color.white, TextAnchor.MiddleLeft, false, FontStyle.Bold);
            trackerObjective.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(50, -20), new Vector2(620, 40));
            trackerObjective.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddTextOutline(trackerObjective);

            trackerDistance = UIKit.Label(tracker, "Distance", "", 21, new Color(0.93f, 0.93f, 0.9f), TextAnchor.MiddleLeft, false, FontStyle.Bold);
            trackerDistance.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(54, -52), new Vector2(300, 28));
            AddTextOutline(trackerDistance);

            // Quest name, small and muted, only while the objective has just changed.
            trackerTitle = UIKit.Label(tracker, "QuestName", "", 16, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleLeft);
            trackerTitle.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(48, 6), new Vector2(570, 22));
            AddTextOutline(trackerTitle);
        }

        private static void AddTextOutline(Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.1f, 0.08f, 0.05f, 0.85f);
            o.effectDistance = new Vector2(1.6f, -1.6f);
        }

        private void BuildPrompts()
        {
            promptRoot = UIKit.Rect("Prompts", root).Place(new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(90, -40), new Vector2(360, 200));
            UIKit.VStack(promptRoot.gameObject, 8, null, false).childControlHeight = false;
            for (int i = 0; i < 3; i++)
            {
                RectTransform row = UIKit.Rect("Prompt" + i, promptRoot);
                row.sizeDelta = new Vector2(360, 44);
                UIKit.HStack(row.gameObject, 12);
                Image keyBg = UIKit.Panel9(row, "Key", new Color(0.95f, 0.92f, 0.85f, 0.95f), 0.5f);
                keyBg.rectTransform.sizeDelta = new Vector2(40, 40);
                Text key = UIKit.Label(keyBg.transform, "K", "E", 20, new Color(0.1f, 0.1f, 0.12f), TextAnchor.MiddleCenter, true);
                key.GetComponent<Shadow>().enabled = false;
                key.rectTransform.Stretch();
                Image labelBg = UIKit.Panel9(row, "LabelBg", UIKit.PanelSoft, 0.5f);
                labelBg.rectTransform.sizeDelta = new Vector2(300, 40);
                Text label = UIKit.Label(labelBg.transform, "L", "", 19, UIKit.Text, TextAnchor.MiddleLeft);
                label.rectTransform.Stretch(14, 10, 0, 0);
                row.gameObject.SetActive(false);
                promptRows.Add((row, key, label));
            }
        }

        private void BuildMarker()
        {
            marker = UIKit.Rect("QuestMarker", root).Place(Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));
            markerGlow = UIKit.Image(marker, "Glow", UIKit.Soft, new Color(1f, 0.8f, 0.35f, 0.55f));
            markerGlow.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 110));
            markerIcon = UIKit.Image(marker, "Icon", UIKit.Diamond, UIKit.Gold);
            markerIcon.rectTransform.Stretch();
            Image inner = UIKit.Image(markerIcon.transform, "Inner", UIKit.Diamond, new Color(0.25f, 0.16f, 0.04f, 0.9f));
            inner.rectTransform.Stretch(12, 12, 12, 12);
            markerArrow = UIKit.Image(marker, "Arrow", UIKit.Arrow, UIKit.Gold);
            markerArrow.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(24, 22));
            markerDistance = UIKit.Label(marker, "Distance", "", 17, UIKit.Text, TextAnchor.UpperCenter);
            markerDistance.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0, -4), new Vector2(120, 24));
            marker.gameObject.SetActive(false);
        }

        private void BuildToasts()
        {
            toastRoot = UIKit.Rect("Toasts", root).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(620, 400));
            var v = UIKit.VStack(toastRoot.gameObject, 10, null, true);
            v.childAlignment = TextAnchor.UpperCenter;
        }

        // ================================================================ runtime

        private void Update()
        {
            if (cam == null) cam = Camera.main;
            bool dead = PlayerDead;
            if (hudHidden != dead)
            {
                hudHidden = dead;
                tracker.gameObject.SetActive(false);
                marker.gameObject.SetActive(false);
                toastRoot.gameObject.SetActive(!dead);
            }
            if (dead) { UpdateNpcIconsHidden(); return; }
            HandleHotkeys();
            UpdateTracker();
            UpdateMarker();
            UpdateNpcIcons();
            UpdateToasts();
        }

        private bool hudHidden;
        private PirateGame.Combat.Health playerHealth;

        private bool PlayerDead
        {
            get
            {
                if (playerHealth == null && QuestPlayer.GameObject != null) playerHealth = QuestPlayer.GameObject.GetComponent<PirateGame.Combat.Health>();
                return playerHealth != null && playerHealth.IsDead;
            }
        }

        private void UpdateNpcIconsHidden()
        {
            foreach (var icon in npcIcons) icon.rt.gameObject.SetActive(false);
        }

        private void HandleHotkeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[debugKey].wasPressedThisFrame) QuestDebugUI.Instance?.Toggle();

            bool menuBlocking = DialogueUI.IsOpen || ShopUI.IsOpen || QuestOfferUI.IsOpen;
            if (!menuBlocking && kb[questLogKey].wasPressedThisFrame) QuestLogUI.Instance?.Toggle();

            if (!AnyMenuOpen && kb[trailToggleKey].wasPressedThisFrame)
            {
                QuestTrail trail = QuestTrail.Instance;
                if (trail != null)
                {
                    trail.PlayerEnabled = !trail.PlayerEnabled;
                    Toast(trail.PlayerEnabled ? "Guide trail on" : "Guide trail off", null, ToastKind.Info);
                }
            }
        }

        private void UpdateTracker()
        {
            QuestManager m = QuestManager.Instance;
            QuestDefinition q = m != null ? m.TrackedQuest : null;
            QuestStage stage = q != null ? m.GetCurrentStage(q) : null;
            bool show = stage != null && !DialogueUI.IsOpen && !QuestLogUI.IsOpen && !ShopUI.IsOpen;
            if (tracker.gameObject.activeSelf != show) tracker.gameObject.SetActive(show);
            if (!show) { lastObjective = null; return; }

            string obj = stage.objective;
            if (stage.requiredCount > 1) obj += $" <color=#ffd27a>({m.GetProgress(q)}/{stage.requiredCount})</color>";
            if (obj != lastObjective)
            {
                lastObjective = obj;
                trackerAnim = 0f; // replay the slide-in when the objective changes
                trackerTitle.text = q.title;
            }
            trackerObjective.text = obj;

            // Slide in + fade, quest name fades out after a few seconds.
            trackerAnim += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(trackerAnim / 0.35f);
            float e = 1f - Mathf.Pow(1f - t, 3f);
            tracker.anchoredPosition = trackerOffset + new Vector2(-40f * (1f - e), 0f);
            trackerGroup.alpha = e;
            trackerTitle.color = new Color(1f, 0.85f, 0.5f, Mathf.Clamp01((4f - trackerAnim) / 0.6f));
            float pulse = trackerAnim < 1.2f ? 1f + 0.25f * Mathf.Sin(trackerAnim * Mathf.PI / 1.2f) : 1f;
            trackerIcon.localScale = Vector3.one * pulse;
            trackerIcon.localRotation = Quaternion.identity;
            trackerIconGlow.color = new Color(1f, 0.78f, 0.3f, 0.3f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f));

            if (QuestGuidance.TryGetCurrent(out _, out _, out QuestTarget target) && QuestPlayer.TryGetPosition(out Vector3 p))
            {
                if (target.IsArea && target.Contains(p)) trackerDistance.text = "In the search area";
                else
                {
                    float dy = target.transform.position.y - p.y;
                    string arrow = dy > 3f ? "▲ " : dy < -3f ? "▼ " : "▾ ";
                    trackerDistance.text = arrow + Mathf.RoundToInt(Horizontal(target.transform.position - p)) + "m";
                }
            }
            else trackerDistance.text = "";
        }

        private void UpdateMarker()
        {
            bool show = false;
            if (cam != null && !AnyMenuOpen && QuestGuidance.TryGetCurrent(out _, out QuestStage stage, out QuestTarget target) &&
                stage.showMarker && QuestPlayer.TryGetPosition(out Vector3 p))
            {
                float dist = Horizontal(target.transform.position - p);
                show = dist > 2.5f;
                if (show)
                {
                    Vector3 world = target.MarkerPosition;
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    float scale = root.lossyScale.x;
                    Vector2 size = new Vector2(Screen.width, Screen.height);
                    bool behind = sp.z < 0f;
                    Vector2 s = new Vector2(sp.x, sp.y);
                    if (behind) s = size - s;

                    const float margin = 70f;
                    Vector2 min = new Vector2(margin, margin) * scale, max = size - new Vector2(margin, margin + 80f) * scale;
                    bool offscreen = behind || s.x < min.x || s.x > max.x || s.y < min.y || s.y > max.y;
                    Vector2 center = size * 0.5f;
                    if (offscreen)
                    {
                        Vector2 dir = s - center;
                        if (dir.sqrMagnitude < 1f) dir = Vector2.down;
                        float kx = dir.x != 0 ? ((dir.x > 0 ? max.x : min.x) - center.x) / dir.x : float.MaxValue;
                        float ky = dir.y != 0 ? ((dir.y > 0 ? max.y : min.y) - center.y) / dir.y : float.MaxValue;
                        s = center + dir * Mathf.Min(Mathf.Abs(kx), Mathf.Abs(ky));
                        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                        markerArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, ang);
                        markerArrow.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, ang) * new Vector2(0, 30);
                    }
                    markerArrow.enabled = offscreen;
                    marker.anchoredPosition = s / scale;
                    markerDistance.text = $"{Mathf.RoundToInt(dist)} m";
                    float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f);
                    markerIcon.rectTransform.localScale = Vector3.one * pulse;
                    markerGlow.color = new Color(1f, 0.8f, 0.35f, 0.35f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f));
                }
            }
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        }

        private void UpdateNpcIcons()
        {
            int used = 0;
            if (cam != null && !AnyMenuOpen && QuestPlayer.TryGetPosition(out Vector3 p))
            {
                float scale = root.lossyScale.x;
                foreach (Interactable it in Interactable.All)
                {
                    if (!(it is QuestNpc npc) || !npc.isActiveAndEnabled) continue;
                    bool offer = npc.HasQuestToOffer, turnIn = !offer && npc.HasTurnIn;
                    if (!offer && !turnIn) continue;
                    if (Vector3.Distance(p, npc.transform.position) > npcIconRange) continue;
                    Vector3 sp = cam.WorldToScreenPoint(npc.transform.position + Vector3.up * 2.45f);
                    if (sp.z < 0f) continue;

                    if (used >= npcIcons.Count) npcIcons.Add(MakeNpcIcon());
                    var icon = npcIcons[used++];
                    icon.rt.gameObject.SetActive(true);
                    icon.rt.anchoredPosition = new Vector2(sp.x, sp.y) / scale + Vector2.up * (6f * Mathf.Sin(Time.time * 3f));
                    icon.glyph.text = offer ? "!" : "?";
                    icon.bg.color = offer ? UIKit.Gold : UIKit.Teal;
                }
            }
            for (int i = used; i < npcIcons.Count; i++) npcIcons[i].rt.gameObject.SetActive(false);
        }

        private (RectTransform, Text, Image) MakeNpcIcon()
        {
            RectTransform rt = UIKit.Rect("NpcIcon", root).Place(Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(50, 50));
            rt.SetSiblingIndex(0);
            Image bg = UIKit.Image(rt, "Bg", UIKit.Diamond, UIKit.Gold);
            bg.rectTransform.Stretch();
            Image inner = UIKit.Image(bg.transform, "Inner", UIKit.Diamond, new Color(0.12f, 0.09f, 0.04f, 0.95f));
            inner.rectTransform.Stretch(5, 5, 5, 5);
            Text glyph = UIKit.Label(rt, "Glyph", "!", 32, UIKit.Text, TextAnchor.MiddleCenter, true);
            glyph.rectTransform.Stretch();
            return (rt, glyph, bg);
        }

        public void ShowPrompts(List<InteractionPrompt> prompts)
        {
            bool hide = AnyMenuOpen;
            for (int i = 0; i < promptRows.Count; i++)
            {
                bool on = !hide && prompts != null && i < prompts.Count;
                var row = promptRows[i];
                if (row.row.gameObject.activeSelf != on) row.row.gameObject.SetActive(on);
                if (!on) continue;
                row.key.text = UIKit.KeyName(prompts[i].Key);
                row.label.text = prompts[i].Text;
            }
        }

        // ================================================================ toasts

        public static void Toast(string title, string body = null, ToastKind kind = ToastKind.Info)
        {
            if (Instance != null) Instance.toastQueue.Enqueue((title, body, kind));
            else Debug.Log($"[Quest toast] {title} {body}");
        }

        private void UpdateToasts()
        {
            while (toastQueue.Count > 0 && activeToasts.Count < 4)
            {
                var (title, body, kind) = toastQueue.Dequeue();
                activeToasts.Add(MakeToast(title, body, kind));
            }

            for (int i = activeToasts.Count - 1; i >= 0; i--)
            {
                ToastView t = activeToasts[i];
                t.age += Time.unscaledDeltaTime;
                float a = Mathf.Min(Mathf.Clamp01(t.age / 0.25f), Mathf.Clamp01((toastSeconds - t.age) / 0.5f));
                t.group.alpha = a;
                t.rt.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, Mathf.Clamp01(t.age / 0.25f));
                if (t.age >= toastSeconds)
                {
                    Destroy(t.rt.gameObject);
                    activeToasts.RemoveAt(i);
                }
            }
        }

        private ToastView MakeToast(string title, string body, ToastKind kind)
        {
            Color c = UIKit.KindColor(kind);
            Image bg = UIKit.Panel9(toastRoot, "Toast", UIKit.Panel, 0.7f);
            UIKit.VStack(bg.gameObject, 2, new RectOffset(24, 24, 14, 14));
            Image bar = UIKit.Image(bg.transform, "Accent", null, c);
            bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            bar.rectTransform.anchorMin = new Vector2(0.5f, 1f); bar.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.sizeDelta = new Vector2(120, 3);
            bar.rectTransform.anchoredPosition = new Vector2(0, -3);
            UIKit.Label(bg.transform, "Title", title, 24, c, TextAnchor.MiddleCenter, true);
            if (!string.IsNullOrEmpty(body)) UIKit.Label(bg.transform, "Body", body, 18, UIKit.Text, TextAnchor.MiddleCenter);
            var group = bg.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            return new ToastView { rt = bg.rectTransform, group = group };
        }

        // ================================================================ quest event -> toast

        private void OnAccepted(QuestDefinition q)
        {
            acceptFrame = Time.frameCount;
            QuestStage first = q.GetStage(0);
            Toast("Quest Accepted: " + q.title, first != null ? first.objective : null, ToastKind.Quest);
        }

        private void OnStageStarted(QuestDefinition q, QuestStage stage)
        {
            if (Time.frameCount == acceptFrame) return;
            QuestAudio.Play(QuestSound.ObjectiveUpdated);
            Toast("Objective Updated", stage.objective, ToastKind.Quest);
        }

        private void OnStageReverted(QuestDefinition q, QuestStage from) { }

        private void OnProgress(QuestDefinition q, int progress, int needed)
        {
            if (progress < needed) Toast(q.title, $"{QuestManager.Instance.GetCurrentStage(q)?.objective} ({progress}/{needed})", ToastKind.Info);
        }

        private void OnCompleted(QuestDefinition q) => Toast("Quest Completed", q.title, ToastKind.Quest);

        private void OnReward(QuestDefinition q, QuestReward r) => Toast("Reward: " + r.displayName, r.description, ToastKind.Reward);

        private void OnStatusChanged(QuestDefinition q, QuestStatus from, QuestStatus to)
        {
            if (to == QuestStatus.Available && q.startMode == QuestStartMode.AcceptFromNpc)
                Toast("New quest available", string.IsNullOrEmpty(q.giverName) ? q.title : $"Talk to the {q.giverName}", ToastKind.Quest);
        }

        private static float Horizontal(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
