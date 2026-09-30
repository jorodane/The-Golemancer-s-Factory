# The Golemancer’s Factory / PackEngine

게임 작업은 **[Golemancer](Golemancer)** 폴더 안에서 진행한다. 게임 소스·모듈·XML·이미지·Windows 실행 파일·Android/Linux/iOS 호스트·게임 계약·고정 엔진 SDK가 모두 들어 있다. 이 폴더만 복사해 실행하고 개발할 수 있다.

- 실행: 루트 `Start.bat` 또는 `Golemancer/Start.bat`.
- 엔진 에디터: 루트 **`StartEditor.exe`**. 골레맨서를 로드된 프로젝트로 열어 팩·상속·계약·공유 문맥·편집·개별 빌드·실행을 다룬다. [사용 방법과 범위](docs/EDITOR.md). 첫 실행에서 필요한 Codex를 자동으로 준비하고, Node.js가 없으면 설치 페이지와 재시도를 안내한다. ChatGPT 로그인과 **‘이거’ 포인팅 모드**로 조회·수정·빌드를 연결할 수 있다. [입주 실험 시작하기](docs/RESIDENT_AGENT.md).
- 게임 빌드: `Golemancer/Build.bat`. 게임팩만 빌드: `Golemancer/BuildPacks.bat`.
- Linux: `Golemancer/BuildLinux.sh`, `Golemancer/StartLinux.sh`. iPhone: [Mac 빌드 절차](Golemancer/docs/IOS.md).
- [플랫폼 공통점 분석](Golemancer/docs/PLATFORMS.md).
- 독립 게임 검증: `Golemancer/TestIsolation.bat`.
- [게임 안내](Golemancer/README.md) · [폴더와 API 경계](Golemancer/docs/STANDALONE_GAME.md) · [엔진 개발 안내](docs/ENGINE.md).

`src/PackEngine.Contracts`와 `src/PackEngine.Runtime`은 게임을 참조하지 않는 공통 엔진이다. DLL 로딩·팩 의존성·XML 진입점·UI 계약과 조립·우선순위 타이밍·[렌더 카메라](docs/CAMERA.md)를 담당한다. 골렘·마나·이동·녹화·인벤토리·상점·퀘스트·저장·지형의 게임 연결은 전부 `Golemancer` 안에 있다. `Engine.slnx`와 게임 솔루션은 서로의 소스 프로젝트를 빌드하지 않는다.

게임 DLL 계약은 v2로 갱신됐고 배포 모듈도 함께 재빌드했다. 기존 JSON 저장 형식은 v1을 유지한다. 예전 DLL 팩은 새 게임 계약으로 다시 빌드해야 한다.

루트 `Start.bat`은 이전 위치의 Content·Saves에서 새 위치에 없는 파일만 복사한다. 기존 파일과 원본은 삭제하지 않는다. 이전 구조에서 업데이트했다면 한 번은 루트 런처로 실행한 뒤 게임 폴더를 따로 복사한다.
