using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// 소품 FBX(<c>Assets/Art/Characters/**/prop_*.fbx</c>)를 가져올 때 삼각형 감김(앞면 방향)을 한 방향으로 맞춘다.
    ///
    /// 호플리테스 방패(prop_aspis) 274면 중 한 덩어리가 이웃과 반대로 감겨 있었다(어긋난 모서리 36, 2026-10-26 BridgeActions.MeshInfo).
    /// 그 면은 바깥에서 보면 뒷면이라 안 그려지고 안쪽 면이 비쳐서, 금테가 두 줄 고리로 보이고 붉은 앞면 대신
    /// 손잡이 끈이 보였다(형님 "노말이 여전히 이슈"). 아트 원본을 다시 뽑아도 같은 실수가 다시 들어올 수 있어서
    /// 가져오기 단계에서 고친다 — 이미 맞는 메시(창 등)는 아무것도 바뀌지 않는다.
    ///
    /// 방법: 위치가 같은 정점을 합쳐 이웃 삼각형을 잇고, 공유 모서리를 같은 방향으로 지나는 이웃을 뒤집어 덩어리마다
    /// 감김을 통일한 뒤, 덩어리의 부호 있는 부피가 음수면(면이 안쪽을 봄) 덩어리째 뒤집는다. 뒤집은 면이 있으면 노말을 다시 계산한다.
    /// </summary>
    internal sealed class PropMeshRepair : AssetPostprocessor
    {
        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith("Assets/Art/Characters/") || !CharacterModelPostprocessor.IsProp(assetPath))
                return;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;

                int flipped = Repair(mf.sharedMesh);
                if (flipped > 0)
                    Debug.Log("[소품 면 방향] " + assetPath + " / " + mf.name + ": 뒤집힌 삼각형 " + flipped + "개를 바로잡았습니다.");
            }
        }

        /// <summary>감김을 통일하고 바깥을 보게 한다. 뒤집은 삼각형 수를 돌려준다.</summary>
        internal static int Repair(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            var tris = new List<int>();
            var subStart = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                subStart.Add(tris.Count);
                tris.AddRange(mesh.GetTriangles(s));
            }
            int triCount = tris.Count / 3;
            if (triCount == 0)
                return 0;

            // 위치가 같은 정점 = 같은 점(UV·노말 이음매로 쪼개진 정점을 합친다).
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var q = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                int k;
                if (!weld.TryGetValue(q, out k)) { k = weld.Count; weld[q] = k; }
                id[i] = k;
            }

            // 무방향 모서리 → 그 모서리를 가진 삼각형들(그리고 그 삼각형이 지나는 방향).
            var edges = new Dictionary<long, List<(int tri, bool forward)>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = id[tris[t * 3 + e]], b = id[tris[t * 3 + (e + 1) % 3]];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    List<(int, bool)> list;
                    if (!edges.TryGetValue(key, out list)) edges[key] = list = new List<(int, bool)>();
                    list.Add((t, a < b));
                }
            }

            // 덩어리마다 첫 삼각형 기준으로 감김을 퍼뜨린다(모서리를 둘이 공유할 때만 — 셋 이상이면 판단 보류).
            var flip = new bool[triCount];
            var seen = new bool[triCount];
            var component = new int[triCount];
            int components = 0;
            var queue = new Queue<int>();
            for (int start = 0; start < triCount; start++)
            {
                if (seen[start]) continue;
                seen[start] = true;
                component[start] = components;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int t = queue.Dequeue();
                    for (int e = 0; e < 3; e++)
                    {
                        int a = id[tris[t * 3 + e]], b = id[tris[t * 3 + (e + 1) % 3]];
                        if (a == b) continue;
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        List<(int tri, bool forward)> list = edges[key];
                        if (list.Count != 2) continue;

                        bool mine = (a < b) ^ flip[t];
                        foreach ((int other, bool forward) in list)
                        {
                            if (other == t || seen[other]) continue;
                            // 맞게 감긴 이웃은 공유 모서리를 반대 방향으로 지난다.
                            flip[other] = forward == mine;
                            seen[other] = true;
                            component[other] = components;
                            queue.Enqueue(other);
                        }
                    }
                }
                components++;
            }

            // 덩어리마다 부호 있는 부피 — 음수면 면이 안쪽을 보고 있다(Unity 앞면 = 시계 방향 감김, 바깥 노말 = Cross(b-a, c-a)).
            Vector3 c0 = mesh.bounds.center;
            var volume = new double[components];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = v[tris[t * 3]] - c0, b = v[tris[t * 3 + 1]] - c0, c = v[tris[t * 3 + 2]] - c0;
                double vol = Vector3.Dot(a, Vector3.Cross(b - a, c - a));
                volume[component[t]] += flip[t] ? -vol : vol;
            }

            int changed = 0;
            for (int t = 0; t < triCount; t++)
            {
                bool f = flip[t] ^ (volume[component[t]] < 0.0);
                if (!f) continue;
                int tmp = tris[t * 3 + 1];
                tris[t * 3 + 1] = tris[t * 3 + 2];
                tris[t * 3 + 2] = tmp;
                changed++;
            }

            if (changed == 0)
                return 0;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int from = subStart[s];
                int to = s + 1 < mesh.subMeshCount ? subStart[s + 1] : tris.Count;
                mesh.SetTriangles(tris.GetRange(from, to - from), s);
            }

            // 뒤집은 면의 정점 노말은 반대를 보고 있다 — 감김에서 다시 계산한다(분리된 정점의 각진 모서리는 그대로 남는다).
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return changed;
        }
    }
}
