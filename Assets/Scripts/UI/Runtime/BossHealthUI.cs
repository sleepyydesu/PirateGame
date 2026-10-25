using UnityEngine;
using PirateGame.Combat;
using PirateGame.Enemies;

namespace PirateGame.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup), typeof(HealthBarUI))]
    [DefaultExecutionOrder(310)]
    public sealed class BossHealthUI : MonoBehaviour
    {
        [Header("Scene references")]
        public BossAI boss;
        [Tooltip("Optional: hide the boss bar when this player dies.")]
        public Health playerHealth;
        public HealthBarUI bar;
        public CanvasGroup group;

        [Header("Visibility")]
        public bool alwaysShowWhileAlive;
        public bool hideWhileReturningHome = true;
        [Min(0f)] public float showAfterDeathSeconds = 1.5f;
        [Min(0.1f)] public float fadeSpeed = 6f;

        private BossAI boundBoss;
        private bool wasDead, wantedVisible, showDeath;
        private float deathStartedAt;

        private void Awake()
        {
            if (bar == null) bar = GetComponent<HealthBarUI>();
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            // Bind before the first bar refresh; do not reveal an empty bar for a frame.
            BindBoss();
        }

        private void OnEnable()
        {
            wasDead = wantedVisible = showDeath = false;
        }

        private void OnDisable()
        {
            if (group != null) group.alpha = 0f;
        }

        public void SetBoss(BossAI next)
        {
            boss = next;
            BindBoss();
        }

        private void BindBoss()
        {
            boundBoss = boss;
            wasDead = wantedVisible = showDeath = false;
            if (bar != null) bar.SetSource(boss != null ? boss.GetComponent<Health>() : null);
        }

        private void LateUpdate()
        {
            if (boss != boundBoss) BindBoss();
            bool show = ShouldShow();
            if (group != null)
                group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f,
                    Mathf.Max(0.1f, fadeSpeed) * Time.unscaledDeltaTime);
            wantedVisible = show;
        }

        public bool ShouldShow()
        {
            if (boss == null || bar == null || bar.source == null ||
                !boss.gameObject.activeInHierarchy ||
                (playerHealth != null && playerHealth.IsDead))
                return false;

            if (bar.source.IsDead)
            {
                if (!wasDead)
                {
                    wasDead = true;
                    showDeath = wantedVisible || (group != null && group.alpha > 0.001f);
                    deathStartedAt = Time.unscaledTime;
                }
                return showDeath && Time.unscaledTime - deathStartedAt <
                       Mathf.Max(0f, showAfterDeathSeconds);
            }

            wasDead = false;
            if (!boss.isActiveAndEnabled) return false;
            if (alwaysShowWhileAlive) return true;
            return boss.CurrentState != BossAI.State.Idle &&
                   boss.CurrentState != BossAI.State.Dead &&
                   (!hideWhileReturningHome ||
                    boss.CurrentState != BossAI.State.ReturnHome);
        }
    }
}
