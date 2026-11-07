#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CoopDeck.Core;
using CoopDeck.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The generated PF_Player with its full-body model (Fox Gunner): the motor's speed reaches the
    /// Animator, the body stays over the CharacterController, and the planted foot keeps pace with
    /// the ground (the controller speeds the run cycle up to the player's 6.5 m/s).
    /// </summary>
    public class PlayerBodyPlayModeTests
    {
        const float Dt = 1f / 60f;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        readonly List<GameObject> spawned = new List<GameObject>();

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;
            public CharacterCommands GetCommands(float deltaTime) => Next;
        }

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject go in spawned.Where(g => g != null))
            {
                Object.Destroy(go);
            }

            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator PlayerBody_RunsWithTheMotor_WithoutSlidingFeet()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(floor);
            floor.transform.position = new Vector3(0f, -0.5f, 30f);
            floor.transform.localScale = new Vector3(20f, 1f, 80f);
            Physics.SyncTransforms();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(prefab, Is.Not.Null, PlayerPath + " missing: run the bootstrap");
            GameObject player = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            spawned.Add(player);
            var commands = new ScriptedCommands();
            player.GetComponent<FpsCharacter>().CommandSource = commands;

            Animator body = player.transform.Find("BodyVisual").GetComponent<Animator>();
            Assert.That(body.isHuman, Is.True);
            Transform hips = body.GetBoneTransform(HumanBodyBones.Hips);
            Transform[] toes = { body.GetBoneTransform(HumanBodyBones.LeftToes), body.GetBoneTransform(HumanBodyBones.RightToes) };

            for (int i = 0; i < 30; i++)
            {
                yield return null; // settle on the floor, idle
            }

            Assert.That(body.GetFloat("Speed"), Is.LessThan(0.1f), "idle");
            Assert.That(hips.position.y - player.transform.position.y, Is.InRange(0.65f, 0.95f), "standing hips height");

            commands.Next.Move = Vector2.up; // forward = +Z
            for (int i = 0; i < 30; i++)
            {
                yield return null; // reach walking speed
            }

            const int Frames = 90;
            Vector3 start = player.transform.position;
            var lowest = new[] { float.MaxValue, float.MaxValue };
            var previous = new[] { toes[0].position, toes[1].position };
            var samples = new List<(int foot, float height, float forwardSpeed)>();
            for (int i = 0; i < Frames; i++)
            {
                yield return null;
                Vector3 local = player.transform.InverseTransformPoint(hips.position);
                Assert.That(new Vector2(local.x, local.z).magnitude, Is.LessThan(0.3f), "the body stays over the controller");
                for (int f = 0; f < 2; f++)
                {
                    Vector3 p = toes[f].position;
                    samples.Add((f, p.y, (p.z - previous[f].z) / Dt));
                    lowest[f] = Mathf.Min(lowest[f], p.y);
                    previous[f] = p;
                }
            }

            float groundSpeed = (player.transform.position.z - start.z) / (Frames * Dt);
            Assert.That(groundSpeed, Is.GreaterThan(5f), "the motor moves the player at walking speed");
            Assert.That(body.GetFloat("Speed"), Is.EqualTo(groundSpeed).Within(1f), "the Animator receives the ground speed");

            // A planted foot (within 2 cm of its lowest point) should barely move along the ground. Played at 1x the run
            // cycle would let it slide forward at about 2.5 m/s, at the plain blend's 1.27x at about 1.5 m/s.
            List<float> slide = samples.Where(s => s.height < lowest[s.foot] + 0.02f).Select(s => Mathf.Abs(s.forwardSpeed)).OrderBy(s => s).ToList();
            Assert.That(slide.Count, Is.GreaterThanOrEqualTo(6), "planted-foot samples");
            float median = slide[slide.Count / 2];
            Debug.Log($"[PlayerBody] ground {groundSpeed:0.00} m/s, planted toe slides {median:0.00} m/s (median of {slide.Count})");
            Assert.That(median, Is.LessThan(0.75f), "planted foot slides along the ground");
        }
    }
}
#endif
