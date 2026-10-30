using System;
using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Battle;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.Territory;

namespace Olympus.Game.Territory
{
    /// <summary>건물 아트 보정 한 줄. 편집 툴이 쓰고 게임이 읽는 같은 파일이다.</summary>
    [Serializable]
    public sealed class BuildingArtRecord
    {
        public string defId;

        /// <summary>이 건물만 더 돌리는 각도(도).</summary>
        public float yaw;

        /// <summary>칸에 맞춘 크기에 곱하는 값.</summary>
        public float scale = 1f;

        /// <summary>자리 중앙에서 밀어낸 거리(월드 단위).</summary>
        public float offsetX;
        public float offsetY;
        public float offsetZ;
    }

    [Serializable]
    public sealed class BuildingArtFile
    {
        public List<BuildingArtRecord> buildings = new List<BuildingArtRecord>();
    }

    [Serializable]
    public sealed class BaseSlotRecord
    {
        public int slotId;
        public string defId;

        /// <summary>점유 영역의 최소 코너 칸.</summary>
        public int x;
        public int y;

        /// <summary>확장 구역 번호. 0은 처음부터 열린 구역.</summary>
        public int zoneId;
    }

    [Serializable]
    public sealed class BaseLayoutFile
    {
        public int baseSize = 48;
        public List<BaseSlotRecord> slots = new List<BaseSlotRecord>();
    }

    /// <summary>
    /// JSON 파일을 Core 객체로 바꾼다.
    ///
    /// 파싱이 Core가 아니라 여기(Game)에 있는 이유 — Core는 UnityEngine을 참조하지 않아야
    /// <c>dotnet test</c>로 Unity 없이 돌 수 있다. JsonUtility는 UnityEngine이므로 Core에
    /// 들어갈 수 없다. 그래서 얇은 변환층만 이쪽에 두고, 검증·규칙은 전부 Core가 한다
    /// (<see cref="BaseLayout.Validate"/>).
    ///
    /// 형식을 JSON으로 고른 이유 — 사람이 diff로 검토할 수 있고, 나중에 만들 배치 툴이
    /// **같은 파일**을 쓰기 때문이다. 뉴포리아의 위치 저작 씬이 죽은 원인이 툴 산출물과
    /// 런타임이 읽는 원본이 갈라진 것이었다(툴은 없는 xlsx를 가리킨 채 주석 처리돼 있다).
    /// 툴이 런타임 원본을 직접 쓰면 그렇게 갈라질 수 없다.
    /// </summary>
    public static class TerritoryDataLoader
    {
        public static BuildingCatalog LoadCatalog(TextAsset json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json), "건물 정의 파일이 배선되지 않았다.");

            BuildingDefFile file = JsonUtility.FromJson<BuildingDefFile>(json.text);

            if (file == null)
                throw new InvalidOperationException("건물 정의를 읽을 수 없다: " + json.name);

            return file.ToCatalog(json.name);
        }

        /// <summary>
        /// 진척 규칙(본부 레벨·임무·기능 해금)을 읽고 건물 정의와 맞는지 확인한다.
        /// 어긋나면 무엇이 문제인지 전부 대며 터진다 — 달성할 수 없는 조건은 게임을 멈추지 않고
        /// 그 자리에서 영원히 막히게 만드므로, 거기까지 플레이해서 발견하기 전에 켜는 순간 잡는다.
        /// </summary>
        public static ProgressionConfig LoadProgression(
            TextAsset progression, TextAsset quests, TextAsset features, BuildingCatalog catalog)
        {
            if (progression == null)
                throw new ArgumentNullException(nameof(progression), "progression.json이 배선되지 않았다.");

            ProgressionConfig config = ProgressionFileMapper.ToConfig(
                JsonUtility.FromJson<ProgressionFile>(progression.text),
                quests == null ? null : JsonUtility.FromJson<QuestFile>(quests.text),
                features == null ? null : JsonUtility.FromJson<FeatureUnlockFile>(features.text));

            List<string> problems = config.Validate(catalog);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "진척 데이터가 맞지 않는다 (" + problems.Count + "건):\n  - " +
                    string.Join("\n  - ", problems));
            }

            return config;
        }

        /// <summary>전투 규칙을 읽고 건물·진척 데이터와 맞는지 확인한다. 어긋나면 전부 대며 터진다.</summary>
        public static BattleConfig LoadBattle(TextAsset json, BuildingCatalog catalog, ProgressionConfig progression)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json), "battle.json이 배선되지 않았다.");

            BattleFile file = JsonUtility.FromJson<BattleFile>(json.text);
            if (file == null)
                throw new InvalidOperationException("전투 규칙을 읽을 수 없다: " + json.name);

            BattleConfig config = file.ToConfig();
            List<string> problems = config.Validate(catalog, progression);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "전투 데이터가 맞지 않는다 (" + problems.Count + "건):" + Environment.NewLine + "  - " +
                    string.Join(Environment.NewLine + "  - ", problems));
            }

            return config;
        }

        /// <summary>
        /// 1장 규칙(훈련·거인)을 읽는다. 배선이 없으면 빈 규칙 — 거인도 훈련도 없는 옛 기지로 돈다(경고만).
        /// 모르는 병사·건물을 가리키면 여기서 터진다.
        /// </summary>
        public static Olympus.Core.Chapter.ChapterConfig LoadChapter(TextAsset json, BattleConfig battle, BuildingCatalog catalog)
        {
            if (json == null)
            {
                Debug.LogWarning("chapter1.json이 배선되지 않았다 — 훈련·거인 없이 돕니다. Olympus/Setup/기지 씬 만들기를 다시 누르세요.");
                return Olympus.Core.Chapter.ChapterConfig.Empty;
            }

            var file = JsonUtility.FromJson<Olympus.Core.Chapter.ChapterFile>(json.text);
            if (file == null)
                throw new InvalidOperationException("장 규칙을 읽을 수 없다: " + json.name);

            Olympus.Core.Chapter.ChapterConfig config = file.ToConfig();
            List<string> problems = config.Validate(battle, catalog);

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "장 데이터가 맞지 않는다 (" + problems.Count + "건):" + Environment.NewLine + "  - " +
                    string.Join(Environment.NewLine + "  - ", problems));
            }

            return config;
        }

        /// <summary>
        /// 건물 아트 보정을 읽는다. 파일이 없으면 빈 표 — 보정이 없다는 뜻이고,
        /// 그때는 모든 건물이 칸에 꽉 맞는 기본 포즈로 뜬다. 여기서 터지면 안 된다.
        /// </summary>
        public static BuildingArtTable LoadArtTable(TextAsset json)
        {
            var table = new BuildingArtTable();

            if (json == null)
                return table;

            BuildingArtFile file = JsonUtility.FromJson<BuildingArtFile>(json.text);

            if (file == null || file.buildings == null)
                throw new InvalidOperationException("건물 아트 보정을 읽을 수 없다: " + json.name);

            for (int i = 0; i < file.buildings.Count; i++)
            {
                BuildingArtRecord r = file.buildings[i];

                if (r == null || string.IsNullOrEmpty(r.defId))
                    continue;

                // 값 검증은 Core가 한다 — 배율 0 같은 값이 여기서 걸러지지 않으면
                // 그 건물만 화면에서 사라지고 원인을 아트에서 찾게 된다.
                table.Add(new BuildingArtAdjustment(
                    r.defId, r.yaw, r.scale, r.offsetX, r.offsetY, r.offsetZ));
            }

            return table;
        }

        /// <summary>
        /// 편집 툴이 저장할 때 쓰는 형식. 게임이 읽는 파일과 같은 형식이어야 하므로
        /// 쓰는 쪽도 여기에 둔다 — 형식이 두 군데로 갈라지면 갈라진 쪽이 먼저 죽는다.
        /// </summary>
        public static string ToJson(BuildingArtTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            var file = new BuildingArtFile();

            foreach (BuildingArtAdjustment a in table.All)
            {
                file.buildings.Add(new BuildingArtRecord
                {
                    defId = a.DefId,
                    yaw = a.YawDegrees,
                    scale = a.ScaleMultiplier,
                    offsetX = a.OffsetX,
                    offsetY = a.OffsetY,
                    offsetZ = a.OffsetZ,
                });
            }

            return JsonUtility.ToJson(file, true);
        }

        public static BaseLayoutFile LoadLayoutFile(TextAsset json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json), "기지 레이아웃 파일이 배선되지 않았다.");

            BaseLayoutFile file = JsonUtility.FromJson<BaseLayoutFile>(json.text);

            if (file == null || file.slots == null)
                throw new InvalidOperationException("기지 레이아웃을 읽을 수 없다: " + json.name);

            return file;
        }

        /// <summary>
        /// 기지 배치를 파일 형식으로 되돌린다. 저작 툴이 씬에서 읽은 자리를 저장할 때 쓴다.
        /// 읽는 쪽과 같은 파일에 둔다 — 형식이 두 군데로 갈라지면 갈라진 쪽이 먼저 죽는다.
        /// </summary>
        public static string ToJson(BaseLayoutFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            return JsonUtility.ToJson(file, true);
        }

        /// <summary>
        /// 레이아웃 파일을 Core <see cref="BaseLayout"/>으로 바꾼다.
        /// 유효성 검사는 하지 않는다 — 그건 Core의 <see cref="BaseLayout.Validate"/> 몫이고
        /// <see cref="ConstructionService.InitializeFromLayout"/>이 부른다.
        /// </summary>
        public static BaseLayout ToLayout(BaseLayoutFile file, GridRect bounds)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            var layout = new BaseLayout(bounds);

            for (int i = 0; i < file.slots.Count; i++)
            {
                BaseSlotRecord s = file.slots[i];
                layout.Add(new BaseSlot(s.slotId, s.defId, new GridPos(s.x, s.y), s.zoneId));
            }

            return layout;
        }
    }
}
