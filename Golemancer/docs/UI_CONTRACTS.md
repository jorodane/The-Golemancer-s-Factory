# UI 요소 제공·요청 명세 v1

UI 팩은 **무엇을 받을 수 있고 무엇을 내보내는지** 공개한다. 게임·객체·다른 팩은 그 명세를 보고 UI 요소를 요청하고, 표시할 값과 실행할 명령을 연결한다. 엔진은 UI 구성과 연결을 검증한다. 값이 체력인지 마나인지, 명령이 상점을 여는지 저장을 하는지는 해석하지 않는다.

WCW의 `GenericWidgetClaim`, `Contain…`, `InfoConnectable`에서 참고한 책임 분리를 XML과 C#의 공통 계약으로 옮겼다. v1은 등록·검증·조립·값 갱신·이벤트·해제 경로를 구현한 기반이다. 현재 Golemancer의 WPF/Android 화면을 이 API로 이식하거나 네이티브 위젯 어댑터를 구현한 상태는 아니다. 예제의 `wpf.*`, `skia.*`는 어댑터가 제공해야 할 식별자이며, 검증용 백엔드는 호출과 값을 기록한다.

## 1. 제공하는 것과 받는 것

| 주체 | 제공 | 받음 |
|---|---|---|
| UI 요소 팩 | 요소 ID, 속성 명세, 이벤트 명세, 자식 슬롯, 플랫폼별 렌더러 ID | 값·배치·자식·이벤트 연결을 지정한 요청 |
| 게임의 표시 모델 | 이름이 있는 값 공급자와 명령 수신자 | UI 이벤트의 검증된 인자 |
| 화면/객체/모드 | 필요한 요소의 요청 트리, 공개할 확장 슬롯 | 조립된 화면의 수명 핸들 |
| 플랫폼 어댑터 | 해당 계약을 구현하는 실제 위젯 | 속성 설정, 자식 연결, 레이아웃, 이벤트 구독 |
| 엔진 | 계약 카탈로그, 검증, 플랫폼 선택, 조립, 연결 해제 | 위의 등록과 요청 |

`ContainFloat2`에 해당하는 요소는 `value: number`, `maximum: number` 속성을 공개할 수 있다. 게임은 두 속성에 값을 제공한다. 같은 객체의 텍스트 설명은 별도의 `text: text` 속성에 연결한다. 여러 속성을 동시에 받는 요소, 하나의 값을 여러 위젯에 연결하는 경우 모두 허용한다.

관찰자별 공개 여부, Minimal/Hover/Detail 등의 표시 문맥, 번역·단위·문자열 포맷은 게임 표시 모델에서 결정한다. 엔진에는 HP나 MP의 전용 처리, 객체 멤버 이름 추측, 임의 리플렉션 호출을 넣지 않는다.

## 2. 파일 등록과 식별자

기존 객체팩 매니페스트에서 UI 파일을 명시한다. UI 파일은 `<GameContent>` 파일과 별개다.

```xml
<ObjectPack id="my.ui" version="1.0.0" contracts="2">
  <Depends id="common.widgets" minVersion="1.0.0" />
  <Ui path="ui.xml" />
</ObjectPack>
```

`Ui` 파일은 해당 팩 내부 경로만 허용한다. 파일 내용은 쿠킹 fingerprint에 포함한다. DLL 없이 XML만 있는 UI 팩도 가능하다. DLL은 선택적 `IUiRegistry.RegisterUi(UiDocument)`로 같은 내용을 등록할 수 있다. 모든 팩 등록 후 `CookedGame.Registry.Ui`가 확정된다.

UI XML의 루트는 `<Ui version="1" id="my.ui">`다. 속성명과 요소명은 대소문자를 구분한다. 모르는 이름·속성·버전·중복 정의는 오류다. 선언 순서나 파일명으로 정의를 덮어쓰지 않는다.

식별자 문법은 `[A-Za-z_][A-Za-z0-9_.-]*`이며 문서·위젯·화면·노드에는 `팩이름.요소이름`을 권장한다. 위젯/화면/문서 ID는 카탈로그 안에서 유일하다. 노드 ID는 화면 하나 안에서 유일하다. 속성·이벤트·슬롯 이름은 해당 위젯 안에서 유일하며, 값·명령 이름은 화면을 열 때 전달하는 context 안에서 찾는다. 호환되지 않는 위젯 계약 변경에는 새 ID를 사용한다.

## 3. UI 요소가 노출하는 명세

```xml
<Ui version="1" id="common.widgets">
  <Widget id="common.range" description="현재값/최대값 표시">
    <Property name="value" type="number" required="true" min="0" />
    <Property name="maximum" type="number" required="true" min="0" />
    <Property name="fill" type="color" default="#52B889"
              description="채움 색상" />
    <Slot name="overlay" max="1" description="막대 위에 표시할 UI" />
    <Renderer platform="windows" key="wpf.range" />
    <Renderer platform="android" key="skia.range" />
  </Widget>
</Ui>
```

| 선언 | 설정 | 의미 |
|---|---|---|
| `Widget` | `id`, `description` | UI 요소 계약의 이름과 설명 |
| `Property` | `name`, `type`, `required`, `default`, `min`, `max`, `description` | 요소가 받을 수 있는 설정값 |
| `Property/Option` | `value` | 가능한 값을 한정. 생략하면 타입/범위만 검사 |
| `Event` | `name`, `payload`, `description` | 요소가 내보내는 사용자 이벤트의 인자형 |
| `Slot` | `name`, `min`, `max`, `description` | 받을 수 있는 자식의 위치와 수량 |
| `Renderer` | `platform`, `key` | 해당 플랫폼에서 계약을 구현하는 코드의 식별자 |

기본값: `required=false`, 슬롯 `min=0`, 슬롯 `max`는 제한 없음. `min/max`는 숫자 속성에서만 사용하며 양끝을 포함한다. 필수 속성도 기본값이 있으면 생략할 수 있다. 선택적 속성에 기본값·Set·Bind가 모두 없으면 `Set`을 호출하지 않으므로, 제공자는 그 경우의 동작을 계약 설명에 명시해야 한다.

`description`은 도구에서 표시할 설명이다. `UiCatalog.Describe(widgetId)`가 속성·이벤트·슬롯·기본값·범위·선택지를 돌려주므로, 향후 설정 편집기가 이 정보로 입력 항목을 만들 수 있다. 현재 설정 편집기 UI는 포함하지 않는다.

| 타입 문자열 | C# `UiValueKind` | XML 값 / 용도 |
|---|---|---|
| `text` | `Text` | 문자열. 빈 문자열 허용 |
| `number` | `Number` | 유한한 실수. 소수점은 `.`. NaN/Infinity 금지 |
| `boolean` | `Boolean` | `true` / `false` |
| `color` | `Color` | `#RRGGBB` 또는 `#RRGGBBAA`; 내부 표현은 대문자 8자리 |
| `resource` | `Resource` | 비어 있지 않은 리소스 ID. 네이티브 이미지 객체나 파일 실행 요청이 아님 |
| `vector2` | `Vector2` | `x,y` 유한 실수. 스틱·좌표 등에 사용 |
| `none` | `None` | 이벤트에만 사용. 인자 없는 명령. 속성형으로는 금지 |

엔진은 이름과 타입을 연결한다. 위젯의 의미까지 자동으로 구현하지는 않는다. 예컨대 range 제공자는 `maximum=0`일 때 빈 막대, 시각적 비율은 `[0,1]`로 제한한다는 식으로 자신의 동작을 정해야 한다. 일반 숫자 범위 검사와 `value <= maximum` 같은 속성 간 관계는 구별한다. 후자는 v1 범용 검증기의 기능이 아니다.

## 4. 요청 XML

```xml
<View id="project.status">
  <Node id="project.mana" widget="common.range">
    <Layout anchorMin="1,0" anchorMax="1,0" pivot="1,0"
            offset="-16,16" size="240,24" safeArea="true" />
    <Bind property="value" source="selection.current" />
    <Bind property="maximum" source="selection.maximum" />
    <Set property="fill" value="#4B9AE8" />
  </Node>
</View>
```

`View`는 루트 `Node` 하나를 가진다. 노드는 `id`, `widget`, `order`(기본 0)를 갖는다.

| 요청 | 동작 |
|---|---|
| `Set property="fill" value="#4B9AE8"` | 고정 설정값 제공. 위젯 기본값보다 우선 |
| `Bind property="value" source="selection.current"` | context의 값 공급자에 연결. 최초값과 이후 변경을 받음 |
| `On event="activate" command="menu.open"` | UI 이벤트를 context의 명시적 명령에 연결 |
| `Slot name="overlay"` 아래 `Node` | 자식 UI 요청 |
| `Slot name="items" export="true"` | 이 인스턴스의 슬롯에 외부 팩의 추가를 허용 |

하나의 속성에 Set과 Bind를 동시에 지정하면 오류다. 연결 대상 값의 형은 속성형과 정확히 같아야 한다. 숫자를 문자열로 자동 변환하거나, 이름을 분석해 객체 필드를 가져오지 않는다. `42 / 100` 같은 문자열은 표시 모델이 별도의 문자열 값으로 제공한다. 이미지·아이콘은 리소스 ID를 공급하고 실제 해석/디코딩은 어댑터의 리소스 서비스가 담당한다.

## 5. 배치

`Layout`은 노드마다 최대 하나다. 모든 수치는 화면 픽셀이 아닌 논리 UI 단위이며 원점은 왼쪽 위, +X는 오른쪽, +Y는 아래쪽이다. 플랫폼 어댑터가 DPI/사용자 UI 배율을 반영한다.

| 설정 | 기본값 | 의미 |
|---|---|---|
| `anchorMin`, `anchorMax` | `0,0` | 부모 내용 영역에 대한 `[0,1]` 비율. Min은 Max 이하 |
| `pivot` | `0,0` | 자신의 크기에 대한 기준점 비율 |
| `offset` | `0,0` | 앵커 기준 위치에서 이동할 논리 단위 |
| `size` | `0,0` | 앵커 구간 크기에 더할 크기. 늘어진 앵커에서는 음수도 허용 |
| `minSize` | `0,0` | 최종 크기의 최솟값 |
| `maxSize` | 없음 | 최종 크기의 최댓값 |
| `safeArea` | `false` | 루트의 부모 영역으로 플랫폼의 안전 영역 사용 |

각 축의 식은 다음과 같다. `UiLayoutMath.Resolve`가 이 계산을 제공한다.

```text
span = parentSize × (anchorMax - anchorMin)
size = clamp(span + requestedSize, minSize, maxSize)
position = parentOrigin
         + parentSize × (anchorMin + (anchorMax - anchorMin) × pivot)
         + offset - size × pivot
```

루트의 `safeArea=true`이면 어댑터가 노치/시스템 UI 등을 제외한 사각형을 부모 영역으로 전달한다. 자식에서 다시 safeArea를 적용하는 것은 오류다. 화면 크기가 변하면 같은 식으로 재배치한다.

가로/세로 목록·그리드·원형 메뉴 등 자식 자동 배치는 해당 컨테이너의 계약이다. 예제 panel은 `direction`, `gap`을 노출한다. 자동 배치 슬롯에서는 컨테이너가 자식 위치/크기를 정하며, 개별 앵커와의 우선순위도 그 슬롯의 계약으로 명시해야 한다. padding, 폰트 크기, 아이콘 크기 등도 받는 위젯이 Property로 공개한다. 모든 위젯이 존재하지 않는 설정까지 자동 지원하는 것으로 취급하지 않는다.

## 6. 버튼 하나 추가하기

기존 화면의 부모 노드가 `items` 슬롯을 공개했다면 모드는 다음만 제공한다.

```xml
<Ui version="1" id="extension.ui">
  <Contribute view="project.menu" parent="project.menu.root" slot="items">
    <Node id="extension.open" widget="common.button" order="50">
      <Set property="text" value="추가 정보" />
      <On event="activate" command="extension.open" />
    </Node>
  </Contribute>
</Ui>
```

같은 슬롯의 자식들은 `order` 오름차순, 같은 값이면 노드 ID의 ordinal 순서로 배치한다. 기존 버튼 ID를 다시 선언하면 오류다. 모드는 자신의 명령을 표시 context에 등록해야 하며 XML에 이름을 썼다고 게임 기능이 생기지는 않는다.

공개하지 않은 슬롯, 없는 부모/화면/위젯, 중복 노드, 슬롯 수량 초과는 쿠킹 오류다. 기여된 노드도 슬롯을 공개할 수 있다. 엔진이 부모 존재 관계로 조립하며 없는 부모/순환 관계를 거부한다. 팩 배포에서는 제공 팩에 `Depends`도 명시한다.

v1은 **추가** 계약이다. 기존 노드 삭제·속성 patch·위젯 타입 덮어쓰기·before/after 제약은 아직 정의하지 않았다. 현재는 명시적인 순서값과 공개 슬롯으로 확장한다. 실행 중 팩 제거는 화면을 Dispose하고 새 카탈로그/context로 다시 여는 방식이며 라이브 트리 diff는 포함하지 않는다.

## 7. 함수/API 명세

공유 타입: `PackEngine.Contracts.UI`. 엔진 구현: `PackEngine.Runtime.UI`.

| API | 책임 |
|---|---|
| `IUiRegistry.RegisterUi(UiDocument)` | 모듈이 제공자 정의·요청 트리·기여를 등록 |
| `UiXml.Read(path)` / `Read(TextReader)` | XML을 공통 요청 DTO로 읽음 |
| `new UiCatalog(documents)` | DTO를 복사하고 교차 참조·설정·트리를 검증 |
| `UiCatalog.Describe(widgetId)` | 외부 설정 도구를 위한 제공자 명세 사본 |
| `UiCatalog.DescribeView(viewId)` | 기여가 조립된 요청 트리 사본 |
| `UiCatalog.Mount(viewId, context, backend)` | 플랫폼 선택·연결 검증·실제 요소 생성. 수명 핸들 반환 |
| `UiMountedView.Root` | 부모 네이티브 화면에 붙일 루트 요소 핸들 |
| `UiMountedView.Dispose()` | 구독/이벤트와 요소를 해제. 반복 호출 가능 |

C#도 XML과 동일한 DTO를 만든다. 아래 정의는 `UiCompositionTests.CodeRequest`에서 실행하는 예제와 같은 형태다.

```csharp
var document = new UiDocument {
    Id = "code.ui",
    Widgets = [new() {
        Id = "code.label",
        Properties = [new() {
            Name = "text", Type = UiValueKind.Text, Required = true
        }],
        Renderers = new() { ["*"] = "portable.text" }
    }],
    Views = [new() {
        Id = "code.view",
        Root = new() {
            Id = "code.root", Widget = "code.label",
            Bindings = new() { ["text"] = "model.text" }
        }
    }]
};
// IGameModule.Register에서 선택적 확장 계약으로 등록:
// ((IUiRegistry)registry).RegisterUi(document);
```

동적으로 선택한 객체/관찰자 문맥으로 요청을 만들 수도 있다. 표시 중인 UI는 카탈로그 생성 당시의 사본을 사용하며 원본 DTO 변경을 따라가지 않는다. 값만 변하면 바인딩으로 갱신하고, 구조를 바꾸려면 새 요청으로 다시 조립한다. 모듈형 DLL과 향후 소스 쿠킹 빌드 모두 이 등록 API를 사용할 수 있다. 고정형 빌드 파이프라인 자체는 이번 변경의 구현 범위가 아니다.

### 값 공급자와 이벤트 수신자

```csharp
public interface IUiValueSource {
    UiValueKind Type { get; }
    UiValue Read();
    IDisposable Subscribe(Action<UiValue> changed);
}
public interface IUiCommand {
    UiValueKind Payload { get; }
    void Execute(UiValue value);
}
public interface IUiContext {
    IUiValueSource Value(string name);
    IUiCommand Command(string name);
}
```

간단한 표시 모델은 제공된 `UiContext`, `UiSignal`을 사용한다.

```csharp
var label = new UiSignal(UiValue.Text("준비"));
var context = new UiContext();
context.AddValue("model.text", label);
context.AddCommand("screen.close", UiValueKind.None, _ => CloseScreen());
using var screen = new UiCatalog(new[] { document })
    .Mount("code.view", context, backend);
label.Set(UiValue.Text("완료"));
```

`CloseScreen()`과 `backend`는 호출 프로젝트가 제공한다. source의 Type은 수명 동안 고정이다. 구독은 각각 독립적이며 해제 가능해야 한다. 읽기와 구독 등록은 UI 표시용 작업이고 게임 상태를 변경하지 않아야 한다. `UiSignal.Set`은 같은 값을 재전달하지 않는다. 구독 하나가 오류를 내더라도 다른 구독에 변경을 전달한 뒤 오류를 모아 보고한다.

### 토글·슬라이더·버튼의 양방향 연결

토글은 `value: boolean`을 받고 `change: boolean`을 보낸다. 슬라이더는 `value: number`를 받고 `change: number`를 보낼 수 있다. 이벤트는 **변경 요청**이며 값의 소유권은 게임에 있다. 게임 명령이 검증하고 확정한 값을 source로 돌려준다. 어댑터는 단순 `Set`으로 사용자 이벤트를 다시 발생시키지 않는다. 요청이 거부되어 같은 값이 유지돼도 화면이 원래 확정값을 유지하도록, 어댑터는 로컬 조작값을 권위 있는 값으로 확정하지 않는다.

단순 버튼은 `activate: none`. 지속 입력용 버튼은 `press/release/cancel: none`을 각각 공개할 수 있고 스틱은 `change: vector2`를 공개할 수 있다. 이때 포인터 캡처·이탈·해제·취소 규칙은 제공자 계약과 어댑터에서 구현한다. 논리 입력과 연결하는 수신자는 자신의 input source ID만 갱신/해제해야 한다. UI 화면을 닫거나 포커스를 잃었을 때의 지속 입력 해제는 해당 화면/입력 어댑터가 담당한다. 이번 v1에는 기존 InputState와의 자동 브리지나 가상 스틱 구현은 포함하지 않는다.

## 8. 플랫폼 구현 계약

```csharp
public interface IUiBackend {
    string Platform { get; }
    bool Supports(string renderer, UiWidgetDefinition contract);
    IUiElement Create(string renderer, string nodeId, UiLayout layout);
}
public interface IUiElement : IDisposable {
    void Set(string property, UiValue value);
    void Add(string slot, IUiElement child);
    IDisposable Listen(string eventName, Action<UiValue> handler);
}
```

정확한 Platform 항목을 우선하고 없을 때만 `platform="*"`를 사용한다. 지원하지 않는 플랫폼/렌더러는 오류다. `Supports`는 넘겨받은 속성·이벤트·슬롯 계약 전체의 구현 가능성을 확인해야 한다. OS 이름만 보고 무조건 true를 반환해서는 안 된다. 별표 렌더러도 실제 해당 플랫폼 구현이 있어야 한다.

`Create`는 아직 외부 화면에 게시하지 않은 요소를 만든다. 생성 도중 실패하면 자신이 아직 반환하지 않은 자원을 직접 해제해야 한다. `Set`은 사용자 이벤트를 발생시키지 않는다. `Add`는 지정 슬롯으로 자식을 연결한다. `Listen`/`Subscribe`는 등록 해제용 non-null 핸들을 반환하며, 실패할 때 자체 등록을 남기지 않아야 한다. `Dispose`는 그 요소 자체만 해제한다. 자식 해제는 엔진이 역순으로 수행하므로 부모가 중복 해제하지 않는다.

엔진과 context 및 backend의 호출/알림은 모두 호스트 UI 스레드에서 직렬 실행한다. 백그라운드 게임 작업은 표시 모델 갱신을 그 스레드로 전달해야 한다. 엔진이 임의로 Android UI 스레드나 WPF Dispatcher를 호출하지 않는다.

## 9. 실패·수명 규칙

1. 카탈로그 단계에서 파일/스키마, 속성 타입·범위, 참조, 슬롯 공개·수량, 중복, 배치, 최대 64단계/4096노드를 검사한다. XML DTD/외부 entity는 금지한다.
2. Mount는 전체 트리의 source/command 및 renderer 지원을 먼저 검사한다. 잘못된 요청 때문에 일부 네이티브 화면이 먼저 생성되는 것을 막는다.
3. 요소 생성과 연결 중 실패하면 이미 만든 구독과 요소를 해제한다. 정리 오류가 나더라도 나머지 정리를 진행한 뒤 오류를 보고한다.
4. 값은 구독 후 한 번 더 읽어 연결 중 변경을 반영한다. 변경값도 타입/범위 검사를 통과한 뒤 적용한다. 잘못된 변경은 오류를 알리고 마지막 유효 표시값을 유지한다.
5. 화면 해제 시 먼저 이벤트/값 구독을 끊고 자식부터 요소를 해제한다. 지연 도착한 알림/이벤트는 해제된 화면에 적용하지 않는다. 다른 화면의 구독에는 영향이 없다.

프리플라이트는 네이티브 생성 실패까지 없애지는 못한다. 게임 명령이 실행한 부작용을 엔진이 롤백하지도 않는다. 문서 주석/들여쓰기는 허용하지만 모르는 필드를 저장했다가 통과시키지 않는다. 호환되지 않는 UI 팩은 쿠킹 단계에서 오류를 명확히 알린다.

## 10. 실행 가능한 예제와 검증

[examples/UiComposition](../examples/UiComposition/README.md)에 UI 요소 제공 팩, 이를 요청하는 프로젝트 팩, 버튼 하나를 기여하는 모드 팩을 분리했다. 게임 객체·액션·게임 DLL 없이 쿠킹하고 조립한다.

```bash
dotnet build tests/Golemancer.Verification/Golemancer.Verification.csproj \
  -c Release -p:GolemancerTargetFramework=net10.0
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll --ui
```

이 검증은 설정 노출/검증, 플랫폼 ID 선택, 슬롯 추가, 값·이벤트, 복수 화면의 수명, 실패 정리, 레이아웃 계산을 검사한다. 실제 네이티브 표시·터치·접근성·DPI 처리는 각 플랫폼 어댑터를 연결한 뒤 별도로 확인해야 한다. APK를 만들거나 에뮬레이터를 실행하지 않아도 계약/게임 표시 모델 변경을 검사할 수 있다.
