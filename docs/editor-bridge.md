# 에디터 브리지 — AI가 Unity를 직접 조작하고 확인하는 통로

## 왜 만들었나

코드는 AI가 고치는데 Unity 화면은 사람만 볼 수 있었다. 그래서 고칠 때마다 사람이
"Unity 열기 → 조립 메뉴 누르기(여덟 개 중 맞는 것) → 플레이 → 건물 눌러 보기 → 캡처해서 보내기"를 했다.
조작과 확인을 AI 쪽으로 넘기려고 두 가지를 넣었다(2026-10-23).

1. **자동 재조립** (`Assets/Editor/SceneAutoRebuild.cs`) — 조립 코드·UI 그림·글꼴 내용이 바뀌면
   코드가 다시 읽힐 때 해당 씬을 저절로 다시 짓는다. 지문(내용 해시)은 `Library/OlympusSceneBuildStamp.txt`.
   열린 씬에 저장 안 한 변경이 있으면 **짓지 않고 미룬다**(저장하면 이어서 한다) — 사람의 작업을 묻지 않고 날리지 않게.
   손으로 돌리고 싶으면 `Olympus/Setup/바뀐 씬 지금 다시 조립` / `모든 씬 강제로 다시 조립`.
   고친 조립 코드가 **아직 컴파일 전**이면 짓지 않는다 — 플레이를 끈 순간 옛 코드로 지어 놓고 새 코드의 지문을 남긴 일이
   있었다(2026-10-26). 그래서 코드가 읽힐 때 .cs 내용을 기억해 두고, 디스크와 다르면 새로고침만 밀고 기다린다.
2. **브리지** (`Assets/Editor/EditorBridge.cs` + `tools/editor-bridge/ub.sh`) — 파일로 명령을 넣으면 에디터가
   실행하고 결과를 파일로 돌려준다. 포트·외부 패키지·서버가 없다.

## 쓰는 법

```bash
U=tools/editor-bridge/ub.sh
bash $U refresh                 # 바뀐 파일 가져오기 + 컴파일 (Unity 창이 뒤에 있어도 됨)
UB_TIMEOUT=180 bash $U wait     # 컴파일·가져오기가 끝날 때까지
grep "컴파일 오류" Temp/EditorBridge/console.log
bash $U play
bash $U call "Olympus.Editor.BridgeActions.ListSlots"
bash $U call "Olympus.Editor.BridgeActions.SelectSlot 19"
bash $U call "Olympus.Editor.BridgeActions.Click RepairButton"
bash $U shot popup               # → Temp/EditorBridge/shots/popup.png (플레이 중에만)
bash $U stop
```

| 명령 | 하는 일 |
|---|---|
| `ping` | 상태(플레이 중·컴파일 중·열린 씬) |
| `refresh` | `AssetDatabase.Refresh` — 새 `.cs`의 `.meta`도 이때 생긴다 |
| `menu <경로>` | 메뉴 실행. ⚠️ 확인 창을 띄우는 메뉴는 아무도 못 눌러 에디터가 멈춘다 |
| `play` / `stop` | 플레이 켜기·끄기 |
| `open <씬 경로>` | 씬 열기 (저장 안 한 변경이 있으면 거절) |
| `shot <이름>` | 게임 화면 캡처 (플레이 중에만) |
| `call "<Olympus.클래스.메서드> 인자..."` | 정적 메서드 호출. `Olympus` 이름공간만 |

Unity가 꺼져 있으면 `ub.sh`가 바로 그렇게 말한다 → `bash $U launch` 후 `UB_TIMEOUT=400 bash $U wait`.

### `BridgeActions`에 있는 것

| 호출 | 하는 일 |
|---|---|
| `ListSlots` / `SelectSlot n` / `Click 버튼이름` | 기지 자리 목록·선택(정보 팝업), 버튼 누르기 |
| `Focus 이름접두사 거리` | 기지 카메라를 그 오브젝트로 (`부모/접두사`로 범위 좁히기, 성문 밖도 됨) |
| `Booth 유닛id 속도 초` → `BoothShoot 태그` → `BoothClear` | **촬영 부스** — 빈 곳에 병사 하나를 세워 앞·옆·위에서 찍는다(`booth_<태그>_front/side/top.png`). 정면 = 앞 사진에서 얼굴, 옆 사진에서 오른쪽. 속도 1이면 행군 동작 |
| `Probe 이름접두사` | 몸이 보는 방향(발끝·어깨)과 걷는 방향의 각도, 소품이 어느 뼈 어느 쪽에 붙었는지 |
| `AnimInfo 이름접두사` | 동작 장치 상태(사람형인지·컨트롤러·지금 클립) — "동작이 안 걸린다"를 볼 때 |
| `Reimport 경로` | 에셋 다시 가져오기(가져오기 규칙을 고친 뒤) |

병사 모델·동작·소품을 고치면 **부스로 앞·옆 사진을 찍어 보고** 판단한다 — 숫자(Probe)는 달리는 동작 중에 흔들린다.

플레이 중 조작은 `Assets/Editor/BridgeActions.cs`에 모은다. 손가락으로 하던 확인이 새로 필요하면 여기에 더한다
(게임 코드에 시험용 공개 메서드를 늘리지 않는다).

## 함정

- **처음 한 번**은 Unity가 브리지 코드를 읽어야 한다 — 창이 앞에 나와야 새 파일을 본다.
  (`WScript.Shell.AppActivate(<Unity 프로세스 번호>)`로 창을 불러오면 된다.) 그 뒤로는 `refresh`가 한다.
- **Unity의 파일 가져오기 보조 프로세스(Asset Import Worker)도 에디터 코드를 읽는다.** 브리지가 거기서도 돌면
  보조 프로세스가 명령을 가로채 "플레이 안 함" 같은 엉뚱한 답을 한다(실제로 헤맸다).
  그래서 브리지·자동 재조립·`BattleArtAutoWire`는 `AssetDatabase.IsAssetImportWorkerProcess()`이면 아무것도 안 한다.
  에디터 시작 시 자동으로 도는 코드를 새로 만들면 같은 줄을 넣는다.
- `%LOCALAPPDATA%\Unity\Editor\Editor.log`는 같은 기계에서 켠 다른 Unity(뉴포리아)와 섞여 쓰여서 읽기 어렵다.
  올림푸스 것은 `Temp/EditorBridge/console.log`를 본다(컴파일 시작·끝·오류, 코드 다시 읽기, 플레이 전환도 적힌다).
- `Temp/`는 Unity가 닫힐 때 지운다 — 캡처를 남기려면 다른 곳으로 옮긴다.
