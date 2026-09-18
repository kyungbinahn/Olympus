# 레퍼런스 실측 — 뉴포리아(dfw)·고양이(Cat)에서 가져온 수치와 판단

Olympus는 두 레포를 포크하지 않고 **레퍼런스로만** 참조한다. 이 문서는 설계 수치를
어디서 가져왔는지, 무엇을 일부러 다르게 했는지 남긴다. 숫자를 바꿀 때 근거를 다시
찾지 않아도 되게 하는 것이 목적이다.

조사 시점 · CatClient `main` @ `9be15b7e` (2026-09-07) / `CatResource` @ `6fed9d741`
데이터 원본 · `CatResource\gamedata\json\data\FloatingIsland*.json`

---

## 기지 규모

| 항목 | 뉴포리아 실측 | Olympus 채택 |
|---|---|---|
| 시작 구역 건물 수 | **74채** | 30~40채 목표 |
| 풋프린트 | `LandSize 4→3×3`(44채) · `7→5×5`(18채) · `9→6×6`(12채) | **동일 3종 채택** |
| 건물 면적 합 | 1,278칸 | — |
| 배치 범위 | 약 70 × 94 월드 유닛 | — |
| **건물 밀도** | **약 26%** (나머지는 길·여백·장식) | 같은 밀도 가정 |
| 기지 크기 | (격자 아님, 아래 참조) | **48 × 48 칸** |
| 칸 크기 | `CELL_WORLD_SIZE = 1.15f` | **1.0 유닛** |
| 구역 | 4구역, `DistrictClear(N)`으로 순차 해금 | `ZoneId`로 지원 (지금은 전부 0) |

48×48 = 2,304칸. 밀도 26%면 약 600칸이 건물이고, 3×3~6×6 섞으면 30~40채다.
74채는 뉴포리아가 수년 운영하며 쌓인 전량이라 시작 규모로는 과하다. 그리고 폐허가
처음부터 전부 보이는 설계에서는 **한눈에 읽히는 크기**가 더 중요하다.

지면 메쉬 비용: 2,304 쿼드 = 9,216 정점 (16비트 인덱스 한계 65,000). 여유 충분.

## ★뉴포리아 건물은 격자 위에 없다

가장 중요한 발견이다. `FloatingIslandBuildData.StartPosition`이 격자 좌표가 아니라
**소수점 월드 좌표 문자열**이다:

```
"0_0_0"                      MainBuilding
"-17.15_0.4375_-46.075"      SubBuilding
"-21.1975_0_-32.5325"        WallManager
"99_0_99"                    SurvivorBuilding  ← "아직 안 놓음" 매직 센티넬
```

그래서 그쪽 배치 검증이 격자 점유가 아니라 float 바운즈 스캔 + 쌍별 겹침 비교다.
`TerritoryPlacementValidator`가 전 건물을 훑어 min/max를 구하고 여백 ±15를 더하고
그 결과를 세션 단위로 캐시하는 구조, 그리고 거기서 파생된 함정들
("영지 재진입 후 배치가 안 된다", "드래그가 안 잡히는데 로그가 한 줄도 안 남는다")이
모두 이 자유 배치 전제에서 나온다.

→ **좌표를 그대로 베낄 수 없다.** 격자 레이아웃은 새로 저작해야 한다.
→ 대신 우리 격자 방식에서는 그 함정군이 원리적으로 발생할 수 없다.

## 건물 타입 로스터 (37종, 콘텐츠 참고용)

`AutoProductionFacilities`(15) `ClassBuilding`(6) `Squad`(4) `Barracks`(4)
`Hospital`(3) `Ground`(3) `SubBuilding`(3) `LinearPlayerUpgrade`(2) `AttackTower`(2)
`Technology`(2) `WoodCutterAcademy`(2) `WoodCollectorAcademy`(2) —
그리고 각 1종: `MainBuilding` `MainBuildingSub` `WareHouse` `ResourceStorage`
`WallManager` `GuardianSpawn` `MinionSpawn` `GoldGarden` `LivestockFarm`
`GachaBuilding` `Farm` `Arena` `HallofLegend` `Airship` `StoryAirShip`
`Telescope` `DungeonBuilding` `MysteryMissionBuilding` `EquipProduction`
`GuildBuilding` `VipShop` `ScheduleBuilding` `FreeBuildTime`
`PremiumBarrackSpeedUp` `PremiumBuildingSpeedUp` `SurvivorBuilding`

SLG 기지가 무엇으로 채워지는지의 실물 목록이다. 다만 이름 다수가 뉴포리아 세계관에
묶여 있으니(`Airship`·`Telescope`·`GoldGarden`·`FloatingIsland`) 그릭로만에서는
기능만 가져오고 이름은 새로 짓는다.

## 미건설 자리의 표현 — 우리가 다르게 하는 지점

| | 뉴포리아 | 고양이 | **Olympus** |
|---|---|---|---|
| 자리 결정 | 데이터(+플레이어 이동) | 플레이어 자유 배치 | **데이터 고정** |
| 미건설 상태 | **빈 터**로 서 있음 | 없음(카드에서 꺼냄) | **반파 건물**로 서 있음 |

뉴포리아는 `SetVisualOnInit`이 `_level >= 1`에서만 모델을 붙여, 해금됐지만 안 지은
건물이 빈 터로 서 있다. 우리는 같은 자리에 폐허 모델을 세운다 —
배경 아트 디렉션의 **"진행 = 복구"** 원칙(황폐한 세계를 복구하며 진행)과 같은 방향이다.

## 저작 툴에 대한 교훈

뉴포리아에는 위치 저작 씬이 **있었고 죽었다**:
`Assets\TT\Scenes\BuildingPosScene.unity`(787KB)가 그 씬이고,
`Assets\TT\Editor\SaveTerritoryData.cs`·`BuildingPosSceneCleaner.cs`는 전체가
주석 처리된 채 존재하지 않는 `Assets/TT/Resources/csv/FloatingIslandData.xlsx`를
가리키고 있다.

반대로 고양이의 맵 저작 툴(`Edit3DMapWindow`, 10파일 2,157행)은 **살아 있고 쓰인다** —
출력이 `Game/Chapter/District_N/` 에셋으로 나가고 런타임이 그것을 읽기 때문이다.

→ 기지 레이아웃 툴을 만들 때: **툴의 산출물이 곧 런타임이 읽는 유일한 원본**이어야 한다.
툴 전용 중간 형식을 두면 런타임 경로와 갈라지고, 갈라진 쪽이 먼저 죽는다.
그래서 레이아웃을 JSON으로 두기로 한다 — Core가 UnityEngine 없이 읽을 수 있고,
사람이 diff로 검토할 수 있고, 씬 툴이 같은 파일을 쓴다.

## 관련 코드에 남긴 근거

수치·판단의 이유는 해당 코드 주석에도 적어 뒀다:

- `Assets/Core/Grid/GridPos.cs` — 헥스 q/r을 안 물려받은 이유
- `Assets/Core/Grid/GridFootprint.cs` — 앵커를 최소 코너로 고정한 이유
- `Assets/Core/Grid/GridRect.cs` — 건설 범위를 데이터로 선언한 이유
- `Assets/Core/Grid/SquareGrid.cs` — 좌표 변환에 보정이 없는 이유
- `Assets/Core/State/StateDelta.cs` — 델타 봉투를 `GeneralInfoDto`에서 가져온 이유
- `Assets/Core/Territory/BaseLayout.cs` — 로드 시점 검증이 필요한 이유
- `Assets/Game/Territory/TerritoryCamera.cs` — 광고의 레인 카메라를 안 쓰는 이유
- `Assets/Editor/TerritorySceneBuilder.cs` — 씬을 손으로 안 쓰는 이유

---

## 뉴포리아 건물 수치 (2026-09-14 조사)

올림푸스의 임시 밸런스가 무근거였다. 뉴포리아의 실제 라이브 테이블을 조사해서
그 곡선 위에 다시 올렸다. 여기 적힌 게 `Assets/Data/building-defs.json`의 근거다.

### 데이터가 어디 있나

- `C:\Users\user\dfw-resource\gamedata\json\data\` (클라이언트에서 `dfw-client\JsonData`로 심볼릭 링크)
- `FloatingIslandBuildLevelData.json` — **1,975행**. 레벨별 비용·시간·조건·효과. 여기가 본체다.
- `FloatingIslandBuildData.json` — 건물 종류·해금 조건·풋프린트
- `ScheduleData.json` — 서버 오픈 후 D+N 컨텐츠 일정 (520행)
- 원본 xlsx는 그 레포에 없다(빌드된 json만 배포). 수식·기획 주석은 볼 수 없다.
- **비용·시간은 서버가 주는 값이 아니라 클라이언트가 읽는 정적 테이블이다.** 서버는 같은 테이블을 공유한다.
- `BuildTime` 단위는 **초**다.

### 발견 1 — 시간은 자유값이 아니라 공유 "사다리"다

1,975행의 BuildTime이 소수의 값에 몰려 있다:
`2, 3, 5, 10, 12, 15, 45, 47, 117, 176, 351, 702, 2106, 4212, 8424…`
(47이 38회, 117이 48회, 702가 41회 재사용). 대략 **×2 / ×3을 번갈아 가는 계단**이고
여러 건물이 그 계단을 공유한다.

→ 올림푸스도 건물마다 제각각인 숫자를 쓰지 않는다. 사다리
`0 / 2 / 5 / 10 / 15 / 45 / 120초`를 만들고 건물마다 **칸만 지정**한다.
그래야 나중에 기획이 곡선 전체를 한 번에 옮길 수 있다.

### 발견 2 — 초반은 전부 "초" 단위다 (올림푸스가 틀렸던 지점)

튜토리얼 본부(생명의 묘목) Lv1~15가 전부 초 단위다:
목재 10 / 0초 → 20 / 3초 → 30 / 3초 → 200 / 10초 → … → Lv15 목재 1,800 + 금화 200 / 300초.
**본부 Lv5(702초)를 넘어서야 분 단위 대기가 시작된다.**

다른 건물 Lv1도 마찬가지로 싸고 빠르다 — 창고 목재 20 / 5초, 벌목훈련소 30 / 5초,
성벽 200 / 2초, 병영 500 / 2초, 금광 300 / 2초.

→ 고치기 전 올림푸스는 첫 건물이 45초~10분이었다. 뉴포리아 기준으로 **Lv4~6에 해당하는
수치를 Lv1 자리에 넣고 있었다.** 폐허 복구는 "Lv1 건설"이므로 그 대역으로 내렸다.

### 발견 3 — 본부 레벨이 모든 것의 상한이다

- `BuildConditionList`에 `BuildFloatingIslandBuilding(1, N, false)`이 **1,105회** 나온다.
  즉 **모든 하위 건물의 최대 레벨 = 본부 레벨** (1:1 캡).
- 신규 건물 해금도 본부 레벨이다 — 병원 본부2, 금광 본부3, 길드 연회장 본부5,
  투기장 본부9, 대장간·던전 본부10, 전설의 전당 본부24.
- 본부 업글에는 추가 게이트가 붙는다(던전 N층 클리어, 특정 건물 레벨).
  본부만 올려서 건너뛰는 것을 막는 장치다.
- 가디언 최대 레벨 = 본부 레벨 × 5 (`ConstantData.CharacterLevelLimitCondition = "1,5"`).

### 발견 4 — 자원과 생산

- 기본 3종: **목재(키 4) · 금화(1) · 과일(1301)**. 올림푸스는 여기에 석재를 더해 4종이다
  (그리스·로마는 대리석이 주재료라 석재가 테마에 맞는다).
- 시간당 생산은 `AutoProductionFacilities` 타입의 `ParamList`.
  금광 Lv1 금화 5,856/h → Lv2 11,904 → … Lv6 38,016. **거의 선형**(레벨당 +6,000 부근).
- 저장 한도는 창고 `ParamList`: 30 → 40 → 60 → 100 → 150 → 200 → 300.
- 성벽 HP: 150,000 → 200,000 → 250,000 → 300,000 → 350,000.

### 발견 5 — 서버 오픈 후 D+N 일정이 미리 깔려 있다

`ScheduleData.json`의 `OpenDate` = 서버 오픈 후 경과일.

- **D+1에 27건** 집중 (스타터 이벤트, 가디언 뽑기, 배틀패스, 연속 출석…)
- D+2 요일 보스·투기장 / D+3 요새 수호자 / **D+4 농장 해금**(건물 해금이 일정에 묶여 있다)
- D+5·7·10·13·17·21에 요새전 1→6등급 (3~4일 간격)
- D+24 서버 대전 / D+25 공성전 / D+30 길드 대결
- D+1~30에 약 130건 집중 → 이후 주 1~2건 → D+750 이후는 30일 간격으로 **D+1441까지**(약 4년치)

→ 건물 해금이 본부 레벨뿐 아니라 **달력**에도 묶인다는 게 중요하다.
올림푸스에 이걸 넣을지는 아직 정하지 않았다.

### 확인 못 한 것

- 원본 xlsx가 없어 수식·기획 의도는 볼 수 없다.
- 스케줄의 **종료일**은 `ScheduleData`에 없다(`EventData.json` 계열로 추정, 미확인).
- 본부 `ParamList` 2,000,000의 의미는 추정만 했다(본부 HP로 보이나 파서에서 확정 못 함).
