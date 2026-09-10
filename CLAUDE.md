# Olympus (그리스·로마 SLG) — AI 작업 규칙

## 0. 프로젝트 지도

- **제로베이스 프로젝트다.** 사내 기존 게임(`뉴포리아`/`고양이`)을 포크하지 않았다.
  두 레포는 **레퍼런스로만** 참조하고 필요한 것만 그때그때 가져온다.
  - `C:\Users\user\CatClient` — 고양이(Cat). Unity 6000.3.16f1. `CatClient\CLAUDE.md`가 훌륭한 지도다.
  - `C:\Users\user\dfw-client` — 뉴포리아. 라이브 서비스 중. Unity 2022.3.62f2.
  - 무엇을 가져올 가치가 있고 무엇을 피해야 하는지는 [`docs/reference-findings.md`](docs/reference-findings.md).
- 아트 원본은 **형제 폴더** `D:\OlympusArt`에 있다(별도 git 레포). Unity 프로젝트 안에 넣지 않는다.
  건물 로스터·화풍·프롬프트가 거기 있고, `Assets/Data/building-defs.json`의 `artCode`가 둘을 잇는다.
- 폴더 규칙과 그 근거는 [`docs/project-layout.md`](docs/project-layout.md). **파일을 어디 둘지 헷갈리면 먼저 읽는다.**
- 사용자는 **비프로그래머**다. 보고는 쉬운 일상어로, 전문용어는 처음 등장 시 풀어서 쓴다.
  호칭은 **형님**.

## 1. 시험 — Unity를 열지 않고 검증한다

```
1_CoreTest.bat            (또는)  dotnet test tools\DomainTests\DomainTests.csproj
```

`Assets/Core`는 asmdef에 **`noEngineReferences: true`** 가 걸려 있어 UnityEngine을 참조할 수 없다.
그래서 같은 파일을 Unity 밖에서 컴파일할 수 있고, 순수 로직이 `dotnet test`로 검증된다.

- **판단이 가는 로직은 되도록 `Core`로 민다.** 그러면 시험이 붙는다.
- `dotnet`이 PATH에 없을 수 있다 → `C:\Program Files\dotnet\dotnet.exe` 풀패스를 쓴다.
- ⚠️ **이 하네스가 통과했다고 Unity 컴파일까지 보장되지 않는다.** 조립체 배선과 API 호환은
  Unity가 봐야 안다. 실제로 밟았다(§4 NUnit 항목).

## 2. 코딩 규칙

1. **C# 9까지만 쓴다.** Unity 6000.3의 상한이다. 파일 스코프 namespace(C#10)·`record struct`(C#10)·
   `required`(C#11)·클래스 주 생성자(C#12) 전부 금지. 하네스의 `LangVersion`도 9.0으로 고정돼 있다.
2. **`Core`에 UnityEngine을 끌어들이지 않는다.** 컴파일러가 막지만, 막히면 설계를 다시 본다 —
   보통 그 로직이 Core에 있으면 안 되는 게 아니라, Unity 타입을 쓰지 않게 바꿀 수 있다.
   (좌표는 `WorldXZ`, 시간은 `IClock`처럼)
3. **상태를 바꾸는 길은 `StateStore.Apply(StateDelta)` 하나다.** `BuildingInstance`·`ResourcePool`의
   setter가 `internal`이라 Game·UI에서는 읽기만 된다. 이 규약이 나중에 서버를 붙일 때의 비용을 결정한다.
4. **"지금"은 `IClock`만 지난다.** 타이머는 남은 시간을 세지 말고 **끝나는 시각**을 들고 비교한다.
   앱이 백그라운드에 있어도 결과가 같고, 서버 시각으로 바꿔도 계산식이 그대로다.
5. **거부에는 이유를 붙인다.** `PlacementRejection`·`BuildRejection`처럼. 이유가 없으면 UI가
   사용자에게 무엇이 문제인지 말할 수 없고 무음 실패로 남는다.
6. 주석은 코드가 스스로 보여줄 수 없는 **제약과 이유**만 적는다. 특히 "왜 이렇게 안 했나"를 남긴다.

## 3. Unity 작업 방식

- **씬·프리팹을 손으로 쓰지 않는다.** `.unity`/`.prefab`은 GUID·fileID로 얽힌 YAML이라
  손으로 만들면 조용히 깨진다. 대신 **에디터 메뉴 명령**으로 조립한다:
  `Olympus/Setup/기지 씬 만들기` (`Assets/Editor/TerritorySceneBuilder.cs`)
  재실행 가능하고, 코드라서 diff로 읽히고 리뷰된다.
- 새 `.cs`를 만들면 `.meta`는 Unity가 생성한다. 사용자가 Unity를 한 번 열어야 하고, 그 다음 커밋한다.
- 입력은 **새 Input System API**를 직접 읽는다(`Mouse.current`·`Touchscreen.current`·`Keyboard.current`).
  프로젝트가 `activeInputHandler: 1`(새 시스템 전용)이라 레거시 `Input.*`은 동작하지 않는다.
- UI는 **UGUI + TextMeshPro**. 어셈블리 이름은 `UnityEngine.UI`·`Unity.TextMeshPro`
  (Unity 6에서 TMP가 `com.unity.ugui`에 합쳐졌다).

## 4. 함정 목록 (이미 밟은 것들)

- **`.gitignore` 빌드 패턴에는 루트 앵커(`/`) 필수.** 앵커 없이 `*.csproj`로 뒀다가 손으로 쓴
  `tools/DomainTests/DomainTests.csproj`가 통째로 미추적이 됐다(커밋 두 개에 빠져 있었고,
  새로 clone하면 시험이 아예 안 돌았을 상태였다).
- **`Resources/` 폴더를 만들지 않는다.** Cat이 확정한 함정 — 거기 둔 것은 무조건 빌드에 실리고
  핫업데이트 시점에 Script Missing이 된다. 로딩은 직렬화 참조(`[SerializeField] TextAsset` 등)로.
- **`Does.Not.Contain`을 컬렉션에 쓰지 않는다.** `dotnet test`(NUnit 3.14)에서는 통과하는데
  Unity가 물고 있는 NUnit에서는 문자열 오버로드만 잡혀 컴파일이 깨진다.
  → 컬렉션 단정은 `Has.Member` / `Has.No.Member`.
- **`.ps1`은 ASCII만.** Windows PowerShell 5.1이 BOM 없는 `.ps1`을 ANSI(CP949)로 읽어
  한글 주석이 깨지고 파싱까지 실패한다. `.bat`도 같다(cmd.exe는 OEM 코드페이지).
- **커밋 메시지는 한글로 쓰되 `git commit -F <파일>`을 쓴다.** PowerShell에서 한글이 깨진다.
  ⚠️ **메시지 파일 경로를 짧게 둔다** — 스크래치패드 긴 경로는 git이 "Filename too long"으로 거부한다.
  `C:\Users\user\AppData\Local\Temp\olympus-msg.txt` 정도.
- **뷰가 갱신을 잊는 배선을 만들지 않는다.** 녹지 재계산을 "타이머 만료" 경로에만 붙였다가
  건설시간 0인 건물에서 녹지가 안 뜨는 버그가 났다. 화면으로만 보고 있었으므로
  "땅 머티리얼이 안 붙었나?"로 보였다. → 배선을 `TerritoryRuntime`(Core)으로 옮겨
  상태 통지에 묶고 시험을 붙였다. **경로별로 갱신을 붙이는 방식 자체가 원인이다.**
- **로직과 뷰가 같은 인스턴스를 쓰게 한다.** `RestorationMap`을 로직과 뷰가 각각 만들면
  갈라져서 "로직은 복구했는데 화면은 황폐"가 된다. 소유자를 한쪽으로 못 박는다.
- **보이는 것과 논리가 어긋나지 않게 검증을 넣는다.** `TerritoryPresenter`는 지면의 `baseSize`와
  레이아웃 파일의 `baseSize`가 다르면 기동 시점에 터진다. 어긋나면 조용히 틀린다.
- **한글 폴더 경로를 파이프라인에 넣지 않는다.** 아트 쪽에서 `retopo*.ps1`이 한글 경로로 깨지고
  산출물이 엉뚱한 드라이브에 생긴 사고가 있었다(`D:\OlympusArt\배경\HANDOFF.md` 기록).
  그래서 아트 폴더를 `D:\그릭로만` → `D:\OlympusArt`로 옮겼다.

## 5. 데이터

- `Assets/Data/*.json`이 **사람이 고치는 원본**이다. 나중에 만들 배치 툴도 여기에 쓰고,
  런타임도 같은 파일을 읽는다. **툴 산출물과 런타임 입력이 갈라지면 갈라진 쪽이 먼저 죽는다**
  (뉴포리아의 위치 저작 씬이 그렇게 죽었다 — 존재하지 않는 xlsx를 가리킨 채 주석 처리됨).
- 파싱은 `Game`(JsonUtility는 UnityEngine), **검증은 `Core`**(`BaseLayout.Validate`).
- 건물 로스터는 아트(`D:\OlympusArt\배경\gen-buildings.sh`)와 **반드시 일치해야 한다.**
  `artCode`(B01·C12 등)가 링크다. 아트 로스터가 바뀌면 `tools/seed-layout/gen-layout.ps1`도 바꾼다.
- 초기 레이아웃은 `tools/seed-layout/`이 씨를 뿌린 것이다. **저작 툴이 아니다** —
  실제 저작 경로는 씬에서 손으로 옮기고 같은 JSON에 저장하는 Unity 툴이고, 아직 안 만들었다.

## 6. 작업 진행

- **코드를 고치면 `1_CoreTest.bat`으로 확인하고 결과를 숫자로 보고한다.** 사용자가 비프로그래머라
  "통과했습니다"에 근거가 붙어야 한다.
- 화면으로만 판단해야 하는 것(색·크기·간격·연출)은 **사용자가 눈으로 정한다.**
  인스펙터에서 돌릴 수 있게 만들고(예: 녹지 반경 슬라이더), 기다리지 않게 디버그 단축키를 준다
  (플레이 중 `F` 공사 즉시 완료 / `R` 전체 즉시 복구).
- **커밋·푸시는 해도 되지만, 되돌리기 어려운 것(파일 이동·삭제)은 먼저 묻는다.**
- 마일스톤: ①인게임 HUD ②기지 땅 + 건물(**완료**) ③월드맵 생성.
