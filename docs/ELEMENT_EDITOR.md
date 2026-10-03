# 요소 중심 에디터

메인 작업 공간에는 파일 대신 XML 안의 요소를 표시한다. 카테고리 안의 카드에는 아이콘·이름·설명이 보인다. 클릭은 대상을 선택하며 별도 검사 창을 열지 않는다. 이름 클릭/F2와 설명 클릭으로 직접 편집하고, **▾ 자세히**는 해당 카드 안에서 속성·하위 요소·문자열을 펼친다. **변경 확정**으로 버전을 확인하고 검토한 내용을 저장한다. 취소하면 입력 초안은 유지한다. 원문과 관리 기능은 **도구**에서 명시적으로 연다.

기본 계층 양식은 `workspace.main` 패널 안에서 동작한다. 호스트 등록 ID는 `panel.workspace.main`이며, 해당 패널을 상속한 프로젝트 팩이 초기화·입력·동작 명령도 자기 ID로 상속한다. 종류별 전용 에디터는 **다른 양식**에서 선택한다. `editor.card`는 선택 가능한 항목 컨테이너, `editor.inline`은 표시 상태에서 클릭/F2로 입력을 여는 네이티브 위젯이다. 인라인 입력도 기존 컨트롤 재사용·지연 응답·IME 보존 규칙을 따른다.

기본 탐색기와 계층 편집기는 독립 코어 UI팩이다. 구현은 `editor/Packs/CoreTools/Elements.cs`, 레이아웃은 같은 폴더의 `ui.xml`, 등록은 `editor.xml`이다. 전용 게임 에디터도 같은 공개 요소 API를 사용할 수 있다. 게임 SDK와 엔진 코어의 DLL 계약은 변경하지 않는다.

## 프로젝트 메뉴와 창

Windows의 **도구 → 팩 창**, Android의 **도구 → 창 관리**는 로드된 팩의 등록 창을 보여준다. 창은 이미 로드된 DLL로 열고, 같은 창은 다시 만들지 않고 앞으로 가져온다.

~~~xml
<EditorExtensions version="1">
  <Navigation id="my.editor.recipes" title="레시피"
              surface="menu" group="제작" order="10" category="레시피" />
  <Navigation id="my.editor.quick" title="레시피 창"
              surface="hotbar" window="my.editor.recipe.window" />
  <Navigation id="my.editor.side" title="도움말"
              surface="navigation" command="my.editor.help" />
</EditorExtensions>
~~~

surface는 menu/hotbar/navigation, 대상은 category/window/command 중 하나다. category는 해당 분류와 하위 분류를 연다. 명령은 None 또는 Text payload를 받으며 Text 초기값은 payload로 선언한다. group과 order로 메뉴 묶음과 순서를 지정한다. 없는 창·명령 참조는 적용 전에 거부한다.

Navigation은 한 부모의 부분 상속을 사용한다. 같은 surface의 자식은 부모 항목을 대체하며, 다른 surface이면 둘 다 표시한다. 생략은 상속이므로 대상 종류를 바꾸면서 부모 필드를 삭제할 수는 없다. 다른 대상 종류에는 독립 항목을 선언한다.

새 프로젝트 DLL은 프로젝트를 여는 것만으로 실행하지 않는다. Windows의 **선택한 팩 적용**, Android의 **프로젝트 에디터팩 적용**에서 선택한다. Android는 C#를 컴파일하지 않으므로 Windows에서 빌드한 팩을 설치한다.

## 프로젝트 인덱스와 생성 규칙

종류·분류·아이콘·필드 의미는 프로젝트의 IndexRules에 둔다. 엔진에는 레시피, 골렘, 특정 버튼의 이름이나 규칙을 넣지 않는다.

~~~xml
<IndexRules version="1">
  <Symbol kind="recipe" select="/Content/Recipes/Recipe"
          id="id" title="name" category="레시피"
          categoryAttribute="group" icon="⚒" />
  <Authoring kind="recipe" document="recipes.xml">
    <Field name="name" required="true" />
    <Field name="group" default="기본" />
    <Field name="quality" type="enum" default="normal">
      <Option value="normal" title="보통" />
      <Option value="high" title="높음" />
    </Field>
    <Field name="item" element="Input" reference="item" required="true" />
    <Field name="amount" element="Input" type="number" default="1" />
    <Child parent="Recipe" name="Inputs" />
    <Child parent="Inputs" name="Input" />
    <Template><Recipe><Inputs /></Recipe></Template>
  </Authoring>
</IndexRules>
~~~

| 선언 | 의미 |
|---|---|
| Symbol kind/select/id/title | 종류, XPath, 식별자 속성, 표시 이름 속성 |
| category | /로 구분한 분류 경로. 생략하면 kind |
| categoryAttribute | 요소 속성의 값을 하위 분류로 추가 |
| icon | 고정 글리프 또는 선언된 이미지 경로 |
| iconAttribute | 고정 icon이 없을 때 읽을 요소 속성. 생략하면 icon 속성 |
| Authoring parent/element | 생성 컬렉션의 단순 절대 경로와 태그. 단순 Symbol XPath이면 추론 가능 |
| document | 선택한 팩 안에서 사용할 기존 선언 문서의 파일명 |
| Field name/element | 속성 이름과 적용 태그. element 생략은 선택한 루트에만 적용 |
| type | text/enum/boolean/number. number는 유한한 숫자 |
| required/default | 필수 값과 생성 기본값. 기존 미지정 속성을 기본값으로 가장하지 않음 |
| reference | 다른 인덱스 종류의 ID 후보. 런타임 구현 증명은 아님 |
| Child parent/name | 부모 태그 아래에 추가할 수 있는 세부 요소 |
| Template | 새 요소에 복사할 XML 한 개. 선언 element와 태그가 같아야 함 |

Symbol은 Data와 Ui 모두를 분류한다. UI 정의를 button 같은 프로젝트 종류로 분류하면 기본 widget/view 인덱스는 참조·상속 조회용으로 남고 일반 탐색에서는 중복 표시하지 않는다. Browsable로 이 차이를 조회할 수 있다.

카드는 이름으로 정렬한다. **그리드 / 세로 목록 / 가로 목록**을 고를 수 있고 검색·분류 이동에도 Key에서 만든 카드 ID를 유지한다. 아이콘이 없으면 글리프를 표시한다. 이미지는 기존 Asset 선언 범위의 bitmap만 읽는다. 기본 미리보기는 한 화면 최대 16개 고유 이미지, 이미지별 64,000자, 전체 600,000자로 제한한다. 큰 이미지는 글리프로 표시한다.

**요소 추가**는 종류·이름·소속 팩을 고른다. 호스트는 그 팩의 기존 선언 컬렉션 하나에 Template과 기본값을 넣은 변경안을 만들고 검토 후 저장한다. 임의 파일을 만들지 않는다. 컬렉션이 여러 문서에 있으면 document로 지정한다. 표시 이름이 ID 자체인 종류는 이름을 ID로 사용하고, 별도 title 속성이 있으면 ID를 자동 생성한다.

## 계층과 입력

각 노드를 펼쳐 속성·문자열을 수정하고 세부 요소·필드를 추가한다. 선언한 enum/boolean, reference 후보와 다른 요소에서 관찰한 문자열을 제공한다. 관찰 문자열은 자유 입력을 제한하지 않는다. 타입·필수 값·허용 자식은 변경안 생성 때 검증한다. 읽기 전용·원문 미저장 초안·디스크 충돌은 먼저 정리해야 한다.

입력 ID는 Key, 상대 하위 경로, 속성 이름의 SHA-256 일부에서 만든다. 타이핑은 팩 메모리에 값만 기록하고 목록 조회나 XML 갱신을 반환하지 않는다. 다른 요소로 이동했다 돌아오면 같은 ID와 미저장 값을 공급한다. 후보를 고르면 동일 입력의 Set text로 즉시 표시한다. 검토 후 새 서비스로 읽어 최신 해시에 맞추며 취소한 입력은 보관한다.

초안·펼침은 로드된 모듈의 메모리에 프로젝트별로 보관한다. 모듈 교체·앱 종료를 넘는 초안 영속화는 별도 작업이다. 동일 네이티브 컨트롤 재사용·커서·IME 계약은 [에디터팩](EDITOR_PACKS.md)을 참고한다.

## 전용 에디터와 UI팩 상속

ObjectEditor는 kind와 선택적인 category 하위 트리를 창에 연결한다. 높은 priority, 같은 우선순위에서는 구체적인 kind와 좁은 category를 고른다. 기본 폼은 priority -100이다. 특정 kind/category로 파생해도 다른 종류에 대한 부모의 기본 폼을 제거하지 않는다. **전용 에디터 선택**으로 같은 요소를 다른 양식에서 열 수 있다.

기본 폼의 배치·표시를 상속하는 팩은 다음처럼 선언한다. pack.xml은 editor-1이며 core 팩에 의존하거나 core 팩을 상속한다.

~~~xml
<!-- ui.xml -->
<Ui version="1" id="my.editor.ui">
  <View id="my.editor.recipe.view" extends="editor.core.inspector">
    <Override node="inspectorHeading"><Set property="fontSize" value="24" /></Override>
    <Override node="inspectorRoot"><Slot name="children">
      <Node id="recipeHelp" widget="editor.text" order="-10">
        <Set property="text" value="이 게임의 제작 규칙을 확인하고 수정해." />
      </Node>
    </Slot></Override>
  </View>
</Ui>

<!-- editor.xml -->
<EditorExtensions version="1">
  <Window id="my.editor.recipe.window" title="레시피 편집기" view="my.editor.recipe.view" />
  <Command id="my.editor.recipe.open" extends="editor.core.inspector.open">
    <Argument name="window" value="my.editor.recipe.window" />
    <Argument name="baseView" value="my.editor.recipe.view" />
    <Argument name="actionCommand" value="my.editor.recipe.action" />
    <Argument name="inputCommand" value="my.editor.recipe.input" />
  </Command>
  <Command id="my.editor.recipe.action" extends="editor.core.elements.action" />
  <Command id="my.editor.recipe.input" extends="editor.core.elements.input" />
  <ObjectEditor id="my.editor.recipe" extends="editor.core.element.form"
    kind="recipe" title="레시피 편집기" priority="10"
    window="my.editor.recipe.window" command="my.editor.recipe.open" />
</EditorExtensions>
~~~

세 명령을 자식 팩 소유 ID로 선언해야 자식 창의 동적 뷰 소유권이 맞는다. 이 예제는 별도 DLL 없이 코어의 실제 handler를 재사용한다. 더 특수한 조작·배치는 자체 DLL이 ReadElement 결과를 게임용 View로 만들고 ProposeElement를 검토에 제출하는 방식으로 구현한다.

호스트는 창에 EditorObjectContext를 묶고 초기 Text 명령에 Key를 전달한다. `editor.object.key`, `.kind`, `.title`, `.pack`은 UI Bind 소스다. 명령 Context는 이벤트 발생 시점의 project/projectId/editorPack/windowId/nodeId/selection과 해당 창의 objectKey/objectKind/objectTitle/objectPack/objectEditor를 보관한다. 전역 선택이 바뀌어도 다른 창은 그 창의 요소를 기준으로 실행한다.

## 공개 API

IEditorProjectElements는 기존 EditorProjectCommand의 프로젝트 서비스에 선택적으로 제공한다.

| 메서드 / 결과 | 동작 |
|---|---|
| ListElementTypes / ListElementPacks | 종류, 생성 가능 여부, 소속 팩·편집 가능 여부 |
| ReadElement(Key) | 원문을 열지 않고 해시·계층·필드·후보·생성 템플릿 조회 |
| ProposeElement(Edit) | Key, 화면의 ExpectedHash, 상대 경로의 attribute/text/child 변경을 문서 변경안으로 변환 |
| ProposeNewElement(Create) | Kind/Pack/Name과 선택적인 Id로 기존 선언 컬렉션에 생성안 준비 |
| Result.OpenObject | Key와 선택적인 EditorId/ChooseEditor로 등록 에디터 열기 |
| Result.OpenXml | Key의 원문을 명시적으로 열기 |
| Result.Continue | 출력·검토 후 같은 팩의 Text 명령을 새 프로젝트 서비스로 실행 |

변경 경로는 `.` 또는 `./*[1]/*[2]`처럼 선택 요소 안의 인덱스 경로다. 원문 해시가 바뀌면 거부하므로 오래된 경로로 순서 변경을 덮어쓰지 않는다. 루트 ID는 수정할 수 없고 변경은 1~200개다. child의 Index를 지정하면 현재 자식 수 다음 위치인지도 검사한다. 취소한 검토는 Continue를 실행하지 않는다.

`packengine_editor(operation="api")`는 호스트 소스 공유 없이 이 계약과 명세 경로를 반환한다. inspect에는 Navigation/ObjectEditors도 포함한다. 실제 DLL과 후보·초안 복귀, 검토·취소·해시 충돌, 생성·읽기 전용 거부, 자식 UI팩 상속, 창 컨텍스트를 자동검증한다. 네이티브 화면·터치·스크롤·한글 IME 수용 확인은 Windows와 Android에서 별도로 수행한다.
