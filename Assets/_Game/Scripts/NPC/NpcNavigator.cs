using UnityEngine;
using UnityEngine.AI;

namespace CoopDeck.NPC
{
    /// <summary>
    /// NavMesh pathing for a character moved by its own motor (not a NavMeshAgent): computes a
    /// path at most every <see cref="RepathInterval"/> seconds (sooner only if the destination
    /// jumps), shares a per-frame budget with every other NPC, and hands back the direction to the
    /// next corner. With no NavMesh it steers straight at the destination.
    /// </summary>
    public sealed class NpcNavigator
    {
        public const int MaxCorners = 32;

        /// <summary>Path calculations allowed per frame across all NPCs (scales to more allies).</summary>
        public const int PathBudgetPerFrame = 2;

        static int budgetFrame = -1;
        static int budgetUsed;

        readonly NavMeshPath path = new NavMeshPath();
        readonly Vector3[] corners = new Vector3[MaxCorners];
        int cornerCount;
        int cornerIndex;
        Vector3 destination;
        float nextRepath;
        bool hasDestination;
        bool retryPending;

        public NpcNavigator(float repathInterval)
        {
            RepathInterval = Mathf.Max(0.1f, repathInterval);
        }

        public float RepathInterval { get; set; }
        public bool HasPath => cornerCount > 0;
        public Vector3 Destination => destination;
        public int PathsComputed { get; private set; }

        /// <summary>The last path request found no route (destination off the mesh or unreachable).</summary>
        public bool LastPathFailed { get; private set; }

        public void Clear()
        {
            cornerCount = 0;
            cornerIndex = 0;
            hasDestination = false;
            retryPending = false;
        }

        /// <param name="force">Recompute now (stuck, teleported) if the shared budget allows.</param>
        public void SetDestination(Vector3 from, Vector3 to, float time, bool force = false)
        {
            float shift = hasDestination ? (to - destination).sqrMagnitude : float.MaxValue;
            bool moved = shift > 2.25f;   // more than 1.5 m: worth a new path once the interval allows
            bool jumped = shift > 25f;    // more than 5 m: right away
            bool needPath = force || cornerCount == 0 || retryPending || jumped || moved && time >= nextRepath;
            if (!needPath)
            {
                return;
            }

            if (!TakeBudget())
            {
                if (cornerCount == 0)
                {
                    // Head straight there until a path can be computed (a later frame retries).
                    corners[0] = to;
                    cornerCount = 1;
                    cornerIndex = 0;
                    retryPending = true;
                }

                return;
            }

            retryPending = false;

            destination = to;
            hasDestination = true;
            nextRepath = time + RepathInterval;
            PathsComputed++;
            LastPathFailed = false;

            if (NavMesh.SamplePosition(from, out NavMeshHit start, 2.5f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out NavMeshHit end, 3f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                path.status != NavMeshPathStatus.PathInvalid)
            {
                cornerCount = path.GetCornersNonAlloc(corners);
                cornerIndex = cornerCount > 1 ? 1 : 0;
                if (path.status == NavMeshPathStatus.PathPartial)
                {
                    LastPathFailed = true; // walk as close as possible, but report it
                }

                return;
            }

            // No NavMesh here (tests, open ground): go straight.
            LastPathFailed = NavMesh.SamplePosition(from, out _, 2.5f, NavMesh.AllAreas);
            corners[0] = to;
            cornerCount = 1;
            cornerIndex = 0;
        }

        /// <summary>
        /// Flat direction to the next corner (length 1, or less when arriving), and the straight-line
        /// distance left to the destination.
        /// </summary>
        public Vector3 Steer(Vector3 position, float arriveRadius, out float remaining)
        {
            remaining = 0f;
            if (cornerCount == 0)
            {
                return Vector3.zero;
            }

            while (cornerIndex < cornerCount - 1 && FlatDistance(position, corners[cornerIndex]) < 0.6f)
            {
                cornerIndex++;
            }

            Vector3 corner = corners[cornerIndex];
            Vector3 to = corner - position;
            to.y = 0f;
            remaining = FlatDistance(position, destination);
            float distance = to.magnitude;
            if (distance < 0.05f)
            {
                return Vector3.zero;
            }

            bool last = cornerIndex >= cornerCount - 1;
            float speed = last && arriveRadius > 0f ? Mathf.Clamp01(distance / arriveRadius) : 1f;
            return to / distance * speed;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static bool TakeBudget()
        {
            int frame = Time.frameCount;
            if (frame != budgetFrame)
            {
                budgetFrame = frame;
                budgetUsed = 0;
            }

            if (budgetUsed >= PathBudgetPerFrame)
            {
                return false;
            }

            budgetUsed++;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            budgetFrame = -1;
            budgetUsed = 0;
        }
    }
}
