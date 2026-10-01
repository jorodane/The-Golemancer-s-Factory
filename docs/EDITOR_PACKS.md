# 에디터 객체팩

게임팩을 편집하는 WPF 셸에 `editor-1` 객체팩을 동적으로 장착한다. 게임의 DLL은 여전히 게임 프로세스에서만 로드한다. 에디터팩도 공통 `PackCompiler`와 `UiCatalog`를 사용하며, 에디터 전용 등록 계약은 `PackEngine.Editor.Contracts.dll`에 있다. 엔진 코어와 게임 SDK는 변경하지 않는다.

## 사용

1. `StartEditor.exe`를 실행한다. 기본 제공 **작업 도구** 패널은 `editor.core.tools`의 실제 DLL/XML이다.
2. **에디터팩** 탭에서 새 팩 ID와 저장 범위를 선택한다. **새 독립 패널 팩**은 기본 도구를 재사용하는 새 탭, **선택한 팩의 패널 상속**은 현재 활성 패널의 부분 수정본을 만든다.
3. 파일을 선택해 XML을 편집하고 **변경 미리보기 → 검토한 변경 저장 → 선택한 팩 적용**으로 반영한다. 저장본 다시 읽기는 미저장 초안을 버린다. 마지막 변경은 버전이 맞으면 되돌릴 수 있다.
4. **등록된 창 열기**는 이미 로드된 기능으로 화면을 만들거나 기존 창을 앞으로 가져온다. **임시 테스트 창**은 선택한 뷰를 별도 창으로 등록해서 열고, **임시 창 해제**는 그 창의 등록과 화면만 제거한다. 창을 닫아도 기능 DLL은 유지된다.
5. 새 동작이 필요하면 **DLL 구현 추가**를 누른다. 생성된 `Commands.cs`를 수정하고 **선택 팩 빌드 → 선택한 팩 적용**을 사용한다. 명령은 `editor.xml`에 등록되며, 화면의 `On`에서 연결한다. DLL 빌드는 .NET SDK가 필요하다. XML 수정에는 컴파일러가 필요 없다.
6. 다른 프로젝트에서 쓸 팩은 공용 폴더에 넣고 목록을 새로고침한 뒤 체크해 적용한다. 팩 폴더에 XML·소스·`Bin/net48`을 함께 보관하면 된다.

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

모듈 로딩과 창 수명은 분리한다. 컴파일 단위는 팩이 선언한 하나의 빌드 프로젝트이며, 여러 창이 같은 기능 모듈을 사용할 수 있다. 창을 열거나 닫을 때 컴파일하지 않는다. 창을 닫거나 임시 등록을 해제해도 모듈의 DLL과 메모리 상태는 유지된다.

`EditorPackRuntime`은 활성 팩의 원본을 임시 폴더에 복사해서 해시를 검사하고, DLL 실행 전에 의존성·버전·범위와 요청 권한을 확인한다. DLL이 있는 팩마다 의존 팩을 포함한 별도 `PackEngine.PackHost` 실행 버전을 관리한다. DLL/관리 종속성 또는 로딩 선언이 바뀐 모듈만 새 worker로 준비하고, 나머지는 기존 worker를 재사용한다. 부모/의존 DLL의 변경은 그 모듈에 의존하는 worker에도 반영된다. XML 화면·명령 설정만 변경했을 때에는 worker를 유지하고 선언과 바인딩을 다시 검사한다. 글로벌 명령 ID의 XML 인자와 payload는 호스트가 해석하고 실제 handler를 소유한 모듈에 전달한다.

창은 `EditorWindowRegistry`의 정의와 인스턴스로 관리한다. `Panel`은 기존 에디터의 탭으로, `Window`는 독립 WPF 창으로 표시한다. 일반 창은 등록만 하고 열 때 화면을 생성하며, `autoOpen="true"`이면 최초 등록 때 연다.

```xml
<EditorExtensions version="1">
  <Window id="my.editor.recipe" title="레시피 검사" view="my.editor.recipe.view" />
</EditorExtensions>
```

Window는 `title`, `view`, `autoOpen`을 부분 상속할 수 있다. `Panel`의 등록 ID는 `panel.<slot>`이므로 같은 슬롯에서 부모/자식이 교체되어도 호스트의 입력 상태를 이어갈 수 있다. 열린 뷰·위젯 계약·참조 명령·소유 모듈 버전이 달라진 창만 새로 구성하고, 다른 창의 인스턴스는 유지한다. 창을 닫으면 입력 상태를 보관하고 다음에 열 때 복원한다. 재구성할 때 입력 문자열, 선택 위치, 스크롤, 독립 창 위치와 크기를 보관한다. 상태는 호스트의 문자열 데이터이며 DLL 객체·네이티브 컨트롤·delegate를 보관하지 않는다. 사라진 입력 ID의 값은 새 화면에 강제로 넣지 않는다.

새 DLL과 화면 준비가 성공하면 활성 버전과 관련 창을 교체한다. 실패하면 이전 모듈과 화면을 유지한다. 바뀐 모듈의 이전 worker는 참조가 없어진 뒤 종료하며, 재사용한 모듈은 계속 실행된다. 원본 DLL은 worker에 복사해서 사용하므로 실행 중에도 국소 컴파일할 수 있다. 프로젝트 전환/에디터 종료 시 창 레지스트리와 모듈 런타임을 각각 정리한다. 별도 프로세스는 수명과 장애 분리를 위한 것으로 OS 권한 샌드박스가 아니다. 네이티브 DLL/PInvoke와 같은 worker 안에서 충돌하는 managed 어셈블리 identity는 지원하지 않는다.

임시 창은 이미 등록된 UI 뷰를 재사용하며 DLL 로드/컴파일 없이 등록·열기·닫기·해제한다. 팩 명령은 `EditorCommandResult.Windows`에 `EditorWindowAction`을 반환할 수 있다. Operation은 `register/open/close/unregister`, Id는 창 ID, 등록할 때 View와 Title을 지정한다. register는 임시 정의만 추가하고 open이 실제 화면을 만든다. 기존에 선언한 일반 창은 close 후 다시 열 수 있으며 임시 해제로 삭제할 수 없다.

## 프로젝트 데이터 접근

프로젝트 문서를 다루는 DLL 명령은 `EditorProjectCommand`를 상속하고 `Execute(EditorInvocation, IEditorProjectData)`를 구현한다. 기존 `IEditorPackCommand`는 그대로 작동한다. 공개 타입은 `PackEngine.Editor.Contracts.dll`에 있으며 엔진 코어와 게임 SDK는 변경하지 않는다.

| API | 반환 내용 |
|---|---|
| `ListDocuments(pack = "")` | 선언된 문서의 프로젝트 상대 경로, 소유 팩, 종류, 수정 가능 여부. 내용은 읽지 않는다. |
| `ReadDocument(path, maximumCharacters = 200000)` | 요청한 문서의 분리된 텍스트 스냅샷, 전체 텍스트 해시 `DocumentHash`, 실제 디스크 바이트 해시 `DiskHash`, `Draft`, `DiskChanged`, `Partial`. |
| `EditorCommandResult.DocumentChanges` | `Path`, `ExpectedHash`, 완전한 교체 `Text`, `Intent`로 구성된 변경안 목록. 호스트가 검토하고 선택된 파일만 저장한다. |

```csharp
public sealed class ChangeDocument : EditorProjectCommand
{
    public override UiValueKind Payload => UiValueKind.Text;
    public override EditorCommandResult Execute(EditorInvocation invocation, IEditorProjectData project)
    {
        var document = project.ReadDocument(invocation.Arguments["path"]);
        if (!document.Editable || document.Partial || document.Draft || document.DiskChanged)
            return new() { Message = "문서 전체를 읽고 미저장·외부 변경을 먼저 정리해줘." };
        return new() { DocumentChanges = [new() {
            Path = document.Path, ExpectedHash = document.DocumentHash,
            Text = invocation.Payload, Intent = "사용자가 편집한 문서 반영"
        }] };
    }
}
```

등록은 기존 `registry.Command(key, command)`를 사용한다. XML의 Command에 맞는 payload와 `path` Argument를 선언한다. 명령을 실행할 때마다 호스트가 현재 프로젝트에 묶인 서비스를 제공하며, worker가 필요한 목록·문서를 역방향 파이프로 요청한다. 프로젝트 전체 내용이나 로컬 절대 경로를 미리 DLL에 전달하지 않는다. 읽기는 문서를 열거나 사용자 초안을 변경하지 않으며, 실제 모듈의 작업 기록을 남긴다. AI가 읽었다는 기록이나 대화 요청을 만들지 않는다.

경로와 문서는 기존 프로젝트 인덱스·상대 경로·심볼릭 링크 검사를 따른다. 계약·프로젝트 설정과 수정 불가능한 팩은 읽기 전용이다. 한 명령에서 조회는 최대 64회, 반환 텍스트는 총 200만 자, 한 문서의 `maximumCharacters`는 1~200만 자이며 기존 파일 크기 제한은 2 MB다. 목록은 최대 5000개다. 더 큰 전체 읽기가 필요하면 상한 안에서 요청 크기를 지정하며, `Partial`인 읽기로 전체 파일 교체를 제안할 수 없다.

변경안은 **같은 명령 호출에서 실제로 읽은 완전하고 깨끗한 문서**의 `DocumentHash`를 요구한다. 파일당 하나, 최대 100개를 반환한다. 호스트는 XML·수정 권한·관찰한 버전을 검사한 뒤 한 검토 창을 연다. 검토 중에 사용자 초안이나 디스크가 바뀌면 다시 검사해서 덮어쓰기를 거부한다. 선택한 파일은 기존 원자적 저장·UTF-8 BOM 보존·되돌리기 경로를 사용하며, 취소하거나 제외한 파일은 쓰지 않는다. 저장 뒤 빌드·게임 실행·DLL 재로딩을 자동으로 수행하지 않는다.

서비스는 명령 호출에만 유효하며 다음 호출에서 재사용할 수 없다. DTO를 보관할 수는 있지만 살아 있는 문서나 세션에 연결된 객체가 아니며, 다음 저장 명령에서는 다시 읽고 원래 편집 기준과 비교해야 한다. 창 닫기·임시 등록 해제는 계속 모듈 로딩과 독립적이다.

AI는 `packengine_editor(operation="api")`로 이 실제 계약과 컴파일 가능한 예제를 조회할 수 있다. 호스트 소스가 게임 프로젝트의 읽기 범위 밖에 있어도 별도 소스 공유가 필요 없다. 이 API는 공통 문서 접근 기반이며 특정 게임의 편집창이나 레시피 해석기를 제공하지 않는다.

## 현재 UI 제공 범위

첫 어댑터는 WPF 흐름 배치의 `editor.stack`, `editor.text`, `editor.button`, `editor.input`을 제공한다. 명시된 활성·표시·툴팁·글자 크기·여백, 크기 제한, 텍스트, 방향, `activate`/`changed`와 자식 슬롯을 지원한다. 부모 위젯에 없는 속성을 추가하더라도 해당 어댑터가 처리하지 못하면 로딩을 거부한다. anchor/offset/safeArea, 사용자 정의 네이티브 컨트롤·렌더러는 아직 제공하지 않는다.

현재 값 바인딩은 `editor.project`, `editor.selection`이며 장착 시점의 스냅샷이다. 명령의 Context는 호출 시점의 프로젝트 이름·선택 ID다. 프로젝트 데이터는 위의 호출 단위 API로 요청한다. 호스트 효과는 프로젝트 새로고침, 기존 탭 선택, 집중/기본 배치 전환이다. 임의 WPF 객체 접근이나 임의 파일 실행 효과는 없다. 그 외 독자적인 계산은 팩 DLL에서 수행하고 결과 메시지를 반환할 수 있다.

기존 탐색기·채팅·문서 편집기 전체를 객체팩으로 이관한 버전은 아니다. 새 패널과 기본 작업 도구를 팩으로 구성하고 일부 기존 셸 동작을 공개 계약으로 연결한 첫 호스트다.

## AI 문맥과 수정

에디터 안의 Codex와 ChatGPT 연결 모두 `packengine_editor`를 사용한다. 새 연결에서는 팩별 쓰기 범위를 미리 고르지 않는다. `ReviewChanges=true`는 요청 설정이며 별도 도구 이름이 아니다. 일반 patch/apply/build/reload를 호출하면 변경안과 후속 작업을 모으고, 턴 종료 뒤 한 검토 창에서 사용자가 체크한 항목만 실행한다. 검토 시 등록된 에디터팩의 원본 해시와 사용자 초안을 다시 확인한다. 기존 고정 범위 요청에서는 WritableEditorPacks와 AllowEditorReload를 계속 검사한다.

‘이거’ 모드로 팩 패널의 요소를 클릭하면 명령을 실행하는 대신 객체 ID와 XML 출처를 선택한다. 범위 모드에서는 여러 요소를 클릭해 추가한다. 일반 대화는 화면·hover·최근 선택을 자동 첨부하지 않는다. 전송/MCP 문맥 요청 시 `EditorInput`과 해당 XML의 해시·제한된 내용을 동결한다. `inspect`는 실행 중인 세대의 상속 출처, `read`는 현재 디스크 소스라는 차이를 유지한다.

도구는 `list/api/inspect/read/patch/apply/undo/build/reload/windows/window`를 제공한다. list는 로드된 모듈 버전도 보고하고, windows는 등록/열림 상태를 조회한다. window는 `action`, `windowId`, `pack`과 등록 시 `view`, `title`을 받는다. AI의 창 동작도 검토 전에는 실행하지 않으며 선택된 빌드→재로드→창 동작 순서로 처리한다. 여러 창 동작은 각각 보관하여 등록과 열기가 덮어써지지 않는다. 수정은 현재 파일을 읽어 얻은 해시와 정확히 한 번 일치하는 문구를 요구하며, 미리보기·저장·빌드·실행 반영을 구분한다. 열린 사용자 초안이나 오래된 해시는 거부한다. 재로딩은 바뀐 모든 팩이 요청의 허용 범위에 있는지 DLL 실행 전에 검사한다. 변경 원본과 결과는 PC의 `PackEngine/EditorPackChanges`에 보존하고, 에디터의 읽기/작업 기록에도 실제 도구 호출을 남긴다. AI가 새 파일을 임의 생성하는 도구는 없으며, 새 팩과 DLL 뼈대는 관리 화면에서 생성한 뒤 AI가 수정한다.

## 검증

`python tools/verify-editor-packs.py --dotnet <SDK의 dotnet>`은 실제 독립 DLL 컴파일/실행, 모듈별 버전 교체·worker 재사용·메모리 상태 유지, XML만 수정한 갱신, 창 등록·열기·닫기·임시 해제, 입력 상태 복원, 선택한 창 동작의 검토, 실패한 컴파일·창 생성 뒤 기존 상태 보존, 부분 상속·추가·출처, 네이티브 계약 사전 검사, 구독 해제, 권한·해시·초안·되돌리기, 프로젝트 팩 해제를 검사한다. 공통 데이터 API의 실제 worker 조회, 변경안 검토·선택·취소, UTF-8 보존·되돌리기, 부분 읽기·읽기 전용·미저장 초안·외부 변경 거부와 계약 예제 컴파일도 검사한다. 이 검증은 실제 모델 응답이나 Windows 화면 조작을 대신하지 않는다. Windows net48 빌드도 별도로 수행한다.
