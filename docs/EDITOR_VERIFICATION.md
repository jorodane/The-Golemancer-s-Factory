# Project Studio 자동 연결·대화 접근·입주 환경 검증 — 2026-10-01

## Android 공통 에디터팩 호스트 (2026-10-02)

- 에디터팩 149개 검사 통과. 데스크톱 모듈·창 검사 32개, 프로젝트 문서 검사 36개와 앱 내 모듈 검사 28개를 포함한다. 앱 프로필의 실제 외부 DLL/공유 ABI, 입력 payload, XML 변경 시 모듈과 메모리 상태 유지, 같은 이름의 DLL 교체, 임시 창과 입력 복원·구독 해제, 실패한 XML/렌더러/권한 검토 후 보존, 팩 ZIP 왕복·경로 거부를 확인했다.
- `net10.0-android` arm64 Release 빌드 및 개발 키 서명 통과, 경고·오류 0개. 기본 팩과 공용 예제의 실제 XML·독립 DLL이 APK assets에 포함됨을 확인했다. PowerShell 빌드 스크립트를 Linux에서 끝까지 실행하여 서명 APK와 PC/Android 두 DLL을 담은 예제 ZIP·메타데이터 생성을 확인했다. 도구의 telemetry는 비활성화했다.
- Windows WPF/net48 솔루션과 공용 예제 빌드 통과, 경고·오류 0개. 전체 게임 캠페인 540개 통과, 게임 소스와 고정 SDK는 변경하지 않았다.
- 실제 Android 기기의 설치·키보드·회전·파일 선택, Windows에서의 배치 실행과 GUI 조작은 미검증이다. APK는 배포하지 않으며 [Windows APK 빌드 안내](EDITOR_ANDROID.md)에 따라 직접 생성한다.

## ChatGPT 웹 연결 마법사

- ‘연결 이름’ 입력을 제거하고 게임팩 이름으로 표시한다. 기존 `Title` 저장값은 호환성을 위해 보존한다.
- 웹을 기본으로 주소 → 계정 개발자 모드·터널·키 준비 → 작업 및 설치 동의 → 자동 설치·내부 검사·프로그램 실행 → 웹 플러그인 승인·외부 도구 호출 확인으로 진행한다. Windows ChatGPT 앱이나 설정 파일 편집·JSON 입력을 요구하지 않는다. 데스크톱 로컬 연결은 별도로 선택한다.
- `tools/verify-chatgpt-setup.py`: 57개 통과. 웹과 데스크톱 설치 동의 누락, 고정 SHA-256 불일치, 실패·취소 후 정리·재시도를 검사했다. 웹 프로필의 비밀 제외, 자식 프로세스 환경으로만 키 전달, 중복 실행 차단, 실제 로컬 fixture 프로세스·STDIO 자식 종료, 초기 종료·취소·재시도, 비루프백 진단 주소 거부를 확인했다. `/healthz` 성공과 `/readyz` 실패를 동시에 반환하는 fixture로 실행 여부와 계정·외부 호출 증거를 구분했다.
- 데스크톱 경로는 공식 Codex 0.159.2로 임시 설정 폴더에서 실제 `mcp add/list/get`을 실행해 기존 설정·연결 보존, 백업, 경로 특수문자, 중복·충돌 처리를 확인했다. 웹 경로는 Codex 설정을 수정하지 않는다. 모델 추론·계정 로그인은 하지 않았다.
- `tools/verify-mcp.py`: 41개 호스트·프로토콜 검사와 4개 실행 파일 검사 통과. 기기 터널 설정과 보호된 키가 상태·작업 문맥에 포함되지 않는 검사를 추가했다. 내부 검사는 명시적 진단 표시로 전달되고 상태 조회만 허용한다. 내부 검사와 실패한 외부 도구 요청으로 마법사의 외부 응답 확인 상태를 만들지 않는다.
- `tools/verify-conversations.py`: 공식 CLI 옵션 없이 로컬 fixture로 대화·설치 회귀 검사를 재확인했다. 기록 저장 22개, 시작 프로그램 22개 검사를 포함한다. 공식 CLI의 추가 네트워크 동작에 대한 자동 승인 검토가 제한되어 해당 실행을 반복하지 않았다. 아래 공식 원본 대화 복원·재개 결과는 이전 검증 기록이다.
- 최신 에디터팩 통신 수정까지 포함해 `tools/verify-editor-packs.py` 48개, 에디터 작업 검증, 전체 게임 캠페인 540개, 외부 DLL을 사용한 Linux 실행 30프레임 통과.
- Windows WPF 솔루션 빌드를 수행해 배포 EXE/DLL을 갱신한다. 실제 Windows 다운로드·창 조작·DPAPI·Job Object·named pipe와 인증된 OpenAI 터널·ChatGPT 웹의 최종 도구 호출은 이 Linux 환경에서 시험하지 않았다. 설치 다운로드와 터널 계정 경계는 별도 fixture이며 실제 계정 터널을 생성하지 않았다.

## ChatGPT MCP 브리지 추가 검증

`tools/verify-mcp.py`에서 실제 MCP 실행 파일의 STDIO handshake·도구 목록·ping·오류 응답 4개와 프로토콜/실제 작업 세션 검사 37개를 통과했다. 지정 XML 캡처 후 포인팅 초기화, 일반 요청의 본문 제외, 미저장 버퍼 읽기, 허용 범위·관측 해시·초안 충돌, 다른 요청/연결의 변경 적용 거부, 실제 XML 수정·팩 검증·정확한 되돌리기, 접근 철회·요청 종료·취소 전달·재연결, 설정 저장을 포함한다.

이 실행 환경은 named pipe의 Linux 구현에 필요한 로컬 소켓 생성을 거부했다. 검사 보고서에 `namedPipeTested: false`를 남기고 생산 프로토콜을 익명 스트림으로 구동했으며, IPC 메시지 직렬화는 메모리 스트림으로 검사했다. **Windows named pipe ACL/연결과 WPF 화면, 실제 ChatGPT 플러그인·터널·모델 호출은 확인하지 않았다.** 에디터의 **PC 내부 연결 검사**는 배포 실행 파일과 실제 WPF 세션 간의 읽기 전용 상태 조회를 수행한다. 외부 연결은 그 다음 ChatGPT의 `packengine_status` 호출로 확인해야 한다.

```sh
python tools/verify-mcp.py --dotnet /path/to/dotnet
```

상세 결과는 `TestResults/mcp/report.json`, `stdio.jsonl`, `verification.log`에 남는다. 이 통신 검증은 모델 응답을 만들지 않으며 제품에 테스트 응답기를 배포하지 않는다.

## 기존 기능 검증

사용자가 기존 Windows 에디터에서 Codex 연결과 정상 채팅을 확인했다(2026-10-01). 아래는 자동 검사에서 확인한 범위이며, 새 대화·접근 화면과 시작 창의 실제 Windows 조작은 별도로 남아 있다.

`tools/verify-editor.py`를 통해 실제 골레맨서 프로젝트의 임시 복사본으로 작업 흐름을 확인했다. 결과는 20개 작업 흐름 확인 통과, 기존 전체 캠페인 540개 확인 통과, Linux 실행 검사 30개 통과와 SDL offscreen 30프레임 실행이다.

| 확인한 흐름 | 결과 |
|---|---|
| `.packproject`로 실제 18개 팩 로드 | 진단 없이 색인 구성; 에디터에서 게임 소스·어셈블리 참조 없음 |
| 부모·자식 UI와 게임 정의의 관계 탐색 | 상속 계약과 출처 조회; 실행하지 않은 DLL 구현은 미확인으로 유지 |
| 문맥 준비·명시적 읽기 | 포함 이유·문자 예산·일부 포함·초안·외부 변경 구별; 내보내기를 AI 읽기로 기록하지 않음 |
| 미적용 문서 복원 | 재시작 후 편집 버퍼 복원; 프로젝트 파일은 그대로 유지 |
| 실제 구매 버튼 XML 편집 | 의미 차이와 영향 미리보기, 적용 후 상속 계약 갱신, 외부 변경 충돌 거부, 정확한 바이트 되돌리기 |
| 독립 AI 제공자 DLL | 외부 프로세스와 실제 JSON Lines 읽기·계약 조회·응답 교환; 테스트 클라이언트 사용 |
| Commerce 구현 팩 개별 빌드 | 다른 팩 DLL과 고정 엔진 SDK 보존 |
| 의도적인 C# 컴파일 실패 | 이전에 배포된 Commerce DLL 보존 |
| 프로젝트가 선언한 검증·실행 | 기존 전체 캠페인과 실제 Linux DLL 로더·SDL 실행 통과 |
| 기존 Windows 게임·SDK | 파일 해시 변경 없음 |

추가로 `tools/verify-resident.py`에서 의미 입력·에디터 도구·접근 설정 검사 33개와 연결·기록 흐름 검사 52개를 통과했다.

| 입주 환경 확인 | 결과 |
|---|---|
| 일반 대화·단일·범위 포인팅 | 탐색만으로 본문이 첨부되지 않음; 객체 집합과 줄 범위를 전송 시점에 고정 |
| 다음 입력과 초안 변경 | 후속 포인팅이 기존 요청을 바꾸지 않음; 오래된 줄 범위는 다시 선택하도록 거부 |
| 실제 에디터 작업 도구 | 지정 팩의 구매 버튼 XML 조회·미리보기·적용·팩 검증·정확한 바이트 복구 |
| 요청 권한·충돌 | 다른 팩, 미등록·외부 파일, 미저장 사용자 버퍼, 외부 수정, 다른 요청의 변경 ID 거부 |
| 지속 대화와 전송 | 별도 프로세스 재시작 후 대화 재개, 새 대화, 스트리밍, 이전 turn 알림 제외 |
| 자동 연결·접근 설정 | 프로젝트와 전체 연결 차단, 자동 연결만 해제, 설정 재시작 복원, 전송 시점 권한 복사 |
| 대화 목록·전환 | 프로젝트·클라이언트 필터, 목록·메시지 페이지 나눔, 오래된 대화 선택 후 정확한 ID 재개 |
| 기록 접근 차단 | 차단한 ID·프로젝트 밖 기록·다른 클라이언트·진행 중 대화의 본문 읽기 거부, 기록 접근 해제 시 새 대화만 생성 |
| 웹 문맥 | 명시적으로 공유한 항목만 고정, 필요한 줄만 읽기, 실제 읽기 기록, 비공유·쓰기·외부 도메인 링크 거부 |
| 실패 처리 | API 키 자동 대체 거부, 모델 오류·연결 끊김 전달, 취소 시 중단 요청과 연결 종료 |
| 공식 Codex CLI 0.159.2 | 실제 initialize/account 연결 및 동일 dynamicTools/config의 임시 thread/start 수락. read-only 정책 및 동일 목록·본문 페이지 API 수락 확인 |

수정과 대화 전송 검사는 명시적인 통신 테스트 프로세스를 사용했다. **실제 ChatGPT 로그인 후 모델 추론·자율 편집은 실행하지 않았다.** 공식 CLI 확인에서도 모델 turn을 시작하거나 API 사용량을 발생시키지 않았다. 기록 API 검사는 별도의 임시 Codex 저장소에 명시적인 테스트 사용자 항목만 넣고 메타데이터·목록·페이지 요청이 수락되는지 확인했다. 실제 계정의 웹 채팅을 읽거나 실시간 동기화를 검증한 것은 아니다. 테스트 응답기는 Windows 배포본에 포함하지 않는다.

Windows WPF 에디터와 CLI, 두 제공자 DLL은 .NET Framework 4.8 대상으로 교차 빌드했으며 경고·오류가 없었다. 이 환경에서는 Windows GUI를 실행하거나 화면을 조작하지 못했으므로 네이티브 UI 확인은 남아 있다. 새 대화·접근 화면과 통합 시작 창, 실제 계정의 여러 대화 전환, 모델의 자율 편집, Windows 마우스 포인팅 조작, Android/iOS 네이티브 실행, 실행 중 DLL 교체는 추가 확인이 필요하다.

재현 명령:

```sh
python tools/verify-editor.py --dotnet /path/to/dotnet
python tools/verify-resident.py --dotnet /path/to/dotnet --codex /path/to/codex
dotnet build editor/Editor.slnx -c Release -p:EngineTargetFramework=net48 -p:UseSharedCompilation=false -m:1 --disable-build-servers
```

검증 스크립트의 상세 결과는 실행한 작업 영역의 `TestResults/editor/report.json`, `TestResults/resident/report.json`과 해당 폴더의 명령 로그에 기록된다. 배포된 에디터 실행 파일의 원본 소스 커밋과 해시는 `editor/Builds/Windows/build-info.json`, `SHA256SUMS`에 기록한다.

## 통합 시작 프로그램

`tests/PackEngine.Launcher.Verification`에서 23개 검사를 통과했다. Node/npm 누락, 같은 창의 재탐색, 이미 설치된 Codex 재사용, 설치 실패·취소 시 보존, 실패를 성공으로 오인하지 않는 처리, 동시 설치 배제, 한글·공백·특수문자 인자 보존을 포함한다. 별도로 실제 npm을 실행해 임시 사용자 경로에 공식 Codex 0.159.2를 설치하고 네이티브 실행 파일의 버전을 확인했다. 계정 로그인이나 모델 추론은 하지 않았다.

Windows 시작 프로그램은 WPF/net48로 빌드하고 루트 `StartEditor.exe`로 배포한다. 시작 프로그램과 기존 에디터·제공자는 같은 네이티브 Codex 탐색 코드를 사용한다. Node 설치 후 재시도할 때 환경 경로를 다시 읽는 흐름은 자동 검사로 확인했으며, 실제 Windows Node 설치 프로그램과 브라우저 전환은 이 Linux 환경에서 직접 조작하지 않았다.

```sh
dotnet build tests/PackEngine.Launcher.Verification/PackEngine.Launcher.Verification.csproj -c Release -p:EngineTargetFramework=net10.0
dotnet tests/PackEngine.Launcher.Verification/bin/Release/net10.0/PackEngine.Launcher.Verification.dll
# 실제 npm 설치도 검사하려면 위 실행 명령 뒤에 node 실행 파일과 npm-cli.js의 절대 경로를 순서대로 지정한다.
```

`editor/Builds/Windows/SHA256SUMS`에는 루트 시작 프로그램과 설정 파일의 해시도 포함한다. `BuildEditor.bat`은 에디터와 시작 프로그램을 함께 빌드·복사한다.


## 게임팩별 대화 선택·이동

`tools/verify-conversations.py`로 새 게임팩 생성, 대화 방식·기존 ChatGPT 링크 유지, 프로젝트 내부 원본 보관, 다른 절대 경로에서 기록 목록·본문 복원, 같은 대화 재개, 기기별 접근 권한 분리, 손상·동기화 충돌, 취소·연결 끊김을 확인했다. 원래 PC의 Codex 저장소와 프로젝트를 삭제한 뒤 공식 Codex 0.159.2의 원본을 실제 제공자로 복원하고, 공식 `thread/resume` 및 기록 추가가 기존 내용을 보존하는 것도 모델 호출 없이 확인했다.

동일 소스에서 저장소 검사 22개, 이식·제공자 통합 검사 31개, 시작 준비 검사 22개를 통과했다. 기존 resident 52개 + workspace 33개, MCP 37개 + 실행 파일 프로토콜 4개, 에디터 워크플로 20개, 전체 캠페인 540개와 Linux 30개 및 30프레임 smoke도 통과했다. 동기화 충돌로 마지막 대화를 열지 못해도 새 대화로 넘어갈 수 있음을 검사했다.

Windows WPF/net48 빌드는 오류·경고 없이 통과했다. 실제 Windows UI, 물리적 두 PC 사이의 이동, 클라우드 동기화 서비스, 로그인 후 모델의 이어지는 답변은 미검증이다. 스크립트는 `TestResults/conversations/report.json`에 실제 공식 CLI 검사 여부를 별도 기록한다. 런타임·새 UI는 [대화 사용법](PROJECT_CONVERSATIONS.md)을 따른다.

## 에디터 객체팩 동적 장착 (2026-10-01)

- 실제 별도 DLL/worker 기반 검사 48개 통과: 부분 상속, 설정 출처, Shell 배치, 동적 DLL 교체, 이전 프로세스 종료, 실패 복구, private DLL 구분, 프로젝트/공용 범위, 미리보기·해시·초안·되돌리기·MCP 에디터팩 라우팅. `tools/verify-editor-packs.py`로 재현한다.
- 기존 에디터 작업 흐름 20개, 전체 게임 캠페인 540개, Linux 검증 30개와 실제 30프레임 실행 통과. 고정 SDK 및 Windows 게임 엔진 파일 해시 보존 확인.
- MCP 37개와 실행 파일 프로토콜 4개, Codex 전송 fixture 49개와 작업 모델 33개 통과. 실제 모델 대화·ChatGPT 플러그인 연결·보안 터널·Windows GUI 조작은 이 검증에 포함하지 않는다. 이 Linux 환경에서는 named pipe 검증을 실행하지 못해 익명 스트림과 실제 stdio 실행 파일을 검사했다.
- Windows net48 에디터/계약/worker/기본 팩 빌드를 확인한다. 기존 탐색기·채팅·문서 전체의 객체팩 이관이나 사용자 정의 네이티브 렌더러는 이번 범위가 아니다.
