# ChatGPT 대화에서 Project Studio 사용하기

대화는 ChatGPT에 남긴다. 에디터는 현재 열고 허용한 `.packproject`의 객체팩·XML 조회, 변경 미리보기·적용, 빌드·검증 도구를 MCP로 제공한다. 브리지는 모델을 실행하거나 별도 추론 API를 호출하지 않는다. 다른 ChatGPT 채팅 본문, 로그인 쿠키, 화면 이미지를 가져오지 않는다.

## 처음 연결

1. `StartEditor.exe`를 연다. Codex 설치 준비가 필요하지 않으면 시작 창의 **에디터만 열기**를 사용할 수 있다.
2. **ChatGPT 연결** 탭에서 **ChatGPT에서 이 프로젝트 접근 허용**을 켠다. 선택한 팩만 수정·빌드할 수 있고, 처음에는 모두 읽기 전용이다. 실행·검증은 별도 설정이다.
3. **PC 내부 연결 검사**로 배포한 `PackEngine.Mcp.exe`가 실제 에디터의 상태를 받는지 확인한다. 이 검사는 ChatGPT 로그인이나 모델 호출을 하지 않으며 외부 플러그인 연결 성공을 뜻하지 않는다.
4. PC ChatGPT 앱에서 **Settings → MCP servers → Add server → STDIO**를 선택한다. 탭의 **실행 파일**과 **인자**를 등록하고 Restart한다. JSON 설정을 받는 로컬 MCP 클라이언트에는 **MCP 설정 JSON 복사**를 사용한다.
5. ChatGPT에서 이 게임의 프로젝트나 작업 대화를 열고 **처음 연결할 안내 복사**의 내용을 한 번 붙인다. `packengine_status`가 실제 게임 ID·식별값을 반환하는지 확인한다. 도구가 없거나 에디터가 꺼져 있으면 연결 실패로 처리한다.
6. 그 대화나 프로젝트의 URL을 에디터에 한 번 저장하면 **ChatGPT 열기**로 다시 열 수 있다. 이후 대화 본문을 옮기지 않는다.

다음 실행에는 저장한 프로젝트 접근을 복원한다. ChatGPT 모드에서는 시작 프로그램이 Node.js·Codex 설치 준비를 건너뛴다. 에디터 안에서 별도 Codex 대화를 사용하려면 오른쪽의 접힌 설정을 펼친다. 기존 Codex가 없으면 **고급 연결 설정 → 다음 시작 때 Codex 설치 준비**를 누르고 `StartEditor.exe`를 다시 실행한다.

## 새 게임과 대화

새 `.packproject`를 열면 그 프로젝트에 독립된 접근 설정·도구 서버 이름·대화 주소를 사용한다. 에디터의 프로젝트 명세가 스스로 접근 권한을 얻지는 않는다. ChatGPT에서 게임별 프로젝트를 만들고 작업별로 새 대화를 시작해 같은 프로젝트의 도구를 사용하면 된다.

**ChatGPT 프로젝트·대화 자동 생성 API는 구현하지 않았다.** `ChatGPT 열기`는 저장한 주소 또는 ChatGPT 홈을 여는 기능이며 새 채팅을 생성했다고 표시하지 않는다. 생성은 ChatGPT UI에서 한다. [Work의 프로젝트 안내](https://learn.chatgpt.com/docs/projects)는 프로젝트 내 Chat/Work 대화를 설명한다. [프로젝트 도움말](https://help.openai.com/en/articles/10169521-projects-in-chatgpt)의 프로젝트 전용 메모리 항목에는 Work 제한이 있으므로 실제 계정의 설정과 사용 가능 여부를 확인한다.

## 웹·휴대폰 연결

로컬 STDIO 등록만으로 웹·휴대폰에 도구가 생기지는 않는다. 공식 [Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels)에 같은 MCP 실행 파일을 연결하고 해당 ChatGPT 워크스페이스에 개발자 모드 플러그인을 등록한다. 터널에는 Platform 권한, 터널 ID, 실행용 API 키가 필요하다. 터널 이용 자격·비용은 이 프로그램이 판정하지 않는다.

공식 터널 클라이언트를 준비한 뒤 PowerShell에서 다음과 같이 실제 설치 경로를 넣는다. API 키는 공식 안내에 따라 **로컬 환경**에서 설정하고 채팅이나 저장소에 붙이지 않는다.

```powershell
$bridgeExe = 'C:\your-folder\editor\Builds\Windows\PackEngine.Mcp.exe'
$gameProject = 'C:\your-folder\Golemancer\Golemancer.packproject'
$bridgeCommand = '"' + $bridgeExe + '" --project "' + $gameProject + '"'
tunnel-client init --sample sample_mcp_stdio_local --profile packengine --tunnel-id <YOUR_TUNNEL_ID> --mcp-command $bridgeCommand
tunnel-client doctor --profile packengine --explain
tunnel-client run --profile packengine
```

ChatGPT의 플러그인 추가 화면에서 Tunnel 연결을 선택하고 같은 터널을 지정한다. 터널은 대상 ChatGPT 워크스페이스와 연결되어야 한다. 터널 클라이언트는 에디터와 같은 Windows 사용자로 실행한다. PC·에디터·터널 클라이언트가 켜져 있어야 실제 파일을 읽거나 수정할 수 있다. PC가 꺼져 있을 때 ChatGPT에서 대화를 이어가는 것과 게임 도구가 실행되는 것은 별개다. 이 배포 작업에서는 실제 계정의 터널을 생성하거나 원격 플러그인을 연결하지 않았다.

## 문맥과 작업 범위

| 단계 | 실제 동작 |
|---|---|
| `packengine_status` | 접근 허용한 현재 프로젝트와 작업 범위 확인. 파일 본문 없음 |
| `packengine_context` | 새 작업 ID 발급. 당시 허용 범위·플랫폼·문서 버전과 명시적 포인팅만 고정 |
| `packengine_find/inspect/read` | 선언된 색인·XML 정의·한 단계 관계·필요한 소스 구간 조회 |
| `packengine_patch/apply` | 관측한 해시와 유일한 원문을 확인해 미리보기 생성, 같은 작업 안에서만 적용 |
| `packengine_build/project` | 허용한 팩 빌드 또는 별도 허용한 프로젝트 명령 실행 |
| `packengine_finish` | 해당 작업 권한 종료. 적용한 변경은 에디터의 되돌리기 기록으로 남음 |

오른쪽의 **이거 · 단일 객체 / 범위**로 대상을 지정한다. 포인팅은 다음 `packengine_context` 호출 때 한 번 전달하고 일반 모드로 돌아온다. **ChatGPT 메시지 전송 순간을 감지하는 것은 아니다.** 실제 캡처 시각과 문서 해시를 반환한다. 일반 대화에서는 도구 호출이 필요 없고, 일반 프로젝트 작업의 기본 문맥에는 파일 본문·호버·화면이 들어가지 않는다.

읽기는 최대 160줄/12,000자다. 문맥 기본 예산은 8,000자이고 잘린 부분을 표시한다. 수정은 에디터의 미저장 초안과 디스크 충돌을 거부한다. 작업은 30분 뒤 만료되고 최대 8개까지 유지한다. 각 작업 ID는 해당 MCP 연결에 속하며, 접근 범위를 바꾸거나 브리지를 다시 시작하면 이전 작업 권한을 철회한다. 모델 인자로 권한을 늘리거나 다른 프로젝트를 여는 도구는 없다.

## 구현·확인 범위

`PackEngine.Mcp.exe`는 MCP STDIO 프록시다. 연결 설정에 지정한 한 프로젝트의 실행 중인 WPF 세션으로 요청을 전달한다. 새 `EditorSession`을 만들거나 디스크만 읽어서 사용자 초안을 놓치지 않는다. Windows IPC는 현재 사용자만 허용한 named pipe이고 네트워크 사용자를 거부하며 TCP 포트를 열지 않는다. 프로토콜 메타데이터 조회는 에디터가 없어도 가능하지만 프로젝트 도구는 실제 에디터에 도달해야 한다.

프록시는 2025-11-25 및 이전 handshake 계열을 협상하며 newline JSON-RPC, 취소, bounded IPC 메시지를 사용한다. 도구 실행 오류는 MCP `isError`로 반환한다. stdout에는 프로토콜 메시지만 기록한다. 도구의 읽기 전용 표시는 설명이며, 실제 권한은 에디터 호스트가 검사한다. MCP 연결에 접근할 수 있는 사용자에게 선택한 프로젝트 권한이 주어지므로 터널의 계정·워크스페이스 접근 설정도 적용해야 한다. ChatGPT 대화별 서버 인증을 구현한 것은 아니다.

`python tools/verify-mcp.py --dotnet /path/to/dotnet`으로 생산 코드의 프로토콜과 실제 XML 작업을 검증한다. 검증 환경이 named pipe 생성을 막으면 그 부분을 명시적으로 건너뛰고 익명 스트림으로 프로토콜·세션·메시지 직렬화를 검사한다. Windows WPF·named pipe·실제 ChatGPT 플러그인·터널은 이 Linux 환경에서 연결 확인하지 못했다. Windows에서는 **PC 내부 연결 검사** 후 ChatGPT에서 `packengine_status`를 요청해 두 경계를 각각 확인한다.
