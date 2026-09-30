# The Golemancer’s Factory

게임 작업은 **[Golemancer](Golemancer)** 폴더 안에서 진행한다. 게임 모듈 소스, XML·이미지, Windows 실행 파일, Android 호스트, 예제, 테스트, API 명세와 고정 SDK를 함께 넣었다. **이 폴더만 별도 위치에 복사해도 실행·게임 빌드·팩 확장 실험을 할 수 있다.**

- 실행: 루트의 `Start.bat` 또는 `Golemancer/Start.bat`.
- 게임 모듈만 빌드: `Golemancer/BuildPacks.bat`. 엔진/API 소스 프로젝트는 빌드하지 않는다.
- 독립 폴더 실험: `Golemancer/TestIsolation.bat` 또는 `python Golemancer/tools/verify-isolation.py`.
- 게임 조작·기획·모드: [게임 안내](Golemancer/README.md).
- 실험 기준과 한계: [독립 게임 폴더 안내](Golemancer/docs/STANDALONE_GAME.md).

루트 `Start.bat`은 이전 위치의 `Content`와 `Saves`에 남은 파일을 게임 폴더로 보충 복사한다. 새 위치에 이미 있는 파일은 덮어쓰지 않고 원본도 삭제하지 않는다. 이전 버전에서 업데이트했다면 한 번은 루트 런처로 실행한 뒤 `Golemancer` 폴더를 따로 복사한다.

`src/Golemancer.Engine`·`src/Golemancer.Contracts`와 `Engine.slnx`는 기존 공통 코드의 개발 영역이다. 게임 폴더의 프로젝트는 이 소스를 참조하지 않고 `SDK`의 기존 DLL만 참조한다. 이번 실험의 엔진/API 기준은 `c77bcdc`이며, **현재 공통 코드에도 골렘·마나 같은 게임 규칙이 남아 있다.** 이 구조는 고정된 현재 API로 팩을 확장할 수 있는지 검증하기 위한 것으로, 모든 게임에 독립적인 범용 엔진 분리 완료를 뜻하지 않는다.
