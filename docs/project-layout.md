# 폴더 구조와 규칙

파일을 어디에 둘지 헷갈릴 때 이 문서를 본다. 규칙마다 왜 그런지 근거를 달았다 —
근거를 모르면 규칙이 먼저 무너진다.

## 구조

```
Assets/
  Core/       Olympus.Core        순수 C# 로직. UnityEngine 참조 금지(asmdef이 강제).
  Game/       Olympus.Game        게임플레이. Unity 의존.
  UI/         Olympus.UI
  Editor/     Olympus.Editor      에디터 전용 (includePlatforms: Editor)
  Tests/      Olympus.Core.Tests  EditMode 시험

  Data/       JSON 원본 — 사람이 고치고, 툴이 쓰고, 런타임이 읽는 같은 파일
  Art/        아트 실파일
    Buildings/  건물 모델
    Env/        환경·지형
    UI/         스프라이트·아이콘
    VFX/
  Prefabs/    조립된 프리팹
  Scenes/
  Settings/   URP·렌더 파이프라인 설정 (Unity 템플릿이 만든 자리)

tools/
  DomainTests/  Unity 밖 dotnet test 하네스
docs/
```

번호 접두사(Cat의 `00_Scene`·`10_Scripts`·`20_UI`)를 쓰지 않는다. asmdef가 이미
`Core/`·`Game/`에 붙어 있어 개명하면 GUID churn만 생기고, 이름 열 개는 번호 없이 읽힌다.

## 규칙 1 — `Data/`가 사람이 고치는 데이터의 유일한 집

JSON으로 두고, **툴 산출물과 런타임 입력이 같은 파일**이어야 한다.

근거: 뉴포리아에는 기지 위치 저작 씬이 있었고 죽었다
(`Assets\TT\Scenes\BuildingPosScene.unity` 787KB는 남아 있는데,
`Assets\TT\Editor\SaveTerritoryData.cs`가 전체 주석 처리된 채 존재하지 않는
`Assets/TT/Resources/csv/FloatingIslandData.xlsx`를 가리킨다).
반대로 Cat의 맵 저작 툴은 살아 있다 — 산출물이 런타임이 읽는 원본이기 때문이다.

툴 전용 중간 형식을 두면 런타임 경로와 갈라지고, 갈라진 쪽이 먼저 죽는다.

JSON을 고른 이유: 사람이 diff로 검토할 수 있고, Core가 UnityEngine 없이 다룰 수 있는
평평한 형식이다. 파싱만 `Game/`에 얇게 두고(JsonUtility는 UnityEngine), 검증·규칙은
Core가 한다(`BaseLayout.Validate`).

## 규칙 2 — `Art/`에는 완성된 것만 들어온다

아트 원본은 `D:\그릭로만`에 있고 거기서 생성·반복한다. Unity 프로젝트에는
**최종 산출물만** 가져온다.

근거: 그 폴더에는 생성 반복물·`pick-batch*`·decimate 중간물이 계속 쌓인다.
그게 `Assets/` 안에 들어오면 임포트 시간과 레포가 같이 부푼다.
Cat도 같은 결론에 도달했다 — `Assets/Art/`는 실파일 폴더이고 생성 원본은 CatAsset에 둔다.

## 규칙 3 — `Resources/` 폴더를 만들지 않는다

근거: Cat이 확정한 함정(D4, 2026-09-04). `Resources`에 둔 것은 **무조건 빌드에 실리고**,
나중에 핫업데이트(HybridCLR)로 넘어갈 때 Script Missing이 된다. 지금은 멀쩡해 보이지만
그 시점에 드러난다.

지금 프로젝트에는 `Resources/` 폴더가 없다. 그 상태를 유지한다.
로딩은 **직렬화 참조**(컴포넌트의 `[SerializeField] TextAsset` 등)로 하고, 규모가 커지면
Addressables로 올린다. 참조로 물면 Unity가 의존성을 알아서 챙기고, 빌드에 실릴지도
그 참조가 결정한다.

⚠️ 뉴포리아의 영지는 `Assets/TT/Resources/` 상대경로 상수 약 35개에 묶여 있어
폴더를 옮기면 죽는다(Resources.Load는 실패해도 예외 없이 null을 준다).
그 상태가 되지 않게 처음부터 참조 방식으로 간다.

## 규칙 4 — 머티리얼·텍스처는 쓰는 것 옆에

`Settings/`에는 URP 렌더 설정만 둔다(Unity 템플릿이 만든 자리).
우리가 만드는 머티리얼·텍스처는 그것을 쓰는 아트/프리팹 옆에 둔다.

예외: 그레이박스 지면 머티리얼은 아직 `Settings/`에 있다. 실제 지면 아트가 들어오면
`Art/Env/`로 옮긴다.

## 코드를 어디에 둘까 — 판단 한 줄

**UnityEngine이 필요한가?**

- 아니오 → `Core/`. 규칙·계산·상태 모델이 여기다. `dotnet test`로 Unity 없이 검증된다.
- 예 → `Game/`(게임플레이) 또는 `UI/`(화면) 또는 `Editor/`(에디터 도구).

Core에 두면 시험이 붙고, Unity를 열지 않고 검증된다. 그래서 **판단이 가는 로직은
되도록 Core로 민다.** 반대로 Core에 UnityEngine을 끌어들이려는 순간
asmdef의 `noEngineReferences: true`가 컴파일 에러로 막는다 — 관례가 아니라 강제다.
