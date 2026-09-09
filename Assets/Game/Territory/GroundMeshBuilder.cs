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
        public static Mesh Build(GridRect bounds, SquareGrid grid, string meshName = "TerritoryGround")
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
            var triangles = new int[cellCount * 6];

            float half = grid.CellSize * 0.5f;
            int v = 0;
            int t = 0;

            for (int y = bounds.MinY; y < bounds.MaxYExclusive; y++)
            {
                for (int x = bounds.MinX; x < bounds.MaxXExclusive; x++)
                {
                    WorldXZ center = grid.CellToWorld(new GridPos(x, y));

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
                    triangles[t + 0] = v + 0;
                    triangles[t + 1] = v + 1;
                    triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 0;
                    triangles[t + 4] = v + 2;
                    triangles[t + 5] = v + 3;

                    v += 4;
                    t += 6;
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
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
