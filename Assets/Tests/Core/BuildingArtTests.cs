using System;
using NUnit.Framework;
using Olympus.Core.Territory;

namespace Olympus.Core.Tests
{
    public class BuildingArtTests
    {
        [Test]
        public void 보정이_없는_건물은_중립값이_나온다()
        {
            var table = new BuildingArtTable();

            BuildingArtAdjustment a = table.For("아직_없는_건물");

            Assert.AreEqual(0f, a.YawDegrees);
            Assert.AreEqual(1f, a.ScaleMultiplier);
        }

        [Test]
        public void 넣은_보정을_그대로_돌려준다()
        {
            var table = new BuildingArtTable();
            table.Add(new BuildingArtAdjustment("main_hall", 90f, 1.4f));

            BuildingArtAdjustment a = table.For("main_hall");

            Assert.AreEqual(90f, a.YawDegrees);
            Assert.AreEqual(1.4f, a.ScaleMultiplier);
        }

        [Test]
        public void 크기_배율이_0이면_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildingArtAdjustment("main_hall", 0f, 0f));
        }

        [Test]
        public void 크기_배율이_음수면_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildingArtAdjustment("main_hall", 0f, -1f));
        }

        [Test]
        public void 크기_배율이_숫자가_아니면_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildingArtAdjustment("main_hall", 0f, float.NaN));
        }

        [Test]
        public void DefId가_중복이면_거부한다()
        {
            var table = new BuildingArtTable();
            table.Add(new BuildingArtAdjustment("main_hall", 0f, 1f));

            Assert.Throws<InvalidOperationException>(
                () => table.Add(new BuildingArtAdjustment("main_hall", 0f, 1f)));
        }

        [Test]
        public void 한_바퀴_돈_각도는_같은_값으로_접힌다()
        {
            // 편집 툴은 씬에서 0~360으로 읽어 오고 사람은 파일에 음수로 적는다.
            // 같은 방향이면 같은 값이 나와야 파일이 매번 달라지지 않는다.
            Assert.AreEqual(10f, new BuildingArtAdjustment("a", 370f, 1f).YawDegrees);
            Assert.AreEqual(-90f, new BuildingArtAdjustment("a", 270f, 1f).YawDegrees);
            Assert.AreEqual(170f, new BuildingArtAdjustment("a", -190f, 1f).YawDegrees);
            Assert.AreEqual(180f, new BuildingArtAdjustment("a", 180f, 1f).YawDegrees);
            Assert.AreEqual(180f, new BuildingArtAdjustment("a", -180f, 1f).YawDegrees);
        }

        [Test]
        public void 보정이_없으면_밀어낸_거리도_0이다()
        {
            BuildingArtAdjustment a = new BuildingArtTable().For("없는_건물");

            Assert.AreEqual(0f, a.OffsetX);
            Assert.AreEqual(0f, a.OffsetY);
            Assert.AreEqual(0f, a.OffsetZ);
        }

        [Test]
        public void 밀어낸_거리를_그대로_돌려준다()
        {
            var a = new BuildingArtAdjustment("shipyard", 0f, 1f, 0.5f, -0.2f, 1.25f);

            Assert.AreEqual(0.5f, a.OffsetX);
            Assert.AreEqual(-0.2f, a.OffsetY);
            Assert.AreEqual(1.25f, a.OffsetZ);
        }

        [Test]
        public void 밀어낸_거리가_숫자가_아니면_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildingArtAdjustment("a", 0f, 1f, float.NaN, 0f, 0f));
        }

        [Test]
        public void 각도가_숫자가_아니면_거부한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BuildingArtAdjustment("a", float.PositiveInfinity, 1f));
        }
    }
}
