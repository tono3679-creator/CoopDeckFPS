using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// The fox's own arms in the first-person view model (hand-modelled gloved hands on the body's forearms).
    /// Every goal is read from the third-person body relative to its rifle and placed on the view-model rifle, so
    /// first person shows the same authored motion everyone else sees:
    ///   right hand - the body's right-hand goal (pistol grip)
    ///   left hand  - FP_Grip_L (a handguard hold authored for first person) while the clips have the left hand on the
    ///                rifle, blending to the body's left-hand goal for reloads and casts
    ///   elbows     - fixed hints in this object's space (the body's elbow hints, when given, win)
    /// The shoulders are not the body's: the view-model builder places each one below the screen so the forearms reach
    /// in from the lower corners with the elbows out of view (the arms are separate pieces on Root).
    /// A small analytic two-bone IK bends each arm after the view model has bobbed and swayed (LateUpdate, late
    /// order), shares the wrist twist with the forearm (no candy-wrapper wrist), and the left hand opens (shape key
    /// "Open") while casting.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class FirstPersonArms : MonoBehaviour
    {
        [Header("Third-person source")]
        [Tooltip("The body's rifle (HeldRifle on the Weapon bone).")]
        [SerializeField] Transform bodyRifle;
        [SerializeField] Transform bodyGoalLeft;
        [SerializeField] Transform bodyGoalRight;
        [Tooltip("FP_Grip_L: the first-person left-hand hold on the handguard, riding the body's rifle.")]
        [SerializeField] Transform bodyGripLeft;
        [SerializeField] Transform bodyElbowLeft;
        [SerializeField] Transform bodyElbowRight;
        [SerializeField] CharacterAnimatorDriver driver;

        [Header("First-person")]
        [Tooltip("The view-model rifle (same prefab as the body's).")]
        [SerializeField] Transform viewRifle;
        [SerializeField] Transform leftArm;
        [SerializeField] Transform leftForeArm;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightArm;
        [SerializeField] Transform rightForeArm;
        [SerializeField] Transform rightHand;
        [Tooltip("Elbow directions in this object's space (when the body has no elbow hints).")]
        [SerializeField] Vector3 leftElbow = new Vector3(-0.46f, 0.86f, -0.2f);
        [SerializeField] Vector3 rightElbow = new Vector3(0.14f, 0.83f, -0.55f);
        [Tooltip("Share of the wrist twist the forearm takes (the rest stays in the wrist).")]
        [SerializeField, Range(0f, 1f)] float forearmTwist = 0.6f;

        [Header("Left hand shape")]
        [SerializeField] SkinnedMeshRenderer skin;
        [SerializeField] string openShape = "Open";
        [SerializeField, Min(0.1f)] float openSpeed = 12f;

        Quaternion[] rest;
        Transform[] chain;
        Quaternion restHandInForearmL;
        Quaternion restHandInForearmR;
        int openIndex = -1;
        float open;

        public float OpenAmount => open;

        public void Configure(Transform rifleOnBody, Transform goalLeft, Transform goalRight, Transform rifleInView,
            Transform[] leftChain, Transform[] rightChain)
        {
            bodyRifle = rifleOnBody;
            bodyGoalLeft = goalLeft;
            bodyGoalRight = goalRight;
            viewRifle = rifleInView;
            leftArm = leftChain[0];
            leftForeArm = leftChain[1];
            leftHand = leftChain[2];
            rightArm = rightChain[0];
            rightForeArm = rightChain[1];
            rightHand = rightChain[2];
            rest = null;
        }

        public void ConfigureExtras(Transform gripLeft, Transform elbowLeft, Transform elbowRight, SkinnedMeshRenderer mesh)
        {
            bodyGripLeft = gripLeft;
            bodyElbowLeft = elbowLeft;
            bodyElbowRight = elbowRight;
            skin = mesh;
        }

        public void SetDriver(CharacterAnimatorDriver owner) => driver = owner;

        /// <summary>Elbow directions in this object's space, used when the body gives no elbow hints.</summary>
        public void SetElbowHints(Vector3 left, Vector3 right)
        {
            leftElbow = left;
            rightElbow = right;
        }

        void Awake() => CaptureRest();

        void CaptureRest()
        {
            chain = new[] { leftArm, leftForeArm, leftHand, rightArm, rightForeArm, rightHand };
            rest = new Quaternion[chain.Length];
            for (int i = 0; i < chain.Length; i++)
            {
                rest[i] = chain[i] != null ? chain[i].localRotation : Quaternion.identity;
            }

            if (leftHand != null && rightHand != null)
            {
                restHandInForearmL = Quaternion.Inverse(leftForeArm.rotation) * leftHand.rotation;
                restHandInForearmR = Quaternion.Inverse(rightForeArm.rotation) * rightHand.rotation;
            }

            openIndex = skin != null && skin.sharedMesh != null ? skin.sharedMesh.GetBlendShapeIndex(openShape) : -1;
        }

        void LateUpdate()
        {
            if (bodyRifle == null || viewRifle == null || leftArm == null || rightArm == null)
            {
                return;
            }

            if (rest == null)
            {
                CaptureRest();
            }

            for (int i = 0; i < chain.Length; i++)
            {
                chain[i].localRotation = rest[i]; // no Animator here: start from the rest pose every frame
            }

            float onRifle = driver != null ? driver.LeftHandOnRifle : 1f;
            float cast = driver != null ? driver.CastHand : 0f;

            Goal(bodyGoalRight, out Vector3 rightPos, out Quaternion rightRot);
            Solve(rightArm, rightForeArm, rightHand, rightPos, rightRot, ElbowDirection(bodyElbowRight, rightArm, rightElbow), restHandInForearmR);

            Goal(bodyGoalLeft, out Vector3 leftPos, out Quaternion leftRot);
            if (bodyGripLeft != null && onRifle > 0f)
            {
                Goal(bodyGripLeft, out Vector3 gripPos, out Quaternion gripRot);
                leftPos = Vector3.Lerp(leftPos, gripPos, onRifle);
                leftRot = Quaternion.Slerp(leftRot, gripRot, onRifle);
            }

            Solve(leftArm, leftForeArm, leftHand, leftPos, leftRot, ElbowDirection(bodyElbowLeft, leftArm, leftElbow), restHandInForearmL);

            // the left hand opens for a cast and half-opens while it works away from the rifle (reload)
            float target = Mathf.Max(cast, (1f - onRifle) * 0.45f);
            open = Mathf.MoveTowards(open, target, openSpeed * Time.deltaTime);
            if (openIndex >= 0)
            {
                skin.SetBlendShapeWeight(openIndex, open * 100f);
            }
        }

        /// <summary>A body transform relative to the body's rifle, placed on the view-model rifle.</summary>
        void Goal(Transform bodyTransform, out Vector3 position, out Quaternion rotation)
        {
            position = viewRifle.TransformPoint(bodyRifle.InverseTransformPoint(bodyTransform.position));
            rotation = viewRifle.rotation * (Quaternion.Inverse(bodyRifle.rotation) * bodyTransform.rotation);
        }

        Vector3 ElbowDirection(Transform bodyElbow, Transform upper, Vector3 fallback)
        {
            if (bodyElbow == null)
            {
                return transform.TransformDirection(fallback);
            }

            Vector3 hint = viewRifle.TransformPoint(bodyRifle.InverseTransformPoint(bodyElbow.position));
            Vector3 dir = hint - upper.position;
            return dir.sqrMagnitude > 1e-6f ? dir : transform.TransformDirection(fallback);
        }

        void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Quaternion targetRot, Vector3 elbowDir,
            Quaternion restHandInForearm)
        {
            TwoBoneIK.Solve(upper, lower, hand, target, targetRot, elbowDir);
            TwoBoneIK.ShareTwist(lower, hand, targetRot, restHandInForearm, forearmTwist);
        }

        /// <summary>World position the first-person left/right hand aims for this frame (for tests and debugging).</summary>
        public Vector3 GoalPosition(bool left)
        {
            if (!left)
            {
                Goal(bodyGoalRight, out Vector3 p, out _);
                return p;
            }

            Goal(bodyGoalLeft, out Vector3 leftPos, out _);
            float onRifle = driver != null ? driver.LeftHandOnRifle : 1f;
            if (bodyGripLeft != null && onRifle > 0f)
            {
                Goal(bodyGripLeft, out Vector3 gripPos, out _);
                leftPos = Vector3.Lerp(leftPos, gripPos, onRifle);
            }

            return leftPos;
        }

        public Transform Hand(bool left) => left ? leftHand : rightHand;

        public Transform ForeArm(bool left) => left ? leftForeArm : rightForeArm;

        /// <summary>Bind-pose rotation of the hand relative to its forearm (what "no wrist twist" means).</summary>
        public Quaternion RestHandInForearm(bool left) => left ? restHandInForearmL : restHandInForearmR;
    }

    /// <summary>Analytic two-bone IK on transforms: the elbow bends toward a direction, the hand takes a rotation.</summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Quaternion endRotation, Vector3 elbowDirection)
        {
            Vector3 a = upper.position;
            float l1 = Vector3.Distance(a, lower.position);
            float l2 = Vector3.Distance(lower.position, end.position);
            Vector3 toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, (l1 + l2) * 0.9995f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;
            Vector3 pole = Vector3.ProjectOnPlane(elbowDirection, dir);
            if (pole.sqrMagnitude < 1e-8f)
            {
                pole = Vector3.ProjectOnPlane(Vector3.down, dir);
            }

            pole.Normalize();
            float cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            Vector3 mid = a + dir * (cos * l1) + pole * (Mathf.Sqrt(1f - cos * cos) * l1);

            upper.rotation = Quaternion.FromToRotation(lower.position - a, mid - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + dir * d - lower.position) * lower.rotation;
            end.rotation = endRotation;
        }

        /// <summary>
        /// Turns the forearm about its own axis by <paramref name="share"/> of the hand's twist (measured against the
        /// rest relation of hand and forearm), then puts the hand back on its rotation: the wrist keeps only the rest.
        /// The hand's position does not change (the axis runs through the wrist).
        /// </summary>
        public static void ShareTwist(Transform lower, Transform end, Quaternion endRotation, Quaternion restEndInLower, float share)
        {
            Vector3 axis = end.position - lower.position;
            if (axis.sqrMagnitude < 1e-8f || share <= 0f)
            {
                return;
            }

            axis.Normalize();
            Quaternion following = lower.rotation * restEndInLower; // where the hand would be with no wrist twist
            Quaternion delta = endRotation * Quaternion.Inverse(following);
            float angle = TwistAngle(delta, axis);
            lower.rotation = Quaternion.AngleAxis(angle * share, axis) * lower.rotation;
            end.rotation = endRotation;
        }

        /// <summary>Signed angle (degrees) of the twist part of <paramref name="q"/> about <paramref name="axis"/>.</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            var v = new Vector3(q.x, q.y, q.z);
            Vector3 proj = Vector3.Project(v, axis);
            var twist = new Quaternion(proj.x, proj.y, proj.z, q.w);
            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag < 1e-6f)
            {
                return 0f;
            }

            twist = new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
            twist.ToAngleAxis(out float angle, out Vector3 twistAxis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return Vector3.Dot(twistAxis, axis) < 0f ? -angle : angle;
        }
    }
}
