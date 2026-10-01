# ChatGPT 대화에서 Project Studio 사용하기

대화는 ChatGPT에 남긴다. 에디터는 현재 열고 허용한 `.packproject`의 객체팩·XML 조회, 변경 미리보기·적용, 빌드·검증 도구를 MCP로 제공한다. 브리지는 모델을 실행하거나 별도 추론 API를 호출하지 않는다. 다른 ChatGPT 채팅 본문, 로그인 쿠키, 화면 이미지를 가져오지 않는다.

## PC 앱에서 처음 연결

1. `StartEditor.exe`에서 **기존 ChatGPT 채팅·프로젝트 연결**을 고른다. 이미 선택했다면 **ChatGPT 연결 → ChatGPT 연결 설정 · 단계별로 준비**를 연다.
2. **사용할 대화**에서 기존 채팅·프로젝트 주소를 넣는다. 연결 표시는 게임팩 이름을 쓴다. 예전 ‘연결 이름’은 주소 옆에 보이던 별명이었으며 호칭이나 연결 목록 기능이 아니었다. 현재 입력은 제거했고, 기존 저장 파일의 `Title`은 호환성을 위해 보존한다.
3. **허용할 작업**에서 프로젝트 읽기와 앱 등록을 체크한다. 수정·빌드는 선택한 팩만 허용하고, 프로젝트 실행·검증은 별도 체크다. 필요한 공식 연결 프로그램의 설치 허용도 따로 선택한다. 새 PC의 권한은 기본적으로 꺼져 있다.
4. **준비 내용 확인**에서 대화 주소·권한·설치 여부를 확인한 뒤 **허용한 내용으로 연결 준비**를 누른다. 에디터가 연결 프로그램 확인, 앱 등록, 실제 에디터에 대한 내부 검사를 실행한다. 사용자가 MCP 파일을 열거나 JSON·명령을 입력할 필요는 없다.
5. **ChatGPT에서 확인**에서 앱의 **Settings → MCP servers → Restart**를 선택하고 이 PC의 로컬 작업 대화에 **연결 확인 요청 복사**의 내용을 한 번 보낸다. `packengine_status`가 실제 게임 ID·식별값을 반환하면 외부 도구 응답을 확인했다고 표시한다. 내부 검사만으로 이 상태가 되지는 않는다. 창을 닫아도 이후 호출 기록은 연결 탭에 표시한다.

마법사를 준비 실행 전에 취소하면 대화 방식·권한·앱 설정을 바꾸지 않는다. 실행 중에도 취소할 수 있고, **이전**에서 허용 범위를 수정하거나 **다시 준비**로 재시도한다. 이미 등록된 동일 연결은 재사용한다. 등록 직후 취소한 경우에도 다음 시도에서 저장된 상태를 확인하므로 중복 항목을 만들지 않는다. 등록이나 내부 검사 실패를 연결 완료로 표시하지 않는다.

다음 실행에는 대화 선택·주소와 이 PC의 접근 허용을 복원한다. **ChatGPT 연결** 탭에서 읽기를 끄거나 수정·실행 범위를 바꿀 수 있다. URL은 대화를 여는 바로가기이며 로그인·권한·도구 연결을 대신하지 않는다. **저장한 ChatGPT 대화 열기**는 기본 URL 처리기로 열므로, 브라우저에서 열렸다면 로컬 도구를 사용할 때는 ChatGPT PC 앱에서 해당 대화를 연다.

### 자동 준비의 범위

공식 [MCP 설정 안내](https://learn.chatgpt.com/docs/extend/mcp)에 따라 같은 Windows 호스트의 ChatGPT 앱·Codex가 공유하는 설정을 공식 `codex mcp add/get/list`로 등록·조회한다. 설정을 직접 JSON이나 TOML로 조합하지 않는다. 기존 설정은 현재 Codex 설정 폴더의 `packengine-backups`에 원본 그대로 백업하며, 그 내용과 다른 서버의 환경변수는 실행 기록에 출력하지 않는다. 백업은 프로젝트나 원격 저장소에 넣지 않는다. 기존 다른 연결과 같은 식별값이 충돌하면 덮어쓰지 않고 중단한다.

이미 준비된 네이티브 Codex를 우선 사용한다. 없고 설치에 동의했다면 OpenAI의 `rust-v0.159.2` Windows x64/ARM64 배포 ZIP을 다운로드하고 고정한 SHA-256 및 실행 버전을 확인한 뒤 `%LOCALAPPDATA%/PackEngine/ChatGptTools`에 설치한다. 이 프로그램은 연결 등록에만 사용하며 Node.js, 관리자 권한, 새 계정 로그인이나 모델 추론을 요구하지 않는다. 잘못된 다운로드는 실행하지 않고 임시 파일을 정리한다. 에디터 안의 로컬 Codex 대화 준비는 기존 별도 설치 경로를 사용한다.

이 자동 설정은 **같은 Windows의 로컬 작업**에 적용된다. WSL의 다른 Codex 설정, 웹·휴대폰·클라우드의 플러그인은 별도 연결이다. ChatGPT 앱의 연결 재시작과 실제 확인 요청은 앱에서 수행한다. 에디터가 진행 중인 대화를 강제로 종료하거나 외부 계정 승인을 대신하지 않는다.

## 새 게임과 대화

**새 게임팩**은 빈 폴더에 기본 객체팩과 `.packproject`를 만든 뒤 대화 방식을 고르게 한다. 기존 게임팩도 첫 실행에 같은 선택을 한다. 설정은 `.packengine/<manifest 파일명>/conversation.xml`에 저장하므로 게임팩을 옮기면 주소도 함께 이동한다. 로컬 Codex 기록의 이동은 [게임팩 대화](PROJECT_CONVERSATIONS.md)에 설명한다.

ChatGPT 방식은 기존 채팅·프로젝트를 사용하는 흐름이다. **저장한 ChatGPT 대화 열기**는 저장한 주소를 열며, 대화 생성이나 웹 기록 다운로드를 수행하지 않는다. 주소 등록과 실제 MCP 도구 연결은 구분한다.

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

`python tools/verify-mcp.py --dotnet /path/to/dotnet`으로 생산 코드의 프로토콜과 실제 XML 작업을 검증한다. 검증 환경이 named pipe 생성을 막으면 그 부분을 명시적으로 건너뛰고 익명 스트림으로 프로토콜·세션·메시지 직렬화를 검사한다. Windows WPF·named pipe·실제 ChatGPT 플러그인·터널은 이 Linux 환경에서 연결 확인하지 못했다. Windows에서는 마법사의 **PC 내부 연결 검사** 후 ChatGPT에서 `packengine_status`를 요청해 두 경계를 각각 확인한다.

에디터 자체의 팩도 `packengine_editor`로 조회하고 수정할 수 있다. 권한은 **에디터팩** 탭에서 별도로 설정한다. 요청에는 `EditorInput`, `WritableEditorPacks`, `AllowEditorReload`가 포함되며 게임팩 범위를 에디터팩 권한으로 사용하지 않는다. 자세한 흐름은 [에디터팩 명세](EDITOR_PACKS.md)를 따른다.

`python tools/verify-chatgpt-setup.py --dotnet /path/to/dotnet --codex /path/to/native/codex`는 동의 누락, 손상 다운로드, 설치 실패·취소·재시도, 기존 설정 보존, 경로 특수문자, 충돌, 등록 후 중단을 검사한다. 설치 경계는 명시적 fixture ZIP으로 검사하고, 등록은 공식 Codex CLI와 임시 설정 폴더를 사용한다. 실제 Windows 다운로드·WPF 조작·로그인한 ChatGPT의 최종 호출은 별도로 확인해야 한다.
