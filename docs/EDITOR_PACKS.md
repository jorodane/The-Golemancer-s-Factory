# 에디터 객체팩

게임팩을 편집하는 WPF 셸에 `editor-1` 객체팩을 동적으로 장착한다. 게임의 DLL은 여전히 게임 프로세스에서만 로드한다. 에디터팩도 공통 `PackCompiler`와 `UiCatalog`를 사용하며, 에디터 전용 등록 계약은 `PackEngine.Editor.Contracts.dll`에 있다. 엔진 코어와 게임 SDK는 변경하지 않는다.

## 사용

1. `StartEditor.exe`를 실행한다. 기본 제공 **작업 도구** 패널은 `editor.core.tools`의 실제 DLL/XML이다.
2. **에디터팩** 탭에서 새 팩 ID와 저장 범위를 선택한다. **새 독립 패널 팩**은 기본 도구를 재사용하는 새 탭, **선택한 팩의 패널 상속**은 현재 활성 패널의 부분 수정본을 만든다.
3. 파일을 선택해 XML을 편집하고 **변경 미리보기 → 검토한 변경 저장 → 선택한 팩 적용**으로 반영한다. 저장본 다시 읽기는 미저장 초안을 버린다. 마지막 변경은 버전이 맞으면 되돌릴 수 있다.
4. 새 동작이 필요하면 **DLL 구현 추가**를 누른다. 생성된 `Commands.cs`를 수정하고 **선택 팩 빌드 → 선택한 팩 적용**을 사용한다. 명령은 `editor.xml`에 등록되며, 화면의 `On`에서 연결한다. DLL 빌드는 .NET SDK가 필요하다. XML 수정에는 컴파일러가 필요 없다.
5. 다른 프로젝트에서 쓸 팩은 공용 폴더에 넣고 목록을 새로고침한 뒤 체크해 적용한다. 팩 폴더에 XML·소스·`Bin/net48`을 함께 보관하면 된다.

| 범위 | 기본 경로 | 의존 가능한 범위 |
|---|---|---|
| 기본 제공 / 코어 | `editor/Packs/<팩>` | 코어 |
| 공용 플러그인 | `%LOCALAPPDATA%/PackEngine/EditorPacks/<팩>` | 코어·공용 |
| 프로젝트 전용 | `<현재 게임 프로젝트>/EditorPacks/<팩>` | 코어·공용·같은 프로젝트 |

프로젝트 전용 팩은 게임팩 폴더와 함께 옮길 수 있다. 실행 여부는 이 PC의 설정에 저장하며, 처음 발견한 프로젝트/공용 팩의 DLL을 자동 실행하지 않는다. 프로젝트 전환 시 이전 팩의 화면·프로세스를 먼저 해제한다. 폴더를 삭제하거나 활성 체크를 끈 뒤 적용하면 해제된다. ID는 전체 활성/발견 범위에서 고유해야 한다. 코어로 채택할 팩도 엔진 라이브러리에 합치지 않고 `editor/Packs`의 별도 객체팩으로 유지한다.

## 팩과 정의 상속

```xml
<ObjectPack id="my.editor" version="1.0.0" contracts="editor-1" extends="editor.core.tools">
  <Ui path="ui.xml" /><Data path="editor.xml" />
</ObjectPack>
```

`extends`/`Depends`의 버전·순환·부모 누락은 공통 팩 로더가 검사한다. 팩 상속은 부모 DLL의 C# 클래스 상속이 아니다. 부모의 등록 기능을 사용하며, 바꿀 정의를 별도로 상속한다.

```xml
<Ui version="1" id="my.editor.ui">
  <View id="my.editor.tools" extends="editor.core.tools">
    <Override node="refresh"><Set property="text" value="자료 다시 읽기" /></Override>
    <Override node="tools"><Slot name="children">
      <Node id="myTip" widget="editor.button" order="20">
        <Set property="text" value="내 도구" /><On event="activate" command="my.editor.tip" />
      </Node>
    </Slot></Override>
  </View>
</Ui>
```

```xml
<EditorExtensions version="1">
  <Command id="my.editor.tip" extends="editor.core.notice">
    <Argument name="value" value="이 프로젝트에서만 필요한 안내" />
  </Command>
  <Panel id="my.editor.tools" extends="editor.core.tools" title="내 작업 도구" view="my.editor.tools" />
</EditorExtensions>
```

Widget/View는 기존 `docs/INHERITANCE.md`의 계약 보존·부분 Override·슬롯 추가·설정 출처 추적을 그대로 사용한다. Panel과 Command도 공통 `DefinitionInheritance`로 해석한다. 생략은 상속, 명시는 재정의, 새 ID는 추가이며 삭제 연산은 없다. Command의 payload 타입은 바꿀 수 없고, 같은 payload를 받는 다른 handler로 교체할 수 있다. 선언한 Argument 이름으로 설정을 추가·수정할 수 있다.

에디터의 기존 좌우 패널과 로그 영역도 XML로 조정할 수 있다. `<Shell id="my.layout" extends="editor.core.layout" sidebarWidth="180" />`를 `EditorExtensions`에 추가하면 왼쪽 탐색기 너비만 바뀌고 나머지는 상속한다. `sidebarWidth` 0~600, `contextWidth` 180~700, `logHeight` 0~600이며 좌우 너비 합은 1000 이하여야 한다. DLL을 고치지 않고 AI가 XML을 수정한 뒤 재적용하면 에디터 배치에 즉시 반영된다. 같은 기본 배치를 서로 다른 자식이 동시에 대체하면 충돌로 알린다.

Panel의 `slot`은 에디터 내 장착 자리다. 같은 슬롯에서는 자식 패널이 조상 패널을 대체한다. 서로 상속 관계가 아닌 두 최종 패널이 같은 슬롯을 요구하면 충돌이다. 새 슬롯은 새 탭을 추가한다. `title`, `view`, `slot`, `order`만 부분 재정의할 수 있다.

## DLL 계약과 동적 교체

팩은 `IPackModule<IEditorPackRegistry>`에서 이름 있는 `IEditorPackCommand`를 등록한다. 명령은 `EditorInvocation`의 XML 인자·이벤트 payload·작은 현재 문맥을 받아 `EditorCommandResult`를 반환한다. 구현 DLL을 서로 참조하지 않고 공개 ID와 계약을 사용한다. `Register`는 등록만 하고 외부 상태를 변경하지 않는 방식으로 작성한다.

새 세대는 활성 팩의 DLL/종속 DLL/XML을 임시 폴더로 복사하고 해시가 일치하는지 확인한다. 요청별 실행 권한을 검사한 뒤 별도 `PackEngine.PackHost` 프로세스에서 공통 로더를 실행한다. 의존성·상속·명령·바인딩·플랫폼 렌더러를 검사하고, WPF 화면 구성도 성공했을 때 현재 화면을 교체한다. 실패하면 기존 세대를 유지한다. 이전 구독/화면을 Dispose하고 프로세스를 종료하므로 net48에서도 같은 어셈블리 이름의 새 DLL을 적용할 수 있다. 원본 DLL은 실행 중 잠기지 않는다.

세대 전체를 교체하므로 팩 내부의 메모리 상태와 팩의 입력 컨트롤 상태는 초기화된다. 에디터 대화·게임 세션·편집 중인 문서 자체를 재시작하지 않는다. 별도 프로세스는 수명과 장애 분리를 위한 것으로, OS 권한 샌드박스가 아니다. 팩은 신뢰할 수 있는 로컬 코드여야 한다. 네이티브 DLL/PInvoke 배포와 동일 어셈블리 ID의 서로 다른 private DLL을 동시 로딩하는 구성은 지원하지 않는다. 충돌하는 managed DLL은 사전 거부한다. 팩당 하나의 빌드 프로젝트를 선언하며, 그 프로젝트의 참조로 private managed 의존성을 구성한다.

## 현재 UI 제공 범위

첫 어댑터는 WPF 흐름 배치의 `editor.stack`, `editor.text`, `editor.button`, `editor.input`을 제공한다. 명시된 활성·표시·툴팁·글자 크기·여백, 크기 제한, 텍스트, 방향, `activate`/`changed`와 자식 슬롯을 지원한다. 부모 위젯에 없는 속성을 추가하더라도 해당 어댑터가 처리하지 못하면 로딩을 거부한다. anchor/offset/safeArea, 사용자 정의 네이티브 컨트롤·렌더러는 아직 제공하지 않는다.

현재 값 바인딩은 `editor.project`, `editor.selection`이며 장착 시점의 스냅샷이다. 명령의 Context는 호출 시점의 프로젝트 이름·선택 ID다. 호스트 효과는 프로젝트 새로고침, 기존 탭 선택, 집중/기본 배치 전환이다. 임의 WPF 객체 접근이나 임의 파일 실행 효과는 없다. 그 외 독자적인 계산은 팩 DLL에서 수행하고 결과 메시지를 반환할 수 있다.

기존 탐색기·채팅·문서 편집기 전체를 객체팩으로 이관한 버전은 아니다. 새 패널과 기본 작업 도구를 팩으로 구성하고 일부 기존 셸 동작을 공개 계약으로 연결한 첫 호스트다.

## AI 문맥과 수정

에디터 안의 Codex와 기존 ChatGPT MCP 연결 모두 `packengine_editor`를 사용한다. 게임팩의 `WritablePacks`와 에디터팩의 `WritableEditorPacks`는 구분된다. **에디터팩** 탭의 팩별 **AI 수정 허용**, **AI가 수정한 에디터팩 적용 허용**을 요청을 보내기 전에 설정한다. 권한은 요청 시작 시 고정한다.

‘이거’ 모드로 팩 패널의 요소를 클릭하면 명령을 실행하는 대신 객체 ID와 XML 출처를 선택한다. 범위 모드에서는 여러 요소를 클릭해 추가한다. 일반 대화는 화면·hover·최근 선택을 자동 첨부하지 않는다. 전송/MCP 문맥 요청 시 `EditorInput`과 해당 XML의 해시·제한된 내용을 동결한다. `inspect`는 실행 중인 세대의 상속 출처, `read`는 현재 디스크 소스라는 차이를 유지한다.

도구는 `list/inspect/read/patch/apply/undo/build/reload`를 제공한다. 수정은 현재 파일을 읽어 얻은 해시와 정확히 한 번 일치하는 문구를 요구하며, 미리보기·저장·빌드·실행 반영을 구분한다. 열린 사용자 초안이나 오래된 해시는 거부한다. 재로딩은 바뀐 모든 팩이 요청의 허용 범위에 있는지 DLL 실행 전에 검사한다. 변경 원본과 결과는 PC의 `PackEngine/EditorPackChanges`에 보존하고, 에디터의 읽기/작업 기록에도 실제 도구 호출을 남긴다. AI가 새 파일을 임의 생성하는 도구는 없으며, 새 팩과 DLL 뼈대는 관리 화면에서 생성한 뒤 AI가 수정한다.

## 검증

`python tools/verify-editor-packs.py --dotnet <SDK의 dotnet>`은 실제 독립 DLL 컴파일/실행, 같은 이름 DLL의 새 버전 교체, 이전 프로세스 종료, 실패한 세대 보존, 부분 상속·추가·출처, 네이티브 계약 사전 검사, 구독 해제, 권한·해시·초안·되돌리기, 프로젝트 팩 해제를 검사한다. 이 검증은 실제 모델 응답이나 Windows 화면 조작을 대신하지 않는다. Windows net48 빌드도 별도로 수행한다.
