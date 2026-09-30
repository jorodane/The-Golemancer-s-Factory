# PackEngine Project Studio — 객체팩 작업 환경

골레맨서는 `Golemancer/Golemancer.packproject`로 여는 첫 실제 프로젝트다. 에디터와 공통 작업 모델은 게임 어셈블리를 참조하지 않는다. 프로젝트가 팩 경로, 구현 소스, 계약 문서, 게임 XML 색인 규칙, 플랫폼별 빌드·검증·실행 명령을 제공한다. 게임 런타임은 별도 프로세스에서 기존의 실제 DLL 로더와 시뮬레이션을 사용한다.

## 실행

Windows에서 저장소를 Pull한 뒤 루트 **`StartEditor.exe`**를 실행한다. 필요한 Codex 설치를 준비한 뒤 체크인된 `editor/Builds/Windows/PackEngine.Editor.exe`로 골레맨서를 연다. 실행에는 기존 게임과 같은 .NET Framework 4.8이 필요하며, 소스 빌드에는 .NET 10 SDK가 필요하다.

다른 프로젝트는 에디터의 **프로젝트 열기**에서 `.packproject`를 선택하거나 `StartEditor.exe "다른 프로젝트 경로"`로 연다. 기존 `Start.bat`은 게임 직접 실행 진입점이다. 에디터 자체를 재빌드할 때는 `BuildEditor.bat`을 사용한다. 필요하면 `PACKENGINE_DOTNET` 환경변수로 SDK 실행 파일 경로를 지정한다.

ChatGPT에 대화를 두고 에디터의 객체팩 도구를 사용하는 방식은 **[ChatGPT 연결](CHATGPT_BRIDGE.md)**을 따른다. **ChatGPT 연결** 탭에서 프로젝트 접근을 한 번 허용하면 다음 실행에도 연결을 준비하고 별도 Codex 자동 연결을 쉬며, 시작 프로그램도 Node.js/Codex 준비를 건너뛴다. 읽기·수정·실행 권한과 실제 도구 호출을 구분해 표시한다.

에디터 안에서 별도 Codex 대화를 사용하려면 오른쪽 **에디터 안에서 Codex 대화**를 펼친다. **[입주 환경과 ‘이거’ 모드](RESIDENT_AGENT.md)**를 따른다. 두 대화 기록은 자동으로 합쳐지지 않는다. **대화·접근** 탭의 수동 참고 메모는 선택 사항이며, 본문 없는 링크는 공유 문맥에서 제외한다.

Node.js/npm이 없으면 시작 창이 [공식 Node.js 설치 페이지](https://nodejs.org/ko/download/)를 열고 기다린다. LTS를 설치한 뒤 같은 창에서 **다시 확인**을 누른다. 사용자·시스템 PATH를 다시 읽으므로 보통 창이나 PC를 재시작할 필요가 없다. 이미 네이티브 Codex가 준비돼 있으면 Node.js 설치 확인과 다운로드를 건너뛴다.

시작 프로그램은 별도 콘솔 없이 진행 상황과 펼칠 수 있는 실행 기록을 보여준다. 준비 실패·취소 후에는 다시 확인할 수 있고, **에디터만 열기**로 AI 준비 없이 문서 작업을 시작할 수도 있다. 기존 `StartEditor.bat`, `SetupCodex.bat`도 같은 실행 프로그램으로 연결된다. `StartEditor.exe`는 저장소의 에디터 파일들과 함께 사용하며, 단독 파일만 다른 폴더로 옮겨 실행하는 배포본은 아니다. 기본 에디터와 프로젝트 경로는 `editor/Launcher.xml`에 선언해 시작 프로그램이 게임 이름을 알 필요가 없게 했다.

## 실제 작업 순서

1. 왼쪽에서 `golemancer.controls`의 `golemancer.purchase` 또는 `golemancer.costButton`을 선택한다. ChatGPT 연결을 허용한 프로젝트는 **ChatGPT 연결** 탭으로 시작한다.
2. 관계 탭에서 부모·사용 대상·의존 관계를 눌러 탐색한다. 탐색 이력으로 돌아갈 수 있다. 계약 탭은 상속이 해석된 최종 정의와 설정별 출처를 보여준다.
3. 문서 탭에서 `Content/Packs/02.Controls/ui.xml`을 편집한다. 변경 이유를 적고 **변경·영향 미리보기**를 누른다. XML의 속성 단위 변경과 선언상 영향을 받는 화면·팩을 확인한 뒤 적용한다.
4. 같은 원형의 부모나 형제 정의가 그대로인지 계약을 다시 살핀다. XML 변경은 다음 게임 시작에 로드된다. **실행**으로 프로젝트의 게임 런타임을 연다.
5. C# 구현을 바꿀 때는 해당 팩의 소스 파일을 열고 같은 검토 흐름으로 저장한 뒤 **팩 빌드**를 누른다. 예를 들어 `commerce`는 프로젝트에 선언된 Commerce 프로젝트만 빌드한다. 공용 계약 같은 컴파일 의존성은 MSBuild가 함께 처리한다.
6. **검증**은 프로젝트가 선언한 게임 검증을 실행한다. 골레맨서는 기존 전체 캠페인을 사용한다. 에디터가 골렘·마나·상점의 규칙을 구현하지 않는다.

프로젝트 빌드는 의존 순서대로 팩을 빌드한 뒤 선택한 플랫폼 호스트를 빌드한다. 실행 중인 게임의 DLL을 덮어쓰지 않도록 에디터가 실행한 게임을 먼저 닫는다. 현재는 새 빌드 후 재실행이며, 실행 중 객체 상태를 보존하는 DLL 교체는 구현하지 않았다. 에디터는 소유한 게임 창에 정상 종료를 요청하며 강제 종료하지 않는다.

## 대화와 공유 문맥

오른쪽에는 다음을 구분해 표시한다.

| 영역 | 의미 |
|---|---|
| 사용자가 연 문서 | 실제 열린 파일과 미적용 편집 초안 |
| 최근 요청에 포함한 문맥 | 명시적으로 포인팅한 객체·구간, 전송 시각·문서 버전·정의 조각·일부 포함 여부 |
| 제공자의 명시적 읽기 | AI 제공자가 읽기/계약 조회 인터페이스로 실제 요청한 경로·분량·해시 |

일반 대화에는 열린 문서의 목록과 버전만 붙인다. ‘이거’ 모드로 지정한 정의·범위만 본문에 포함하며 부모·연결 대상은 필요할 때 조회한다. 요청의 기본 본문 문자 예산은 8,000자이고 정의 하나는 최대 6,000자다. 잘린 내용은 `Partial`, 예산에서 빠진 파일은 `Omitted`에 기록한다. 이는 문자 수이며 모델별 토큰 계산은 아니다. 닫힌 프로젝트 전체를 자동으로 AI에 전달하지 않는다.

편집 중인 파일은 `Draft`로 표시한다. 사용자가 보고 있는 버퍼와 디스크가 달라졌다면 `DiskChanged`도 기록한다. 최종 계약 조회는 저장된 정의를 기준으로 한다. 미적용 XML의 내용과 이미 적용된 계약을 혼동하지 않도록 문맥의 설명을 구별한다.

모델은 기본 포함하지 않는다. 연결 전의 **문맥 요청 만들기 → 요청 복사**는 프롬프트와 실제 선택한 문맥을 JSON으로 준비하는 기능이다. Chat에 복사해 이어서 작업할 수 있다. 이 수동 내보내기에는 AI 읽기 기록을 만들지 않는다. ChatGPT Work와 자동으로 세션을 공유하거나 로그인하는 기능은 아니다.

실제 자동 대화는 `IEditorAssistant` DLL로 연결한다. 에디터의 **AI 제공자 연결**에서 선택한 DLL만 로드하며 프로젝트를 열었다고 AI 코드가 자동 실행되지는 않는다. 제공자는 요청의 문맥을 받아 응답하고, 필요하면 `IAssistantWorkspace.Read/Inspect`를 호출한다. 그 읽기는 사용자 문서를 자동으로 열지 않으면서 오른쪽에 표시된다. 원문 수정·빌드 권한은 기본 읽기 인터페이스에 없다. Codex 입주 제공자는 확장 계약 `IResidentAssistant`와 `IAgentWorkspace`를 사용해 요청마다 지정한 팩 범위 안에서 실제 변경·빌드를 수행한다. 전송하는 도구와 권한은 [입주 문서](RESIDENT_AGENT.md)에 설명한다.

함께 제공하는 `Providers/PackEngine.Assistant.Command.dll`은 외부 AI 클라이언트 프로세스와 JSON Lines로 통신한다. 모델이나 계정 정보가 들어 있는 샘플 응답기는 아니다. 설정은 [ASSISTANT_PROTOCOL.md](ASSISTANT_PROTOCOL.md)를 따른다.

## 프로젝트 파일과 엔진 경계

```xml
<EngineProject version="1" id="my.game" name="My Game"
               packs="Content/Packs" schema="editor-schema.xml" defaultTarget="windows">
  <Contract path="docs/CONTRACTS.md" />
  <Pack id="my.controls">
    <Source project="modules/My.Controls/My.Controls.csproj" />
    <Contract path="docs/CONTROLS.md" />
  </Pack>
  <Target id="windows" platform="windows" framework="net48" frameworkProperty="TargetFramework">
    <Run><Exec file="Builds/Windows/MyGame.exe" directory="." /></Run>
  </Target>
</EngineProject>
```

`Build`, `Verify`, `Smoke`, `Run` 아래에 `Exec`를 선언하고 인자는 각각 `Arg`로 제공한다. 환경변수는 `Env name/value`로 선언한다. 셸 문자열을 조합하지 않고 실행 파일과 인자 목록으로 실행한다. Run은 단일 게임 프로세스이며, 다른 단계는 여러 명령을 순서대로 지원한다. 프로젝트를 여는 단계에서는 명령이나 게임 DLL을 실행하지 않는다. 사용자가 빌드·검증·실행을 선택했을 때 해당 프로젝트의 코드가 실행된다.

프로젝트 경로는 파일 기준의 상대 경로다. 루트를 벗어나는 경로나 심볼릭 링크를 통해 다른 폴더를 읽는 것은 지원하지 않는다. 에디터의 문서/AI 읽기는 프로젝트 계약에 등록된 파일로 제한한다. 엔진 SDK 문서와 프로젝트 설정은 여기서는 조회용이다. 골레맨서가 제공받은 기본 엔진 버튼 팩은 `editable="false"`이고, 게임의 자식 테마 팩에서 재정의한다. 구현을 고치려면 소유하는 엔진 소스 작업 영역에서 수정한다.

`editor-schema.xml`은 게임이 정의한 XPath 기반 기호·참조 색인이다. 예를 들어 Action의 handler가 구현 ID라는 의미는 골레맨서가 선언한다. DLL 내부 코드를 추측하거나 실행해서 해석하지 않으므로 해당 구현 노드는 `runtime-unknown`이다. 정의가 없는 정적 참조는 `unresolved`로 남는다. 실행 검증 통과만으로 색인의 모든 미확인 관계를 임의로 확정하지 않는다.

| 구성 | 역할 |
|---|---|
| `src/PackEngine.*` | 기존 공통 런타임·계약·UI 상속. 게임을 참조하지 않음 |
| `editor/PackEngine.Workspace` | 프로젝트 로딩, 의미 관계, 공유 문맥, 변경 기록, 개별 빌드·실행 |
| `editor/PackEngine.Launcher` | Node.js 안내, Codex 자동 준비·재시도, 에디터 실행을 묶는 Windows 시작 프로그램 |
| `editor/PackEngine.Editor` | Windows WPF 에디터 셸 |
| `editor/PackEngine.Tool` | 같은 기능의 net48/net10.0 CLI; 외부 작업 도구 연결점 |
| `editor/PackEngine.Assistant.Codex` | 공식 Codex app-server와 지속 대화·의미 입력·편집 도구 연결 |
| `editor/PackEngine.Assistant.Command` | 선택적으로 연결하는 AI 프로세스 제공자 DLL |
| `Golemancer/*.packproject`, `editor-schema.xml` | 프로젝트의 경로·명령·게임 의미 선언 |

에디터 셸의 WPF 컨트롤은 현재 네이티브 어댑터 구현이다. 에디터 UI 전체를 객체팩으로 교체하는 구성은 아직 아니다. 게임의 UI 객체팩과 공통 런타임은 별도로 유지된다. 이번 작업은 골레맨서의 고정 SDK를 업그레이드하지 않는다.

## 변경과 기록

UTF-8 문서 하나를 한 변경 단위로 다룬다. 미리보기는 의도·수정 전후 해시·원문·XML 의미 차이·정적 영향 목록을 기록하고 프로젝트 파일은 바꾸지 않는다. 적용 시 파일 버전을 다시 비교하고 같은 폴더의 임시 파일을 원자적으로 교체한다. 되돌리기는 현재 파일이 그 변경의 결과와 같을 때만 가능하다. 외부 편집을 덮어쓰지 않는다. XML 자체와 UI 상속 계약은 적용 전에 검사하고, 게임 고유 의미는 프로젝트 검증에서 확인한다.

편집 초안, 열린 문서, 탐색 이력, 대화 요청과 읽기 기록은 사용자별 `LocalApplicationData/PackEngine/Projects/<프로젝트 경로 해시>`에 둔다. 게임 저장 데이터나 Git 추적 파일에 넣지 않는다. 중간 초안도 재시작 후 복원하며, 오래된 파일 버전은 적용 시 충돌로 처리한다. 같은 상태 폴더를 여러 에디터/CLI가 동시에 쓰는 공동 편집은 지원하지 않는다. 자동화는 `--state`로 별도 폴더를 사용한다.

한 팩 빌드가 실패하면 매니페스트에 선언된 해당 팩 DLL과 deps 파일을 이전 상태로 돌린다. 전체 프로젝트의 여러 팩을 하나의 원자적 배포로 바꾸는 기능, 전원 손실을 포함한 변경 저널 자동 복구, 실행 중 상태 이관은 후속 범위다.

## CLI와 재현

Windows 배포본은 `editor/Builds/Windows/PackEngine.Tool.exe`다. Linux에서는 다음처럼 같은 작업 모델을 실행한다.

```sh
dotnet build editor/PackEngine.Tool/PackEngine.Tool.csproj -c Release -p:EngineTargetFramework=net10.0
dotnet editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll inspect --project Golemancer/Golemancer.packproject --node view:golemancer.purchase
dotnet editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll context --project Golemancer/Golemancer.packproject --point view:golemancer.purchase --prompt "구매 버튼을 확인해줘"
dotnet editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll build-pack --project Golemancer/Golemancer.packproject --pack commerce --target linux
python tools/verify-editor.py --dotnet /path/to/dotnet
```

`preview --file 경로 --text-file 제안파일 --intent 이유`, `apply/undo --change ID`, `graph`, `read`, `assist`, `build-project`, `verify`, `smoke`, `run`도 제공한다. `--dotnet`은 프로젝트의 빌드 명령에 사용할 SDK다. `--select`는 탐색만 하며 요청 첨부에는 `--point`를 쓴다. `--point "key;key"`는 여러 객체, `--range-file 경로 --start-line 1 --end-line 5`는 줄 범위를 지정한다. `codex-status`, `codex-chat`은 입주 제공자를 쓰며 `--codex`, `--model`, `--new-thread`, `--write-pack "id;id"`, `--allow-project-commands`를 지원한다.

검증 스크립트는 실제 골레맨서 폴더를 임시 복사해 연다. 구매 버튼의 XML 수정·상속 조회·충돌·되돌리기, 실제 Commerce 팩 개별 빌드와 실패 복구, AI 제공자 전송 규약, 기존 전체 캠페인, Linux SDL 실행을 같은 프로젝트 진입점으로 확인한다. [초안 검증 결과](EDITOR_VERIFICATION.md)를 함께 기록했다. Windows GUI는 Windows에서 별도로 조작 확인해야 한다. 현재 프로젝트 선언에는 Windows와 Linux 실행 대상이 있으며 Android/iOS 호스트와 기존 빌드 절차는 게임 폴더에 유지된다.
