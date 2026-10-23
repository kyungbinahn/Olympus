using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.Territory;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 기지 지면. 격자 정의를 들고 메쉬를 만들며, 화면 좌표를 칸으로 바꿔 준다.
    ///
    /// 격자 정의(<see cref="SquareGrid"/>·<see cref="GridRect"/>)를 여기서 한 번 만들어
    /// 로직과 뷰가 같은 인스턴스를 쓰게 한다. 두 곳에서 따로 만들면 칸 크기나 범위가
    /// 어긋났을 때 "보이는 땅과 놓이는 자리가 다르다"가 되고, 그건 조용히 틀린다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TerritoryGround : MonoBehaviour
    {
        [Header("격자")]
        [Tooltip("칸 하나의 월드 크기. 아트 스케일은 임포트에서 맞추고 이 값은 1로 둔다.")]
        [SerializeField] private float _cellSize = 1f;

        [Tooltip("1구역 기지의 한 변 칸 수. 뉴포리아 실측(건물 면적 1,278칸 / 밀도 26%)에서 " +
                 "시작 규모 30~40채에 맞춰 48로 잡았다. 데이터 값이라 조정 자유롭다.")]
        [SerializeField] private int _baseSize = 48;

        private SquareGrid _grid;
        private GridRect _bounds;
        private Mesh _mesh;
        private RestorationMap _restoration;
        private int _builtRestorationVersion = -1;

        /// <summary>로직에 넘겨줄 격자. 로직과 뷰가 같은 인스턴스를 쓴다.</summary>
        public SquareGrid Grid
        {
            get
            {
                EnsureGrid();
                return _grid;
            }
        }

        /// <summary>건설 가능 범위. 원점이 정중앙에 오도록 잡는다.</summary>
        public GridRect Bounds
        {
            get
            {
                EnsureGrid();
                return _bounds;
            }
        }

        public int BaseSize => _baseSize;

        private void EnsureGrid()
        {
            if (_grid != null && _grid.CellSize == _cellSize)
                return;

            _grid = new SquareGrid(_cellSize);
            _bounds = GridRect.Centered(_baseSize);
        }

        /// <summary>
        /// 어느 칸이 녹지인지 알려주는 지도. 이 컴포넌트는 **받아서 그리기만** 한다.
        ///
        /// 여기서 직접 만들지 않는 이유 — 로직(<see cref="TerritoryRuntime"/>)이 자기
        /// 지도를 갖고 뷰가 또 하나를 만들면 두 인스턴스가 갈라진다. 그러면 로직은
        /// 복구했다고 알고 화면은 황폐한 상태로 남는다. 소유자를 로직 한쪽으로 못 박는다.
        ///
        /// 비워 두면(null) 전부 황폐로 그린다.
        /// </summary>
        public RestorationMap Restoration
        {
            get { return _restoration; }
            set
            {
                if (ReferenceEquals(_restoration, value))
                    return;

                _restoration = value;
                Rebuild();
            }
        }

        private void Awake()
        {
            Rebuild();
        }

        private void LateUpdate()
        {
            // 버전 정수 하나를 비교한다. 집합을 매 프레임 대조하지 않으려고 둔 장치다.
            if (_restoration != null && _restoration.Version != _builtRestorationVersion)
                Rebuild();
        }

        /// <summary>지면 메쉬를 다시 만든다. 기지가 확장되거나 녹지가 번지면 다시 부른다.</summary>
        public void Rebuild()
        {
            EnsureGrid();

            if (_mesh != null)
            {
                // 에디터에서 반복 호출될 수 있어 이전 메쉬를 반드시 버린다 —
                // 안 버리면 씬을 조립할 때마다 메쉬가 누적된다.
                if (Application.isPlaying)
                    Destroy(_mesh);
                else
                    DestroyImmediate(_mesh);
            }

            System.Func<GridPos, bool> isRestored =
                _restoration != null ? (System.Func<GridPos, bool>)_restoration.IsRestored : null;

            _mesh = GroundMeshBuilder.Build(_bounds, _grid, isRestored);
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            _builtRestorationVersion = _restoration != null ? _restoration.Version : -1;
        }

        /// <summary>
        /// 화면 좌표가 가리키는 칸. 지면 평면(Y=0)과 광선을 교차시킨다.
        ///
        /// 콜라이더 레이캐스트를 쓰지 않는 이유 — 지면에 MeshCollider를 붙이면 2,304개
        /// 삼각형을 물리 엔진이 들고 있어야 하고, 손가락이 건물에 가려도 땅을 집어야 하는
        /// 경우가 많다. 평면 교차는 정확하고 공짜다.
        /// </summary>
        public bool TryPickCell(Camera camera, Vector2 screenPosition, out GridPos cell)
        {
            cell = GridPos.Zero;

            if (camera == null)
                return false;

            EnsureGrid();

            Ray ray = camera.ScreenPointToRay(screenPosition);

            // 지면은 이 오브젝트의 Y 높이에 있는 수평면이다.
            var plane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

            float distance;
            if (!plane.Raycast(ray, out distance))
                return false;

            Vector3 hit = ray.GetPoint(distance);
            Vector3 local = transform.InverseTransformPoint(hit);

            cell = _grid.WorldToCell(local.x, local.z);
            return _bounds.Contains(cell);
        }

        /// <summary>칸의 중심 월드 좌표. 건물·하이라이트를 놓을 자리다.</summary>
        public Vector3 CellCenterWorld(GridPos cell, float y = 0f)
        {
            EnsureGrid();
            WorldXZ w = _grid.CellToWorld(cell);
            return transform.TransformPoint(new Vector3(w.X, y, w.Z));
        }

        /// <summary>풋프린트를 앵커에 놓았을 때의 중심. 짝수 크기도 정확히 가운데가 나온다.</summary>
        public Vector3 FootprintCenterWorld(GridPos anchor, GridFootprint footprint, float y = 0f)
        {
            EnsureGrid();
            WorldXZ w = footprint.CenterAt(anchor, _grid);
            return transform.TransformPoint(new Vector3(w.X, y, w.Z));
        }

        // ── 흙바닥 마스크 ─────────────────────────────────────────────
        // 건물 에셋에는 바닥이 없다(OlympusArt/background/art-direction.md — 바닥은 따로 만든다).
        // 대신 건물이 서 있는 풋프린트만큼 지면을 흙으로 바꾼다. 칸 하나당 1픽셀 마스크를
        // 굽고 Olympus/Ground 셰이더가 그걸 읽는다. 지면 메쉬·서브메쉬(황폐/녹지)는 그대로라
        // 복구 표현과 서로 간섭하지 않는다.

        private static readonly int DirtMaskId = Shader.PropertyToID("_OlympusDirtMask");
        private static readonly int DirtMaskRectId = Shader.PropertyToID("_OlympusDirtMaskRect");
        private static readonly int DirtCellSizeId = Shader.PropertyToID("_OlympusDirtCellSize");

        private Texture2D _dirtMask;
        private byte[] _dirtPixels;

        /// <summary>
        /// 건물이 선 자리를 흙바닥으로 칠한다. 넘긴 풋프린트 전체가 이번 상태다 —
        /// 빠진 건물 자리는 원래 지면으로 돌아간다. 건물 상태가 바뀔 때만 부르면 된다.
        /// </summary>
        public void SetDirtPatches(IEnumerable<(GridPos anchor, GridFootprint footprint)> patches)
        {
            EnsureGrid();
            EnsureDirtMask();

            System.Array.Clear(_dirtPixels, 0, _dirtPixels.Length);

            if (patches != null)
            {
                foreach (var (anchor, footprint) in patches)
                {
                    foreach (GridPos cell in footprint.CellsAt(anchor))
                    {
                        if (!_bounds.Contains(cell))
                            continue;

                        int px = cell.X - _bounds.MinX;
                        int py = cell.Y - _bounds.MinY;
                        _dirtPixels[py * _bounds.Width + px] = 255;
                    }
                }
            }

            _dirtMask.SetPixelData(_dirtPixels, 0);
            _dirtMask.Apply(false, false);
            PushDirtGlobals();
        }

        private void EnsureDirtMask()
        {
            if (_dirtMask != null && _dirtMask.width == _bounds.Width && _dirtMask.height == _bounds.Height)
                return;

            if (_dirtMask != null)
                DestroyObject(_dirtMask);

            _dirtMask = new Texture2D(_bounds.Width, _bounds.Height, TextureFormat.R8, false, true)
            {
                name = "OlympusDirtMask",
                // 바이리니어가 핵심 — 칸 경계에서 정확히 0.5가 나와야 셰이더가 사각형을 자른다.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            _dirtPixels = new byte[_bounds.Width * _bounds.Height];
        }

        private void PushDirtGlobals()
        {
            // 마스크 텍셀 중심 = 칸 중심이 되도록, 덮는 범위는 최소 칸의 바깥 코너부터 잡는다.
            WorldXZ minCenter = _grid.CellToWorld(new GridPos(_bounds.MinX, _bounds.MinY));
            float half = _grid.CellSize * 0.5f;
            Vector3 minCorner = transform.TransformPoint(new Vector3(minCenter.X - half, 0f, minCenter.Z - half));

            float sizeX = _bounds.Width * _grid.CellSize;
            float sizeZ = _bounds.Height * _grid.CellSize;

            Shader.SetGlobalTexture(DirtMaskId, _dirtMask);
            Shader.SetGlobalVector(DirtMaskRectId, new Vector4(minCorner.x, minCorner.z, 1f / sizeX, 1f / sizeZ));
            Shader.SetGlobalFloat(DirtCellSizeId, _grid.CellSize);
        }

        private static void DestroyObject(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        private void OnDestroy()
        {
            if (_dirtMask != null)
            {
                // 전역에 남겨 두면 다음 씬의 지면이 지워진 텍스처를 읽는다.
                Shader.SetGlobalVector(DirtMaskRectId, Vector4.zero);
                DestroyObject(_dirtMask);
            }

            if (_mesh != null)
                DestroyObject(_mesh);
        }
    }
}
