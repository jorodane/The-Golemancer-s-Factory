# 객체팩과 정의의 상속

기본 규칙은 **생략하면 상속, 명시하면 재정의, 새 이름이면 추가**다. 삭제 연산은 없다. 하나의 부모만 지정하며, 부모 정의와 자식 정의는 서로 다른 ID를 갖는다. 같은 ID를 파일 순서로 덮어쓰지 않는다.

## 팩과 객체 정의

| 계층 | 선언 | 효과 |
|---|---|---|
| 팩 | `ObjectPack extends="parent.pack"` | 부모를 의존성에 포함하고 먼저 한 번 로드한다 |
| UI 요소 계약 | `Widget extends="parent.widget"` | 속성·이벤트·슬롯·렌더러 계약과 기본값을 상속한다 |
| UI 화면 원형 | `View extends="parent.view"` | 노드 트리를 복사하고 명시한 노드 설정을 재정의한다 |
| 다른 종류의 객체 | `DefinitionInheritance.Resolve<T>` | 해당 계약의 병합 어댑터로 같은 부모 해석·출처 추적을 사용한다 |

팩 상속이 DLL의 C# 클래스를 자동으로 상속시키거나, 부모 파일을 자식 폴더로 복제하지는 않는다. 부모가 등록한 기능과 정의를 함께 사용할 수 있게 한다. 어떤 객체 원형을 상속할지는 각 정의의 `extends`에 명시한다. 기존 게임의 모든 `GameContent` XML이 자동 병합되는 것은 아니다. 현재 제공하는 구체적인 병합 어댑터는 UI Widget과 View다.

```xml
<ObjectPack id="theme.controls" version="1.0.0" engineContracts="1"
            extends="engine.ui.button" extendsMinVersion="1.0.0">
  <Ui path="ui.xml" />
</ObjectPack>
```

다른 부모 외 의존성은 기존 `Depends`를 사용한다. 게임 계약을 사용하는 팩은 `engineContracts` 대신 해당 게임의 `contracts`를 선언한다. `extendsMinVersion`의 기본값은 `1.0.0`이다. 부모 누락·순환 의존·호환되지 않는 버전은 로딩 오류이며, 부모의 DLL을 자식마다 다시 등록하지 않는다. `PackInfo.Parent`와 `Dependencies`로 이 관계를 조회할 수 있다. 부모·자식 XML/DLL은 모두 기존 fingerprint 계산에 포함된다.

## 요소의 기본값과 구현 재정의

다음 `ui.xml`은 기본 버튼을 재사용하면서 색상과 글자 크기를 바꾼다. 보조 문구와 비활성화 사유는 상위 `engine.annotatedButton`이 이미 공개한 속성이다.

```xml
<Ui version="1" id="theme.controls.ui">
  <Widget id="theme.button" extends="engine.annotatedButton">
    <Default property="background" value="#244637DD" />
    <Default property="fontSize" value="13" />
  </Widget>
  <View id="theme.button" extends="engine.button.view">
    <Override node="button" widget="theme.button">
      <Bind property="annotation" source="control.annotation" />
      <Bind property="disabledReason" source="control.disabledReason" />
    </Override>
  </View>
</Ui>
```

`Default`는 이미 선언된 속성의 기본값만 바꾼다. 숫자 범위·선택지·타입 검사는 부모 계약을 그대로 따른다. 새 속성은 `Property`로 추가한다. 기존 Property 스키마를 다시 선언할 수는 없다. 자식이 새 필수 속성을 추가할 때는 기본값도 제공해야 한다.

`Renderer`는 플랫폼 이름을 키로 병합한다. 예를 들어 자식의 `<Renderer platform="android" key="theme.android.button" />`는 Android 구현만 바꾸고 나머지 플랫폼 선택을 상속한다. 새 구현 DLL은 그 키의 생성기를 등록해야 한다. Mount의 `Supports` 검사에서 최종 계약 전체를 처리할 수 있는지 확인한다. 속성이나 이미지 참조만 바꿀 때는 DLL이 필요하지 않다.

이벤트의 이름과 인자형, 기존 슬롯의 수량 계약은 유지한다. 같은 이벤트/슬롯을 다시 명시할 경우 인자형/수량은 같아야 하며, 설명만 재정의할 수 있다. 새 이벤트와 선택적 슬롯은 추가할 수 있다. 새 필수 슬롯은 부모의 사용 조건을 강화하므로 거부한다. 계약이 다른 요소는 새 기본 계약으로 정의한다.

`activate`라는 공통 이벤트의 의미와 구체적인 클릭 인식 정책은 구별한다. 현재 기본 DLL은 안에서 눌렀다가 안에서 놓으면 활성화한다. 대체 DLL은 자신의 설명에 따라 더블 클릭이나 확인 대기 같은 다른 정책을 구현할 수 있다. 상속 해석기가 클릭 횟수를 강제하지 않으며, 실제 의미·취소·비활성화 동작은 제공자가 계약대로 구현해야 한다. 구조 검증만으로 모든 동작 계약을 증명하지는 않는다.

## 화면 원형 재사용

```xml
<View id="theme.purchase" extends="theme.button">
  <Override node="button">
    <Set property="text" value="구매" />
    <Layout offset="0,0" safeArea="false" />
  </Override>
</View>
```

| 명시한 항목 | 병합 규칙 |
|---|---|
| `Default`, `Set`, `Bind` 값 | 빈 문자열·`false`·`0`도 명시한 값으로 처리한다. 단, 속성 자체의 타입·범위는 만족해야 한다 |
| `Set`과 `Bind` | 같은 속성의 기존 연결을 서로 교체한다. 한 Override 안에서 둘 다 지정하면 오류다 |
| `On` | 해당 이벤트의 명령 연결만 바꾼다 |
| `Layout` | 명시한 필드만 바꾼다. 생략한 배치 필드는 유지한다 |
| `widget` | 기존 위젯 또는 그 위젯의 자손으로만 교체한다 |
| `Slot`의 새 `Node` | 기존 자식 뒤에 추가한다. 실제 배치는 `order`, 노드 ID 순서다 |
| 기존 자식 수정 | 동일 ID의 Node를 재선언하지 않고 `Override node="기존ID"`를 쓴다 |
| 빈 `Slot` | 기존 자식을 유지한다 |
| `export="true"` | 슬롯 공개를 추가한다. 기존 공개를 철회하지 않는다 |

상속된 화면은 루트 Node를 통째로 교체하지 않는다. 해당 루트 ID를 Override한다. `Remove`, 목록 비우기, 기본값 삭제, 기존 maxSize 해제 같은 삭제 문법은 제공하지 않는다. UI의 표시/활성 조건은 그 요소가 지원하는 명시적 속성으로 제어한다.

상속은 객체마다 최대 64단계다. 최종 화면의 노드 ID·속성·레이아웃·슬롯 용량도 검증한다. 부모·형제·입력 DTO를 변경하지 않는 사본으로 병합한다. Mount마다 요소·값 구독·이벤트 구독을 새로 생성하므로 하나의 원형으로 만든 여러 버튼이 눌림 상태나 표시값을 공유하지 않는다.

`Contribute`는 모든 원형 상속을 해석한 **뒤에**, 명시한 `view` 하나에만 적용한다. 부모 화면에 기여했다고 자식 화면까지 자동으로 추가되지는 않는다. 여러 화면에 필요한 공통 자식은 부모 원형에 두고, 특정 화면을 확장할 때 기여를 사용한다. 기여로 추가될 노드를 상속 Override에서 미리 수정할 수는 없다.

## 설정 출처 조회

```csharp
UiWidgetInspection widget = catalog.InspectWidget("golemancer.costButton");
UiViewInspection view = catalog.InspectView("golemancer.purchase");
// Definition / Root: 최종 병합된 사본
// Inheritance.Lineage: 부모부터 자신까지 ID 목록
// Inheritance.Members: 설정 경로 -> Pack / Document / Definition
// view.Widgets: 이 화면이 사용하는 요소의 최종 계약과 출처
```

위젯 기본값의 출처는 `property.background.default`, 렌더러는 `renderer.*`, 화면의 값 연결은 `node.button.property.text` 같은 키로 조회한다. 출처는 최종 값을 정한 선언을 가리킨다. 부모 이력 전체와 실행 중 바인딩된 게임 값은 별도 정보다. 반환된 정의를 수정해도 이미 쿠킹한 카탈로그는 바뀌지 않는다.

매니페스트로 읽은 UI에는 팩 ID와 팩 내부 파일 경로가 자동 기록된다. DLL/C#에서 직접 `UiDocument`를 등록할 때는 작성자가 `Pack`, `Source`를 제공한다. 생략한 Source는 문서 ID로 표시된다.

Golemancer 폴더에서 실제 배포된 XML을 검사할 수 있다.

```sh
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll --inspect-ui view golemancer.purchase
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll --inspect-ui widget golemancer.costButton
```

Windows 배포본에서는 `Builds/Windows/Golemancer.Verification.exe`에 같은 인자를 전달한다. 전체 프로젝트를 읽지 않아도 최종 계약과 출처를 확인할 수 있다.

## 현재 버튼 적용

위젯은 `engine.button → engine.annotatedButton → golemancer.button → golemancer.costButton`, 화면은 `engine.button.view → golemancer.button → golemancer.costButton → golemancer.purchase` 순서다.

공통 팩은 활성 여부, 보조 문구, 비활성 사유와 activate 이벤트만 다룬다. 게임이 가격 문자열·구매 가능 수량·사유를 제공한다. 공유 Android/Linux/iOS 표시 계층의 구매 확인 버튼에 이 원형을 사용한다. 누르는 동안 구매 조건이 바뀌면 명령을 취소하고, 키보드 확인에서도 현재 조건을 다시 검사한다. WPF 화면과 전용 원형 메뉴의 렌더링은 기존 방식이다.

이미지 ID는 resource 속성의 기본값/값으로 상속할 수 있으며 렌더러 ID도 교체할 수 있다. 이미지 파일 해석, 이미지 슬롯의 실제 그리기, 9-slice, 다중 고정/늘어남 구간, 반복 타일 렌더러는 이 상속 변경에 구현하지 않았다. 해당 기능을 제공할 팩이 속성·슬롯·DLL 계약을 정의하면 같은 상속 규칙을 사용할 수 있다.
