# 플랫폼 비교 실험

Windows, Android에 Linux/SDL2 호스트와 iPhone/iPad용 UIKit 호스트를 추가했다. Linux는 실행 가능한 패키지를 만들었으며 iOS는 Mac에서 이어서 빌드할 프로젝트 단계다. iOS 기기에서 독립 DLL이 실제로 실행된다는 결론은 아직 내리지 않는다.

## 공통 코드와 플랫폼 코드

| 책임 | 현재 위치 | 공유 범위 |
|---|---|---|
| 팩 DLL 로딩, 의존성, 타이밍, 카메라 보간·투영 | 선택한 엔진의 Confectory SDK | 네 플랫폼의 같은 API/구현 |
| 게임 규칙, 명령, 저장, 게임 카메라 정책, 논리 입력 | Contracts / Runtime / Client | 네 플랫폼 |
| 이미지 해석, 월드·HUD, 버블, 대사, 메모리, 수량창, 터치 제스처 | Golemancer.Presentation | Android / Linux / iOS의 같은 DLL |
| 상호작용 선택·버블 구성·대사 진행 보조 코드 | 기존 Host의 공유 소스 링크 | WPF와 Presentation |
| 창과 그리기 표면, 프레임 신호, OS 입력, 생명주기 | 각 네이티브 호스트 | 플랫폼별 구현 |
| 실행 파일 형식, 아트·팩 배치, 개인 저장 경로, 실행 전략 | 각 빌드/호스트 | 플랫폼별 구현 |

Presentation은 골렘·인벤토리·퀘스트를 아는 **게임 화면 모듈**이다. 엔진의 범용 UI 백엔드로 취급하지 않는다. Windows의 WPF 렌더러는 유지하며 세 플랫폼용 Skia 화면과 완전히 같다고 주장하지 않는다. 공통성이 확인된 정책과 게임에 독립적인 엔진 기능의 경계를 구분한다.

| 연결점 | Windows | Android | Linux | iOS |
|---|---|---|---|---|
| 창·화면 | WPF | Activity / SKCanvasView | SDL2 창·텍스처 | UIKit / SKCanvasView |
| 프레임 | CompositionTarget | Choreographer | SDL 루프 | CADisplayLink |
| 입력 변환 | WPF 이벤트 | MotionEvent / KeyEvent | SDL 이벤트 | UITouch / UIPress / GCController |
| 기본 입력 | 키보드·마우스 | 터치 | 키보드·마우스 | 터치 |
| 독립 팩 DLL | net48, 외부 파일 | net10.0, APK asset 추출 | net10.0, 외부 파일 | net10.0, bundle resource 추출; 인터프리터 실험 |
| 이번 실행 확인 | GUI 미실행 | 새 APK는 기기 미실행 | offscreen 스모크 통과 | 앱 빌드·기기 실행 미확인 |

## 유지한 계약

- 콘텐츠 모듈 프로젝트를 호스트에서 참조하지 않는다. XML이 지정한 독립 DLL을 실제 PackLoadContext로 적재한다.
- 입력 → 고정 시뮬레이션 → 렌더 준비의 엔진 타이밍을 사용한다. 카메라 -10 다음 화면 제출 0이며, 클릭은 마지막으로 그린 불변 카메라를 사용한다.
- 플랫폼은 물리 키 이름과 좌표를 변환한다. 게임 동작은 XML 논리 액션에 연결한다. Android의 Num1과 다른 호스트의 D1은 기존 바인딩을 유지한다.
- 한 포인터 취소는 다른 포인터·키보드의 입력을 지우지 않는다. 비활성 전환은 세션을 멈추고 저장한다.
- 이번 작업은 게임 폴더 안에서만 공통 코드를 추출했다. 엔진 SDK는 선택한 엔진이 생성해 공급하며 게임 ABI v2를 유지한다.

## 이 실험이 주는 근거

Linux에서 새로 작성한 부분은 창·텍스처 제출·입력 전달과 실행 패키징이다. Android의 화면 코드를 다시 복제하지 않고 같은 Presentation DLL로 이미지·버블·카메라·저장 검사를 실행했다. 따라서 이 부분은 OS보다 게임 화면에 속한다는 근거가 생겼다.

iOS는 같은 소스를 참조하도록 만들었지만 UIKit 생명주기, 서명, 번들, 인터프리터를 포함한 실행 경로는 Mac과 기기에서 검증해야 한다. 특히 번들에서 추출한 IL 팩의 런타임 적재·실행을 통과한 뒤에야 네 플랫폼의 DLL 실행 계약이 같다고 결론 낼 수 있다.

[Linux 실행·검증](LINUX.md) · [iOS 빌드·검증](IOS.md) · [Android](ANDROID.md) · [검증 기록](VERIFICATION.md)
