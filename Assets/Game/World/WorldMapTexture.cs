using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.World;

namespace Olympus.Game.World
{
    /// <summary>
    /// 월드 지도를 텍스처 한 장으로 굽는다 — 한 칸이 한 픽셀이다.
    ///
    /// 칸마다 오브젝트를 만들지 않는 이유는 단순하다. 512x512면 26만 칸이고,
    /// 그걸 GameObject로 만들면 씬이 열리지도 않는다. 멀리서 보는 월드맵은
    /// 실제 SLG들도 지형을 이미지로 그리고, 가까이 갔을 때만 실제 모델을 붙인다.
    /// </summary>
    public static class WorldMapTexture
    {
        // 지형 색 — 그레이박스다. 실제 아트가 들어오면 타일셋으로 교체된다.
        private static readonly Color32 Water = new Color32(58, 92, 130, 255);
        private static readonly Color32 Plains = new Color32(150, 160, 96, 255);
        private static readonly Color32 Forest = new Color32(84, 118, 70, 255);
        private static readonly Color32 Mountain = new Color32(120, 114, 105, 255);

        // 거점 색 — 지형보다 확실히 튀어야 한 픽셀이어도 눈에 걸린다.
        private static readonly Color32 PlayerCity = new Color32(90, 220, 255, 255);
        private static readonly Color32 RuinedCity = new Color32(210, 200, 185, 255);
        private static readonly Color32 MonsterCamp = new Color32(200, 70, 70, 255);

        private static readonly Color32 NodeWood = new Color32(170, 120, 60, 255);
        private static readonly Color32 NodeStone = new Color32(190, 190, 200, 255);
        private static readonly Color32 NodeFood = new Color32(220, 190, 90, 255);
        private static readonly Color32 NodeGold = new Color32(240, 210, 80, 255);

        public static Texture2D Build(WorldMap map)
        {
            GridRect b = map.Bounds;

            var tex = new Texture2D(b.Width, b.Height, TextureFormat.RGBA32, false)
            {
                name = "WorldMap",

                // 칸 경계가 흐려지면 좌표를 읽을 수 없다 — 기지 체커 텍스처와 같은 이유.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[b.Width * b.Height];

            for (int y = 0; y < b.Height; y++)
            {
                for (int x = 0; x < b.Width; x++)
                {
                    var cell = new GridPos(b.MinX + x, b.MinY + y);
                    pixels[y * b.Width + x] = ColorFor(map, cell);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return tex;
        }

        private static Color32 ColorFor(WorldMap map, GridPos cell)
        {
            WorldSite site;
            if (map.TryGetSite(cell, out site))
                return SiteColor(site);

            switch (map.TerrainAt(cell))
            {
                case WorldTerrain.Water: return Water;
                case WorldTerrain.Forest: return Forest;
                case WorldTerrain.Mountain: return Mountain;
                default: return Plains;
            }
        }

        private static Color32 SiteColor(WorldSite site)
        {
            switch (site.Kind)
            {
                case WorldSiteKind.PlayerCity: return PlayerCity;
                case WorldSiteKind.RuinedCity: return RuinedCity;
                case WorldSiteKind.MonsterCamp: return MonsterCamp;

                case WorldSiteKind.ResourceNode:
                    switch (site.Resource)
                    {
                        case ResourceKind.Stone: return NodeStone;
                        case ResourceKind.Food: return NodeFood;
                        case ResourceKind.Gold: return NodeGold;
                        default: return NodeWood;
                    }

                default: return Plains;
            }
        }
    }
}
