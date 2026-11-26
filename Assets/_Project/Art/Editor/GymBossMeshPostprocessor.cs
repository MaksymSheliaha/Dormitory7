using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dormitory7.Art.Editor
{
    /// <summary>
    /// Fixes inconsistent / inverted polygon winding in the Mixamo GymBoss.fbx base mesh
    /// using spatial edge-adjacency BFS and signed-volume outward orientation on import.
    /// </summary>
    public sealed class GymBossMeshPostprocessor : AssetPostprocessor
    {
        private const string TargetFbxSuffix = "Characters/GymBoss/GymBoss.fbx";

        private void OnPostprocessModel(GameObject root)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.Replace('\\', '/').EndsWith(TargetFbxSuffix))
                return;

            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr != null && smr.sharedMesh != null)
                    FixOutwardWinding(smr.sharedMesh);
            }

            var mfs = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mfs)
            {
                if (mf != null && mf.sharedMesh != null)
                    FixOutwardWinding(mf.sharedMesh);
            }
        }

        private static void FixOutwardWinding(Mesh mesh)
        {
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0) return;

            // 1. Weld vertices spatially (handles split hard-edge / UV vertices cleanly)
            var posToId = new Dictionary<(int, int, int), int>(verts.Length);
            var vertToSpatial = new int[verts.Length];
            float invGrid = 100000f / Mathf.Max(0.001f, mesh.bounds.size.magnitude);
            int nextSpatialId = 0;

            for (int i = 0; i < verts.Length; i++)
            {
                var key = (
                    Mathf.RoundToInt(verts[i].x * invGrid),
                    Mathf.RoundToInt(verts[i].y * invGrid),
                    Mathf.RoundToInt(verts[i].z * invGrid)
                );
                if (!posToId.TryGetValue(key, out int sid))
                {
                    sid = nextSpatialId++;
                    posToId[key] = sid;
                }
                vertToSpatial[i] = sid;
            }

            // 2. Build undirected edge -> list of (triIndex, directedFromAtoB)
            var edgeMap = new Dictionary<(int, int), List<(int tri, bool forward)>>(triCount * 2);

            void AddEdge(int tri, int s0, int s1)
            {
                if (s0 == s1) return;
                bool fwd = s0 < s1;
                var key = fwd ? (s0, s1) : (s1, s0);
                if (!edgeMap.TryGetValue(key, out var list))
                {
                    list = new List<(int, bool)>(2);
                    edgeMap[key] = list;
                }
                list.Add((tri, fwd));
            }

            for (int t = 0; t < triCount; t++)
            {
                int s0 = vertToSpatial[tris[t * 3]];
                int s1 = vertToSpatial[tris[t * 3 + 1]];
                int s2 = vertToSpatial[tris[t * 3 + 2]];
                AddEdge(t, s0, s1);
                AddEdge(t, s1, s2);
                AddEdge(t, s2, s0);
            }

            // Build triangle adjacency: (neighborTri, sameRelativeOrientationNeeded)
            var adj = new List<(int neighbor, bool mustFlipRelative)>[triCount];
            for (int t = 0; t < triCount; t++)
                adj[t] = new List<(int, bool)>(3);

            foreach (var kv in edgeMap)
            {
                var list = kv.Value;
                if (list.Count != 2) continue; // skip boundary or non-manifold edges
                var a = list[0];
                var b = list[1];
                // Consistent winding across a shared edge means opposite traversal directions (a.forward != b.forward).
                // If a.forward == b.forward, one of them must be flipped relative to the other!
                bool sameDir = (a.forward == b.forward);
                adj[a.tri].Add((b.tri, sameDir));
                adj[b.tri].Add((a.tri, sameDir));
            }

            // 3. BFS over each connected shell to make winding mutually consistent, then orient shell outward
            var visited = new bool[triCount];
            var flip = new bool[triCount];
            var queue = new Queue<int>();
            var component = new List<int>();

            for (int start = 0; start < triCount; start++)
            {
                if (visited[start]) continue;
                component.Clear();
                visited[start] = true;
                flip[start] = false;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    component.Add(cur);
                    foreach (var (nb, mustFlipRel) in adj[cur])
                    {
                        if (visited[nb]) continue;
                        visited[nb] = true;
                        flip[nb] = mustFlipRel ? !flip[cur] : flip[cur];
                        queue.Enqueue(nb);
                    }
                }

                // Compute signed volume of this shell with current flip assignments
                double signedVol = 0.0;
                for (int k = 0; k < component.Count; k++)
                {
                    int t = component[k];
                    int i0 = tris[t * 3];
                    int i1 = tris[t * 3 + (flip[t] ? 2 : 1)];
                    int i2 = tris[t * 3 + (flip[t] ? 1 : 2)];
                    Vector3 v0 = verts[i0];
                    Vector3 v1 = verts[i1];
                    Vector3 v2 = verts[i2];
                    signedVol += Vector3.Dot(v0, Vector3.Cross(v1, v2)) / 6.0;
                }

                // In Unity's left-handed coordinate system, outward-facing CW triangles (Cross(v1-v0, v2-v0) pointing out)
                // produce POSITIVE signed volume. If negative, invert the entire shell!
                if (signedVol < 0.0)
                {
                    for (int k = 0; k < component.Count; k++)
                        flip[component[k]] = !flip[component[k]];
                }
            }

            // 4. Apply flips and recalculate clean outward normals
            int flippedCount = 0;
            for (int t = 0; t < triCount; t++)
            {
                if (flip[t])
                {
                    int tmp = tris[t * 3 + 1];
                    tris[t * 3 + 1] = tris[t * 3 + 2];
                    tris[t * 3 + 2] = tmp;
                    flippedCount++;
                }
            }

            if (flippedCount > 0)
            {
                mesh.triangles = tris;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
        }
    }
}
