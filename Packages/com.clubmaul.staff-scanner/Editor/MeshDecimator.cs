// Staff Scanner V2 — Vertex-cluster mesh decimator

using System.Collections.Generic;
using UnityEngine;

namespace ClubMaul.StaffScanner.Editor
{
    internal static class MeshDecimator
    {
        public static Mesh Decimate(Mesh source, float amount)
        {
            // Derive cell size from mesh bounds so decimation is scale-agnostic.
            // FBX export settings (e.g. Bake Axis Conversion) can shift vertex
            // positions by an order of magnitude, breaking a fixed world-unit size.
            float longestAxis = Mathf.Max(source.bounds.size.x, source.bounds.size.y, source.bounds.size.z);
            if (longestAxis <= 0f) longestAxis = 1f;
            float cellSize = longestAxis / Mathf.Lerp(150f, 15f, Mathf.Clamp01(amount));

            var verts     = source.vertices;
            var normals   = source.normals;
            var tangents  = source.tangents;
            var uv        = source.uv;
            var weights   = source.boneWeights;
            var bindposes = source.bindposes;

            bool hasNormals  = normals  != null && normals.Length  == verts.Length;
            bool hasTangents = tangents != null && tangents.Length == verts.Length;
            bool hasUV       = uv       != null && uv.Length       == verts.Length;
            bool hasWeights  = weights  != null && weights.Length  == verts.Length;

            var cellToIndex = new Dictionary<long, int>(verts.Length);
            var remap       = new int[verts.Length];

            var newVerts    = new List<Vector3>(verts.Length / 4);
            var newNormals  = hasNormals  ? new List<Vector3>(verts.Length / 4) : null;
            var newTangents = hasTangents ? new List<Vector4>(verts.Length / 4) : null;
            var newUV       = hasUV       ? new List<Vector2>(verts.Length / 4) : null;
            // First vertex per cell keeps its bone weights verbatim — averaging weights
            // across unrelated verts would produce nonsense skinning.
            var newWeights  = hasWeights  ? new List<BoneWeight>(verts.Length / 4) : null;

            var sumCounts   = new List<int>(verts.Length / 4);
            var sumPos      = new List<Vector3>(verts.Length / 4);
            var sumNrm      = hasNormals  ? new List<Vector3>(verts.Length / 4) : null;
            var sumTan      = hasTangents ? new List<Vector4>(verts.Length / 4) : null;
            var sumUV       = hasUV       ? new List<Vector2>(verts.Length / 4) : null;

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
                    newIdx = newVerts.Count;
                    cellToIndex[key] = newIdx;
                    newVerts.Add(v);
                    sumCounts.Add(1);
                    sumPos.Add(v);
                    if (hasNormals)  { newNormals.Add(normals[i]);   sumNrm.Add(normals[i]); }
                    if (hasTangents) { newTangents.Add(tangents[i]); sumTan.Add(tangents[i]); }
                    if (hasUV)       { newUV.Add(uv[i]);             sumUV.Add(uv[i]); }
                    if (hasWeights)  { newWeights.Add(weights[i]); }
                }
                else
                {
                    sumCounts[newIdx]++;
                    sumPos[newIdx] += v;
                    if (hasNormals)  sumNrm[newIdx] += normals[i];
                    if (hasTangents) sumTan[newIdx] += tangents[i];
                    if (hasUV)       sumUV[newIdx]  += uv[i];
                }
                remap[i] = newIdx;
            }

            for (int i = 0; i < newVerts.Count; i++)
            {
                float c = sumCounts[i];
                newVerts[i] = sumPos[i] / c;
                if (hasNormals)  newNormals[i] = (sumNrm[i] / c).normalized;
                if (hasTangents)
                {
                    var t = sumTan[i] / c;
                    var n = new Vector3(t.x, t.y, t.z).normalized;
                    // Keep handedness (.w) from the first contributor; averaging it would drift toward 0.
                    newTangents[i] = new Vector4(n.x, n.y, n.z, newTangents[i].w);
                }
                if (hasUV) newUV[i] = sumUV[i] / c;
            }

            var newMesh = new Mesh
            {
                name      = source.name + "_Decimated",
                indexFormat = newVerts.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            newMesh.SetVertices(newVerts);
            if (hasNormals)  newMesh.SetNormals(newNormals);
            if (hasTangents) newMesh.SetTangents(newTangents);
            if (hasUV)       newMesh.SetUVs(0, newUV);
            if (hasWeights)  newMesh.boneWeights = newWeights.ToArray();
            if (bindposes != null && bindposes.Length > 0) newMesh.bindposes = bindposes;

            // Flatten every source submesh into a single submesh — the scanner
            // only ever uses one material, so distinct submeshes serve no purpose.
            newMesh.subMeshCount = 1;
            var allTris = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var srcTris = source.GetTriangles(s);
                for (int t = 0; t < srcTris.Length; t += 3)
                {
                    int a = remap[srcTris[t]];
                    int b = remap[srcTris[t + 1]];
                    int c = remap[srcTris[t + 2]];
                    // Two corners collapsed to the same cell — drop the degenerate triangle.
                    if (a == b || b == c || a == c) continue;
                    allTris.Add(a); allTris.Add(b); allTris.Add(c);
                }
            }
            newMesh.SetTriangles(allTris, 0);

            newMesh.RecalculateBounds();
            if (!hasNormals) newMesh.RecalculateNormals();
            return newMesh;
        }
    }
}
