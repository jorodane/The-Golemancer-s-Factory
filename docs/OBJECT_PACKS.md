# 객체팩 작성 명세 v1

실제 게임 DLL 13개가 이 계약으로 동작한다. `IGameModule`은 등록 시에만 호출되고, 개별 액션·조건·실패 처리·시스템·월드·지형 마스크 객체를 레지스트리에 등록한다. 공유 타입은 `Golemancer.Contracts.dll` 하나다. Windows 모듈은 .NET Framework 4.8 / 계약 메이저 버전 1을 사용한다. Linux 검증용 모듈은 net10.0으로 별도 빌드하며 런타임별 DLL을 섞지 않는다.

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
| Items / Item | `id, name, description, price, stack, color, category, tags` |
| Objects / Object | `id, name, kind, sprite, width, height, solid, slots, actions`; Value, Data, Cost, Placement, InputSlots |
| Actions / Action | `id, name, description, icon, handler, path, subName, range, failure, recordable, interrupts`; Works, Condition |
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

입력 예: `<Inputs><Bind action="toggle_mode" keys="Tab" /><Bind action="pickup" keys="E" /><Bind action="roll" keys="Space" /></Inputs>`. 전체 기본 정의는 foundation의 inputs.xml에 있다. 일상 좌클릭은 빠른 사용, 우클릭은 클릭 주변 버블 메뉴다. 빠른 사용은 `<Data key="quickUse" value="harvest" />`처럼 명시한 개체에만 적용한다. 설정이 없는 골렘·일반 보관 시설은 좌우 클릭 모두 상호작용 메뉴를 연다.

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

## 분류와 자동 생산 투입칸

`ItemCategories/Category`의 `id, name`으로 버블 분류를 정의하고 Item의 `tags="herb"`, `tags="liquid,jelly"`처럼 여러 분류를 지정할 수 있다. 기존 category는 판매 등 기존 규칙에도 사용한다. 아이템 즐겨찾기와 대상별 우선 분류는 저장 파일에 보존하며 양방향 운반에 동일하게 적용한다. 모든 선택 단계는 공통 버블을 사용하며 하위 단계에만 가운데 상위 메뉴 버튼을 표시한다. 각 단계는 클릭 위치에서 열리고 뒤로가면 저장된 위치와 페이지를 복원한다. 화면 보정으로 메뉴 중심이 이동한 경우에만 실제 커서를 그 중심에 맞춘다. 전용 UI로 이미 제공하는 액션은 중복시키지 않고, 그 밖의 팩 액션은 별도 행동 목록 없이 기존 XML path·SubName·Condition·PreserveMenuDirectories 규칙에 따라 직접 노출한다. 한 품목 분류는 재귀적으로 압축한다.

```xml
<Data key="autoProduce" value="true" />
<Data key="preferredCategories" value="fuel,herb,liquid,jelly" />
<InputSlots outputSlots="3">
  <Slot id="fuel" name="연료" items="wood" capacity="50" />
  <Slot id="herb" name="약초" tags="herb" capacity="50" />
  <Slot id="liquid" name="액체" tags="liquid" capacity="50" />
</InputSlots>
```

각 Slot은 한 품목을 받는다. items 또는 tags에 맞지 않는 물건과 다른 품목의 혼입은 거부한다. InputSlots가 없는 보관함은 기존 공용 슬롯 규칙을 따른다. Inventory는 투입 재고, OutputInventory는 별도 완성품이다. Count/Stock은 두 재고의 합, Has/Pay는 투입 재고만 사용한다. 제품은 GiveOutput으로 넣고 Take는 제품부터 꺼낸다. 목표 재고 운반은 도착지 투입 재고를 기준으로 한다.

훈증기는 수동 예약 없이 준비된 재료 조합에 맞는 해금 레시피를 한 회씩 자동 시작한다. 연료와 완성품 공간이 없으면 새 재료를 차감하지 않는다. 기본 열 공급은 목재 1개당 열 100이며 남은 열도 사용할 수 있다. 완성품이 다시 투입 재료로 자동 전환되지는 않는다. 기존 저장의 섞인 약초·과적·알 수 없는 물건을 지우지 않으며, 같은 칸에 여러 품목이 남아 있으면 정리를 기다린다. 기존의 지불 완료 생산 예약은 이어서 처리한다.

## 액션 아이콘과 호버 설명

Action의 `description`은 직접 문자열 또는 `@번역키`, `icon`은 기존 Sprite ID 또는 팩 내부 이미지 경로다. 예: `description="@action.harvest.help" icon="item.common_herb"`. 별도 이미지 경로는 SafePath로 검사하고 시작 시 로드한다. 생략한 액션은 기본 UI 기호를 사용한다. 아이템·레시피는 기존 아이템 이미지와 설명을 재사용한다.

원형 선택지는 페이지당 8개이며 페이지 이동은 선택지 수를 차지하지 않는다. 일반 버튼은 52px, 가운데 뒤로가기는 42px다. 중심 반경은 항상 95px다. 12시부터 45도 간격의 8자리를 고정하고 시계방향으로 하나씩 채운다. 항목 수나 마지막 페이지의 남은 수에 따라 각도를 다시 분배하지 않는다. 이름은 아이콘 아래에 검은 글자 외곽선으로 항상 표시하며, 긴 이름은 한 줄 안에서 말줄임한다. 전체 이름과 추가 정보는 접근성 이름에도 제공한다. 이름은 진입·호버 연출을 함께 따르고 실제 창 경계 계산에 포함한다. 제목은 실제 글자 폭을 사용한다. 버블은 전체 창 위의 레이어에 배치하고, 현재 표시하는 요소를 측정해 창 테두리에서 8px 여백만 보장한다. 하위 메뉴·수량창도 같은 경계 계산을 사용한다. 투명 입력 차단 레이어가 뒤쪽 HUD와 월드의 클릭·휠 입력을 막고, 바깥 클릭은 메뉴만 닫으며 해당 클릭을 소비한다. 12시부터 시계방향 9ms 간격, 이동 170ms, 크기 24% → 114% → 100%(220ms)로 등장한다. 마지막 아이콘까지 약 283ms다. 이동 중에는 클릭을 받지 않으며 진입 애니메이션과 호버 확대(110%, 100ms)는 별도 변환을 쓴다. Windows의 클라이언트 영역 애니메이션 설정이 꺼져 있으면 진입 연출을 생략한다.

제작 호버는 Recipe/Item 데이터와 실제 재료 원본 보관함을 조회한다. 수량 확정 단계에서는 입력 수량에 맞춘 결과·필요 재료를 보여준다. 결과물 설명, 해금 조건, 부족 수량은 별도로 표시한다. 어두운 화면과 설명 레이어는 마우스 입력을 차단하지 않고 선택한 원형 버튼 영역을 밝게 남긴다. 상위 메뉴 이동·페이지 전환·닫기·리사이즈에서 설명과 애니메이션을 정리한다.


### 액션별 버블 표시 설정

```xml
<Action id="buy" name="구매" handler="commerce.buy" recordable="false">
  <Bubble badge="{price}" badgeTone="price" />
</Action>
<Action id="tea.rest" name="@tea.name" handler="tea.rest">
  <Bubble name="@tea.bubble" badge="@tea.badge" details="false" />
</Action>
```

`Bubble`은 선택 사항이다. `name`은 버블 아래의 짧은 이름을 덮어쓰고, `badge`는 아이콘 안에 추가 정보를 붙인다. 둘 다 `@번역키`를 지원한다. `badgeTone`은 neutral(기본), price(금색), warning(연한 빨강)이다. 가격뿐 아니라 수량·작업 상태·임의의 짧은 문자열을 같은 필드로 표시할 수 있다. 기존 숫자/체크 배지는 별도 표시 설정이 없을 때 유지된다.

`details`를 생략하면 호스트에서 명시적으로 제공한 상세 미리보기만 열린다. 뒤로가기·분류·단순 명령은 이름과 확대만 표시하고, 제작 재료·시설 투입 재고·상품 정보처럼 내용이 필요한 곳은 상세 미리보기를 제공한다. `true`는 Action의 설명을 포함한 상세 표시를 요청하고, `false`는 명시적인 미리보기도 숨긴다. 설명 문자열이 있다는 이유만으로 설명창을 자동으로 열지는 않는다.

`badge`와 `name`의 `{값이름}`은 해당 버블의 표시 컨텍스트로 치환한다. 일반 액션은 `{state.gold}`, `{actor.mana}`처럼 실제 State.Values/Actor.Values의 키를 참조할 수 있다. 구매 버블은 `{price}`, `{unitPrice}`, `{totalPrice}`, `{quantity}`를 추가로 제공한다. `{price}`는 상품·1개 구매에서는 `3G`, N개 선택에서는 `3G/개`, 수량 확정에서는 현재 수량의 합계 `21G`처럼 표시한다. 숫자 입력·슬라이더·수량 편의 버튼과 동기화한다. `{unitPrice}`, `{totalPrice}`, `{quantity}`는 숫자만 제공하므로 `총 {totalPrice}G` 같은 표기도 가능하다. 아직 품목을 선택하지 않아 값이 없는 경우에는 해당 표시를 숨기며 중괄호 문자열을 노출하지 않는다.

표시 정의는 Contracts의 `BubbleDisplayDef`, XML 적재와 번역은 Engine, 실제 표시와 컨텍스트 공급은 Host가 담당한다. 별도 DLL은 WPF나 Host에 참조를 추가하지 않아도 자기 Action에 표시 설정을 붙일 수 있다. `examples/TeaBreak`에서 독립 팩의 이름·추가 표시·호버 설정 예제를 확인할 수 있다.


### 묶음 압축과 사용할 수 없는 메뉴

버블은 실행 버튼과 순수 묶음을 구분한다. 묶음은 `Activate` 대신 `BuildChildren` 또는 `Children`으로 내용을 공급하고, 하위 메뉴를 여는 일 자체를 실행 콜백으로 등록하지 않는다. 전체 형제 항목을 구성한 뒤 압축한다. 자식 하나인 묶음의 승격에 더해, 현재 층에 순수 묶음 하나만 남으면 자식이 여러 개여도 그 층을 건너뛴다. XML의 보존 폴더는 다른 선택지와 함께 있을 때 유지하되 단독 층을 만들지는 않는다. 실제 행동 하나나 수량 입력·확정 단계는 자동 실행하거나 생략하지 않는다.

내용이 없는 묶음은 숨기거나 빈 화면으로 이동시키지 않고 회색 비활성 버블로 남긴다. 주문 보기, 건네기·가져오기, 장비, 상품·제작·분류 메뉴가 같은 규칙을 사용한다. 내용은 열 때와 사용 가능 상태를 갱신할 때 조회한다. 마지막 주문 등 내용이 사라져 열린 하위 메뉴가 비면 남아 있는 상위 메뉴로 돌아간다. 압축으로 생략한 층은 뒤로가기 기록에 추가되지 않는다.

빠른 사용은 객체팩의 `quickUse`에 명시된 항목의 현재 상태를 확인한다. 비활성 상태면 우클릭과 같은 메뉴를 열며, 다음으로 가능한 다른 행동을 임의로 실행하지 않는다. 상인처럼 전체가 단독 묶음인 경우 좌클릭과 우클릭 모두 펼쳐진 상품 메뉴를 같은 최상위 단계로 연다. 실행 가능 여부는 부작용 없는 DLL `Check`를 확인해 판단하며 명령을 실제로 보내서 시험하지 않는다. Shift 예약은 차례가 되었을 때 재고·작업 조건을 검사하는 기존 규칙을 유지한다.


## 조작과 예상 재고

일상 WASD/방향키·휠 버튼 드래그는 카메라, 빈 땅 좌클릭은 이동 명령이다. 전투에서만 키보드가 골렘을 움직이고 카메라가 고정 추적한다. Space 구르기는 일상에서도 전투로 전환한다. 수량 액션은 `quantity.one`(Ctrl), `quantity.all`(Alt)을 지원한다.

DLL 액션의 `IActionProjection`과 생산 시스템의 `IProductionProjection`은 복제 문맥에서 예상 재고를 계산한다. 실제 문맥·외부 파일·모듈 내부 상태를 변경해서는 안 된다. 예측을 제공하지 않는 모드 행동은 그대로 실행·녹화할 수 있지만 그 뒤의 재고 예측은 중단한다. 가방과 시설의 입력·출력은 공통 아이콘 버블을 사용한다. 시설 호버도 텍스트 재고창 대신 칸별 버블을 사용하며 `output` 회수는 출력 전용이다.

## 메모리와 외부 이동 계약

`Recording`은 시작 재고/장비와 개정 번호를 저장한다. `Playback.Snapshot`은 재생 시작 시 복사되며, 편집 저장으로 실행 중 단계 인덱스를 바꾸지 않는다. `Failures`는 키프레임별 최근 실패를 보존하고 `Paused`는 진행 중 작업·경로·점유를 유지한다. 기본 retry는 뒤의 프레임부터 확인하고 한 순환의 성공이 0회이면 2초 기다린다. 명시한 `ActionRequest.Failure`의 정책은 이 기본 스캔으로 대체하지 않는다.

`MemoryDraft`는 엔진의 분리된 편집 모델이다. 모듈의 순수 Check/Project를 복제 문맥에서 호출하여 확정 가능한 의존성 오류를 검출한다. 외부 재고·현재 자원 고갈과 예측을 제공하지 않는 모드 행동의 결과는 경고로 남긴다. 모르는 행동/대상은 원본에서 삭제하지 않지만 그 상태의 초안 저장은 차단한다. `Recording.Editing`인 미완료 캡처는 저장된 메모리 사전에 넣지 않으며 로드 시 폐기한다.

`WorldObject.Following`은 리더와 별도 동행 경로를 보존한다. 동행 중에는 기존 Path/Work/Pending/Playback/ActionQueue가 실행되지 않는다. 골렘 DLL이 `IGameContext.Route`로 기존 명령을 변경하지 않고 경로를 조회하고, `MoveExternal`로 충돌을 적용한 외부 이동을 요청한다. 마력 0 견인은 이 경로에만 허용되며 일반 `CanOperate` 권한을 주지 않는다. 골렘 대상 가져오기는 타인 재고에 이동 전 lease를 만들지 않고, 실행 직전에 요청한 수량을 다시 확인한다.
