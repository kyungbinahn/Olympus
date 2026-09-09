using NUnit.Framework;
using Olympus.Core.Grid;

namespace Olympus.Core.Tests
{
    public class GridPosTests
    {
        [Test]
        public void 같은_좌표는_같고_해시도_같다()
        {
            var a = new GridPos(3, -7);
            var b = new GridPos(3, -7);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void 축이_바뀐_좌표는_다르다()
        {
            // 해시를 (X*397)^Y 로 섞는 이유가 이것 — 단순히 X^Y 면 (3,5)와 (5,3)이 충돌한다.
            var a = new GridPos(3, 5);
            var b = new GridPos(5, 3);

            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void 체비셰프_거리는_대각을_1로_센다()
        {
            var a = new GridPos(0, 0);
            var b = new GridPos(3, 2);

            Assert.That(GridPos.ChebyshevDistance(a, b), Is.EqualTo(3));
            Assert.That(GridPos.ManhattanDistance(a, b), Is.EqualTo(5));
        }

        [Test]
        public void 거리는_음수_좌표에서도_맞다()
        {
            var a = new GridPos(-4, 1);
            var b = new GridPos(2, -3);

            Assert.That(GridPos.ChebyshevDistance(a, b), Is.EqualTo(6));
            Assert.That(GridPos.ManhattanDistance(a, b), Is.EqualTo(10));
        }
    }

    public class SquareGridTests
    {
        [Test]
        public void 칸에서_월드로_갔다가_돌아오면_같은_칸이다()
        {
            var grid = new SquareGrid(1.5f);

            var cells = new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(-3, 7),
                new GridPos(120, -240),
            };

            foreach (GridPos cell in cells)
            {
                WorldXZ world = grid.CellToWorld(cell);
                Assert.That(grid.WorldToCell(world), Is.EqualTo(cell), "왕복 실패: " + cell);
            }
        }

        [Test]
        public void 칸_0_0의_중심은_월드_원점이다()
        {
            var grid = new SquareGrid(2f);
            WorldXZ w = grid.CellToWorld(GridPos.Zero);

            Assert.That(w.X, Is.EqualTo(0f));
            Assert.That(w.Z, Is.EqualTo(0f));
        }

        [Test]
        public void 칸_안쪽_어디를_찍어도_그_칸이_나온다()
        {
            var grid = new SquareGrid(2f);

            // 칸 (1,1)의 중심은 (2,2), 경계는 (1,1)~(3,3).
            Assert.That(grid.WorldToCell(1.01f, 1.01f), Is.EqualTo(new GridPos(1, 1)));
            Assert.That(grid.WorldToCell(2.0f, 2.0f), Is.EqualTo(new GridPos(1, 1)));
            Assert.That(grid.WorldToCell(2.99f, 2.99f), Is.EqualTo(new GridPos(1, 1)));
        }

        [Test]
        public void 경계에_정확히_놓인_점은_큰_쪽_칸에_속한다()
        {
            // Floor(v + 0.5)를 쓰는 이유 — Math.Round는 .5에서 짝수로 붕어서
            // 경계가 칸마다 다른 쪽으로 붕는다(0.5→0, 1.5→2). 그러면 드래그 중
            // 같은 위치가 프레임에 따라 다른 칸으로 읽히는 흔들림이 생긴다.
            var grid = new SquareGrid(1f);

            Assert.That(grid.WorldToCell(0.5f, 0.5f), Is.EqualTo(new GridPos(1, 1)));
            Assert.That(grid.WorldToCell(1.5f, 1.5f), Is.EqualTo(new GridPos(2, 2)));
            Assert.That(grid.WorldToCell(2.5f, 2.5f), Is.EqualTo(new GridPos(3, 3)));
        }

        [Test]
        public void 음수_영역에서도_칸이_밀리지_않는다()
        {
            var grid = new SquareGrid(1f);

            Assert.That(grid.WorldToCell(-0.4f, -0.4f), Is.EqualTo(new GridPos(0, 0)));
            Assert.That(grid.WorldToCell(-0.6f, -0.6f), Is.EqualTo(new GridPos(-1, -1)));
            Assert.That(grid.WorldToCell(-1.4f, -1.4f), Is.EqualTo(new GridPos(-1, -1)));
        }
    }

    public class GridRectTests
    {
        [Test]
        public void 홀수_크기_Centered는_원점이_정중앙이다()
        {
            GridRect rect = GridRect.Centered(9);

            Assert.That(rect.MinX, Is.EqualTo(-4));
            Assert.That(rect.MinY, Is.EqualTo(-4));
            Assert.That(rect.MaxXExclusive, Is.EqualTo(5));
            Assert.That(rect.Contains(GridPos.Zero), Is.True);
            Assert.That(rect.CellCount, Is.EqualTo(81));
        }

        [Test]
        public void 경계_칸은_포함되고_한_칸_밖은_아니다()
        {
            var rect = new GridRect(0, 0, 4, 3);

            Assert.That(rect.Contains(new GridPos(0, 0)), Is.True);
            Assert.That(rect.Contains(new GridPos(3, 2)), Is.True);
            Assert.That(rect.Contains(new GridPos(4, 2)), Is.False);
            Assert.That(rect.Contains(new GridPos(3, 3)), Is.False);
            Assert.That(rect.Contains(new GridPos(-1, 0)), Is.False);
        }

        [Test]
        public void 풋프린트가_변에_딱_맞으면_들어가고_한_칸_넘치면_안_들어간다()
        {
            var rect = new GridRect(0, 0, 4, 4);
            var two = GridFootprint.Square(2);

            Assert.That(rect.ContainsFootprint(new GridPos(2, 2), two), Is.True);
            Assert.That(rect.ContainsFootprint(new GridPos(3, 2), two), Is.False);
            Assert.That(rect.ContainsFootprint(new GridPos(2, 3), two), Is.False);
        }

        [Test]
        public void AllCells는_모든_칸을_한_번씩_준다()
        {
            var rect = new GridRect(-1, -1, 3, 2);
            var seen = new System.Collections.Generic.HashSet<GridPos>();
            int count = 0;

            foreach (GridPos c in rect.AllCells())
            {
                Assert.That(seen.Add(c), Is.True, "중복: " + c);
                Assert.That(rect.Contains(c), Is.True);
                count++;
            }

            Assert.That(count, Is.EqualTo(6));
        }
    }

    public class GridFootprintTests
    {
        [Test]
        public void 점유_칸은_앵커에서_플러스_방향으로_뻗는다()
        {
            var fp = new GridFootprint(2, 3);
            var cells = new System.Collections.Generic.List<GridPos>(fp.CellsAt(new GridPos(5, 10)));

            Assert.That(cells.Count, Is.EqualTo(6));
            Assert.That(cells, Contains.Item(new GridPos(5, 10)));
            Assert.That(cells, Contains.Item(new GridPos(6, 12)));
            Assert.That(cells, Does.Not.Contain(new GridPos(7, 10)));
            Assert.That(cells, Does.Not.Contain(new GridPos(5, 13)));
        }

        [Test]
        public void 홀수_풋프린트의_중심은_앵커_칸_중심이다()
        {
            var grid = new SquareGrid(1f);
            var fp = GridFootprint.Square(3);

            WorldXZ center = fp.CenterAt(new GridPos(0, 0), grid);

            Assert.That(center.X, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(center.Z, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void 짝수_풋프린트의_중심은_칸_경계에_온다()
        {
            // 최소 코너 앵커를 쓰는 덕에 짝수 크기에서도 중심이 예외 없이 딱 떨어진다.
            var grid = new SquareGrid(1f);
            var fp = GridFootprint.Square(2);

            WorldXZ center = fp.CenterAt(new GridPos(0, 0), grid);

            Assert.That(center.X, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(center.Z, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void 크기가_0이하면_만들_때_터진다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GridFootprint(0, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GridFootprint(1, -2));
        }
    }

    public class GridOccupancyTests
    {
        private static GridOccupancy NewBoard()
        {
            return new GridOccupancy(new GridRect(0, 0, 10, 10));
        }

        [Test]
        public void 빈_판에는_놓을_수_있다()
        {
            GridOccupancy board = NewBoard();

            Assert.That(board.Check(new GridPos(0, 0), GridFootprint.Square(2)).Allowed, Is.True);
            Assert.That(board.TryPlace(1, new GridPos(0, 0), GridFootprint.Square(2)), Is.True);
            Assert.That(board.OccupiedCellCount, Is.EqualTo(4));
        }

        [Test]
        public void 겹치면_거부하고_누가_막았는지_알려준다()
        {
            GridOccupancy board = NewBoard();
            board.TryPlace(7, new GridPos(2, 2), GridFootprint.Square(2));

            PlacementCheck check = board.Check(new GridPos(3, 3), GridFootprint.Square(2));

            Assert.That(check.Allowed, Is.False);
            Assert.That(check.Rejection, Is.EqualTo(PlacementRejection.Overlapping));
            Assert.That(check.BlockingOccupantId, Is.EqualTo(7));
        }

        [Test]
        public void 범위를_벗어나면_거부한다()
        {
            GridOccupancy board = NewBoard();

            PlacementCheck check = board.Check(new GridPos(9, 9), GridFootprint.Square(2));

            Assert.That(check.Allowed, Is.False);
            Assert.That(check.Rejection, Is.EqualTo(PlacementRejection.OutOfBounds));
        }

        [Test]
        public void 실패한_배치는_판을_전혀_바꾸지_않는다()
        {
            // 검사와 등록을 한 호출로 묶은 이유 — 칸을 하나씩 채우다 중간에 막히면
            // 부분 점유가 남아 판이 조용히 오염된다.
            GridOccupancy board = NewBoard();
            board.TryPlace(1, new GridPos(5, 5), GridFootprint.Single);
            int before = board.OccupiedCellCount;

            bool placed = board.TryPlace(2, new GridPos(4, 4), GridFootprint.Square(3));

            Assert.That(placed, Is.False);
            Assert.That(board.OccupiedCellCount, Is.EqualTo(before));
            Assert.That(board.IsFree(new GridPos(4, 4)), Is.True);
        }

        [Test]
        public void 자기_자신은_겹침에서_빼면_제자리_근처로_옮길_수_있다()
        {
            // 이 예외가 없으면 건물을 한 칸 옮기는 것조차 "자기와 겹친다"고 거부된다.
            GridOccupancy board = NewBoard();
            board.TryPlace(3, new GridPos(1, 1), GridFootprint.Square(2));

            Assert.That(board.Check(new GridPos(2, 1), GridFootprint.Square(2)).Allowed, Is.False);
            Assert.That(board.Check(new GridPos(2, 1), GridFootprint.Square(2), 3).Allowed, Is.True);
        }

        [Test]
        public void 치우면_그_자리에_다시_놓을_수_있다()
        {
            GridOccupancy board = NewBoard();
            board.TryPlace(4, new GridPos(0, 0), GridFootprint.Square(3));

            int cleared = board.RemoveOccupant(4);

            Assert.That(cleared, Is.EqualTo(9));
            Assert.That(board.OccupiedCellCount, Is.EqualTo(0));
            Assert.That(board.TryPlace(5, new GridPos(0, 0), GridFootprint.Square(3)), Is.True);
        }

        [Test]
        public void 점유한_칸의_주인을_찾을_수_있다()
        {
            GridOccupancy board = NewBoard();
            board.TryPlace(9, new GridPos(3, 4), GridFootprint.Square(2));

            int owner;
            Assert.That(board.TryGetOccupant(new GridPos(4, 5), out owner), Is.True);
            Assert.That(owner, Is.EqualTo(9));
            Assert.That(board.TryGetOccupant(new GridPos(0, 0), out owner), Is.False);
        }
    }
}
