using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    public enum ToastKind { Info, Quest, Item, Warning, Reward }

    /// <summary>
    /// Tiny code-first UI toolkit: theme colours, procedurally generated sprites and factory
    /// helpers. Keeps the quest UI self-contained (no prefabs or TMP resources needed).
    /// </summary>
    public static class UIKit
    {
        public static Font BodyFont;
        public static Font HeaderFont;

        public static readonly Color Panel = new Color(0.04f, 0.05f, 0.07f, 0.97f);
        public static readonly Color PanelSoft = new Color(0.04f, 0.05f, 0.07f, 0.88f);
        public static readonly Color PanelLight = new Color(0.13f, 0.16f, 0.21f, 0.95f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.38f);
        public static readonly Color GoldDim = new Color(0.72f, 0.56f, 0.26f);
        public static readonly Color Text = new Color(0.95f, 0.93f, 0.87f);
        public static readonly Color Muted = new Color(0.66f, 0.68f, 0.72f);
        public static readonly Color Teal = new Color(0.45f, 0.85f, 0.9f);
        public static readonly Color Danger = new Color(1f, 0.5f, 0.38f);
        public static readonly Color Good = new Color(0.55f, 0.9f, 0.55f);

        public static Color KindColor(ToastKind k)
        {
            switch (k)
            {
                case ToastKind.Quest: return Gold;
                case ToastKind.Item: return Teal;
                case ToastKind.Warning: return Danger;
                case ToastKind.Reward: return new Color(1f, 0.65f, 0.95f);
                default: return Text;
            }
        }

        // ------------------------------------------------------------------ sprites
        private static Sprite rounded, circle, diamond, arrow, soft, ring;

        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(64, 16);
        public static Sprite Circle => circle != null ? circle : circle = MakeShape(64, (x, y) => Mathf.Clamp01(32f - new Vector2(x - 31.5f, y - 31.5f).magnitude));
        public static Sprite Soft => soft != null ? soft : soft = MakeShape(64, (x, y) => { float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f; return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d); });
        public static Sprite Ring => ring != null ? ring : ring = MakeShape(64, (x, y) => { float d = new Vector2(x - 31.5f, y - 31.5f).magnitude; return Mathf.Clamp01(1.5f - Mathf.Abs(d - 27f)); });
        public static Sprite Diamond => diamond != null ? diamond : diamond = MakeShape(64, (x, y) => Mathf.Clamp01(29f - (Mathf.Abs(x - 31.5f) + Mathf.Abs(y - 31.5f))));
        public static Sprite Arrow => arrow != null ? arrow : arrow = MakeShape(64, (x, y) =>
        {
            // Upward-pointing triangle: wide at the bottom, apex at the top.
            if (y < 8 || y > 56) return 0f;
            float halfWidth = (56 - y) * 0.55f;
            return Mathf.Clamp01(halfWidth - Mathf.Abs(x - 31.5f) + 0.5f);
        });

        private static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static Sprite MakeShape(int size, Func<int, int, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------ factories
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Panel9(Transform parent, string name, Color color, float cornerScale = 1f)
        {
            RectTransform rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, cornerScale);
            img.color = color;
            return img;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color)
        {
            RectTransform rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.UpperLeft, bool header = false, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = header && HeaderFont != null ? HeaderFont : (BodyFont != null ? BodyFont : DefaultFont);
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, Vector2 size, bool primary = false, int fontSize = 20)
        {
            Image bg = Panel9(parent, "Btn_" + label, primary ? new Color(0.62f, 0.45f, 0.15f, 0.98f) : PanelLight, 0.6f);
            bg.raycastTarget = true;
            bg.rectTransform.sizeDelta = size;
            var le = bg.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? new Color(1f, 0.85f, 0.45f, 0.9f) : new Color(1f, 0.8f, 0.4f, 0.25f);
            outline.effectDistance = new Vector2(1f, -1f);

            var btn = bg.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1.05f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1.4f;
            btn.colors = colors;
            btn.targetGraphic = bg;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
                btn.onClick.AddListener(() => QuestAudio.Play(QuestSound.UIClick));
            }

            Text t = Label(bg.transform, "Label", label, fontSize, primary ? Color.white : Text, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch(8, 8, 2, 2);
            return btn;
        }

        public static void SetButtonLabel(Button b, string label)
        {
            Text t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        public static VerticalLayoutGroup VStack(GameObject go, float spacing, RectOffset padding = null, bool controlHeight = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childControlWidth = true;
            v.childControlHeight = controlHeight;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        public static ContentSizeFitter FitHeight(GameObject go)
        {
            var f = go.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return f;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        private static Font defaultFont;
        public static Font DefaultFont
        {
            get
            {
                if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return defaultFont;
            }
        }

        public static string KeyName(UnityEngine.InputSystem.Key k)
        {
            switch (k)
            {
                case UnityEngine.InputSystem.Key.Escape: return "Esc";
                case UnityEngine.InputSystem.Key.Tab: return "Tab";
                case UnityEngine.InputSystem.Key.Space: return "Space";
                case UnityEngine.InputSystem.Key.Enter: return "Enter";
                default:
                    string s = k.ToString();
                    return s.StartsWith("Digit") ? s.Substring(5) : s;
            }
        }
    }
}
