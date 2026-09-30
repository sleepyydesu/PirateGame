using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// A tied-up NPC. Stays restrained (ropes visible, struggling pose) until
    /// <see cref="freeOnEvent"/> is reported, then plays the freed reaction and sets
    /// <see cref="freedFlag"/> so it stays free.
    /// </summary>
    public class CaptiveNpc : MonoBehaviour
    {
        [SerializeField] private NpcAnimator npcAnimator;
        [SerializeField] private GameObject[] restraintVisuals;
        [SerializeField] private string freeOnEvent = "encounter_cleared:kidnappers";
        [SerializeField] private string freedFlag = "merchant_freed";
        [SerializeField] private string freedToastTitle = "The merchant is free!";
        [SerializeField] private string freedToastBody = "Talk to him.";

        public void Setup(NpcAnimator anim, GameObject[] ropes, string evt, string flag)
        {
            npcAnimator = anim; restraintVisuals = ropes; freeOnEvent = evt; freedFlag = flag;
        }

        private void OnEnable()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.EventReported += OnEvent;
            ApplyState(false);
        }

        private void Start()
        {
            // QuestManager may not have existed during the first OnEnable.
            if (QuestManager.Instance != null)
            {
                QuestManager.Instance.EventReported -= OnEvent;
                QuestManager.Instance.EventReported += OnEvent;
            }
            ApplyState(false);
        }

        private void OnDisable()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.EventReported -= OnEvent;
        }

        private bool IsFree => QuestManager.Instance != null && QuestManager.Instance.HasFlag(freedFlag);

        private void OnEvent(string key)
        {
            if (key != freeOnEvent || IsFree) return;
            QuestManager.Instance.SetFlag(freedFlag);
            ApplyState(true);
            QuestAudio.Play(QuestSound.MerchantFreed);
            QuestUI.Toast(freedToastTitle, freedToastBody, ToastKind.Quest);
        }

        private void ApplyState(bool celebrate)
        {
            bool free = IsFree;
            foreach (GameObject go in restraintVisuals) if (go) go.SetActive(!free);
            if (npcAnimator != null)
            {
                npcAnimator.SetRestrained(!free);
                if (free && celebrate) npcAnimator.Play(NpcGesture.Freed);
            }
        }
    }
}
