using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.UI
{
    public class HudFillTrail : MonoBehaviour
    {
        public enum Mode { DamageTrail, GainTrail }

        [SerializeField] private Image front;   // the real fill, drawn on top
        [SerializeField] private Image back;    // trail / preview, drawn behind
        [SerializeField] private Mode mode = Mode.DamageTrail;
        [SerializeField] private float delay = 0.45f;
        [SerializeField] private float speed = 0.6f;   // fill fraction per second

        private float hold;
        private float wrapTo = -1f;

        private Image Leader => mode == Mode.DamageTrail ? front : back;
        private Image Follower => mode == Mode.DamageTrail ? back : front;

        public void Init(Image front, Image back, Mode mode, float delay, float speed)
        {
            this.front = front; this.back = back; this.mode = mode;
            this.delay = delay; this.speed = speed;
        }

        public void Snap(float v)   // no animation (use on first bind / scene load)
        {
            v = Mathf.Clamp01(v);
            front.fillAmount = back.fillAmount = v;
            wrapTo = -1f;
        }

        public void Set(float v)
        {
            v = Mathf.Clamp01(v);
            if (wrapTo >= 0f) { wrapTo = v; return; }
            bool snap = mode == Mode.DamageTrail ? v >= Follower.fillAmount : v <= Follower.fillAmount;
            Leader.fillAmount = v;
            if (snap) Follower.fillAmount = v; else hold = delay;
        }

        public void LevelUp(float newFraction)   // GainTrail only
        {
            Leader.fillAmount = 1f;
            wrapTo = Mathf.Clamp01(newFraction);
            hold = delay;
        }

        private void Update()
        {
            if (wrapTo >= 0f && Follower.fillAmount >= 0.999f)
            {
                Follower.fillAmount = 0f;
                Leader.fillAmount = wrapTo;
                wrapTo = -1f;
                hold = 0.1f;
                return;
            }
            if (Mathf.Approximately(Follower.fillAmount, Leader.fillAmount)) return;
            hold -= Time.unscaledDeltaTime;
            if (hold > 0f) return;
            Follower.fillAmount = Mathf.MoveTowards(Follower.fillAmount, Leader.fillAmount, speed * Time.unscaledDeltaTime);
        }
    }
}