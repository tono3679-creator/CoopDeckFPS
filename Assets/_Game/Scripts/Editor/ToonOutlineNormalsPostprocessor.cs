using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// For rigid toon props (weapons): stores position-averaged normals in UV3 at import time.
    /// The meshes keep hard edges for crisp cel shading; the toon shader's outline pass extrudes along
    /// these averaged normals (_OUTLINE_UV3_NORMALS) so the outline does not split open at the hard edges.
    /// Skinned meshes are not handled: UV3 is not skinned, so characters are smoothed in Blender instead.
    /// </summary>
    public class ToonOutlineNormalsPostprocessor : AssetPostprocessor
    {
        public const string Folder = GamePaths.Root + "/Art/Weapons/";

        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(Folder))
            {
                return;
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    WriteSmoothedNormals(filter.sharedMesh);
                }
            }
        }

        public static void WriteSmoothedNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                return;
            }

            // Average the distinct normals found at each position (UV-seam copies count once).
            var distinct = new Dictionary<Vector3Int, List<Vector3>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int key = Quantize(vertices[i], 1e4f);
                if (!distinct.TryGetValue(key, out List<Vector3> list))
                {
                    list = new List<Vector3>(4);
                    distinct.Add(key, list);
                }

                Vector3 n = normals[i];
                if (!list.Exists(m => Vector3.Dot(m, n) > 0.9999f))
                {
                    list.Add(n);
                }
            }

            var smoothed = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 n in distinct[Quantize(vertices[i], 1e4f)])
                {
                    sum += n;
                }

                smoothed.Add(sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[i]);
            }

            mesh.SetUVs(3, smoothed);
        }

        static Vector3Int Quantize(Vector3 v, float scale)
        {
            return new Vector3Int(Mathf.RoundToInt(v.x * scale), Mathf.RoundToInt(v.y * scale), Mathf.RoundToInt(v.z * scale));
        }
    }
}
