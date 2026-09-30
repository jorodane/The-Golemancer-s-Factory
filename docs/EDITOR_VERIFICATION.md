# Project Studio 자동 연결·대화 접근·입주 환경 검증 — 2026-10-01

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
