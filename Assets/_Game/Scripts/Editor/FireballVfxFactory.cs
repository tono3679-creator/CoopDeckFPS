using System;
using System.Collections.Generic;
using System.IO;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>The generated fireball effects (referenced by the fireball card and the scene).</summary>
    public sealed class FireballVfxSet
    {
        public GameObject Cast;
        public GameObject Projectile;
        public GameObject Explosion;
        public GameObject TargetFlare;
        public GameObject Burning;
        public readonly List<Shader> Shaders = new List<Shader>();
    }

    /// <summary>
    /// Builds the fireball skill's effects from code: meshes (core sphere, comet-shaped flame shell, shockwave
    /// ring, lumpy blast billow, ember chunk, scorch quad), six Shader Graphs whose work is in
    /// Art/VFX/Fireball/Shaders/FireballVfx.hlsl (written by <see cref="ShaderGraphWriter"/>), materials from the
    /// art bible's fire tones, and pooled prefabs for every phase: hand charge, flight, blast, flames clinging
    /// to the enemy hit, and the burn that follows. Optional layers carry a <see cref="VfxQualityGate"/>
    /// (Medium / High); every particle count scales with the VFX quality through <see cref="PooledFx"/>.
    /// Everything is regenerated on each bootstrap; asset GUIDs are kept.
    /// </summary>
    public static class FireballVfxFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/VFX/Fireball";
        public const string MeshFolder = ArtFolder + "/Meshes";
        public const string MaterialFolder = ArtFolder + "/Materials";
        public const string ShaderFolder = ArtFolder + "/Shaders";
        public const string HlslPath = ShaderFolder + "/FireballVfx.hlsl";
        public const string PrefabFolder = GamePaths.Root + "/Prefabs/Skills/Fireball";

        /// <summary>The blast is authored for this radius (the fireball card's area); larger radii grow it.</summary>
        public const float BlastRadius = 2.5f;

        public static readonly string[] GraphNames = { "SG_FX_FireCore", "SG_FX_FlameVolume", "SG_FX_FireParticle", "SG_FX_FireGlow", "SG_FX_Smoke", "SG_FX_Shockwave" };

        sealed class Meshes
        {
            public Mesh Core;
            public Mesh Shell;
            public Mesh Ring;
            public Mesh Billow;
            public Mesh Chunk;
            public Mesh Quad;
        }

        sealed class Materials
        {
            public Material Core;
            public Material CoreBlast;
            public Material Debris;
            public Material FlameInner;
            public Material FlameOuter;
            public Material Billow;
            public Material Flame;
            public Material FlameNear;
            public Material Spark;
            public Material SparkNear;
            public Material Trail;
            public Material TrailCore;
            public Material Smoke;
            public Material Scorch;
            public Material Shockwave;
            public Material CastFlame;
            public Material CastSpark;
            public Material Burn;
        }

        public static FireballVfxSet EnsureAll()
        {
            EditorUtil.EnsureFolder(MeshFolder);
            EditorUtil.EnsureFolder(MaterialFolder);
            EditorUtil.EnsureFolder(PrefabFolder);

            var set = new FireballVfxSet();
            Dictionary<string, Shader> shaders = EnsureShaders();
            set.Shaders.AddRange(shaders.Values);
            Meshes meshes = EnsureMeshes();
            Materials m = EnsureMaterials(shaders);

            set.Cast = Save("FX_Fireball_Cast", () => BuildCast(m));
            set.Projectile = Save("FX_Fireball_Projectile", () => BuildProjectile(m, meshes));
            set.Explosion = Save("FX_Fireball_Explosion", () => BuildExplosion(m, meshes));
            set.TargetFlare = Save("FX_Fireball_TargetFlare", () => BuildTargetFlare(m));
            set.Burning = Save("FX_Burning", () => BuildBurning(m));
            AssetDatabase.SaveAssets();
            return set;
        }

        // ================================================================== shader graphs

        static Dictionary<string, Shader> EnsureShaders()
        {
            AssetDatabase.ImportAsset(HlslPath, ImportAssetOptions.ForceSynchronousImport);
            string hlsl = AssetDatabase.AssetPathToGUID(HlslPath);
            if (string.IsNullOrEmpty(hlsl))
            {
                throw new InvalidOperationException(HlslPath + " is missing");
            }

            const ShaderGraphWriter.Input uv = ShaderGraphWriter.Input.UV0;
            const ShaderGraphWriter.Input color = ShaderGraphWriter.Input.VertexColor;
            const ShaderGraphWriter.Input posOS = ShaderGraphWriter.Input.PositionOS;
            const ShaderGraphWriter.Input posWS = ShaderGraphWriter.Input.PositionWS;
            const ShaderGraphWriter.Input normalOS = ShaderGraphWriter.Input.NormalOS;
            const ShaderGraphWriter.Input normalWS = ShaderGraphWriter.Input.NormalWS;
            const ShaderGraphWriter.Input view = ShaderGraphWriter.Input.ViewDirWS;
            var additive = ShaderGraphWriter.Blend.Additive;
            var alpha = ShaderGraphWriter.Blend.Alpha;
            var front = ShaderGraphWriter.Faces.Front;
            var both = ShaderGraphWriter.Faces.Both;

            var shaders = new Dictionary<string, Shader>();
            shaders["SG_FX_FireCore"] = WriteGraph("SG_FX_FireCore", additive, front,
                w => w.FragmentFunction(hlsl, "FireCore", ("PositionOS", posOS), ("NormalWS", normalWS), ("ViewDirWS", view), ("UV", uv), ("VertexColor", color)));
            shaders["SG_FX_FlameVolume"] = WriteGraph("SG_FX_FlameVolume", alpha, front, w =>
            {
                w.VertexFunction(hlsl, "FlameVolumeVertex", ("PositionOS", posOS), ("NormalOS", normalOS), ("UV", uv));
                w.FragmentFunction(hlsl, "FlameVolume", ("PositionOS", posOS), ("PositionWS", posWS), ("NormalWS", normalWS), ("ViewDirWS", view), ("UV", uv), ("VertexColor", color));
            });
            shaders["SG_FX_FireParticle"] = WriteGraph("SG_FX_FireParticle", alpha, both,
                w => w.FragmentFunction(hlsl, "FireParticle", ("PositionWS", posWS), ("UV", uv), ("VertexColor", color)));
            shaders["SG_FX_FireGlow"] = WriteGraph("SG_FX_FireGlow", additive, both,
                w => w.FragmentFunction(hlsl, "FireParticle", ("PositionWS", posWS), ("UV", uv), ("VertexColor", color)));
            shaders["SG_FX_Smoke"] = WriteGraph("SG_FX_Smoke", ShaderGraphWriter.Blend.Alpha, both,
                w => w.FragmentFunction(hlsl, "Smoke", ("PositionWS", posWS), ("UV", uv), ("VertexColor", color)));
            shaders["SG_FX_Shockwave"] = WriteGraph("SG_FX_Shockwave", alpha, both,
                w => w.FragmentFunction(hlsl, "Shockwave", ("UV", uv), ("VertexColor", color)));
            return shaders;
        }

        static Shader WriteGraph(string name, ShaderGraphWriter.Blend blend, ShaderGraphWriter.Faces faces, Action<ShaderGraphWriter> functions)
        {
            var w = new ShaderGraphWriter(name) { BlendMode = blend, RenderFace = faces };
            // One property set for every graph: the shared HLSL file is compiled whole into each of them, so every
            // property any of its functions reads must be declared (the materials set the ones they use).
            ElementTones fire = Palette.Fire;
            w.ColorProperty("Core Color", "_CoreColor", fire.Core);
            w.ColorProperty("Hot Color", "_HotColor", FireHot);
            w.ColorProperty("Mid Color", "_MidColor", fire.Main);
            w.ColorProperty("Deep Color", "_DeepColor", fire.Deep);
            w.ColorProperty("Smoke Color", "_SmokeColor", Palette.Smoke);
            w.ColorProperty("Glow Color", "_GlowColor", fire.Deep);
            w.FloatProperty("Intensity", "_Intensity", 1.2f, 0f, 6f);
            w.FloatProperty("Core Boost", "_CoreBoost", 3f, 1f, 8f);
            w.FloatProperty("Bands", "_Bands", 0.3f, 0f, 1f);
            w.FloatProperty("Band Count", "_BandCount", 5f, 1f, 8f);
            w.FloatProperty("Noise Scale", "_NoiseScale", 2f, 0.1f, 12f);
            w.FloatProperty("Flow Speed", "_FlowSpeed", 2f, 0f, 12f);
            w.FloatProperty("Swirl", "_Swirl", 2f, -12f, 12f);
            w.FloatProperty("Seed", "_Seed", 0f, 0f, 100f, slider: false);
            w.FloatProperty("Dissolve", "_Dissolve", 0f, 0f, 1f);
            w.FloatProperty("Age Dissolve", "_AgeDissolve", 1f, 0f, 2f);
            w.FloatProperty("Age Heat", "_AgeHeat", 0.8f, 0f, 2f);
            w.FloatProperty("Opacity", "_Opacity", 1f, 0f, 1f);
            w.FloatProperty("Edge Softness", "_EdgeSoftness", 0.12f, 0.01f, 0.5f);
            w.FloatProperty("Mode", "_Mode", 0f, 0f, 3f);
            w.FloatProperty("Pulse Speed", "_PulseSpeed", 9f, 0f, 30f);
            w.FloatProperty("Rim Power", "_RimPower", 1.2f, 0.2f, 6f);
            w.FloatProperty("Displace", "_Displace", 0.15f, 0f, 1f);
            w.FloatProperty("Tail Fade", "_TailFade", 0f, 0f, 1f);
            w.FloatProperty("Heat Bias", "_HeatBias", 0f, -1f, 1f);
            w.FloatProperty("Trail Flip", "_TrailFlip", 0f, 0f, 1f);
            w.FloatProperty("Near Fade", "_NearFade", 0f, 0f, 4f);
            w.Begin();
            functions(w);

            string path = $"{ShaderFolder}/{name}.shadergraph";
            string text = w.Build();
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new InvalidOperationException(path + " did not import as a shader");
            }

            return shader;
        }

        /// <summary>The fire ramp's third stop, between the palette's fire main and core (Docs/ART_BIBLE.md).</summary>
        static Color FireHot => Color.Lerp(Palette.Fire.Main, Palette.Fire.Core, 0.55f);

        // ================================================================== meshes

        static Meshes EnsureMeshes()
        {
            return new Meshes
            {
                Core = SaveMesh("M_Fireball_Core", () => Icosphere(3, 0.5f, null, false)),
                Shell = SaveMesh("M_Fireball_FlameShell", () => FlameShell(36, 10, 24, 2.2f)),
                Ring = SaveMesh("M_Fireball_ShockwaveRing", () => ShockwaveRing(72)),
                Billow = SaveMesh("M_Fireball_Billow", () => Icosphere(2, 0.5f, Lumps(7, 11), false)),
                Chunk = SaveMesh("M_Fireball_EmberChunk", () => Icosphere(0, 0.5f, Jitter(5), true)),
                Quad = SaveMesh("M_Fireball_ScorchQuad", GroundQuad),
            };
        }

        static Mesh SaveMesh(string name, Func<Mesh> build)
        {
            string path = $"{MeshFolder}/{name}.asset";
            Mesh fresh = build();
            fresh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }

            EditorUtility.CopySerialized(fresh, existing);
            existing.name = name;
            UnityEngine.Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Geodesic sphere; <paramref name="radiusAt"/> shapes it per direction, <paramref name="faceted"/> splits vertices for flat facets.</summary>
        static Mesh Icosphere(int subdivisions, float radius, Func<Vector3, float> radiusAt, bool faceted)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var dirs = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < dirs.Count; i++)
            {
                dirs[i] = dirs[i].normalized;
            }

            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>(faces.Count * 4);
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!cache.TryGetValue(key, out int index))
                    {
                        index = dirs.Count;
                        dirs.Add((dirs[a] + dirs[b]).normalized);
                        cache[key] = index;
                    }

                    return index;
                }

                for (int f = 0; f < faces.Count; f += 3)
                {
                    int a = faces[f];
                    int b = faces[f + 1];
                    int c = faces[f + 2];
                    int ab = Mid(a, b);
                    int bc = Mid(b, c);
                    int ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                faces = next;
            }

            var positions = new List<Vector3>(dirs.Count);
            var uvs = new List<Vector2>(dirs.Count);
            foreach (Vector3 d in dirs)
            {
                positions.Add(d * radius * (radiusAt?.Invoke(d) ?? 1f));
                uvs.Add(new Vector2(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f));
            }

            var mesh = new Mesh();
            if (faceted)
            {
                var p = new List<Vector3>(faces.Count);
                var u = new List<Vector2>(faces.Count);
                var tris = new List<int>(faces.Count);
                for (int i = 0; i < faces.Count; i++)
                {
                    p.Add(positions[faces[i]]);
                    u.Add(uvs[faces[i]]);
                    tris.Add(i);
                }

                mesh.SetVertices(p);
                mesh.SetUVs(0, u);
                mesh.SetTriangles(tris, 0);
            }
            else
            {
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(faces, 0);
            }

            FixWinding(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Cauliflower lumps for the blast billows: a few smooth bumps in random directions.</summary>
        static Func<Vector3, float> Lumps(int count, int seed)
        {
            var random = new System.Random(seed);
            var lobes = new Vector3[count];
            var heights = new float[count];
            for (int i = 0; i < count; i++)
            {
                lobes[i] = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f).normalized;
                heights[i] = 0.12f + (float)random.NextDouble() * 0.16f;
            }

            return d =>
            {
                float r = 0.86f;
                for (int i = 0; i < count; i++)
                {
                    float k = Mathf.Max(0f, Vector3.Dot(d, lobes[i]));
                    r += heights[i] * k * k * k * k;
                }

                return r;
            };
        }

        /// <summary>A rough rock for the burning debris.</summary>
        static Func<Vector3, float> Jitter(int seed)
        {
            return d =>
            {
                int h = Mathf.RoundToInt(d.x * 97f) * 73856093 ^ Mathf.RoundToInt(d.y * 97f) * 19349663 ^ Mathf.RoundToInt(d.z * 97f) * 83492791 ^ seed;
                var random = new System.Random(h);
                return 0.62f + (float)random.NextDouble() * 0.4f;
            };
        }

        /// <summary>
        /// The flying flame shell: a round front (radius 0.5 at the origin, +Z = flight direction) drawn out into
        /// a tapering comet tail of <paramref name="tailLength"/> along -Z. UV: x around the axis, y = 0 at the
        /// front tip .. 1 at the tail end (the shader burns the tail away along it).
        /// </summary>
        static Mesh FlameShell(int segments, int frontRings, int tailRings, float tailLength)
        {
            var profile = new List<Vector2>(); // (radius, z)
            for (int i = 0; i <= frontRings; i++)
            {
                float a = i / (float)frontRings * Mathf.PI * 0.5f;
                profile.Add(new Vector2(0.5f * Mathf.Sin(a), 0.5f * Mathf.Cos(a)));
            }

            for (int j = 1; j <= tailRings; j++)
            {
                float u = j / (float)tailRings;
                float r = 0.5f * Mathf.Pow(1f - u, 0.85f) * (1f + 0.12f * Mathf.Sin(u * Mathf.PI));
                profile.Add(new Vector2(r, -u * tailLength));
            }

            var along = new float[profile.Count];
            for (int i = 1; i < profile.Count; i++)
            {
                along[i] = along[i - 1] + Vector2.Distance(profile[i], profile[i - 1]);
            }

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int k = 0; k < profile.Count; k++)
            {
                Vector2 prev = profile[Mathf.Max(0, k - 1)];
                Vector2 next = profile[Mathf.Min(profile.Count - 1, k + 1)];
                Vector2 tangent = (next - prev).normalized; // (dr, dz)
                var normal2 = new Vector2(-tangent.y, tangent.x); // outward: (radial, axial)
                if (k == 0)
                {
                    normal2 = new Vector2(0f, 1f);
                }
                else if (k == profile.Count - 1)
                {
                    normal2 = new Vector2(0f, -1f);
                }

                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    float c = Mathf.Cos(angle);
                    float sn = Mathf.Sin(angle);
                    positions.Add(new Vector3(profile[k].x * c, profile[k].x * sn, profile[k].y));
                    normals.Add(new Vector3(normal2.x * c, normal2.x * sn, normal2.y).normalized);
                    uvs.Add(new Vector2(s / (float)segments, along[k] / along[profile.Count - 1]));
                }
            }

            var tris = new List<int>();
            int row = segments + 1;
            for (int k = 0; k < profile.Count - 1; k++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = k * row + s;
                    int b = a + row;
                    tris.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            FixWinding(mesh, normals);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat ring, outer radius 1, inner 0.5, the inside lifted a little (a shallow bowl reads as 3D). UV: x around, y inner → outer.</summary>
        static Mesh ShockwaveRing(int segments)
        {
            float[] radii = { 0.5f, 0.72f, 1f };
            float[] heights = { 0.07f, 0.03f, 0f };
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int r = 0; r < radii.Length; r++)
            {
                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    positions.Add(new Vector3(Mathf.Cos(angle) * radii[r], heights[r], Mathf.Sin(angle) * radii[r]));
                    uvs.Add(new Vector2(s / (float)segments, r / (float)(radii.Length - 1)));
                }
            }

            var tris = new List<int>();
            int row = segments + 1;
            for (int r = 0; r < radii.Length - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s;
                    int b = a + row;
                    tris.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var up = new List<Vector3>();
            for (int i = 0; i < positions.Count; i++)
            {
                up.Add(Vector3.up);
            }

            mesh.SetNormals(up);
            FixWinding(mesh, up);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A 1 m quad on the XZ plane facing up (the scorch mark).</summary>
        static Mesh GroundQuad()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            FixWinding(mesh, new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Makes every triangle face outward (Unity: clockwise seen from the front). Against the given normals,
        /// or away from the centre for closed shapes.
        /// </summary>
        static void FixWinding(Mesh mesh, List<Vector3> normals = null)
        {
            Vector3[] v = mesh.vertices;
            int[] tris = mesh.triangles;
            Vector3 center = mesh.bounds.center;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]];
                Vector3 b = v[tris[i + 1]];
                Vector3 c = v[tris[i + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                if (faceNormal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                Vector3 outward = normals != null
                    ? normals[tris[i]] + normals[tris[i + 1]] + normals[tris[i + 2]]
                    : (a + b + c) / 3f - center;
                if (Vector3.Dot(faceNormal, outward) < 0f)
                {
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                }
            }

            mesh.triangles = tris;
        }

        // ================================================================== materials

        static Materials EnsureMaterials(Dictionary<string, Shader> s)
        {
            Shader core = s["SG_FX_FireCore"];
            Shader volume = s["SG_FX_FlameVolume"];   // alpha blended
            Shader particle = s["SG_FX_FireParticle"]; // alpha blended
            Shader glow = s["SG_FX_FireGlow"];         // additive
            Shader smoke = s["SG_FX_Smoke"];
            Shader wave = s["SG_FX_Shockwave"];
            return new Materials
            {
                // Additive, white-hot: the only parts that add light (with the sparks).
                Core = Mat("M_FX_Fireball_Core", core, m => Set(m, ("_Mode", 0f), ("_Intensity", 1f), ("_CoreBoost", 2.6f), ("_NoiseScale", 3.4f),
                    ("_FlowSpeed", 1.8f), ("_Swirl", 3.5f), ("_PulseSpeed", 12f), ("_RimPower", 1.1f), ("_AgeDissolve", 0f))),
                Debris = Mat("M_FX_Fireball_Debris", core, m => Set(m, ("_Mode", 1f), ("_Intensity", 1.1f), ("_CoreBoost", 2f), ("_NoiseScale", 9f))),

                // Alpha blended flame bodies: orange and red stay saturated over sky and sand.
                FlameInner = Mat("M_FX_Fireball_FlameInner", volume, m => Set(m, ("_TailFade", 1f), ("_Displace", 0.14f), ("_NoiseScale", 3.2f),
                    ("_FlowSpeed", 3.6f), ("_Swirl", 4.5f), ("_RimPower", 1f), ("_HeatBias", 0.3f), ("_EdgeSoftness", 0.07f),
                    ("_Intensity", 1f), ("_CoreBoost", 2.1f), ("_AgeDissolve", 0f), ("_Bands", 0.35f))),
                FlameOuter = Mat("M_FX_Fireball_FlameOuter", volume, m => Set(m, ("_TailFade", 1f), ("_Displace", 0.24f), ("_NoiseScale", 2.4f),
                    ("_FlowSpeed", 2.8f), ("_Swirl", -3f), ("_RimPower", 1.4f), ("_HeatBias", -0.04f), ("_EdgeSoftness", 0.06f),
                    ("_Intensity", 1f), ("_CoreBoost", 1.5f), ("_Opacity", 0.92f), ("_AgeDissolve", 0f), ("_Dissolve", 0.16f), ("_Bands", 0.35f))),
                CoreBlast = Mat("M_FX_Fireball_BlastCore", volume, m => Set(m, ("_TailFade", 0f), ("_Displace", 0.2f), ("_NoiseScale", 2.2f),
                    ("_FlowSpeed", 2.4f), ("_Swirl", 2f), ("_RimPower", 0.7f), ("_HeatBias", 0.35f), ("_EdgeSoftness", 0.1f),
                    ("_Intensity", 1f), ("_CoreBoost", 2.4f), ("_AgeDissolve", 1.25f), ("_Bands", 0.45f), ("_NearFade", 1.4f))),
                Billow = Mat("M_FX_Fireball_Billow", volume, m => Set(m, ("_TailFade", 0f), ("_Displace", 0.18f), ("_NoiseScale", 1.6f),
                    ("_FlowSpeed", 1.6f), ("_Swirl", 1.2f), ("_RimPower", 0.9f), ("_HeatBias", 0.14f), ("_EdgeSoftness", 0.1f),
                    ("_Intensity", 1f), ("_CoreBoost", 2f), ("_AgeDissolve", 1.1f), ("_NearFade", 1.4f), ("_Bands", 0.45f))),
                Flame = Mat("M_FX_Fireball_Flame", particle, m => Set(m, ("_Mode", 0f), ("_NoiseScale", 2.2f), ("_FlowSpeed", 2.8f), ("_AgeHeat", 0.9f),
                    ("_EdgeSoftness", 0.09f), ("_Intensity", 1f), ("_CoreBoost", 1.9f), ("_NearFade", 1.2f), ("_Bands", 0.45f))),
                FlameNear = Mat("M_FX_Fireball_FlameTrail", particle, m => Set(m, ("_Mode", 0f), ("_NoiseScale", 2.2f), ("_FlowSpeed", 3.2f), ("_AgeHeat", 1f),
                    ("_EdgeSoftness", 0.09f), ("_Intensity", 1f), ("_CoreBoost", 1.7f), ("_NearFade", 2f), ("_Bands", 0.45f))),
                Trail = Mat("M_FX_Fireball_Trail", particle, m => Set(m, ("_Mode", 2f), ("_NoiseScale", 1.6f), ("_FlowSpeed", 6f), ("_EdgeSoftness", 0.1f),
                    ("_Intensity", 1f), ("_CoreBoost", 1.6f), ("_NearFade", 0.8f), ("_Bands", 0.45f))),
                Shockwave = Mat("M_FX_Fireball_Shockwave", wave, m => Set(m, ("_NoiseScale", 3f), ("_FlowSpeed", 2f), ("_AgeDissolve", 0.9f),
                    ("_EdgeSoftness", 0.1f), ("_Intensity", 1f), ("_CoreBoost", 1.8f), ("_HeatBias", 0.1f), ("_Bands", 0.45f))),
                // Hotter than the blast flames: a burning enemy is often red itself, the licks must read against it.
                Burn = Mat("M_FX_Burning_Flame", particle, m => Set(m, ("_Mode", 0f), ("_NoiseScale", 2.4f), ("_FlowSpeed", 3.4f), ("_AgeHeat", 0.7f),
                    ("_EdgeSoftness", 0.09f), ("_Intensity", 1f), ("_CoreBoost", 2f), ("_HeatBias", 0.22f), ("_NearFade", 1f), ("_Bands", 0.45f))),
                CastFlame = Mat("M_FX_Fireball_CastFlame", particle, m => Set(m, ("_Mode", 0f), ("_NoiseScale", 2f), ("_FlowSpeed", 4f), ("_AgeHeat", 0.5f),
                    ("_EdgeSoftness", 0.1f), ("_Intensity", 1f), ("_CoreBoost", 1.6f), ("_HeatBias", 0.1f), ("_Bands", 0.45f))),

                // Additive sparks and glow.
                Spark = Mat("M_FX_Fireball_Spark", glow, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 1f), ("_EdgeSoftness", 0.3f), ("_Intensity", 1.1f),
                    ("_CoreBoost", 2.4f), ("_NearFade", 0.6f))),
                SparkNear = Mat("M_FX_Fireball_SparkTrail", glow, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 1.1f), ("_EdgeSoftness", 0.3f), ("_Intensity", 1f),
                    ("_CoreBoost", 2.2f), ("_NearFade", 0.5f))),
                TrailCore = Mat("M_FX_Fireball_TrailCore", glow, m => Set(m, ("_Mode", 2f), ("_NoiseScale", 1.2f), ("_FlowSpeed", 8f), ("_EdgeSoftness", 0.1f),
                    ("_Intensity", 0.9f), ("_CoreBoost", 2.4f), ("_NearFade", 0.8f), ("_HeatBias", 0.1f))),
                // The hand charge sits 0.4 m from the eye: modest glow, no near fade (it would hide it).
                CastSpark = Mat("M_FX_Fireball_CastSpark", glow, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 0.6f), ("_EdgeSoftness", 0.3f), ("_Intensity", 1f),
                    ("_CoreBoost", 2f))),

                Smoke = Mat("M_FX_Fireball_Smoke", smoke, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.5f), ("_NoiseScale", 2f), ("_FlowSpeed", 0.6f),
                    ("_EdgeSoftness", 0.25f), ("_NearFade", 1.5f))),
                Scorch = Mat("M_FX_Fireball_Scorch", smoke, m => Set(m, ("_Mode", 1f), ("_Opacity", 0.75f), ("_NoiseScale", 3.5f), ("_EdgeSoftness", 0.2f))),
            };
        }

        static Material Mat(string name, Shader shader, Action<Material> configure)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            ElementTones fire = Palette.Fire;
            material.SetColor("_CoreColor", fire.Core);
            material.SetColor("_HotColor", FireHot);
            material.SetColor("_MidColor", fire.Main);
            material.SetColor("_DeepColor", fire.Deep);
            material.SetColor("_SmokeColor", Palette.Smoke);
            material.SetColor("_GlowColor", fire.Deep);
            Set(material, ("_Intensity", 1.2f), ("_CoreBoost", 3f), ("_Bands", 0.3f), ("_BandCount", 5f), ("_NoiseScale", 2f), ("_FlowSpeed", 2f),
                ("_Swirl", 2f), ("_Seed", 0f), ("_Dissolve", 0f), ("_AgeDissolve", 1f), ("_AgeHeat", 0.8f), ("_Opacity", 1f), ("_EdgeSoftness", 0.12f),
                ("_Mode", 0f), ("_PulseSpeed", 9f), ("_RimPower", 1.2f), ("_Displace", 0.15f), ("_TailFade", 0f), ("_HeatBias", 0f),
                ("_TrailFlip", 0f), ("_NearFade", 0f));
            configure(material);
            material.enableInstancing = false;
            // URP's automatic queue follows the graph shader's queue, which reads as Geometry here; effects must draw
            // after the opaque world and the sky, so the queue is set by hand (_QueueControl 1 = user override; order
            // among the effects comes from the renderers' sorting fudge).
            material.SetFloat("_QueueControl", 1f);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Set(Material m, params (string name, float value)[] values)
        {
            foreach ((string name, float value) in values)
            {
                m.SetFloat(name, value);
            }
        }

        // ================================================================== prefabs

        static GameObject Save(string name, Func<GameObject> build)
        {
            GameObject go = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Root(string name, float lifetime)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = lifetime;
            return root;
        }

        static Transform Child(Transform parent, string name, Vector3 position = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        static Transform Gate(Transform parent, string name, VfxQuality minimum)
        {
            Transform t = Child(parent, name);
            t.gameObject.AddComponent<VfxQualityGate>().MinimumQuality = minimum;
            return t;
        }

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.AgePercent,
        };

        static ParticleSystem Ps(Transform parent, string name, Material material, (float min, float max) life, (float min, float max) speed,
            (float min, float max) size, int maxParticles, bool world = true, Vector3 position = default)
        {
            Transform t = Child(parent, name, position);
            var ps = t.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = t.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.5f;
            renderer.SetActiveVertexStreams(Streams);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.min, life.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = Color.white; // the fire colours come from the material's ramp (palette tones)
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // Particles erode away with age in the shader; vertex alpha only fades them in (no pop at birth).
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Alpha((0f, 0f), (1f, 0.06f), (1f, 0.85f), (0.6f, 1f)));
            return ps;
        }

        static Gradient Alpha(params (float alpha, float time)[] keys)
        {
            var g = new Gradient();
            var alphas = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                alphas[i] = new GradientAlphaKey(keys[i].alpha, keys[i].time);
            }

            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            return g;
        }

        static void Burst(ParticleSystem ps, short count, float time = 0f)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(time, count) });
        }

        static void Rate(ParticleSystem ps, float overTime, float overDistance = 0f, float duration = 0.2f, bool loop = false)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = overTime;
            emission.rateOverDistance = overDistance;
            ParticleSystem.MainModule main = ps.main;
            main.duration = duration;
            main.loop = loop;
        }

        static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle = 0f, Vector3 rotation = default,
            float thickness = 1f, Vector3 scale = default, Vector3 position = default)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
            shape.radiusThickness = thickness;
            shape.rotation = rotation;
            shape.position = position;
            shape.scale = scale == default ? Vector3.one : scale;
        }

        static void SizeOverLife(ParticleSystem ps, params Keyframe[] keys)
        {
            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        static void Velocity(ParticleSystem ps, Vector3 linear, Vector3 linearSpread = default, float orbitalY = 0f, float orbitalZ = 0f, float radial = 0f, bool local = true)
        {
            ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
            v.enabled = true;
            v.space = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(linear.x - linearSpread.x, linear.x + linearSpread.x);
            v.y = new ParticleSystem.MinMaxCurve(linear.y - linearSpread.y, linear.y + linearSpread.y);
            v.z = new ParticleSystem.MinMaxCurve(linear.z - linearSpread.z, linear.z + linearSpread.z);
            v.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            v.orbitalY = new ParticleSystem.MinMaxCurve(orbitalY, orbitalY);
            v.orbitalZ = new ParticleSystem.MinMaxCurve(orbitalZ, orbitalZ);
            v.radial = new ParticleSystem.MinMaxCurve(radial, radial);
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
        }

        static void Spin(ParticleSystem ps, float startDegrees, float spinDegrees)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-startDegrees * Mathf.Deg2Rad, startDegrees * Mathf.Deg2Rad);
            if (spinDegrees > 0f)
            {
                ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-spinDegrees * Mathf.Deg2Rad, spinDegrees * Mathf.Deg2Rad);
            }
        }

        static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.damping = true;
        }

        static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale = 1f)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
        }

        static void MeshParticles(ParticleSystem ps, Mesh mesh, bool alignLocal, bool randomRotation)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = alignLocal ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.World;
            renderer.maxParticleSize = 100f;
            if (randomRotation)
            {
                ParticleSystem.MainModule main = ps.main;
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            }
        }

        static void Order(ParticleSystem ps, float sortingFudge)
        {
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = sortingFudge;
        }

        static void Gravity(ParticleSystem ps, float g)
        {
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = g;
        }

        static void Delay(ParticleSystem ps, float seconds)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startDelay = seconds;
        }

        static MeshRenderer MeshLayer(Transform parent, string name, Mesh mesh, Material material, float scale, Quaternion rotation = default)
        {
            Transform t = Child(parent, name);
            t.localScale = Vector3.one * scale;
            t.localRotation = rotation == default ? Quaternion.identity : rotation;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        static TrailRenderer Trail(Transform parent, string name, Material material, float time, float width)
        {
            Transform t = Child(parent, name);
            var trail = t.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = time;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.8f), new Keyframe(1f, 0f));
            trail.colorGradient = Alpha((1f, 0f), (0.9f, 0.5f), (0f, 1f));
            trail.minVertexDistance = 0.12f;
            trail.numCapVertices = 2;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            trail.emitting = true;
            return trail;
        }

        static Light PointLight(Transform parent, string name, float range, float intensity)
        {
            Transform t = Child(parent, name);
            var light = t.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Palette.Fire.Main;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        static readonly Vector3 Up = new Vector3(-90f, 0f, 0f); // shape rotation: emit along +Y (the surface normal) instead of +Z

        // ------------------------------------------------------------------ phase A: the charge in the hand

        /// <summary>
        /// 0.00-0.12 s, parented to the casting palm (drawn by the view-model camera for the local player):
        /// sparks drawn inward, small flames wrapping around a white-hot point that swells, then a spray of
        /// sparks as the fireball leaves. Small and only lightly bloomed, so the hand never flares the view.
        /// </summary>
        static GameObject BuildCast(Materials m)
        {
            GameObject root = Root("FX_Fireball_Cast", 0.45f);
            Transform r = root.transform;

            ParticleSystem core = Ps(r, "Core", m.CastSpark, (0.17f, 0.17f), (0f, 0f), (0.075f, 0.075f), 2, world: false);
            Burst(core, 1);
            SizeOverLife(core, new Keyframe(0f, 0.25f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0.7f));

            ParticleSystem swirl = Ps(r, "Swirl", m.CastFlame, (0.11f, 0.17f), (0f, 0f), (0.045f, 0.065f), 24, world: false);
            Rate(swirl, 90f, 0f, 0.12f);
            Shape(swirl, ParticleSystemShapeType.Circle, 0.06f, thickness: 0f);
            Velocity(swirl, Vector3.zero, default, 0f, 22f, -0.35f);
            Spin(swirl, 180f, 0f);

            ParticleSystem gather = Ps(r, "Gather", m.CastSpark, (0.1f, 0.12f), (-1f, -0.6f), (0.006f, 0.012f), 24, world: false);
            Burst(gather, 10);
            Shape(gather, ParticleSystemShapeType.Sphere, 0.1f, thickness: 0f);
            Stretch(gather, 0.04f);

            ParticleSystem release = Ps(r, "Release", m.CastSpark, (0.08f, 0.16f), (0.7f, 1.6f), (0.006f, 0.012f), 16, world: false);
            Burst(release, 8);
            Delay(release, 0.1f);
            Shape(release, ParticleSystemShapeType.Cone, 0.02f, 50f);
            Stretch(release, 0.04f);
            return root;
        }

        // ------------------------------------------------------------------ phase B: the flight

        static GameObject BuildProjectile(Materials m, Meshes meshes)
        {
            GameObject root = Root("FX_Fireball_Projectile", 6f); // released by FireballProjectileFx after the shot ends
            Transform r = root.transform;
            Transform body = Child(r, "Body");

            MeshRenderer core = MeshLayer(body, "Core", meshes.Core, m.Core, 0.17f);
            MeshRenderer inner = MeshLayer(body, "FlameInner", meshes.Shell, m.FlameInner, 0.3f);
            Transform outerLayer = Gate(body, "FlameOuterLayer", VfxQuality.Medium);
            MeshRenderer outer = MeshLayer(outerLayer, "FlameOuter", meshes.Shell, m.FlameOuter, 0.44f, Quaternion.Euler(0f, 0f, 180f));

            TrailRenderer trail = Trail(r, "Trail", m.Trail, 0.1f, 0.22f);
            Transform trailCoreLayer = Gate(r, "TrailCoreLayer", VfxQuality.Medium);
            TrailRenderer trailCore = Trail(trailCoreLayer, "TrailCore", m.TrailCore, 0.06f, 0.07f);

            // Flames and embers emitted per metre in world space stay behind the shot: the fire streams backward.
            ParticleSystem flames = Ps(r, "Flames", m.FlameNear, (0.18f, 0.32f), (0.2f, 0.8f), (0.13f, 0.24f), 260);
            Rate(flames, 0f, 9f, 1f, loop: true);
            Shape(flames, ParticleSystemShapeType.Sphere, 0.08f);
            Gravity(flames, -0.35f);
            SizeOverLife(flames, new Keyframe(0f, 0.7f), new Keyframe(0.5f, 1.1f), new Keyframe(1f, 0.85f));
            Spin(flames, 14f, 0f);

            ParticleSystem embers = Ps(r, "Embers", m.SparkNear, (0.3f, 0.65f), (0.8f, 2.6f), (0.024f, 0.05f), 180);
            Rate(embers, 0f, 3f, 1f, loop: true);
            Shape(embers, ParticleSystemShapeType.Sphere, 0.15f);
            Gravity(embers, 0.25f);
            Noise(embers, 0.6f, 1.4f);
            Stretch(embers, 0.03f);

            Transform smokeLayer = Gate(r, "SmokeLayer", VfxQuality.High);
            ParticleSystem smoke = Ps(smokeLayer, "Smoke", m.Smoke, (0.45f, 0.7f), (0.1f, 0.4f), (0.25f, 0.45f), 60);
            Rate(smoke, 0f, 1.2f, 1f, loop: true);
            Shape(smoke, ParticleSystemShapeType.Sphere, 0.15f);
            Gravity(smoke, -0.1f);
            SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f));
            Order(smoke, 30f);

            // Launch puff: flames pushed back and sparks thrown out where it leaves the hand.
            ParticleSystem launchFlames = Ps(r, "LaunchFlames", m.FlameNear, (0.12f, 0.22f), (1.5f, 3.5f), (0.14f, 0.28f), 12);
            Burst(launchFlames, 6);
            Shape(launchFlames, ParticleSystemShapeType.Cone, 0.05f, 40f, new Vector3(0f, 180f, 0f));
            ParticleSystem launchSparks = Ps(r, "LaunchSparks", m.SparkNear, (0.1f, 0.22f), (3f, 7f), (0.012f, 0.025f), 16);
            Burst(launchSparks, 10);
            Shape(launchSparks, ParticleSystemShapeType.Sphere, 0.05f);
            Stretch(launchSparks, 0.03f);

            ParticleSystem fizzle = Ps(r, "Fizzle", m.Smoke, (0.35f, 0.6f), (0.3f, 1f), (0.25f, 0.5f), 12);
            Burst(fizzle, 5);
            Shape(fizzle, ParticleSystemShapeType.Sphere, 0.15f);
            SizeOverLife(fizzle, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f));

            Transform glowLayer = Gate(r, "GlowLayer", VfxQuality.High);
            Light glow = PointLight(glowLayer, "Glow", 5f, 2.4f);

            var fx = root.AddComponent<FireballProjectileFx>();
            EditorUtil.SetObject(fx, "body", body);
            EditorUtil.SetObjectArray(fx, "meshes", new UnityEngine.Object[] { core, inner, outer });
            EditorUtil.SetObjectArray(fx, "emitters", new UnityEngine.Object[] { flames, embers, smoke });
            EditorUtil.SetObjectArray(fx, "trails", new UnityEngine.Object[] { trail, trailCore });
            EditorUtil.SetObject(fx, "fizzle", fizzle);
            EditorUtil.SetObject(fx, "glow", glow);
            return root;
        }

        // ------------------------------------------------------------------ phases C and D: the blast and what it leaves

        /// <summary>
        /// Up = the surface normal (set by FireballExplosionFx). 0.00-0.40 s: a white-yellow flash, a fireball
        /// that swells, lumpy 3D billows bursting out and rising while they cool and burn away, flame tongues,
        /// a shockwave ring along the surface, sparks and burning debris. 0.40-1.20 s: embers drifting up,
        /// glowing chunks falling and bouncing, thin smoke thinning out, and a scorch mark on the ground.
        /// </summary>
        static GameObject BuildExplosion(Materials m, Meshes meshes)
        {
            GameObject root = Root("FX_Fireball_Explosion", 2.2f);
            Transform r = root.transform;

            ParticleSystem flash = Ps(r, "Flash", m.Spark, (0.08f, 0.1f), (0f, 0f), (2.6f, 2.6f), 2, position: new Vector3(0f, 0.3f, 0f));
            Burst(flash, 1);
            flash.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.45f;
            Order(flash, -10f);

            Transform scaled = Child(r, "RadiusLayers");

            ParticleSystem fireball = Ps(scaled, "Fireball", m.CoreBlast, (0.26f, 0.3f), (0f, 0f), (1.5f, 1.5f), 2, position: new Vector3(0f, 0.35f, 0f));
            Burst(fireball, 1);
            MeshParticles(fireball, meshes.Billow, false, true);
            SizeOverLife(fireball, new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.15f));

            ParticleSystem billows = Ps(scaled, "Billows", m.Billow, (0.45f, 0.8f), (2.5f, 6f), (0.5f, 0.9f), 16, position: new Vector3(0f, 0.25f, 0f));
            Burst(billows, 9);
            MeshParticles(billows, meshes.Billow, false, true);
            Shape(billows, ParticleSystemShapeType.Hemisphere, 0.35f, rotation: Up);
            Drag(billows, 3.5f);
            Gravity(billows, -0.35f);
            SizeOverLife(billows, new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.15f), new Keyframe(1f, 1.5f));

            ParticleSystem tongues = Ps(scaled, "Flames", m.Flame, (0.3f, 0.55f), (2.5f, 6.5f), (0.45f, 0.85f), 32, position: new Vector3(0f, 0.2f, 0f));
            Burst(tongues, 14);
            Shape(tongues, ParticleSystemShapeType.Hemisphere, 0.4f, rotation: Up);
            Drag(tongues, 3f);
            Gravity(tongues, -0.7f);
            SizeOverLife(tongues, new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.1f), new Keyframe(1f, 1.25f));
            Spin(tongues, 12f, 0f);

            ParticleSystem wave = Ps(scaled, "Shockwave", m.Shockwave, (0.3f, 0.3f), (0f, 0f), (2.7f, 2.7f), 2, world: false, position: new Vector3(0f, 0.05f, 0f));
            Burst(wave, 1);
            MeshParticles(wave, meshes.Ring, true, false);
            SizeOverLife(wave, new Keyframe(0f, 0.15f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));

            Transform waveLayer = Gate(scaled, "Shockwave2Layer", VfxQuality.Medium);
            ParticleSystem wave2 = Ps(waveLayer, "Shockwave2", m.Shockwave, (0.26f, 0.26f), (0f, 0f), (2f, 2f), 2, world: false, position: new Vector3(0f, 0.08f, 0f));
            Burst(wave2, 1);
            Delay(wave2, 0.06f);
            MeshParticles(wave2, meshes.Ring, true, false);
            SizeOverLife(wave2, new Keyframe(0f, 0.2f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));

            ParticleSystem sparks = Ps(r, "Sparks", m.Spark, (0.25f, 0.55f), (7f, 15f), (0.025f, 0.05f), 64, position: new Vector3(0f, 0.25f, 0f));
            Burst(sparks, 36);
            Shape(sparks, ParticleSystemShapeType.Hemisphere, 0.2f, rotation: Up);
            Gravity(sparks, 0.9f);
            Stretch(sparks, 0.035f);

            ParticleSystem debris = Ps(r, "Debris", m.Debris, (0.9f, 1.3f), (3.5f, 7.5f), (0.06f, 0.12f), 20, position: new Vector3(0f, 0.2f, 0f));
            Burst(debris, 10);
            MeshParticles(debris, meshes.Chunk, false, true);
            Shape(debris, ParticleSystemShapeType.Cone, 0.25f, 55f, Up);
            Gravity(debris, 1.6f);
            ParticleSystem.RotationOverLifetimeModule tumble = debris.rotationOverLifetime;
            tumble.enabled = true;
            tumble.separateAxes = true;
            tumble.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            tumble.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            tumble.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            ParticleSystem.CollisionModule bounce = debris.collision;
            bounce.enabled = true;
            bounce.type = ParticleSystemCollisionType.World;
            bounce.mode = ParticleSystemCollisionMode.Collision3D;
            bounce.quality = ParticleSystemCollisionQuality.High;
            bounce.collidesWith = GameLayers.MovementBlockingMask & ~(1 << GameLayers.EnemyIndex);
            bounce.dampen = 0.45f;
            bounce.bounce = 0.3f;
            bounce.lifetimeLoss = 0f;
            bounce.radiusScale = 0.5f;

            ParticleSystem linger = Ps(r, "LingerEmbers", m.Spark, (0.6f, 1.2f), (0.2f, 0.9f), (0.018f, 0.038f), 40, position: new Vector3(0f, 0.5f, 0f));
            Rate(linger, 30f, 0f, 0.6f);
            Delay(linger, 0.15f);
            Shape(linger, ParticleSystemShapeType.Sphere, 0.9f);
            Gravity(linger, -0.25f);
            Noise(linger, 0.8f, 1.2f);

            ParticleSystem smoke = Ps(r, "Smoke", m.Smoke, (0.9f, 1.4f), (0.4f, 1.2f), (0.9f, 1.5f), 12, position: new Vector3(0f, 0.4f, 0f));
            Burst(smoke, 6);
            Delay(smoke, 0.06f);
            Shape(smoke, ParticleSystemShapeType.Hemisphere, 0.5f, rotation: Up);
            Gravity(smoke, -0.12f);
            Drag(smoke, 1.5f);
            SizeOverLife(smoke, new Keyframe(0f, 0.7f), new Keyframe(1f, 1.8f));
            Spin(smoke, 180f, 30f);
            Order(smoke, 30f);

            Transform wispLayer = Gate(r, "SmokeWispsLayer", VfxQuality.High);
            ParticleSystem wisps = Ps(wispLayer, "SmokeWisps", m.Smoke, (0.8f, 1.2f), (0.3f, 0.8f), (0.35f, 0.6f), 12, position: new Vector3(0f, 0.8f, 0f));
            Burst(wisps, 6);
            Delay(wisps, 0.2f);
            Shape(wisps, ParticleSystemShapeType.Sphere, 0.8f);
            Gravity(wisps, -0.2f);
            Noise(wisps, 0.5f, 0.8f);
            SizeOverLife(wisps, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f));
            Order(wisps, 30f);

            ParticleSystem scorch = Ps(r, "Scorch", m.Scorch, (1.8f, 1.8f), (0f, 0f), (2.8f, 2.8f), 2, world: false, position: new Vector3(0f, 0.03f, 0f));
            Burst(scorch, 1);
            MeshParticles(scorch, meshes.Quad, true, false);
            ParticleSystem.MainModule scorchMain = scorch.main;
            scorchMain.startRotation3D = true;
            scorchMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, 0f);
            scorchMain.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            scorchMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            Order(scorch, 60f);

            Transform lightLayer = Gate(r, "FlashLightLayer", VfxQuality.High);
            Light light = PointLight(lightLayer, "FlashLight", 9f, 0f);
            light.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var lightFlash = light.gameObject.AddComponent<LightFlash>();
            EditorUtil.SetFloat(lightFlash, "duration", 0.32f);
            EditorUtil.SetFloat(lightFlash, "peakIntensity", 8f);

            var fx = root.AddComponent<FireballExplosionFx>();
            EditorUtil.SetFloat(fx, "authoredRadius", BlastRadius);
            EditorUtil.SetObjectArray(fx, "radiusLayers", new UnityEngine.Object[] { scaled });
            EditorUtil.SetObjectArray(fx, "surfaceOnly", new UnityEngine.Object[] { scorch });
            EditorUtil.SetObjectArray(fx, "rings", new UnityEngine.Object[] { wave.transform, wave2.transform });
            return root;
        }

        // ------------------------------------------------------------------ on the enemy hit

        /// <summary>Played at the enemy's chest and parented to it: flames spiral up around the body for half a second.</summary>
        static GameObject BuildTargetFlare(Materials m)
        {
            GameObject root = Root("FX_Fireball_TargetFlare", 0.9f);
            Transform r = root.transform;

            ParticleSystem wrap = Ps(r, "WrapFlames", m.Flame, (0.4f, 0.6f), (0f, 0f), (0.22f, 0.4f), 32, world: false);
            Rate(wrap, 60f, 0f, 0.25f);
            Shape(wrap, ParticleSystemShapeType.Circle, 0.42f, rotation: new Vector3(90f, 0f, 0f), thickness: 0.2f, position: new Vector3(0f, -0.7f, 0f));
            Velocity(wrap, new Vector3(0f, 2.2f, 0f), new Vector3(0f, 0.4f, 0f), 7f, 0f, -0.25f);
            SizeOverLife(wrap, new Keyframe(0f, 0.7f), new Keyframe(0.5f, 1.1f), new Keyframe(1f, 0.8f));

            ParticleSystem sparks = Ps(r, "WrapSparks", m.Spark, (0.3f, 0.5f), (0f, 0f), (0.02f, 0.04f), 24, world: false);
            Burst(sparks, 12);
            Shape(sparks, ParticleSystemShapeType.Circle, 0.45f, rotation: new Vector3(90f, 0f, 0f), thickness: 0.2f, position: new Vector3(0f, -0.4f, 0f));
            Velocity(sparks, new Vector3(0f, 3f, 0f), new Vector3(0.4f, 0.8f, 0.4f), 7f, 0f, 0.4f);
            Stretch(sparks, 0.04f);
            return root;
        }

        /// <summary>Looping while a fire damage over time ticks (ElementAuraPresenter): small flames all over the body, embers, a little smoke.</summary>
        static GameObject BuildBurning(Materials m)
        {
            GameObject root = Root("FX_Burning", 3600f);
            Transform r = root.transform;

            // Emitted on the surface of a body-sized cylinder, so the licks show outside the character, not inside it.
            ParticleSystem flames = Ps(r, "Flames", m.Burn, (0.4f, 0.6f), (0f, 0f), (0.26f, 0.44f), 48);
            Rate(flames, 32f, 0f, 1f, loop: true);
            Shape(flames, ParticleSystemShapeType.ConeVolume, 0.5f, 0f, Up, thickness: 0f, position: new Vector3(0f, 0.25f, 0f));
            ParticleSystem.ShapeModule flameShape = flames.shape;
            flameShape.length = 1.35f;
            Velocity(flames, new Vector3(0f, 1.15f, 0f), new Vector3(0f, 0.25f, 0f), local: false);
            SizeOverLife(flames, new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.1f), new Keyframe(1f, 0.7f));
            Spin(flames, 10f, 0f);

            ParticleSystem embers = Ps(r, "Embers", m.Spark, (0.4f, 0.8f), (0f, 0f), (0.015f, 0.03f), 16);
            Rate(embers, 7f, 0f, 1f, loop: true);
            Shape(embers, ParticleSystemShapeType.Box, 0.5f, scale: new Vector3(0.5f, 1.2f, 0.4f), position: new Vector3(0f, 1f, 0f));
            Velocity(embers, new Vector3(0f, 1.5f, 0f), new Vector3(0.3f, 0.5f, 0.3f), local: false);
            Gravity(embers, -0.2f);
            Stretch(embers, 0.04f);

            Transform smokeLayer = Gate(r, "SmokeLayer", VfxQuality.Medium);
            ParticleSystem smoke = Ps(smokeLayer, "Smoke", m.Smoke, (0.8f, 1.1f), (0f, 0f), (0.4f, 0.7f), 8, position: new Vector3(0f, 1.8f, 0f));
            Rate(smoke, 3f, 0f, 1f, loop: true);
            Shape(smoke, ParticleSystemShapeType.Sphere, 0.25f);
            Velocity(smoke, new Vector3(0f, 0.6f, 0f), new Vector3(0.1f, 0.1f, 0.1f), local: false);
            SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f));
            Order(smoke, 30f);
            return root;
        }
    }
}
