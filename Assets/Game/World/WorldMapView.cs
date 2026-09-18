using System;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.World;

namespace Olympus.Game.World
{
    /// <summary>
    /// 월드맵 화면을 조립하는 지점 — 씨앗으로 지도를 만들고, 텍스처로 구워 판 하나에 붙인다.
    ///
    /// 지도 자체는 저장하지 않는다. 씨앗만 있으면 언제든 같은 지도가 다시 나오기 때문이다
    /// (<see cref="WorldGenerator"/>). 나중에 서버가 붙으면 서버가 씨앗을 내려주고,
    /// 이 코드는 그대로 같은 지도를 만든다.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        [Header("월드 생성")]
        [Tooltip("씨앗. 같은 값이면 언제나 같은 월드가 나온다.")]
        [SerializeField] private int _seed = 20260911;

        [Tooltip("한 변의 칸 수. 512면 좌표가 X:0~511 / Y:0~511이 된다.")]
        [SerializeField] private int _size = 512;

        [Header("지형 높이 구간")]
        [Tooltip("이 값들은 눈으로 정한다 — 플레이 중에 돌리면 다음 재생성부터 반영된다.\n\n" +
                 "물이 높을수록 바다가 넓어지고, 산이 낮을수록 산맥이 두꺼워진다.")]
        [SerializeField, Range(0.05f, 0.9f)] private float _waterLevel = 0.38f;
        [SerializeField, Range(0.1f, 0.95f)] private float _forestLevel = 0.62f;
        [SerializeField, Range(0.2f, 0.99f)] private float _mountainLevel = 0.78f;

        [Tooltip("값이 작을수록 대륙이 커진다.")]
        [SerializeField, Range(0.002f, 0.05f)] private float _noiseScale = 0.012f;

        [Header("거점 수")]
        [SerializeField] private int _ruinedCities = 120;
        [SerializeField] private int _resourceNodes = 600;
        [SerializeField] private int _monsterCamps = 300;

        [Header("뷰")]
        [SerializeField] private MeshRenderer _renderer;
        [SerializeField] private MeshFilter _meshFilter;

        private WorldMap _map;
        private Texture2D _texture;
        private Camera _camera;

        public WorldMap Map => _map;

        /// <summary>한 칸이 월드 공간에서 차지하는 크기. 1로 두면 좌표가 곧 위치다.</summary>
        public const float CellSize = 1f;

        private void Awake()
        {
            _camera = Camera.main;
            Rebuild();
        }

        /// <summary>
        /// 지도를 다시 만들고 화면에 올린다. 씨앗·높이 구간을 바꿔 보며 눈으로 정할 때
        /// 인스펙터 우클릭 메뉴에서 부를 수 있게 열어 둔다.
        /// </summary>
        [ContextMenu("월드 다시 만들기")]
        public void Rebuild()
        {
            var settings = new WorldGenSettings
            {
                Size = _size,
                Seed = _seed,
                NoiseScale = _noiseScale,
                WaterLevel = _waterLevel,
                ForestLevel = _forestLevel,
                MountainLevel = _mountainLevel,
                RuinedCityCount = _ruinedCities,
                ResourceNodeCount = _resourceNodes,
                MonsterCampCount = _monsterCamps,
            };

            _map = new WorldGenerator().Generate(settings);

            ApplyTexture(WorldMapTexture.Build(_map));
            BuildQuad();

            Debug.Log(
                "월드 준비 완료 — " + _size + "x" + _size + "칸(" + (_size * _size).ToString("N0") + "), " +
                "거점 " + _map.SiteCount + "곳, 씨앗 " + _seed);
        }

        private void ApplyTexture(Texture2D texture)
        {
            if (_texture != null)
                Destroy(_texture);

            _texture = texture;

            if (_renderer == null)
                return;

            // sharedMaterial을 직접 건드리면 에셋 파일이 더러워진다 — 인스턴스에만 붙인다.
            Material material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;

            if (material != null)
                material.mainTexture = _texture;
        }

        /// <summary>
        /// 지도를 덮는 판 하나. 삼각형 두 개면 끝이다 — 지형은 전부 텍스처가 그린다.
        /// </summary>
        private void BuildQuad()
        {
            if (_meshFilter == null)
                return;

            float span = _size * CellSize;

            var mesh = new Mesh { name = "WorldQuad" };

            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(span, 0f, 0f),
                new Vector3(0f, 0f, span),
                new Vector3(span, 0f, span),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };

            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();

            if (_meshFilter.sharedMesh != null && Application.isPlaying)
                Destroy(_meshFilter.sharedMesh);

            _meshFilter.sharedMesh = mesh;
        }

        /// <summary>월드 공간의 점이 어느 칸인가. 범위 밖이면 false.</summary>
        public bool TryGetCell(Vector3 worldPoint, out GridPos cell)
        {
            int x = Mathf.FloorToInt(worldPoint.x / CellSize);
            int y = Mathf.FloorToInt(worldPoint.z / CellSize);

            cell = new GridPos(x, y);
            return _map != null && _map.Bounds.Contains(cell);
        }

        /// <summary>칸의 중심이 월드 공간 어디인가. 카메라를 특정 좌표로 보낼 때 쓴다.</summary>
        public Vector3 CellCenterWorld(GridPos cell)
        {
            return new Vector3(
                (cell.X + 0.5f) * CellSize,
                0f,
                (cell.Y + 0.5f) * CellSize);
        }

        /// <summary>화면의 한 점이 가리키는 칸. 지면 평면과 교차시켜 구한다.</summary>
        public bool TryPickCell(Vector2 screen, out GridPos cell)
        {
            cell = GridPos.Zero;

            if (_camera == null)
                _camera = Camera.main;

            if (_camera == null)
                return false;

            Ray ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);

            float distance;
            if (!plane.Raycast(ray, out distance))
                return false;

            return TryGetCell(ray.GetPoint(distance), out cell);
        }

        /// <summary>플레이어 기지가 있는 칸. 없으면 지도 한가운데.</summary>
        public GridPos PlayerCityCell()
        {
            if (_map != null)
            {
                foreach (WorldSite s in _map.Sites)
                {
                    if (s.Kind == WorldSiteKind.PlayerCity)
                        return s.Position;
                }
            }

            return new GridPos(_size / 2, _size / 2);
        }

        private void OnDestroy()
        {
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
