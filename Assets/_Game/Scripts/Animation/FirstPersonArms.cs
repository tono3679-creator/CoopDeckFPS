using System;
using UnityEngine;

namespace CoopDeck.Animation
{
    /// <summary>
    /// The fox's own arms in the first-person view model: cut from the high-resolution sculpt with the body's bones
    /// (the fox's own proportions) plus three joints per finger and thumb. Every hand goal is read from the third-person
    /// body relative to its rifle and placed on the view-model rifle, so first person shows the same authored holds and
    /// motion (reloads, casts) everyone else sees:
    ///   right hand - the body's right-hand goal (pistol grip)
    ///   left hand  - the body's left-hand goal (its hold on the rifle, reloads, casts)
    ///   elbows     - fixed hints in this object's space (the body's elbow hints, when given, win)
    /// The shoulders sit where the fox's shoulders are below its eyes, with the chest turned into a rifle stance (set by
    /// the view-model builder). A small analytic two-bone IK bends each arm after the view model has bobbed and swayed
    /// (LateUpdate, late order) and shares the wrist twist with the forearm (no candy-wrapper wrist). Then the fingers
    /// curl toward the palm around the grip (per finger and joint); the left hand opens for casts and half-opens while it
    /// works away from the rifle (reload).
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class FirstPersonArms : MonoBehaviour
    {
        /// <summary>Curl of one digit toward the palm, degrees per joint (base, middle, tip).</summary>
        [Serializable]
        public struct DigitCurl
        {
            public float a, b, c;

            public DigitCurl(float a, float b, float c)
            {
                this.a = a;
                this.b = b;
                this.c = c;
            }

            public static DigitCurl Lerp(DigitCurl x, DigitCurl y, float t) =>
                new DigitCurl(Mathf.Lerp(x.a, y.a, t), Mathf.Lerp(x.b, y.b, t), Mathf.Lerp(x.c, y.c, t));
        }

        public static readonly string[] Digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        [Header("Third-person source")]
        [Tooltip("The body's rifle (HeldRifle on the Weapon bone).")]
        [SerializeField] Transform bodyRifle;
        [SerializeField] Transform bodyGoalLeft;
        [SerializeField] Transform bodyGoalRight;
        [Tooltip("Optional first-person left-hand hold riding the body's rifle; empty = the body's own left-hand goal.")]
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
        [SerializeField] Vector3 leftElbow = new Vector3(-0.6f, -0.8f, -0.1f);
        [SerializeField] Vector3 rightElbow = new Vector3(0.9f, -0.35f, -0.25f);
        [Tooltip("Share of the wrist twist the forearm takes (the rest stays in the wrist).")]
        [SerializeField, Range(0f, 1f)] float forearmTwist = 0.6f;
        [Tooltip("Most a wrist bends off its forearm (degrees); past it the hand turns back toward the forearm's line.")]
        [SerializeField, Range(10f, 90f)] float maxWristBend = 38f;
        [Tooltip("How far (metres) the left hand may slide back along the rifle when its hold is out of the arm's reach.")]
        [SerializeField, Min(0f)] float maxLeftSlide = 0.12f;

        [Header("Shoulders (the fox's own, below its eyes)")]
        [Tooltip("The player camera: the fox's eyes.")]
        [SerializeField] Transform eye;
        [Tooltip("Shoulder joints relative to the eyes with the chest square (body bind pose; x right, y up, z forward).")]
        [SerializeField] Vector3 leftShoulderFromEye = new Vector3(-0.15f, -0.22f, 0.03f);
        [SerializeField] Vector3 rightShoulderFromEye = new Vector3(0.15f, -0.22f, 0.03f);
        [Tooltip("Chest turned right into a rifle stance (degrees): the left shoulder comes forward toward the handguard.")]
        [SerializeField, Range(0f, 60f)] float torsoYaw = 40f;
        [Tooltip("Chest leaning forward into the stock (degrees): both shoulders come forward and up under the eyes.")]
        [SerializeField, Range(0f, 45f)] float torsoLean = 33f;

        [Header("Fingers: thumb, index, middle, ring, pinky (degrees toward the palm per joint)")]
        [SerializeField] DigitCurl[] rightGrip =
        {
            new DigitCurl(25f, 30f, 20f), new DigitCurl(35f, 45f, 25f), new DigitCurl(70f, 75f, 45f),
            new DigitCurl(75f, 75f, 45f), new DigitCurl(80f, 70f, 40f),
        };
        [SerializeField] DigitCurl[] leftGrip =
        {
            new DigitCurl(20f, 25f, 15f), new DigitCurl(55f, 60f, 35f), new DigitCurl(60f, 60f, 35f),
            new DigitCurl(65f, 60f, 35f), new DigitCurl(70f, 55f, 30f),
        };
        [SerializeField] DigitCurl[] openHand =
        {
            new DigitCurl(5f, 5f, 5f), new DigitCurl(5f, 5f, 0f), new DigitCurl(5f, 5f, 0f),
            new DigitCurl(8f, 5f, 0f), new DigitCurl(10f, 5f, 0f),
        };
        [SerializeField, Min(0.1f)] float openSpeed = 12f;

        Quaternion[] rest;
        Transform[] chain;
        Quaternion restHandInForearmL;
        Quaternion restHandInForearmR;
        Joint[][] fingersL;
        Joint[][] fingersR;
        float open;

        struct Joint
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 axis; // bend toward the palm, in the bone's rest frame
        }

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

        public void ConfigureExtras(Transform gripLeft, Transform elbowLeft, Transform elbowRight)
        {
            bodyGripLeft = gripLeft;
            bodyElbowLeft = elbowLeft;
            bodyElbowRight = elbowRight;
        }

        public void SetDriver(CharacterAnimatorDriver owner) => driver = owner;

        /// <summary>The eyes (player camera) and the shoulder joints relative to them, chest square.</summary>
        public void SetShoulders(Transform eyes, Vector3 leftFromEye, Vector3 rightFromEye, float chestYaw, float chestLean)
        {
            eye = eyes;
            leftShoulderFromEye = leftFromEye;
            rightShoulderFromEye = rightFromEye;
            torsoYaw = chestYaw;
            torsoLean = chestLean;
        }

        public float TorsoLean
        {
            get => torsoLean;
            set => torsoLean = value;
        }

        public float TorsoYaw
        {
            get => torsoYaw;
            set => torsoYaw = value;
        }

        /// <summary>
        /// Shoulder joints relative to the eyes in a body's bind pose (x right, y up, z forward of the body). The eyes
        /// sit <paramref name="eyeAboveHead"/> above and <paramref name="eyeForward"/> in front of the Head bone.
        /// </summary>
        public static void ShoulderOffsets(Transform bodyRoot, Transform head, Transform leftUpperArm, Transform rightUpperArm,
            float eyeAboveHead, float eyeForward, out Vector3 left, out Vector3 right)
        {
            Vector3 eyes = bodyRoot.InverseTransformPoint(head.position) + new Vector3(0f, eyeAboveHead, eyeForward);
            left = bodyRoot.InverseTransformPoint(leftUpperArm.position) - eyes;
            right = bodyRoot.InverseTransformPoint(rightUpperArm.position) - eyes;
        }

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
                fingersL = CaptureDigits(leftHand, "Left");
                fingersR = CaptureDigits(rightHand, "Right");
            }
        }

        /// <summary>
        /// The digit joints under a hand ("LeftIndex1".."LeftIndex3" ...) in the rest pose, each with the axis that bends
        /// it toward the palm: perpendicular to the digit and to the palm normal (Palm -> PalmOut markers), signed so a
        /// positive angle moves the digit toward the palm side.
        /// </summary>
        static Joint[][] CaptureDigits(Transform hand, string side)
        {
            Transform palm = Find(hand, side + "Palm");
            Transform palmOut = Find(hand, side + "PalmOut");
            if (palm == null || palmOut == null)
            {
                return null;
            }

            Vector3 normal = (palmOut.position - palm.position).normalized;
            var digits = new Joint[Digits.Length][];
            for (int d = 0; d < Digits.Length; d++)
            {
                var bones = new Transform[3];
                for (int j = 0; j < 3; j++)
                {
                    bones[j] = Find(hand, side + Digits[d] + (j + 1));
                }

                if (Array.IndexOf(bones, null) >= 0)
                {
                    return null;
                }

                digits[d] = new Joint[3];
                for (int j = 0; j < 3; j++)
                {
                    Vector3 along = j < 2 ? bones[j + 1].position - bones[j].position : bones[2].position - bones[1].position;
                    Vector3 axis = Vector3.Cross(along, normal).normalized;
                    if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * along, normal) < Vector3.Dot(along, normal))
                    {
                        axis = -axis;
                    }

                    digits[d][j] = new Joint
                    {
                        bone = bones[j],
                        rest = bones[j].localRotation,
                        axis = Quaternion.Inverse(bones[j].rotation) * axis,
                    };
                }
            }

            return digits;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
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

            if (eye != null)
            {
                Quaternion chest = Quaternion.Euler(-torsoLean, torsoYaw, 0f); // lean pitches the shoulders forward, then the turn
                leftArm.position = eye.TransformPoint(chest * leftShoulderFromEye);
                rightArm.position = eye.TransformPoint(chest * rightShoulderFromEye);
            }

            float onRifle = driver != null ? driver.LeftHandOnRifle : 1f;
            float cast = driver != null ? driver.CastHand : 0f;

            Goal(bodyGoalRight, out Vector3 rightPos, out Quaternion rightRot);
            Solve(rightArm, rightForeArm, rightHand, rightPos, rightRot, ElbowDirection(bodyElbowRight, rightArm, rightElbow), restHandInForearmR);

            LeftGoal(onRifle, out Vector3 leftPos, out Quaternion leftRot);
            Solve(leftArm, leftForeArm, leftHand, leftPos, leftRot, ElbowDirection(bodyElbowLeft, leftArm, leftElbow), restHandInForearmL);

            // the left hand opens for a cast and half-opens while it works away from the rifle (reload)
            float target = Mathf.Max(cast, (1f - onRifle) * 0.45f);
            open = Mathf.MoveTowards(open, target, openSpeed * Time.deltaTime);
            Curl(fingersR, rightGrip, null, 0f);
            Curl(fingersL, leftGrip, openHand, open);
        }

        void Curl(Joint[][] digits, DigitCurl[] grip, DigitCurl[] opened, float openness)
        {
            if (digits == null || grip == null || grip.Length < digits.Length)
            {
                return;
            }

            for (int d = 0; d < digits.Length; d++)
            {
                DigitCurl c = opened != null && opened.Length > d ? DigitCurl.Lerp(grip[d], opened[d], openness) : grip[d];
                Joint[] joints = digits[d];
                joints[0].bone.localRotation = joints[0].rest * Quaternion.AngleAxis(c.a, joints[0].axis);
                joints[1].bone.localRotation = joints[1].rest * Quaternion.AngleAxis(c.b, joints[1].axis);
                joints[2].bone.localRotation = joints[2].rest * Quaternion.AngleAxis(c.c, joints[2].axis);
            }
        }

        void LeftGoal(float onRifle, out Vector3 position, out Quaternion rotation)
        {
            Goal(bodyGoalLeft, out position, out rotation);
            if (bodyGripLeft != null && onRifle > 0f)
            {
                Goal(bodyGripLeft, out Vector3 gripPos, out Quaternion gripRot);
                position = Vector3.Lerp(position, gripPos, onRifle);
                rotation = Quaternion.Slerp(rotation, gripRot, onRifle);
            }

            // Past the arm's reach, the hand slides back along the rifle (toward the grip) until it can hold it,
            // instead of hanging short of the rifle in the air.
            float reach = (Vector3.Distance(leftArm.position, leftForeArm.position) +
                           Vector3.Distance(leftForeArm.position, leftHand.position)) * 0.985f;
            Vector3 d = position - leftArm.position;
            Vector3 back = -viewRifle.forward;
            float c = d.sqrMagnitude - reach * reach;
            if (c > 0f && onRifle > 0f)
            {
                float b = Vector3.Dot(d, back);
                float disc = b * b - c;
                float t = disc >= 0f ? -b - Mathf.Sqrt(disc) : -b;
                position += back * (Mathf.Clamp(t, 0f, maxLeftSlide) * onRifle);
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
            // the goals come from the body's arms; these shoulders reach them from another angle, so a wrist could
            // fold further than a wrist can: keep the hand within maxWristBend of its forearm
            Quaternion following = lower.rotation * restHandInForearm;
            Vector3 axis = (hand.position - lower.position).normalized;
            float bend = Vector3.Angle(axis, hand.rotation * Quaternion.Inverse(following) * axis);
            if (bend > maxWristBend)
            {
                hand.rotation = Quaternion.Slerp(following, hand.rotation, maxWristBend / bend);
            }
        }

        /// <summary>World position the first-person left/right hand aims for this frame (for tests and debugging).</summary>
        public Vector3 GoalPosition(bool left)
        {
            if (!left)
            {
                Goal(bodyGoalRight, out Vector3 p, out _);
                return p;
            }

            float onRifle = driver != null ? driver.LeftHandOnRifle : 1f;
            LeftGoal(onRifle, out Vector3 leftPos, out _);
            return leftPos;
        }

        public Transform Hand(bool left) => left ? leftHand : rightHand;

        public Transform ForeArm(bool left) => left ? leftForeArm : rightForeArm;

        /// <summary>The digit joints of a hand (thumb, index, middle, ring, pinky; three each), or null.</summary>
        public Transform[][] DigitJoints(bool left)
        {
            Joint[][] digits = left ? fingersL : fingersR;
            if (digits == null)
            {
                return null;
            }

            var result = new Transform[digits.Length][];
            for (int d = 0; d < digits.Length; d++)
            {
                result[d] = new[] { digits[d][0].bone, digits[d][1].bone, digits[d][2].bone };
            }

            return result;
        }

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
