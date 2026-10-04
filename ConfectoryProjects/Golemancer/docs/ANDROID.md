# Android 플랫폼

별도 다운로드로 제공하는 `Golemancer.apk`는 Android 8.0(API 26) 이상 ARM64 및 x86_64용 개발 빌드다. APK 파일은 Git에 커밋하지 않는다. 저장소의 `Builds/Android/build-info.json`과 `Builds/Android/SHA256SUMS`에서 빌드 정보와 해시를 확인할 수 있다. .NET 런타임, 공유 엔진, 독립 객체팩 DLL, XML, 저장소에 있는 아트를 포함한다. 개발용 서명이며 스토어 배포 빌드는 아니다. 기존 앱의 서명과 다른 키로 만든 APK는 덮어쓸 수 없으므로 배포 시 동일한 서명 키를 유지해야 한다.

이전 타이밍 버전은 API 29 x86_64 에뮬레이터에서 실행·터치·저장·DLL 로딩·앱 복귀를 검증했다. 이번 공통 Presentation 버전은 APK 빌드와 Linux에서의 공통 화면 검사를 통과했으며 Android 기기에서는 아직 재실행하지 않았다. 실제 ARM64 휴대폰과 물리 게임패드도 미검증이다. 상세 결과는 [검증 기록](VERIFICATION.md)에 있다.

## 실행과 조작

APK를 기기에 복사하고 설치한 뒤 가로 화면에서 실행한다. Android 호스트는 네이티브 Activity와 SkiaSharp를 사용한다.

| 입력 | 동작 |
|---|---|
| 왼쪽 가상 스틱 | 일상: 카메라 이동 / 전투: 골렘 이동 |
| 빈 땅 터치 | 일상 모드 이동 지시 |
| 개체 터치 / 길게 터치 | 빠른 사용 또는 버블 / 행동 버블 |
| 오른쪽 아래 버튼 | 줍기, 행동 메뉴, 사용·공격, 구르기, 모드 전환 |
| 예약을 누른 채 다른 손가락으로 선택 | 행동 예약 |
| 오른쪽 위 가방·골렘·행동 | 인벤토리, 조종 전환, 모듈별 기능·건설·녹화·메모리 |
| 게임패드 왼쪽 / 오른쪽 스틱 | 이동·카메라 / 조준 |
| 게임패드 A / X / Y / R1 | 사용·공격 / 행동 메뉴 / 모드 / 구르기 |
| 게임패드 L1 / L2 / R2 / Select | 줍기 / 예약 / 반복 / 녹화 |
| 메뉴 안 방향키 / A / B | 항목 선택 / 실행 / 뒤로 |
| 외부 키보드 | 기존 Windows 키 배치, 플랫폼별 키 이름은 XML에서 변환 |

수량 입력은 공통 화면의 숫자 키패드·슬라이더·확인 버튼을 사용한다. 외부 키보드의 숫자와 Enter/Escape도 지원한다. 메뉴·대사·앱 비활성 전환 시 남아 있는 이동과 터치를 해제한다. 앱을 벗어날 때 자동 저장하며, 시작 화면의 이어하기는 자동 저장을 읽는다. 저장은 앱 전용 `FilesDir/Saves`, 스모크 검증은 별도 `SmokeSaves`를 사용한다.

## 빌드

- .NET SDK 10과 `dotnet workload install android`
- Android SDK API 36, build-tools 36.0.0, JDK 21
- 저장소 `Content/Packs`의 실제 이미지

저장소 루트에서 `ConfectoryEngine\Build.bat "ConfectoryProjects\Golemancer" android`를 실행한다. Linux/macOS는 `ConfectoryEngine/build.sh "/절대/프로젝트/경로" android`를 쓴다. AndroidSdkDirectory와 JavaSdkDirectory가 필요한 환경은 빌드 프로세스의 환경 변수로 지정한다.

엔진이 portable SDK와 객체팩 DLL을 먼저 만들고 프로젝트에 선언된 Android 호스트를 빌드한다. 공통 `PublishProjectAndroid.proj`가 로컬 `Builds/Android`에 APK와 체크섬을 기록한다. 생성된 APK와 빌드 정보는 Git에 올리지 않는다. Windows 대상 빌드에는 Android 도구가 필요하지 않다.

## 실제 DLL 로딩

`Golemancer.Android → Golemancer.Presentation → Golemancer.Client → Golemancer.Runtime → Golemancer.Contracts`는 게임 폴더 안의 프로젝트 참조로 연결한다. 공통 `Confectory.Contracts`·`Confectory.Runtime`은 SDK 바이너리만 참조한다. 콘텐츠 모듈은 독립적으로 빌드한 `Content/Packs/*/Bin/net10.0/*.dll`이다. APK asset에서 앱 전용 Content 디렉터리로 추출한 뒤 `PackLoadContext.LoadFromAssemblyPath`로 읽는다. 모듈별 XML과 번역도 같은 팩에서 읽는다.

호스트 런타임의 assembly store/압축은 기본값을 유지한다. 이 패키징과 외부 객체팩 DLL 로딩은 별개다. `PublishTrimmed=false`, `RunAOTCompilation=false`를 유지하여 동적으로 참조하는 타입과 JIT 실행을 보존한다. Release 시작 시 JNI 메서드 등록 누락을 피하도록 `AndroidEnableMarshalMethods=false`로 동적 JNI 등록을 사용한다. [공식 빌드 속성 설명](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidenablemarshalmethods)에 해당 옵션을 정리하고 있다. Android에서 사용할 수 없는 `AssemblyDependencyResolver`는 인접 DLL 탐색으로 대체한다. 사용자 팩을 가져오는 화면은 아직 제공하지 않는다.

## 모듈별 입력 계약

입력은 논리 액션 ID와 플랫폼·장치의 물리 컨트롤을 구분한다. 기존 `<Bind action="roll" keys="Space"/>`는 모든 플랫폼의 keyboard 기본값으로 계속 동작한다.

```xml
<Inputs>
  <Action id="tea.rest" name="차 한 잔" command="tea.rest" target="point" virtual="button" group="actions" order="100" />
  <Bind action="tea.rest" keys="K" />
  <Bind action="tea.rest" device="gamepad" controls="ButtonR3" />
  <Bind action="tea.rest" device="touch" platforms="android,ios" controls="tea.rest" />
  <Bind action="heal" keys="D1" />
  <Bind action="heal" platforms="android" controls="Num1" />
</Inputs>
```

- `platforms="*"`가 기본이며 쉼표로 타깃을 나열한다. 특정 플랫폼 행은 같은 액션·장치의 기본 행을 대체하고, `controls=""`는 해당 바인딩을 해제한다. 팩 적용 순서상 마지막 일치 행이 우선한다.
- DLL에서는 선택적인 `IInputRegistry.Input(InputActionDef)`로 액션을 선언할 수 있다. 동일한 ID의 XML 정의가 우선한다. Contracts는 WPF나 Android 키 enum을 참조하지 않는다.
- `command`와 `item`은 엔진 명령으로 전달된다. `target="point"`는 다음 월드 선택을 대상으로 삼는다. `virtual="button"` 액션은 Android 행동 메뉴에 나타나며, `virtual="none"`은 숨긴다.
- 각 키·포인터·게임패드 축은 독립적인 source ID를 사용한다. 한 손가락을 떼어도 다른 손가락이나 키보드의 입력을 지우지 않는다. 취소·연결 해제는 아직 처리하지 않은 눌림 이벤트도 정리한다.
- `examples/TeaBreak`는 호스트 수정 없이 DLL 입력 계약, 키보드 K와 게임패드 R3 바인딩을 추가하는 예제다.

Android의 게임 화면은 공통 Presentation DLL로 옮겼다. [Linux 호스트](LINUX.md)는 같은 화면으로 실행 검증했고, [iOS 호스트](IOS.md)는 Mac 빌드·실행을 준비했다. iOS에서 동적 DLL 실행이 성공하는지는 아직 검증하지 않았다. [플랫폼 공통점 분석](PLATFORMS.md)에 코드 경계를 정리했다.

## Android 스모크 검증 실행

에뮬레이터 또는 테스트 기기를 연결하고 APK를 설치한 뒤 실행한다. 아래 명령은 로컬 빌드 출력 경로를 사용하므로, 별도로 받은 APK를 검사할 때는 설치 경로를 해당 다운로드 파일로 바꾼다. `smoke` 시작 옵션은 실제 플레이 저장과 분리된 디렉터리를 사용한다.

```sh
adb install --no-incremental -r Builds/Android/Golemancer.apk
adb shell am force-stop com.golemancer.factory
adb logcat -c
adb shell am start -W -n com.golemancer.factory/com.golemancer.factory.MainActivity --ez smoke true
adb logcat -s GolemancerSmoke:I Golemancer:E AndroidRuntime:E '*:S'
```

`ANDROID_SMOKE_PASS`를 확인한다. 이 검사는 기기 안에서 외부 DLL 로딩, 저장·복원, 이미지 디코딩, Android MotionEvent의 두 손가락 입력과 취소, 버블 뒤 HUD 차단을 실행한다. CPU 에뮬레이션만으로 실행하면 부팅·설치·첫 로딩이 오래 걸릴 수 있다. 임시 AVD 디스크는 하나만 유지하여 반복 검증으로 저장공간이 누적되지 않게 한다.
