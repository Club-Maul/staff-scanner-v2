// Staff Scanner V2 — Vertex-cluster mesh decimator

using System.Collections.Generic;
using UnityEngine;

namespace ClubMaul.StaffScanner.Editor
{
    internal static class MeshDecimator
    {
        public static Mesh Decimate(Mesh source, float amount)
        {
            var verts = source.vertices;
            var remap = BuildRemap(verts, source.bounds, amount, out int newCount);

            var normals   = source.normals;
            var tangents  = source.tangents;
            var uv        = source.uv;
            var weights   = source.boneWeights;
            var bindposes = source.bindposes;

            bool hasNormals  = normals  != null && normals.Length  == verts.Length;
            bool hasTangents = tangents != null && tangents.Length == verts.Length;
            bool hasUV       = uv       != null && uv.Length       == verts.Length;
            bool hasWeights  = weights  != null && weights.Length  == verts.Length;

            var counts = new int[newCount];
            var sumPos = new Vector3[newCount];
            var sumNrm = hasNormals  ? new Vector3[newCount] : null;
            var sumTan = hasTangents ? new Vector4[newCount] : null;
            var sumUV  = hasUV       ? new Vector2[newCount] : null;
            // First vertex per cell keeps its bone weights verbatim — averaging weights
            // across unrelated verts would produce nonsense skinning.
            var firstWeights = hasWeights ? new BoneWeight[newCount] : null;
            // Keep handedness (.w) from the first contributor; averaging it would drift toward 0.
            var firstTanW = hasTangents ? new float[newCount] : null;

            for (int i = 0; i < verts.Length; i++)
            {
                int idx = remap[i];
                if (counts[idx] == 0)
                {
                    if (hasWeights)  firstWeights[idx] = weights[i];
                    if (hasTangents) firstTanW[idx]    = tangents[i].w;
                }
                counts[idx]++;
                sumPos[idx] += verts[i];
                if (hasNormals)  sumNrm[idx] += normals[i];
                if (hasTangents) sumTan[idx] += tangents[i];
                if (hasUV)       sumUV[idx]  += uv[i];
            }

            var newVerts    = new Vector3[newCount];
            var newNormals  = hasNormals  ? new Vector3[newCount] : null;
            var newTangents = hasTangents ? new Vector4[newCount] : null;
            var newUV       = hasUV       ? new Vector2[newCount] : null;
            for (int i = 0; i < newCount; i++)
            {
                float c = counts[i];
                newVerts[i] = sumPos[i] / c;
                if (hasNormals) newNormals[i] = (sumNrm[i] / c).normalized;
                if (hasTangents)
                {
                    var t = sumTan[i] / c;
                    var n = new Vector3(t.x, t.y, t.z).normalized;
                    newTangents[i] = new Vector4(n.x, n.y, n.z, firstTanW[i]);
                }
                if (hasUV) newUV[i] = sumUV[i] / c;
            }

            var newMesh = new Mesh
            {
                name      = source.name + "_Decimated",
                indexFormat = newCount > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            newMesh.vertices = newVerts;
            if (hasNormals)  newMesh.normals  = newNormals;
            if (hasTangents) newMesh.tangents = newTangents;
            if (hasUV)       newMesh.uv       = newUV;
            if (hasWeights)  newMesh.boneWeights = firstWeights;
            if (bindposes != null && bindposes.Length > 0) newMesh.bindposes = bindposes;

            // Flatten every source submesh into a single submesh — the scanner
            // only ever uses one material, so distinct submeshes serve no purpose.
            newMesh.subMeshCount = 1;
            var allTris = new List<int>();
            CollectTriangles(source, remap, allTris);
            newMesh.SetTriangles(allTris, 0);

            newMesh.RecalculateBounds();
            if (!hasNormals) newMesh.RecalculateNormals();
            return newMesh;
        }

        /// <summary>
        /// Exact post-decimation triangle count for the given slider value, without
        /// building (or having to destroy) a Mesh — used by the inspector preview.
        /// </summary>
        public static int CountTriangles(Mesh source, float amount)
        {
            var remap = BuildRemap(source.vertices, source.bounds, amount, out _);
            return CollectTriangles(source, remap, null);
        }

        // Remaps each source triangle through 'remap', dropping degenerates (corners
        // collapsed into the same cell) and exact duplicates (same corners, same winding).
        // Appends surviving indices to 'output' when given; returns the surviving count.
        private static int CollectTriangles(Mesh source, int[] remap, List<int> output)
        {
            var seen = new HashSet<(int, int, int)>();
            int kept = 0;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var srcTris = source.GetTriangles(s);
                for (int t = 0; t < srcTris.Length; t += 3)
                {
                    int a = remap[srcTris[t]];
                    int b = remap[srcTris[t + 1]];
                    int c = remap[srcTris[t + 2]];
                    if (a == b || b == c || a == c) continue;
                    if (!seen.Add(Canonical(a, b, c))) continue;
                    kept++;
                    if (output != null) { output.Add(a); output.Add(b); output.Add(c); }
                }
            }
            return kept;
        }

        // Rotates the triangle so the smallest index comes first. Winding is preserved,
        // so duplicates hash identically while mirrored (back-face) copies stay distinct.
        private static (int, int, int) Canonical(int a, int b, int c)
        {
            if (a <= b && a <= c) return (a, b, c);
            if (b <= a && b <= c) return (b, c, a);
            return (c, a, b);
        }

        // Assigns every vertex to a spatial cell and returns old-index → new-index.
        private static int[] BuildRemap(Vector3[] verts, Bounds bounds, float amount, out int uniqueCount)
        {
            // Derive cell size from mesh bounds so decimation is scale-agnostic.
            // FBX export settings (e.g. Bake Axis Conversion) can shift vertex
            // positions by an order of magnitude, breaking a fixed world-unit size.
            float longestAxis = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longestAxis <= 0f) longestAxis = 1f;
            float cellSize = longestAxis / Mathf.Lerp(150f, 15f, Mathf.Clamp01(amount));

            var cellToIndex = new Dictionary<long, int>(verts.Length);
            var remap       = new int[verts.Length];

            float inv = 1f / cellSize;
            for (int i = 0; i < verts.Length; i++)
            {
                var v  = verts[i];
                int cx = Mathf.FloorToInt(v.x * inv);
                int cy = Mathf.FloorToInt(v.y * inv);
                int cz = Mathf.FloorToInt(v.z * inv);
                long key = ((long)(cx & 0x1FFFFF)) | (((long)(cy & 0x1FFFFF)) << 21) | (((long)(cz & 0x1FFFFF)) << 42);

                if (!cellToIndex.TryGetValue(key, out int newIdx))
                {
                    newIdx = cellToIndex.Count;
                    cellToIndex[key] = newIdx;
                }
                remap[i] = newIdx;
            }
            uniqueCount = cellToIndex.Count;
            return remap;
        }
    }
}
