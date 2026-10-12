using UnityEngine;
using UnityEngine.Events;

namespace PirateGame.Quests
{
    /// <summary>
    /// Reports "entered:&lt;id&gt;" / "exited:&lt;id&gt;" when the player crosses its collider.
    /// Uses a position test rather than physics triggers so it works with CharacterController,
    /// ragdoll colliders and teleports alike. The collider is only used for its shape.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class QuestZone : MonoBehaviour
    {
        [SerializeField] private string zoneId = "zone";
        [Tooltip("Defaults to 'entered:<zoneId>'")]
        [SerializeField] private string enterEvent;
        [Tooltip("Defaults to 'exited:<zoneId>'")]
        [SerializeField] private string exitEvent;
        [SerializeField] private bool reportOnce;
        [Tooltip("Extra distance the player must move outside before 'exited' fires (prevents flicker).")]
        [SerializeField, Min(0f)] private float exitMargin = 1f;

        [Header("Only report while…")]
        [SerializeField] private bool useCondition;
        [SerializeField] private QuestStateCondition condition = new QuestStateCondition();

        [Header("Feedback")]
        [SerializeField] private bool playSoundOnEnter;
        [SerializeField] private QuestSound enterSound = QuestSound.EmptyShop;

        public UnityEvent onEnter;
        public UnityEvent onExit;

        private Collider area;
        private bool reported;

        public string ZoneId => zoneId;
        public bool PlayerInside { get; private set; }
        public string EnterEvent => string.IsNullOrEmpty(enterEvent) ? "entered:" + zoneId : enterEvent;
        public string ExitEvent => string.IsNullOrEmpty(exitEvent) ? "exited:" + zoneId : exitEvent;

        private void Awake()
        {
            area = GetComponent<Collider>();
            area.isTrigger = true;
        }

        private void Update()
        {
            if (!QuestPlayer.TryGetPosition(out Vector3 p)) return;
            Vector3 probe = p + Vector3.up * 0.5f;
            Vector3 closest = area.ClosestPoint(probe);
            float outside = (closest - probe).magnitude; // 0 when inside

            if (!PlayerInside && outside <= 0.001f) Enter();
            else if (PlayerInside && outside > exitMargin) Exit();
        }

        private bool ConditionOk => !useCondition || condition.Evaluate(QuestManager.Instance);

        private void Enter()
        {
            PlayerInside = true;
            if (!ConditionOk || (reportOnce && reported)) return;
            reported = true;
            if (playSoundOnEnter) QuestAudio.Play(enterSound, transform.position);
            QuestManager.Instance?.Report(EnterEvent);
            onEnter?.Invoke();
        }

        private void Exit()
        {
            PlayerInside = false;
            if (!ConditionOk) return;
            QuestManager.Instance?.Report(ExitEvent);
            onExit?.Invoke();
        }

        public void Configure(string id, bool once, QuestStateCondition onlyWhen = null)
        {
            zoneId = id;
            reportOnce = once;
            useCondition = onlyWhen != null;
            if (onlyWhen != null) condition = onlyWhen;
        }

        private void OnDrawGizmos()
        {
            var c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.15f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
