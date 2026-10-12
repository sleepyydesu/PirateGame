using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PirateGame.Quests
{
    /// <summary>Simple buy screen for a <see cref="MerchantShop"/>. Shows the quest discount on every price.</summary>
    public class ShopUI : MonoBehaviour
    {
        public static ShopUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.shop != null;

        private RectTransform panel, rows;
        private Text title, goldText, discountText;
        private MerchantShop shop;
        private QuestNpc owner;

        public void Build(RectTransform root)
        {
            Instance = this;
            Image bg = UIKit.Panel9(root, "Shop", UIKit.Panel, 0.9f);
            panel = bg.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 640));
            bg.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.8f, 0.4f, 0.35f);

            title = UIKit.Label(panel, "Title", "Shop", 34, UIKit.Gold, TextAnchor.MiddleLeft, true);
            title.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -24), new Vector2(520, 50));
            goldText = UIKit.Label(panel, "Gold", "", 24, UIKit.Gold, TextAnchor.MiddleRight);
            goldText.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -26), new Vector2(260, 44));
            discountText = UIKit.Label(panel, "Discount", "", 18, UIKit.Good, TextAnchor.MiddleLeft);
            discountText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -74), new Vector2(700, 30));

            rows = UIKit.Rect("Rows", panel);
            rows.anchorMin = new Vector2(0, 0); rows.anchorMax = new Vector2(1, 1);
            rows.offsetMin = new Vector2(30, 90); rows.offsetMax = new Vector2(-30, -115);
            UIKit.VStack(rows.gameObject, 10, null, false).childAlignment = TextAnchor.UpperCenter;

            Button close = UIKit.Button(panel, "Leave  [Esc]", Close, new Vector2(220, 52));
            ((RectTransform)close.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(220, 52));

            panel.gameObject.SetActive(false);
        }

        public void Open(MerchantShop s, QuestNpc npc)
        {
            if (s == null) return;
            bool wasOpen = IsOpen;
            shop = s;
            owner = npc;
            panel.gameObject.SetActive(true);
            if (!wasOpen) PlayerControlLock.Lock();
            Rebuild();
        }

        public void Close()
        {
            if (!IsOpen) return;
            shop = null;
            panel.gameObject.SetActive(false);
            QuestUI.LastMenuCloseFrame = Time.frameCount;
            PlayerControlLock.Unlock();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        private void Rebuild()
        {
            UIKit.ClearChildren(rows);
            PlayerInventory inv = PlayerInventory.Instance;
            title.text = shop.ShopName;
            goldText.text = $"{(inv != null ? inv.Gold : 0)} gold";
            float disc = shop.Discount;
            discountText.text = disc > 0f ? $"★  Quest reward: {Mathf.RoundToInt(disc * 100)}% off everything" : "";

            foreach (MerchantShop.Entry e in shop.Stock)
            {
                MerchantShop.Entry entry = e;
                Image row = UIKit.Panel9(rows, "Row", UIKit.PanelLight, 0.6f);
                row.rectTransform.sizeDelta = new Vector2(760, 86);

                string name = e.item != null ? e.item.displayName : "Item";
                string desc = e.item != null ? e.item.description : "";
                int owned = inv != null && e.item != null ? inv.Count(e.item) : 0;
                Text n = UIKit.Label(row.transform, "Name", $"{name}{(owned > 0 ? $"  <size=15><color=#9aa0a8>(owned {owned})</color></size>" : "")}", 22, UIKit.Text);
                n.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -12), new Vector2(430, 30));
                Text d = UIKit.Label(row.transform, "Desc", desc, 16, UIKit.Muted);
                d.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -44), new Vector2(430, 40));

                int price = shop.PriceOf(e);
                string priceStr = price < e.basePrice
                    ? $"<size=16><color=#7f8690>{e.basePrice}g</color></size>  <color=#8ce68c>{price}g</color>"
                    : $"<color=#ffcc61>{price}g</color>";
                Text p = UIKit.Label(row.transform, "Price", priceStr, 24, UIKit.Text, TextAnchor.MiddleRight);
                p.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-170, 0), new Vector2(160, 40));

                Button buy = UIKit.Button(row.transform, "Buy", () => Buy(entry), new Vector2(130, 48), true);
                ((RectTransform)buy.transform).Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(130, 48));
                bool uniqueOwned = e.item != null && e.item.unique && owned > 0;
                buy.interactable = inv != null && inv.Gold >= price && !uniqueOwned;
            }
        }

        private void Buy(MerchantShop.Entry e)
        {
            if (shop != null && shop.TryBuy(e))
            {
                QuestAudio.Play(QuestSound.CoinPurchase);
                QuestUI.Toast("Purchased", $"{e.item.displayName} for {shop.PriceOf(e)} gold", ToastKind.Item);
            }
            Rebuild();
        }
    }
}
