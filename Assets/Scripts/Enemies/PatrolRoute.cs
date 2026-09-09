using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Enemies
{
    /// <summary>
    /// A named walking route for enemies, authored in the scene.
    ///
    /// HOW TO USE:
    ///   1. Create an empty GameObject, add this component, name it (e.g. "Route_DockGuard").
    ///   2. Add empty child GameObjects for each waypoint and drag them where you want.
    ///      With "Use Children As Waypoints" ticked (the default) they're picked up
    ///      automatically in order — no dragging into the list needed.
    ///   3. Drag the route object into the enemy's "Patrol Route" field.
    ///
    /// Several enemies can share one route; each keeps its own position along it.
    /// The route draws itself in the Scene view so you can see the path.
    /// </summary>
    public class PatrolRoute : MonoBehaviour
    {
        public enum RouteMode
        {
            /// <summary>...→ C → A → B → C → A ... (walks back to the start)</summary>
            Loop,
            /// <summary>A → B → C → B → A → B ... (turns around at the ends)</summary>
            PingPong,
            /// <summary>Picks a different waypoint at random each time.</summary>
            Random
        }

        [Tooltip("How the enemy moves through the waypoints.")]
        [SerializeField] private RouteMode mode = RouteMode.Loop;

        [Tooltip("Use this object's children as the waypoints, in hierarchy order. " +
                 "Simplest option — just parent empty GameObjects under this one.")]
        [SerializeField] private bool useChildrenAsWaypoints = true;

        [Tooltip("Explicit waypoint list. Only used when 'Use Children As Waypoints' is off.")]
        [SerializeField] private List<Transform> waypoints = new List<Transform>();

        [Header("Gizmos")]
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private float gizmoRadius = 0.35f;

        public RouteMode Mode => mode;

        private readonly List<Transform> resolved = new List<Transform>();

        private List<Transform> Points
        {
            get
            {
                if (!useChildrenAsWaypoints) return waypoints;

                resolved.Clear();
                foreach (Transform child in transform) resolved.Add(child);
                return resolved;
            }
        }

        public int Count => Points.Count;

        public Vector3 GetPosition(int index)
        {
            var pts = Points;
            if (pts.Count == 0) return transform.position;
            index = Mathf.Clamp(index, 0, pts.Count - 1);
            return pts[index] != null ? pts[index].position : transform.position;
        }

        /// <summary>
        /// Work out which waypoint to head to next. <paramref name="direction"/> is
        /// only used by PingPong (it flips at the ends) — pass the enemy's own copy
        /// so several enemies can walk the same route independently.
        /// </summary>
        public int GetNextIndex(int currentIndex, ref int direction)
        {
            int count = Count;
            if (count <= 1) return 0;

            switch (mode)
            {
                case RouteMode.PingPong:
                    if (currentIndex + direction >= count || currentIndex + direction < 0)
                        direction = -direction;
                    return Mathf.Clamp(currentIndex + direction, 0, count - 1);

                case RouteMode.Random:
                    int next = Random.Range(0, count);
                    if (next == currentIndex) next = (next + 1) % count;  // never stand still twice
                    return next;

                default: // Loop
                    return (currentIndex + 1) % count;
            }
        }

        /// <summary>Index of the waypoint nearest a position — a good place to start.</summary>
        public int GetClosestIndex(Vector3 position)
        {
            var pts = Points;
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i] == null) continue;
                float d = (pts[i].position - position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ---------------------------------------------------------------- gizmos

        private void OnDrawGizmos()
        {
            var pts = Points;
            if (pts.Count == 0) return;

            Gizmos.color = gizmoColor;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i] == null) continue;
                Gizmos.DrawWireSphere(pts[i].position, gizmoRadius);

                Transform next = null;
                if (i + 1 < pts.Count) next = pts[i + 1];
                else if (mode == RouteMode.Loop && pts.Count > 1) next = pts[0];

                if (next != null) Gizmos.DrawLine(pts[i].position, next.position);
            }

            // Mark the first waypoint so the start of the route is obvious.
            if (pts[0] != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(pts[0].position + Vector3.up * 0.5f, Vector3.one * 0.3f);
            }
        }
    }
}
