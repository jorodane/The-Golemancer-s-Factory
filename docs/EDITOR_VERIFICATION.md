# Project Studio 초안 검증 — 2026-09-30

`tools/verify-editor.py`를 통해 실제 골레맨서 프로젝트의 임시 복사본으로 작업 흐름을 확인했다. 결과는 19개 작업 흐름 확인 통과, 기존 전체 캠페인 540개 확인 통과, Linux 실행 검사 30개 통과와 SDL offscreen 30프레임 실행이다.

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

Windows WPF 에디터와 CLI, 제공자 DLL은 .NET Framework 4.8 대상으로 교차 빌드했으며 경고·오류가 없었다. 이 환경에서는 Windows GUI를 실행하거나 화면을 조작하지 못했으므로 네이티브 UI 확인은 남아 있다. 실제 모델 서비스 연결, Android/iOS 네이티브 실행, 실행 중 DLL 교체는 이번 검증 범위가 아니다.

재현 명령:

```sh
python tools/verify-editor.py --dotnet /path/to/dotnet
dotnet build editor/Editor.slnx -c Release -p:EngineTargetFramework=net48 -p:UseSharedCompilation=false -m:1 --disable-build-servers
```

검증 스크립트의 상세 결과는 실행한 작업 영역의 `TestResults/editor/report.json`과 같은 폴더의 명령 로그에 기록된다. 배포된 에디터 실행 파일의 원본 소스 커밋과 해시는 `editor/Builds/Windows/build-info.json`, `SHA256SUMS`에 기록한다.
