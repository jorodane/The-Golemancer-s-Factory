# 객체팩 상속 검증 — 2026-09-30

공통 단일 부모 해석기와 팩 의존성, UI Widget/View 상속을 구현하고 구매 확인 버튼에 적용했다. 사용 문법과 범위는 [SDK/INHERITANCE.md](../SDK/INHERITANCE.md)를 따른다.

| 검증 | 결과 |
|---|---|
| 소비자 소스 없이 엔진 및 외부 버튼 팩 빌드 | net48 / net10.0 통과 |
| 독립 엔진 검사 | 100개 통과: 기존 타이밍·카메라·팩 검사 및 새 상속 검사 |
| 게임 폴더만 복사한 검증 | 540개 통과, 전체 캠페인 실행 |
| SDK 격리 | 게임 검증 동안 엔진/Windows 파일 24개 유지, 변조 SDK 빌드 거부 |
| Linux SDL2/Skia 실행 | offscreen 드라이버, 30개 검사 및 실제 프레임 30회 통과 |
| Windows 빌드 | net48 WPF 호스트 및 검증 실행 파일 컴파일 통과 |
| 설정 출처 명령 | 구매 화면의 4단계 화면/위젯 계보와 라벨·색상의 출처 확인 |

상속 검사는 문서 순서와 무관한 해석, 빈 값/false/0의 재정의, Set와 Bind의 교체, 부분 배치, 자식 추가, 부모·형제·반환 사본의 격리, 출처 추적, C#/XML 동등성, 비 UI 페이로드 어댑터, 계약 위반·순환·없는 부모·깊이 제한을 다룬다. 팩 검사는 부모를 먼저 한 번만 로드하는지, 버전과 fingerprint가 반영되는지 확인한다.

게임 검사는 같은 구매 원형의 독립 가격·상태, 비활성 이유 표시, 원형 변경 시 포인터 취소를 확인한다. 공유 화면의 Linux 검사는 누르는 중 구매 가능 수량이 0으로 바뀌면 클릭과 Enter가 모두 막히고, 조건을 회복한 뒤 새 입력으로 지정 수량을 확정하는지 확인한다.

Windows GUI 실기 실행, 실제 Linux 모니터 입력, Android APK 재빌드, iOS 네이티브 빌드/기기 실행은 이번에 수행하지 않았다. UI 상속은 공통 코드이지만 플랫폼별 실행 확인을 대신하지 않는다. 이미지 슬롯 렌더링·9-slice·다중 분할/반복 렌더러와 기존 모든 게임 데이터의 자동 상속은 이번 구현 범위에 포함하지 않는다.

재현 명령:

```sh
# 저장소 루트
python tools/verify-engine.py --dotnet /path/to/dotnet
python tools/export-sdk.py Golemancer --dotnet /path/to/dotnet
# Golemancer 폴더
python tools/verify-isolation.py --dotnet /path/to/dotnet
GOLEMANCER_DOTNET=/path/to/dotnet ./BuildLinux.sh
SDL_VIDEODRIVER=offscreen ./Builds/Linux/linux-x64/Golemancer.Linux --smoke --frames 30
```
