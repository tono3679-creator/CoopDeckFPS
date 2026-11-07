using UnityEngine;

namespace CoopDeck.NPC
{
    /// <summary>
    /// Pure math for turning an NPC intention into <c>CharacterCommands</c>: world direction →
    /// local stick input, aim point → look delta, where to stand when following, and whether the
    /// NPC is in the leader's line of fire.
    /// </summary>
    public static class NpcSteering
    {
        /// <summary>World move direction (magnitude 0..1) to the motor's local input (x = right, y = forward).</summary>
        public static Vector2 LocalMove(Vector3 worldDirection, Vector3 forward, Vector3 right)
        {
            worldDirection.y = 0f;
            Vector3 f = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 r = new Vector3(right.x, 0f, right.z).normalized;
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(worldDirection, r), Vector3.Dot(worldDirection, f)), 1f);
        }

        /// <summary>Yaw (x) / pitch (y) error in degrees from the current aim direction to the target direction.</summary>
        public static Vector2 AimError(Vector3 aimDirection, Vector3 toTarget)
        {
            float flat = new Vector2(toTarget.x, toTarget.z).magnitude;
            float yawError = Mathf.DeltaAngle(Mathf.Atan2(aimDirection.x, aimDirection.z) * Mathf.Rad2Deg, Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg);
            float pitchNow = Mathf.Asin(Mathf.Clamp(aimDirection.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitchWanted = Mathf.Atan2(toTarget.y, Mathf.Max(0.001f, flat)) * Mathf.Rad2Deg;
            return new Vector2(yawError, pitchWanted - pitchNow);
        }

        /// <summary>Look delta for this frame: proportional to the error, capped at the turn speed.</summary>
        public static Vector2 LookDelta(Vector2 error, float gain, float maxDegreesPerSecond, float deltaTime)
        {
            float cap = maxDegreesPerSecond * deltaTime;
            float yaw = Mathf.Clamp(error.x * gain * deltaTime, -cap, cap);
            float pitch = Mathf.Clamp(error.y * gain * deltaTime, -cap, cap);
            // Never overshoot within one step.
            if (Mathf.Abs(yaw) > Mathf.Abs(error.x)) yaw = error.x;
            if (Mathf.Abs(pitch) > Mathf.Abs(error.y)) pitch = error.y;
            return new Vector2(yaw, pitch);
        }

        /// <summary>
        /// The follow position: <paramref name="angle"/> degrees from the leader's view direction
        /// (180 = straight behind), on the given side (+1 right, -1 left), at <paramref name="distance"/>.
        /// </summary>
        public static Vector3 FollowSlot(Vector3 leaderPosition, Vector3 leaderView, float angle, float distance, int side)
        {
            Vector3 view = new Vector3(leaderView.x, 0f, leaderView.z);
            if (view.sqrMagnitude < 0.0001f)
            {
                view = Vector3.forward;
            }

            view.Normalize();
            Vector3 offset = Quaternion.Euler(0f, angle * (side >= 0 ? 1f : -1f), 0f) * view;
            return leaderPosition + offset * distance;
        }

        /// <summary>Distance from <paramref name="point"/> to the ray, and how far along the ray it lies.</summary>
        public static float DistanceToRay(Vector3 point, Vector3 origin, Vector3 direction, out float along)
        {
            Vector3 d = direction.normalized;
            Vector3 to = point - origin;
            along = Vector3.Dot(to, d);
            return (to - d * along).magnitude;
        }

        /// <summary>
        /// True when a body at <paramref name="point"/> stands in the leader's line of fire: in front,
        /// within <paramref name="maxRange"/>, closer to the aim line than <paramref name="radius"/>
        /// (plus a little per metre so far-away allies need a wider berth).
        /// </summary>
        public static bool InFireLine(Vector3 point, Vector3 aimOrigin, Vector3 aimDirection, float maxRange, float radius)
        {
            float lateral = DistanceToRay(point, aimOrigin, aimDirection, out float along);
            return along > 0.5f && along < maxRange && lateral < radius + along * 0.035f;
        }

        /// <summary>True when an ally stands between the shooter and the target, within <paramref name="radius"/> of the line.</summary>
        public static bool AllyBlocksShot(Vector3 shooter, Vector3 target, Vector3 ally, float radius)
        {
            Vector3 line = target - shooter;
            float length = line.magnitude;
            if (length < 0.01f)
            {
                return false;
            }

            float lateral = DistanceToRay(ally, shooter, line / length, out float along);
            return along > 0.3f && along < length - 0.3f && lateral < radius;
        }

        /// <summary>Picks the follow side away from the leader's aim (the side the NPC is already on wins ties).</summary>
        public static int PreferredSide(Vector3 npcPosition, Vector3 leaderPosition, Vector3 leaderView)
        {
            Vector3 right = Vector3.Cross(Vector3.up, new Vector3(leaderView.x, 0f, leaderView.z).normalized);
            return Vector3.Dot(npcPosition - leaderPosition, right) >= 0f ? 1 : -1;
        }
    }
}
