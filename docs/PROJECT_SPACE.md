# Confectory 프로젝트 작업 공간

프로젝트 메인 페이지 설계와 추가 실행·Pack 설계를 함께 적용한다. 새 프로젝트와 기존 프로젝트는 같은 프레임을 사용한다. 프로젝트에 들어오면 112px AI 관리 사이드바 아래에 공용 프로젝트 채팅이 나타나고, 중앙은 프로젝트의 MainScreen 또는 선택한 편집 화면이 사용한다. 우하단의 고정된 `▶ / ■`와 `≡` 외에는 상단 툴바·탐색기·Inspector·Console·상태줄을 상시 배치하지 않는다.

새 프로젝트는 Main Pack과 빈 개념·객체·View 문서를 가진다. 별도 첫 실행 화면이나 첫 요청창을 만들지 않는다. Helper 대화는 아이콘을 선택할 때 열며, MAIN은 기존 Helper 한 명에게 붙는 붉은 테두리와 사각 라벨이다. 프로젝트 채팅은 특정 Helper의 개인 대화와 별개다.

## 탐색과 편집

`≡`의 일반 항목은 프로젝트의 카테고리 구조로 생성한다. 카테고리는 다음 분류로 이동하고, 개념은 객체 편집 공간을 연다. 하단 시스템 진입점은 **개념·기능·팩**이다. 실행 설정·기존 XML·에디터팩·참여자·로그 등은 명시적인 도구 동작으로 연다. 개념·테이블·팩·기능 화면은 중앙에 열리며 AI·채팅과 우하단 버튼을 재배치하지 않는다.

| 표현 | 의미 | 동작 |
|---|---|---|
| 카테고리 가지 | 분류 | 카테고리와 개념을 포함한다 |
| ○ 개념 | 객체를 생성할 수 있는 정의 | 클릭하면 가까운 플로팅 스키마를 연다 |
| ◎ 개념 | Variation을 가진 정의 | 더블클릭하면 다음 상속 레이어에 들어간다 |
| 레이어 깊이·경로 | 상속 | 카테고리 문맥과 상속 경로를 함께 표시한다 |
| Hover 점선 | 사용하는 개념 | 평소에는 숨긴다 |
| Shift + Hover 점선 | 사용하는 쪽의 개념 | Android 터치에서는 길게 눌러 관계를 선택한다 |
| ≡ | 탐색 | 공간을 연다 |
| + | 생성 | 현재 개념의 객체·반복값·현재 공간의 정의를 만든다 |

맵은 카테고리부터 개념으로 일정한 방향을 가진다. 스키마 Popup은 노드 위, 부족하면 아래에 열며 노드 좌표를 바꾸지 않는다. 빈 공간을 누르거나 다른 개념을 선택하면 닫힌다. 스키마 행에는 이름·타입·단일/다중·일반/복합/기능·오른쪽 삭제를 둔다. 단일/다중과 항목 종류는 같은 Popup의 독립된 두 선택이다.

## Schema, Object, View

세 문서는 서로 다른 책임을 가진다. 일반 값·복합 구조·함수 계약은 Schema, 실제 값과 고정 ID는 Object, 표시와 편집 방식은 View에 둔다. Source Pack은 문서의 소유권으로 결정되는 시스템 정보이며 Schema에 필드를 추가하지 않는다.

기본 테이블은 모든 스키마를 즉시 편집하는 fallback이다. 문자열·숫자·논리·타입에 맞는 객체 참조·복합·반복 목록·계약을 만족하는 기능 선택을 지원한다. Source Pack 셀은 직접 이주할 수 있으며 전체/Pack 필터와 Main 우선 그룹 정렬을 제공한다. Schema에 단일 문자열 `이름`/`Name`이 있다면 그 값을 객체 표시 이름으로 사용한다. Schema의 이름 열을 다시 별도 시스템 열로 중복 표시하지 않는다.

`table`, `cards`, `slots` View는 동일한 값에 직접 연결한다. `slots`는 입력/출력 아이콘·수량과 클릭 편집을 제공한다. 슬래시로 나눈 Field ID 경로로 복합 하위 값을 연결하고, 필요한 열만 선택해 밸런싱 표를 만들 수 있다. Source Pack 표시는 View별 선택이다. 여러 View를 추가해도 Schema와 Object를 다시 만들지 않는다. 신규 스키마 항목의 기본값을 표시하는 것만으로 Object 문서를 수정하지 않고 실제 입력 시 값이 생긴다. 연결 경로가 사라진 View는 기본 테이블로 돌아간다.

임의의 UI·동작은 기존 `editor-1` 팩으로 구현하고 `pack` View의 `editor`에 ObjectEditor ID를 지정한다. 대상 종류는 `concept.<ConceptId>`, 객체 키는 `concept-object:<ObjectId>`다. 기존 프로젝트 데이터 API로 같은 객체를 읽고 버전 확인 후 수정 제안을 반환한다. AI가 표현만 변경하는 요청을 받으면 View 문서 또는 해당 에디터팩을 수정하고 Schema와 Object 문서를 유지한다. 이 형식과 예제는 `packengine_editor`의 `api`에도 포함된다. 새 DLL 실행과 AI 제안 확정은 기존 검토 경계를 유지한다.

## Pack과 Namespace

Pack 화면은 생성·이름·설명·Namespace·의존성·대량 이주만 담당한다. 개념·기능·View 편집을 복제하지 않는다. 요소의 ID, 소속 Pack, 논리 이름은 분리한다. 논리 주소는 `Namespace.Symbol`이며 파일 경로가 아니다. 일반 화면은 짧은 이름을 사용하고 충돌·상세·기능 경로에서 주소를 표시한다.

이주는 같은 ID와 참조를 유지하면서 정의를 소유 Pack의 문서로 옮긴다. 스키마·카테고리·객체·기능·View의 참조로 추론한 의존성은 `Depends reason="concept-space"`로 갱신한다. 수동 의존성은 유지한다. 순환하는 Pack 의존성은 연결된 요소를 함께 옮겨 해결하며, 실패한 이주는 디스크와 메모리의 기존 소유권을 유지한다. 개별 요소의 컨텍스트 메뉴나 Source Pack 버튼에서도 이주한다. 읽기 전용 Pack은 수정·이주하지 않는다.

등록 예:

```xml
<ConceptSpace mainPack="foundation">
  <Pack id="foundation"
        schema="Packs/00.Foundation/concept-schema.xml"
        objects="Packs/00.Foundation/concept-objects.xml"
        views="Packs/00.Foundation/concept-views.xml" />
</ConceptSpace>
```

Pack의 `pack.xml`에 각 문서를 `Data`로 등록한다. 세 루트는 `ConceptSchema`, `ConceptObjects`, `ConceptViews`, `version="1"`이다.

```xml
<ConceptSchema version="1">
  <Category id="library" name="콘텐츠" symbol="Library" parent="" />
  <Concept id="entry" name="기록" symbol="Entry" category="library" extends="">
    <Field id="name" name="이름" type="text" kind="normal" multiple="false" />
    <Field id="attributes" name="속성" type="text" kind="composite" multiple="true">
      <Field id="label" name="라벨" type="text" kind="normal" multiple="false" />
      <Field id="weight" name="가중치" type="number" kind="normal" multiple="false" />
    </Field>
  </Concept>
</ConceptSchema>
```

```xml
<ConceptObjects version="1">
  <Object id="entry-one" name="첫 기록" concept="entry" icon="">
    <Value field="name" text="첫 기록" />
    <Value field="attributes" text="">
      <Item text=""><Value field="label" text="기본" /><Value field="weight" text="1" /></Item>
    </Value>
  </Object>
</ConceptObjects>
```

```xml
<ConceptViews version="1">
  <View id="balance" name="가중치 보기" symbol="Balance" concept="entry"
        layout="table" editor="" sourcePack="true">
    <Field path="name" label="이름" side="" icon="" quantity="" />
    <Field path="attributes/weight" label="가중치" side="" icon="" quantity="" />
  </View>
</ConceptViews>
```

## 기능과 실행

스키마의 기능 항목은 구현 ID를 고르기 위한 계약이다. 이름·입력 이름/타입/단일·다중/구조·반환 타입을 정적으로 비교한다. 반환 없음은 `void`다. 기능 화면에서는 Pack Namespace와 논리 Symbol의 계층으로 실제 Function을 탐색하고, 입력·출력에서 **구현**으로 들어간다. 일반 흐름에서 소스 파일을 먼저 찾지 않는다.

구현을 생성하면 고정 ID의 C# 소스, public static Handler, 소유 Pack의 Functions 프로젝트를 등록한다. 기본 코드의 원시 타입은 string/double/bool이며 개념과 구조 매개변수는 object로 전달하는 계약이다. 소비 프로젝트의 실행 호스트가 해당 데이터를 해석한다. 저장은 C# 구문과 Handler의 정적 서명을 확인하고, **팩 빌드**는 실제 .NET DLL을 만든다. Namespace 또는 Pack이 바뀌어도 소스의 물리 경로와 Handler를 유지할 수 있다. 새 소유 프로젝트의 Compile 링크와 `{framework}` DLL 경로를 갱신한다.

`FunctionAssembly`는 함수 라이브러리다. 엔진 모듈의 `Assembly`처럼 IPackModule 진입점이 있다고 가정하지 않는다. 프로젝트 실행 패키지는 선언된 기능 DLL과 의존성 파일을 포함하고 대상 framework를 확인한다. 정적 계약 일치·코드 빌드·실행 결과는 서로 다른 근거다. Function을 선언하거나 프로젝트를 여는 것만으로 코드를 실행하지 않는다.

`▶`는 Windows에서 선택한 프로젝트 Target의 Run 명령을 실행하며 `■`는 이 세션이 실행한 프로세스를 닫는다. 새 빈 프로젝트는 중앙의 빈 실행 공간을 열고 같은 버튼으로 닫는다. 객체/기능이 있는 프로젝트에 실행 대상이 없으면 연결이 필요하다고 표시한다. 런타임과 MainScreen 구성은 소비 프로젝트의 책임이며 Schema를 만들었다고 임의의 게임 실행기를 생성하지 않는다.

Android는 실행 설정의 `Target platform="android" androidApplication="..."`에 연결된 설치 앱 Activity를 연다. 종료 결과를 받아 버튼을 복원하고 `■`는 해당 요청으로 실행한 Activity를 닫는다. 프로젝트 앱은 Activity 결과와 종료 요청을 지원해야 한다. Android 앱 안에서 PC용 빌드·검증 명령을 실행하지 않는다.

## 저장과 검증

개념 공간의 읽기는 문서·DLL·명령을 실행하거나 생성하지 않는다. 저장은 모든 관찰 문서의 해시와 열린 초안을 검사하고 하나의 FileProposalBundle로 적용하며 이전 텍스트를 보관한다. 다른 편집기의 Schema·Object·View·코드 변경이 있으면 덮어쓰지 않는다. 다시 불러오기는 디스크의 최신 상태를 연다. 공동 편집 클라이언트의 확정은 호스트에서 진행한다.

`tools/verify-studio.py`는 빈 프로젝트, 분류/Variation, 스키마 상속, 타입/기능 계약, 복합·다중 값, View/Data 분리, Pack 이주·의존성·Namespace, 함수 DLL 실제 호출, 버전 충돌과 읽기 전용을 검사한다. `tools/verify-editor-packs.py`는 빈 MainScreen, 기존 커스텀 에디터와 실행 패키지 호환을 검사한다. Windows WPF와 Android 참조 컴파일은 GUI 실기기 검증과 구분한다.
