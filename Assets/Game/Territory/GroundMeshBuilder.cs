using UnityEngine;
using Olympus.Core.Grid;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 기지 지면 메쉬를 격자 정의에서 생성한다.
    ///
    /// 핵심은 <see cref="GridRect"/>·<see cref="SquareGrid"/>를 로직과 **같은 것**을 받는다는 점이다.
    /// 보이는 땅과 놓을 수 있는 자리가 같은 소스에서 나오므로 둘이 어긋날 수 없다.
    /// (뉴포리아는 건설 가능 경계를 런타임에 건물들을 훑어 추정하는데, 그 계보에서
    ///  "보이는 자리인데 안 놓인다"·"배치 미리보기가 잡히지 않는데 로그가 없다"가 나온다.)
    ///
    /// 칸마다 GameObject를 두지 않고 메쉬 하나로 만든다 — 48×48이면 쿼드 2,304개,
    /// 드로우콜 하나다. 칸을 오브젝트로 쪼개면 모바일에서 그것만으로 죽는다.
    /// </summary>
    public static class GroundMeshBuilder
    {
        /// <summary>Unity 메쉬 하나가 담을 수 있는 정점 한계(16비트 인덱스).</summary>
        private const int MaxVertices = 65000;

        /// <summary>
        /// 칸당 쿼드 하나로 지면을 만든다.
        ///
        /// 정점을 칸끼리 공유하지 않는다(칸당 4개). 공유하면 정점이 1/4로 줄지만,
        /// UV와 정점색을 칸 단위로 따로 줄 수 없어진다 — 칸 하이라이트와 체커 무늬가
        /// 그 두 채널을 쓴다. 이 규모에서 정점 수는 문제가 아니다.
        ///
        /// UV는 칸 하나가 정확히 0~1을 쓰게 깐다. 그래서 2×2 체커 텍스처를 반복으로
        /// 물리면 칸 경계가 그대로 눈에 보인다 — 커스텬 셰이더가 필요 없다.
        /// </summary>
        /// <param name="isRestored">
        /// 그 칸이 녹지로 복구됐는지. 참인 칸은 서브메쉬 1(녹지), 나머지는 서브메쉬 0(황폐)으로
        /// 나뉜다 — 머티리얼 두 장이면 셰이더 작업 없이 두 지면이 한 메쉬에서 그려진다.
        /// null이면 전부 황폐로 둔다.
        ///
        /// 칸 경계가 각져 보이는 것이 이 방식의 한계다. 부드럽게 번지는 연출이 필요해지면
        /// 복구도를 칸 하나당 1픽셀인 마스크 텍스처로 만들고 Shader Graph에서 두 지면을
        /// 블렌딩하면 된다 — 실제 아트가 들어오는 시점의 작업이다.
        /// </param>
        public static Mesh Build(
            GridRect bounds,
            SquareGrid grid,
            System.Func<GridPos, bool> isRestored = null,
            string meshName = "TerritoryGround")
        {
            if (grid == null)
                throw new System.ArgumentNullException(nameof(grid));

            int cellCount = bounds.CellCount;
            int vertexCount = cellCount * 4;

            if (vertexCount > MaxVertices)
            {
                throw new System.ArgumentException(
                    "지면이 메쉬 하나에 담기지 않는다: " + bounds + " = " + cellCount + "칸 (" +
                    vertexCount + "정점, 한계 " + MaxVertices + "). " +
                    "이 크기가 필요하면 청크로 쪼개야 한다 — 월드맵이 그 경우다.",
                    nameof(bounds));
            }

            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var normals = new Vector3[vertexCount];

            // 황폐/녹지 두 서브메쉬로 나눈다.
            var devastated = new System.Collections.Generic.List<int>(cellCount * 6);
            var restored = new System.Collections.Generic.List<int>(64);

            float half = grid.CellSize * 0.5f;
            int v = 0;

            for (int y = bounds.MinY; y < bounds.MaxYExclusive; y++)
            {
                for (int x = bounds.MinX; x < bounds.MaxXExclusive; x++)
                {
                    var cell = new GridPos(x, y);
                    WorldXZ center = grid.CellToWorld(cell);

                    // 칸 중심이 격자점이므로 네 코너는 중심 ± 반칸이다.
                    vertices[v + 0] = new Vector3(center.X - half, 0f, center.Z - half);
                    vertices[v + 1] = new Vector3(center.X - half, 0f, center.Z + half);
                    vertices[v + 2] = new Vector3(center.X + half, 0f, center.Z + half);
                    vertices[v + 3] = new Vector3(center.X + half, 0f, center.Z - half);

                    // 칸마다 0~1 — 체커 텍스처가 칸에 딱 맞아떨어진다.
                    uv[v + 0] = new Vector2(0f, 0f);
                    uv[v + 1] = new Vector2(0f, 1f);
                    uv[v + 2] = new Vector2(1f, 1f);
                    uv[v + 3] = new Vector2(1f, 0f);

                    normals[v + 0] = Vector3.up;
                    normals[v + 1] = Vector3.up;
                    normals[v + 2] = Vector3.up;
                    normals[v + 3] = Vector3.up;

                    // 위(+Y)에서 봤을 때 시계방향이 앞면이다.
                    var target = (isRestored != null && isRestored(cell)) ? restored : devastated;

                    target.Add(v + 0);
                    target.Add(v + 1);
                    target.Add(v + 2);
                    target.Add(v + 0);
                    target.Add(v + 2);
                    target.Add(v + 3);

                    v += 4;
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;

            // 서브메쉬 수를 항상 2로 둔다 — 녹지가 하나도 없어도 렌더러의 머티리얼
            // 슬롯 수와 어긋나지 않게 한다(어긋나면 Unity가 조용히 첫 머티리얼로 덮어 그린다).
            mesh.subMeshCount = 2;
            mesh.SetTriangles(devastated, 0);
            mesh.SetTriangles(restored, 1);
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 지정한 칸들만 덮는 얇은 메쉬. 배치 미리보기·선택 하이라이트에 쓴다.
        /// 지면보다 살짝 띄워서 Z 파이팅을 피한다.
        /// </summary>
        public static Mesh BuildCellOverlay(
            System.Collections.Generic.IList<GridPos> cells,
            SquareGrid grid,
            float yOffset = 0.02f,
            string meshName = "CellOverlay")
        {
            if (cells == null)
                throw new System.ArgumentNullException(nameof(cells));

            var vertices = new Vector3[cells.Count * 4];
            var uv = new Vector2[cells.Count * 4];
            var normals = new Vector3[cells.Count * 4];
            var triangles = new int[cells.Count * 6];

            float half = grid.CellSize * 0.5f;

            for (int i = 0; i < cells.Count; i++)
            {
                WorldXZ c = grid.CellToWorld(cells[i]);
                int v = i * 4;
                int t = i * 6;

                vertices[v + 0] = new Vector3(c.X - half, yOffset, c.Z - half);
                vertices[v + 1] = new Vector3(c.X - half, yOffset, c.Z + half);
                vertices[v + 2] = new Vector3(c.X + half, yOffset, c.Z + half);
                vertices[v + 3] = new Vector3(c.X + half, yOffset, c.Z - half);

                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(0f, 1f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(1f, 0f);

                normals[v + 0] = Vector3.up;
                normals[v + 1] = Vector3.up;
                normals[v + 2] = Vector3.up;
                normals[v + 3] = Vector3.up;

                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
