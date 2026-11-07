using System.Collections.Generic;
using CoopDeck.Combos;
using CoopDeck.Combat;
using CoopDeck.Enemies;
using CoopDeck.NPC;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Builds Sandbox_Phase1: a shooting range (static, moving and distant targets) and a
    /// movement course (stairs, ramps, slopes up to/over the slope limit, jump boxes, a low
    /// tunnel and pillars) for checking FPS feel.
    /// </summary>
    public static class SandboxSceneBuilder
    {
        public const string NavMeshDataPath = GamePaths.Root + "/Scenes/Sandbox_Phase1_NavMesh.asset";

        /// <summary>Where the NPC ally starts: just behind-right of the player spawn.</summary>
        public static readonly Vector3 NpcSpawn = new Vector3(2.6f, 0.05f, -32.5f);

        public static void Build(GameObject playerPrefab, GameObject dummyPrefab, MaterialSet m, VolumeProfile volumeProfile, EnemySet enemies,
            ComboAssetSet combos = null, GameObject npcPrefab = null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting(m, volumeProfile);

            var environment = new GameObject("Environment").transform;
            BuildArena(environment, m);
            BuildShootingRange(environment, m, dummyPrefab);
            BuildMovementCourse(environment, m);
            BuildSlopeTests(environment, m);
            WaveDirector director = BuildCombatArena(environment, m, enemies);
            BakeNavMesh(environment);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -30f), Quaternion.identity);

            var hud = new GameObject("HUD");
            var monitor = hud.AddComponent<FrameTimeMonitor>();
            var hudController = hud.AddComponent<HudController>();
            EditorUtil.SetObject(hudController, "player", player.GetComponent<FpsCharacter>());
            EditorUtil.SetObject(hudController, "frameMonitor", monitor);
            var deckHud = hud.AddComponent<DeckHud>();
            EditorUtil.SetObject(deckHud, "deckController", player.GetComponent<DeckController>());
            var damageHud = hud.AddComponent<DamageFeedbackHud>();
            EditorUtil.SetObject(damageHud, "player", player.GetComponent<FpsCharacter>());
            var arenaHud = hud.AddComponent<ArenaHud>();
            EditorUtil.SetObject(arenaHud, "director", director);
            var comboHud = hud.AddComponent<ComboHud>();
            EditorUtil.SetObject(comboHud, "deck", player.GetComponent<DeckController>());

            ComboSystem comboSystem = null;
            if (combos != null)
            {
                // Element combos (host-side rules) and the element auras on enemies.
                var comboGo = new GameObject("ComboSystem");
                comboSystem = comboGo.AddComponent<ComboSystem>();
                comboSystem.Configure(new List<ComboDefinition>(combos.Combos));
                comboGo.AddComponent<ElementAuraPresenter>().Configure(combos.FireAura, combos.IceAura, combos.LightningAura, combos.GravityAura);
                comboGo.AddComponent<ComboAudio>();
            }

            // "Is the team fighting?": locks NPC deck edits during combat.
            var squadGo = new GameObject("Squad");
            var engagement = squadGo.AddComponent<TeamEngagementMonitor>();

            if (npcPrefab != null)
            {
                // The NPC ally: same body and rules as the player, driven by its AI controller.
                var npcGo = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab);
                npcGo.transform.SetPositionAndRotation(NpcSpawn, Quaternion.identity);
                var npc = npcGo.GetComponent<NpcController>();
                EditorUtil.SetObject(npc, "leader", player.GetComponent<FpsCharacter>());
                EditorUtil.SetObject(npc, "comboSystem", comboSystem);
                EditorUtil.SetObject(npcGo.GetComponent<DeckLoadoutController>(), "engagement", engagement);

                var npcHud = hud.AddComponent<NpcHud>();
                EditorUtil.SetObject(npcHud, "npc", npc);
                EditorUtil.SetObject(npcHud, "player", player.GetComponent<FpsCharacter>());
                EditorUtil.SetObject(npcHud, "comboSystem", comboSystem);
                var editor = hud.AddComponent<NpcDeckEditorHud>();
                EditorUtil.SetObject(editor, "loadout", npcGo.GetComponent<DeckLoadoutController>());
                EditorUtil.SetObject(editor, "npc", npc);
            }

            EditorUtil.EnsureFolder(System.IO.Path.GetDirectoryName(GamePaths.SandboxScene)?.Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, GamePaths.SandboxScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GamePaths.SandboxScene, true) };
        }

        static void BuildLighting(MaterialSet m, VolumeProfile volumeProfile)
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = m.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.5f, 0.68f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.27f, 0.36f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.7f, 0.92f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 180f;

            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = volumeProfile;
        }

        static void BuildArena(Transform parent, MaterialSet m)
        {
            Block(parent, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(90f, 1f, 90f), m.Ground);
            Block(parent, "Wall_North", new Vector3(0f, 3f, 45.5f), new Vector3(92f, 6f, 1f), m.WallLight);
            Block(parent, "Wall_South", new Vector3(0f, 3f, -45.5f), new Vector3(92f, 6f, 1f), m.WallLight);
            Block(parent, "Wall_East", new Vector3(45.5f, 3f, 0f), new Vector3(1f, 6f, 92f), m.WallLight);
            Block(parent, "Wall_West", new Vector3(-45.5f, 3f, 0f), new Vector3(1f, 6f, 92f), m.WallLight);

            // Glowing accent pillars in the corners for the colorful look and bloom check.
            Vector3[] corners = { new Vector3(-42f, 0f, 42f), new Vector3(42f, 0f, 42f), new Vector3(-42f, 0f, -42f), new Vector3(42f, 0f, -42f) };
            for (int i = 0; i < corners.Length; i++)
            {
                Block(parent, "Pillar", corners[i] + new Vector3(0f, 4f, 0f), new Vector3(2f, 8f, 2f), m.WallDark);
                Block(parent, "PillarGlow", corners[i] + new Vector3(0f, 8.2f, 0f), new Vector3(2.2f, 0.4f, 2.2f), i % 2 == 0 ? m.GlowCyan : m.GlowPink);
            }

            // Spawn pad and cover near the spawn.
            Block(parent, "SpawnPad", new Vector3(0f, 0.02f, -30f), new Vector3(4f, 0.04f, 4f), m.GlowCyan);
            Block(parent, "Crate_A", new Vector3(-4f, 0.6f, -20f), new Vector3(1.2f, 1.2f, 1.2f), m.Sun);
            Block(parent, "Crate_B", new Vector3(-4f, 1.8f, -20f), new Vector3(1.2f, 1.2f, 1.2f), m.Coral, new Vector3(0f, 20f, 0f));
            Block(parent, "Crate_C", new Vector3(5f, 0.6f, -18f), new Vector3(1.2f, 1.2f, 1.2f), m.Teal);
            Block(parent, "Cover_Low", new Vector3(0f, 0.55f, -8f), new Vector3(6f, 1.1f, 0.6f), m.Violet);
        }

        static void BuildShootingRange(Transform parent, MaterialSet m, GameObject dummyPrefab)
        {
            var range = new GameObject("ShootingRange").transform;
            range.SetParent(parent, false);

            Block(range, "Lane_Left", new Vector3(-12f, 0.5f, 22f), new Vector3(0.4f, 1f, 36f), m.WallDark);
            Block(range, "Lane_Right", new Vector3(12f, 0.5f, 22f), new Vector3(0.4f, 1f, 36f), m.WallDark);
            Block(range, "Marker_10m", new Vector3(0f, 0.01f, 2f), new Vector3(23f, 0.02f, 0.3f), m.GlowCyan);
            Block(range, "Marker_25m", new Vector3(0f, 0.01f, 17f), new Vector3(23f, 0.02f, 0.3f), m.GlowPink);
            Block(range, "Marker_40m", new Vector3(0f, 0.01f, 32f), new Vector3(23f, 0.02f, 0.3f), m.GlowCyan);

            // Static targets at roughly 12 m, 25 m and 40 m from the cover line.
            Dummy(range, dummyPrefab, new Vector3(-6f, 0f, 4f));
            Dummy(range, dummyPrefab, new Vector3(0f, 0f, 5f));
            Dummy(range, dummyPrefab, new Vector3(6f, 0f, 4f));
            Dummy(range, dummyPrefab, new Vector3(-4f, 0f, 18f));
            Dummy(range, dummyPrefab, new Vector3(4f, 0f, 18f));
            Dummy(range, dummyPrefab, new Vector3(0f, 0f, 34f));

            // Moving targets.
            GameObject moverA = Dummy(range, dummyPrefab, new Vector3(0f, 0f, 11f));
            MakeMover(moverA, new Vector3(16f, 0f, 0f), 4f, 0f);
            GameObject moverB = Dummy(range, dummyPrefab, new Vector3(0f, 0f, 26f));
            MakeMover(moverB, new Vector3(18f, 0f, 0f), 6f, 0.5f);

            // Elevated target to practice vertical aim.
            Block(range, "Tower", new Vector3(9f, 1.5f, 30f), new Vector3(3f, 3f, 3f), m.Violet);
            Dummy(range, dummyPrefab, new Vector3(9f, 3f, 30f));

            // Tight group for area, chain and pull cards (right of the lanes, open ground).
            Block(range, "GroupPad", new Vector3(24f, 0.02f, 14f), new Vector3(9f, 0.04f, 9f), m.GlowPink);
            Vector3[] group =
            {
                new Vector3(22.5f, 0f, 12.5f), new Vector3(25.5f, 0f, 12.5f), new Vector3(24f, 0f, 14f),
                new Vector3(22.5f, 0f, 15.5f), new Vector3(25.5f, 0f, 15.5f),
            };
            foreach (Vector3 position in group)
            {
                Dummy(range, dummyPrefab, position);
            }

            // Hazard pad right of the spawn: stand on it to lose HP and try heal / shield cards.
            Block(range, "HazardPad", new Vector3(8f, 0.02f, -30f), new Vector3(4f, 0.04f, 4f), m.Coral);
            var hazard = new GameObject("HazardZone");
            hazard.transform.SetParent(range, false);
            hazard.transform.position = new Vector3(8f, 0f, -30f);
            hazard.AddComponent<DamageZone>().Configure(new Vector3(4f, 2f, 4f), 6f, 0.5f, CoopDeck.Core.Element.Fire);
        }

        static void BuildMovementCourse(Transform parent, MaterialSet m)
        {
            var course = new GameObject("MovementCourse").transform;
            course.SetParent(parent, false);

            // Stairs: 8 steps of 0.25 m (below the 0.4 m step offset) up to a 2 m platform.
            for (int i = 0; i < 8; i++)
            {
                float height = 0.25f * (i + 1);
                Block(course, $"Step_{i}", new Vector3(-30f, height * 0.5f, -24f + i * 0.5f), new Vector3(4f, height, 0.5f), i % 2 == 0 ? m.Teal : m.WallLight);
            }

            Block(course, "Platform", new Vector3(-30f, 1f, -16f), new Vector3(8f, 2f, 8f), m.Teal);

            // 20° ramp back down from the platform.
            float rampLength = 2f / Mathf.Sin(20f * Mathf.Deg2Rad);
            float rampRun = Mathf.Cos(20f * Mathf.Deg2Rad) * rampLength;
            Block(course, "Ramp_20", new Vector3(-30f, 1f, -12f + rampRun * 0.5f), new Vector3(4f, 0.3f, rampLength), m.Sun, new Vector3(20f, 0f, 0f));

            // Jump boxes: 0.5 m, 1.0 m and 1.4 m (jump height is 1.35 m, so the last needs a run-up or fails).
            Block(course, "JumpBox_050", new Vector3(-20f, 0.25f, -28f), new Vector3(2f, 0.5f, 2f), m.Coral);
            Block(course, "JumpBox_100", new Vector3(-20f, 0.5f, -24f), new Vector3(2f, 1f, 2f), m.Coral);
            Block(course, "JumpBox_140", new Vector3(-20f, 0.7f, -20f), new Vector3(2f, 1.4f, 2f), m.Coral);

            // Low tunnel (2.2 m ceiling) to check head bumps.
            Block(course, "Tunnel_Left", new Vector3(-35f, 1.1f, 2f), new Vector3(0.6f, 2.2f, 8f), m.WallDark);
            Block(course, "Tunnel_Right", new Vector3(-31f, 1.1f, 2f), new Vector3(0.6f, 2.2f, 8f), m.WallDark);
            Block(course, "Tunnel_Roof", new Vector3(-33f, 2.45f, 2f), new Vector3(4.6f, 0.5f, 8f), m.Violet);

            // Pillar field for wall sliding and strafing around corners.
            for (int x = 0; x < 3; x++)
            {
                for (int z = 0; z < 3; z++)
                {
                    Block(course, "Pillar", new Vector3(-24f + x * 4f, 1.5f, 12f + z * 4f), new Vector3(1f, 3f, 1f), (x + z) % 2 == 0 ? m.Coral : m.Sun, new Vector3(0f, 45f * ((x + z) % 2), 0f));
                }
            }
        }

        /// <summary>
        /// North-west combat arena (x −41…−15, z 24…42): cyan border, cover, four spawn points at the
        /// far wall and a <see cref="WaveDirector"/> that starts when the player steps inside.
        /// </summary>
        static WaveDirector BuildCombatArena(Transform parent, MaterialSet m, EnemySet enemies)
        {
            var arena = new GameObject("CombatArena").transform;
            arena.SetParent(parent, false);
            var center = new Vector3(-28f, 0f, 33f);
            var size = new Vector3(26f, 6f, 18f);

            Block(arena, "Border_N", center + new Vector3(0f, 0.03f, size.z * 0.5f), new Vector3(size.x, 0.06f, 0.3f), m.GlowCyan);
            Block(arena, "Border_S", center + new Vector3(0f, 0.03f, -size.z * 0.5f), new Vector3(size.x, 0.06f, 0.3f), m.GlowCyan);
            Block(arena, "Border_W", center + new Vector3(-size.x * 0.5f, 0.03f, 0f), new Vector3(0.3f, 0.06f, size.z), m.GlowCyan);
            Block(arena, "Border_E", center + new Vector3(size.x * 0.5f, 0.03f, 0f), new Vector3(0.3f, 0.06f, size.z), m.GlowCyan);
            Block(arena, "Gate_L", new Vector3(-17f, 1.5f, 24f), new Vector3(0.6f, 3f, 0.6f), m.WallDark);
            Block(arena, "Gate_R", new Vector3(-21f, 1.5f, 24f), new Vector3(0.6f, 3f, 0.6f), m.WallDark);
            Block(arena, "Gate_Top", new Vector3(-19f, 3.2f, 24f), new Vector3(4.6f, 0.4f, 0.6f), m.GlowCyan);

            Block(arena, "Cover_A", new Vector3(-32f, 0.75f, 30f), new Vector3(3f, 1.5f, 0.6f), m.Violet);
            Block(arena, "Cover_B", new Vector3(-23f, 0.75f, 35f), new Vector3(0.6f, 1.5f, 3f), m.Violet);
            Block(arena, "Cover_C", new Vector3(-28f, 1.25f, 38f), new Vector3(2f, 2.5f, 2f), m.WallDark);
            Block(arena, "Cover_D", new Vector3(-36f, 0.5f, 35.5f), new Vector3(1.5f, 1f, 1.5f), m.Sun);

            Vector3[] spawns = { new Vector3(-38f, 0f, 41f), new Vector3(-31f, 0f, 41.5f), new Vector3(-24f, 0f, 41.5f), new Vector3(-17f, 0f, 41f) };
            var points = new Transform[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                var point = new GameObject($"SpawnPoint_{i}").transform;
                point.SetParent(arena, false);
                point.position = spawns[i];
                points[i] = point;
                Block(arena, $"SpawnPad_{i}", spawns[i] + new Vector3(0f, 0.02f, 0f), new Vector3(1.6f, 0.04f, 1.6f), m.GlowPink);
            }

            var directorGo = new GameObject("WaveDirector");
            directorGo.transform.SetParent(arena, false);
            directorGo.transform.position = center;
            var director = directorGo.AddComponent<WaveDirector>();
            var waves = new List<WaveDirector.WaveCounts>
            {
                new WaveDirector.WaveCounts { counts = new List<int> { 3, 0 } },
                new WaveDirector.WaveCounts { counts = new List<int> { 2, 2 } },
                new WaveDirector.WaveCounts { counts = new List<int> { 4, 2 } },
                new WaveDirector.WaveCounts { counts = new List<int> { 3, 4 } },
            };
            director.Configure(new List<GameObject> { enemies.SlasherPrefab, enemies.ShooterPrefab }, waves, points, size);
            return director;
        }

        /// <summary>Bakes walkable areas from the environment colliders (Default layer only) and saves the data.</summary>
        static void BakeNavMesh(Transform environment)
        {
            var surface = environment.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << 0;
            surface.BuildNavMesh();

            AssetDatabase.DeleteAsset(NavMeshDataPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshDataPath);
        }

        static void BuildSlopeTests(Transform parent, MaterialSet m)
        {
            var slopes = new GameObject("SlopeTests").transform;
            slopes.SetParent(parent, false);

            // 15° / 30° / 45° are walkable, 55° exceeds the 50° slope limit and should not be climbable.
            float[] angles = { 15f, 30f, 45f, 55f };
            for (int i = 0; i < angles.Length; i++)
            {
                float angle = angles[i];
                const float rise = 2.5f;
                float length = rise / Mathf.Sin(angle * Mathf.Deg2Rad);
                float run = Mathf.Cos(angle * Mathf.Deg2Rad) * length;
                float x = 24f + i * 5f;
                Material mat = angle > 50f ? m.Coral : m.Teal;
                Block(slopes, $"Slope_{angle:0}", new Vector3(x, rise * 0.5f, -26f + run * 0.5f), new Vector3(3.5f, 0.3f, length), mat, new Vector3(-angle, 0f, 0f));
                Block(slopes, $"SlopeTop_{angle:0}", new Vector3(x, rise - 0.15f, -26f + run + 1.5f), new Vector3(3.5f, 0.3f, 3f), m.WallLight);
            }
        }

        static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size, Material material, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static GameObject Dummy(Transform parent, GameObject prefab, Vector3 position)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            return instance;
        }

        static void MakeMover(GameObject dummy, Vector3 travel, float speed, float phase)
        {
            dummy.name = "TargetDummy_Moving";
            var body = dummy.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            dummy.AddComponent<PatrolMover>().Configure(travel, speed, phase);
        }
    }
}
