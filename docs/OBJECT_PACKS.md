# 객체팩 작성 명세 v1

실제 게임 DLL 12개가 이 계약으로 동작한다. `IGameModule`은 등록 시에만 호출되고, 개별 액션·조건·실패 처리·시스템·월드 객체를 레지스트리에 등록한다. 공유 타입은 `Golemancer.Contracts.dll` 하나다. Windows 모듈은 .NET Framework 4.8 / 계약 메이저 버전 1을 사용한다. Linux 검증용 모듈은 net10.0으로 별도 빌드하며 런타임별 DLL을 섞지 않는다.

## 배포 폴더

팩은 `Content/Packs/<폴더>/pack.xml`로 발견한다. 폴더명보다 매니페스트 `id`가 정체성을 결정한다.

```xml
<ObjectPack id="my_pack" version="1.0.0" contracts="1">
  <Depends id="foundation" minVersion="1.0.0" />
  <Depends id="feast_trail" minVersion="1.0.0" />
  <Assembly path="Bin/{framework}/MyPack.dll" />
  <Data path="objects.xml" />
  <Data path="localization_ko-KR.xml" />
</ObjectPack>
```

`{framework}`는 Windows 앱에서는 `net48`, 이식 가능한 검증에서는 `net10.0`으로 치환된다. DLL과 해당 DLL의 외부 의존 파일을 같은 Bin 하위 폴더에 배치한다. net10.0 검증은 `.deps.json`도 함께 배포한다. Contracts의 별도 복사본은 포함하지 않는다. 매니페스트에 DLL을 여럿 등록할 수 있고 XML만 담은 팩도 가능하다. 의존팩은 먼저 쿠킹된다. 게임 시작 때 쿠킹하며 **플레이 도중 DLL 핫 리로드는 제공하지 않는다**. 외부 DLL은 로컬 실행 권한을 가진 신뢰할 수 있는 코드여야 한다.

이미지는 같은 팩의 `Images/tea.svg` 등에 넣는다. Object의 `sprite="Images/tea.svg"`는 해당 팩 내부 경로로 해석된다. 기본 벡터 스프라이트를 재사용하려면 `sprite="storage"`처럼 ID를 지정한다. 등록된 이미지가 없으면 시작 시 실제 누락 경로를 표시한다. 정의는 있지만 sprite ID를 알 수 없는 객체는 이름으로 표시한다. 그림을 코드로 대체 생성하지 않는다. 타일셋과 애니메이션은 독립 이미지 객체팩으로 등록한다. 자세한 XML은 [ART_PACKS.md](ART_PACKS.md)를 참고한다.

## 구현 경계

| 계약 | 역할 | 구현 규칙 |
|---|---|---|
| `IGameModule.Register` | 구현 ID 등록 | 게임 상태 변경과 자체 스레드 금지 |
| `IActionHandler.Check` | 실행 가능성 확인 | 부작용 없음; 이동 전과 작업 완료 후 다시 호출 |
| `IActionHandler.Execute` | 영구 효과와 결과 | 재고 검사 후 원자적으로 반영; 실패 시 부분 효과를 임의 취소하지 않음 |
| `IConditionHandler.Evaluate` | 조건 객체 | 중첩 노드는 `context.Evaluate`로 위임 |
| `IFailureHandler.Handle` | 실패의 추가 효과와 흐름 | Advance / Repeat / Halt 반환, XML 지연·횟수 사용 |
| `IRuntimeSystem.Tick` | 수동 조작과 무관한 처리 | WPF 고정 1/60초; 전달된 dt 사용; Order 순서; 비동기 상태 변경 금지 |
| `IWorldGenerator.Populate` | 초기 타일과 인스턴스 생성 | 새로운 게임에서만 호출 |

엔진이 WorldObject와 GameState를 소유한다. `Values`는 수치, `Data`는 문자열, `Inventory`는 아이템 수량이다. 모드별 키에 접두사를 붙여 충돌을 피한다. 공용 `Find`, `Spawn`, `Navigate`, `Dispatch`, `Evaluate`, `Notice`, `Effect`를 사용하고 다른 모듈의 구체 클래스를 참조하지 않는다.

## XML 요소

모든 데이터 파일의 루트는 `<GameContent>`다. 필요한 섹션만 넣으면 된다. 동일 정의 ID를 뒤에서 선언하면 정의 전체를 교체한다. 부분 속성 병합은 아니다. 다만 Tileset은 Tile ID 단위, Sprite는 Animation state 단위로 병합하며 해당 Tile/Animation 하나는 전체 교체한다. 기본 팩을 수정하는 팩은 의존성을 명시한다.

| 섹션 / 요소 | 주요 속성과 하위 요소 |
|---|---|
| Items / Item | `id, name, description, price, stack, color, category` |
| Objects / Object | `id, name, kind, sprite, width, height, solid, slots, actions`; Value, Data, Cost, Placement |
| Actions / Action | `id, name, handler, path, subName, range, failure, recordable, interrupts`; Works, Condition |
| ActionSets / ActionSet | `id, actions` — 쉼표로 구분한 구체 액션 ID |
| MenuDirectories / Directory | `path, collapse` — `collapse="false"`로 한 자식 폴더 유지 |
| Failures / Failure | `id, handler, delay, maxRetries` |
| Recipes / Recipe | `id, name, facility, unlock, output, amount, work, defaultEfficiency`; Inputs, Efficiency |
| Quests / Quest | `id, name, description, requires, flag, reward`; Goal, Dialogue |
| Texts / Text | `id`와 문자열 본문; 이름·설명에 `@text.id`로 참조 |
| Inputs / Bind | `action, keys` — WPF Key 이름을 쉼표로 구분; 같은 action은 뒤 팩에서 교체 |
| Tilesets / Tileset | `id`; Tile: `id, image, walkable, x, y, width, height` |
| Sprites / Sprite | `id`; Animation과 선택적 Frame — ART_PACKS.md 참고 |
| Maps / Map | `id, tileset, width, height`; Legend / Tile, Rows / Row, Spawns / Spawn |

`width/height`는 정수 타일 점유 크기다. `X/Y`는 판정 타일, `SubX/SubY`는 저장되는 칸 내부 오프셋이며 `WorldX/WorldY`가 실제 연속 위치다. `SetPosition`으로 이동하면 둘을 함께 갱신한다. X/Y 직접 대입은 해당 오프셋을 0으로 초기화하는 순간 이동이다. 그림의 시각적 돌출과 충돌 크기는 별개다. 인스턴스 ID와 정의 ID를 혼동하지 않는다. Maps의 레전드 글자 하나가 타일 하나이며 행 길이는 width와 일치해야 한다.

```xml
<GameContent>
  <Texts><Text id="tea.name">조용한 티타임</Text></Texts>
  <Actions>
    <Action id="tea.rest" name="@tea.name" handler="tea.rest"
            path="휴식/차" failure="stop" range="1" recordable="true">
      <Works><Work type="craft" amount="4" /></Works>
      <Condition><And><Ability id="craft" /><Not><Flag id="busy" /></Not></And></Condition>
    </Action>
  </Actions>
  <Objects>
    <Object id="tea.table" name="찻상" kind="facility" width="1" height="1"
            solid="true" slots="4" actions="tea.rest" sprite="workbench">
      <Data key="buildable" value="true" />
      <Cost><Item id="wood" amount="4" /></Cost>
      <Placement><Ground /></Placement>
    </Object>
  </Objects>
</GameContent>
```

지원 기본 조건은 true, and, or, not, flag, ability, shop, ground, hasitem이다. `Flag`의 `id`는 GameState.Flags, `Ability`의 `id`는 골렘 Values의 양수 값이다. 조건 DLL을 등록하면 새 태그 이름을 추가할 수 있다. Placement는 점유 타일 하나씩 검증한다.

공장·창고는 기본적으로 Ground 조건으로 야외에 설치한다. 판매용 시설만 `<And><Shop /><Ground /></And>`와 `<Data key="shopOnly" value="true" />`를 함께 지정해 상점 위치와 판매 시설 수 제한을 적용한다. 공통 `Rules.Placement`가 건설 미리보기와 실제 실행에 같은 점유 검사를 제공한다.

입력 예: `<Inputs><Bind action="toggle_mode" keys="Tab" /><Bind action="pickup" keys="E" /><Bind action="roll" keys="Space" /></Inputs>`. 전체 기본 정의는 foundation의 inputs.xml에 있다. 일상 좌클릭은 빠른 사용, 우클릭은 클릭 주변 버블 메뉴다. 다른 골렘의 빠른 사용은 물건 건네기이며 조종 전환은 별도 동작이다.

`range=-1`은 거리 제한 없음, `recordable=false`는 행동 녹화 제외, `interrupts=true`는 기존 작업을 중단하는 행동이다. Works의 모든 종류가 완료되어야 실행된다. 작업량 증가 = 능력치 × 골렘 효율 × 시간이다. 기본 수동 무마력 효율은 0.5다.

## 운반·생산·실패

ActionRequest에는 `Action, ActorId, TargetId, X, Y, Route, Item, Quantity, Mode, Option, Failure`가 있다. JSON은 camelCase를 사용한다. `Quantity`는 0~9999 범위다. 각각의 구현이 더 좁게 검증할 수 있다. `Route`는 직접 이동하며 통과한 정수 타일 목록이다. 재생은 그 방향으로 연속 이동하며 칸 중심으로 맞추지 않는다.

운반의 `Mode=exact`는 전량 가능할 때만 변경한다. `fill`은 목적지의 현재 수량에서 목표까지 부족분을, `all`은 가능한 만큼 옮긴다. `Option=take`는 대상 → 조종 골렘, 기본은 조종 골렘 → 대상이다. 빈 fill은 성공한 no-op이다. 적재 한도 축소는 이미 저장된 물건을 삭제하지 않는다.

골렘 간 전달도 같은 운반 규칙을 쓰며 조종 대상을 바꾸지 않는다. 수확은 `Rules.Drop`으로 바닥 아이템을 먼저 만든다. `pickupOwner`를 지정한 수확물만 0.35초 뒤 해당 골렘이 주변에서 자동 습득한다. 초과 수량은 바닥에 남는다. 내려놓기·철거·파괴 부산물은 소유자를 지정하지 않아 E로 직접 수집한다. E 짧게는 인접 묶음 하나, 0.35초 이상 길게는 맨해튼 거리 2 이내의 여러 묶음이다.

생산 예약은 재료를 한 번 지불하고 `IngredientsCommitted`와 진행률을 저장한다. 목재는 열 공급원이고 레시피 재료가 아니다. `Efficiency source="heat" value="1"`처럼 공급원별 작업 전환율을 정의한다. 레시피 팩이 사라지면 생산 대기 상태로 남긴다. 같은 출력의 예약량도 목표 재고 계산에 포함한다.

실패 구현은 엔진의 enum 목록으로 콘텐츠를 제한하지 않는다. DLL에서 추가 실패 객체를 등록하고 XML에 연결한다. 제공되는 stop, skip, retry와 선택적 explode 예제를 참고한다. 기본 retry는 2초마다 최대 120회다. 사용자 녹화에는 실패 정책 ID와 대상 인스턴스가 저장되며, 팩이 없어도 기록 자체는 유지된다.

## 검증과 작은 문맥으로 작업하기

`python tools/context.py <모듈명>`으로 필요한 파일만 확인하고 해당 csproj를 빌드할 수 있다. 기본 모듈의 AfterBuild는 자기 DLL만 대상 팩의 Bin에 복사한다. Contracts를 바꾸면 전체 검증, 동작이나 퀘스트를 바꾸면 캠페인 검증을 실행한다. `examples/TeaBreak`는 이 경계 밖의 독립 예제다.

타일·재고·녹화·퀘스트는 `Simulation.Save/ReadSave`로 보존한다. `JsonExtensionData`가 모르는 필드를 유지한다. 저장 형식 메이저는 1이며 새로운 비호환 저장 형식은 마이그레이션 코드를 추가해야 한다.
