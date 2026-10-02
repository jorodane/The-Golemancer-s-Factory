# The Golemancer’s Factory / PackEngine

게임 작업은 **[Golemancer](Golemancer)** 폴더 안에서 진행한다. 게임 소스·모듈·XML·이미지·Windows 실행 파일·Android/Linux/iOS 호스트·게임 계약·고정 엔진 SDK가 모두 들어 있다. 이 폴더만 복사해 실행하고 개발할 수 있다.

- 실행: 루트 `Start.bat` 또는 `Golemancer/Start.bat`.
- 엔진 에디터: 루트 **`StartEditor.exe`**. 왼쪽 **ChatGPT 웹 패널**에서 로그인하고, 오른쪽에서 게임팩을 만들거나 연다. **에디터 연결**은 대화 주소나 수정할 팩을 미리 고르지 않아도 된다. Codex가 변경안을 모으면 팩 목록과 세부 변경을 한 창에서 검토하고 선택한 내용만 적용한다. **로컬 Codex 대화**도 같은 흐름을 사용한다. 세부 편집·빌드 도구는 **작업 도구 펼치기**로 연다. [에디터 사용법](docs/EDITOR.md) · [변경안 검토와 웹 작업](docs/SHARED_EDITOR.md) · [여러 기기에서 이어가기](docs/PROJECT_CONVERSATIONS.md).
- 게임 빌드: `Golemancer/Build.bat`. 게임팩만 빌드: `Golemancer/BuildPacks.bat`.
- Linux: `Golemancer/BuildLinux.sh`, `Golemancer/StartLinux.sh`. iPhone: [Mac 빌드 절차](Golemancer/docs/IOS.md).
- Android 에디터팩 실험: `BuildEditorAndroid.bat`. [Windows에서 APK 빌드·같은 팩 테스트](docs/EDITOR_ANDROID.md).
- [플랫폼 공통점 분석](Golemancer/docs/PLATFORMS.md).
- 독립 게임 검증: `Golemancer/TestIsolation.bat`.
- [게임 안내](Golemancer/README.md) · [폴더와 API 경계](Golemancer/docs/STANDALONE_GAME.md) · [엔진 개발 안내](docs/ENGINE.md).

`src/PackEngine.Contracts`와 `src/PackEngine.Runtime`은 게임을 참조하지 않는 공통 엔진이다. DLL 로딩·팩 의존성·XML 진입점·UI 계약과 조립·우선순위 타이밍·[렌더 카메라](docs/CAMERA.md)를 담당한다. 골렘·마나·이동·녹화·인벤토리·상점·퀘스트·저장·지형의 게임 연결은 전부 `Golemancer` 안에 있다. `Engine.slnx`와 게임 솔루션은 서로의 소스 프로젝트를 빌드하지 않는다.

게임 DLL 계약은 v2로 갱신됐고 배포 모듈도 함께 재빌드했다. 기존 JSON 저장 형식은 v1을 유지한다. 예전 DLL 팩은 새 게임 계약으로 다시 빌드해야 한다.

루트 `Start.bat`은 이전 위치의 Content·Saves에서 새 위치에 없는 파일만 복사한다. 기존 파일과 원본은 삭제하지 않는다. 이전 구조에서 업데이트했다면 한 번은 루트 런처로 실행한 뒤 게임 폴더를 따로 복사한다.
