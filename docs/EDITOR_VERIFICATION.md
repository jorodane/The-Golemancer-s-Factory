# Project Studio 입주 환경 검증 — 2026-09-30

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

추가로 `tools/verify-resident.py`에서 의미 입력·에디터 도구 검사 20개와 연결 흐름 검사 17개를 통과했다.

| 입주 환경 확인 | 결과 |
|---|---|
| 일반 대화·단일·범위 포인팅 | 탐색만으로 본문이 첨부되지 않음; 객체 집합과 줄 범위를 전송 시점에 고정 |
| 다음 입력과 초안 변경 | 후속 포인팅이 기존 요청을 바꾸지 않음; 오래된 줄 범위는 다시 선택하도록 거부 |
| 실제 에디터 작업 도구 | 지정 팩의 구매 버튼 XML 조회·미리보기·적용·팩 검증·정확한 바이트 복구 |
| 요청 권한·충돌 | 다른 팩, 미등록·외부 파일, 미저장 사용자 버퍼, 외부 수정, 다른 요청의 변경 ID 거부 |
| 지속 대화와 전송 | 별도 프로세스 재시작 후 대화 재개, 새 대화, 스트리밍, 이전 turn 알림 제외 |
| 실패 처리 | API 키 자동 대체 거부, 모델 오류·연결 끊김 전달, 취소 시 중단 요청과 연결 종료 |
| 공식 Codex CLI 0.159.2 | 실제 initialize/account 연결 및 동일 dynamicTools/config의 임시 thread/start 수락. read-only 정책 확인 |

수정과 대화 전송 검사는 명시적인 통신 테스트 프로세스를 사용했다. **실제 ChatGPT 로그인 후 모델 추론·자율 편집은 실행하지 않았다.** 공식 CLI 확인에서도 모델 turn을 시작하거나 API 사용량을 발생시키지 않았다. 테스트 응답기는 Windows 배포본에 포함하지 않는다.

Windows WPF 에디터와 CLI, 두 제공자 DLL은 .NET Framework 4.8 대상으로 교차 빌드했으며 경고·오류가 없었다. 이 환경에서는 Windows GUI를 실행하거나 화면을 조작하지 못했으므로 네이티브 UI 확인은 남아 있다. ChatGPT 로그인과 실제 모델의 작업, Windows 마우스 포인팅 조작, Android/iOS 네이티브 실행, 실행 중 DLL 교체는 추가 확인이 필요하다.

재현 명령:

```sh
python tools/verify-editor.py --dotnet /path/to/dotnet
python tools/verify-resident.py --dotnet /path/to/dotnet --codex /path/to/codex
dotnet build editor/Editor.slnx -c Release -p:EngineTargetFramework=net48 -p:UseSharedCompilation=false -m:1 --disable-build-servers
```

검증 스크립트의 상세 결과는 실행한 작업 영역의 `TestResults/editor/report.json`, `TestResults/resident/report.json`과 해당 폴더의 명령 로그에 기록된다. 배포된 에디터 실행 파일의 원본 소스 커밋과 해시는 `editor/Builds/Windows/build-info.json`, `SHA256SUMS`에 기록한다.
