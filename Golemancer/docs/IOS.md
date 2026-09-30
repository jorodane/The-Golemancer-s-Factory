# iPhone / iPad 빌드 실험

`src/Golemancer.iOS`는 UIKit 기반 .NET iOS 앱 프로젝트다. Mac의 Xcode 도구 체인으로 빌드하며, 별도 Xcode `.xcodeproj`를 생성하는 구조는 아니다. 공통 게임 소스·고정 SDK·독립 팩·아트가 모두 `Golemancer` 폴더에 있어 이 폴더만 옮길 수 있다.

**현재 제공 상태:** UIKit 호스트, 입력·생명주기 연결, 번들 리소스 추출, 실제 팩 로더 경로, 빌드 스크립트. Microsoft.iOS 26.2 및 SkiaSharp iOS 참조 DLL에 대한 C# 타입 검사를 통과했다. Linux에서 수행한 이 검사는 Xcode 앱 빌드, 링커, 네이티브 연결, 서명 또는 iPhone 실행 검증을 대신하지 않는다. IPA는 아직 생성하지 않았다.

## Mac에서 빌드

.NET 10 SDK, iOS workload 및 그 workload가 요구하는 Xcode 버전을 설치한다. 프로젝트의 `TargetPlatformVersion`은 26.2이며 최소 OS는 15.0으로 설정했다. [Microsoft의 Xcode 버전 요구사항](https://learn.microsoft.com/en-us/dotnet/ios/troubleshooting/xcode-requirement)을 따른다. Xcode 앱을 설치하고 개발자 도구 경로를 선택한 상태여야 한다.

```sh
dotnet workload install ios
./BuildiOS.sh simulator
```

스크립트는 독립 net10.0 팩 DLL을 먼저 빌드하고 Mac CPU에 맞는 `iossimulator-arm64` 또는 `iossimulator-x64` 앱을 만든다. 출력은 `src/Golemancer.iOS/bin/Release/net10.0-ios/<RID>/Golemancer.iOS.app`이다. 시뮬레이터를 부팅한 뒤 다음과 같이 설치한다. 아래 RID는 Apple Silicon 예시다.

```sh
xcrun simctl install booted src/Golemancer.iOS/bin/Release/net10.0-ios/iossimulator-arm64/Golemancer.iOS.app
xcrun simctl launch booted com.golemancer.factory
```

실기기는 개발자 인증서와 프로비저닝 프로파일을 전달한다. 키나 인증서는 저장소에 포함하지 않는다.

```sh
./BuildiOS.sh device -p:CodesignKey="Apple Development: YOUR NAME (TEAMID)" -p:CodesignProvision="YOUR PROFILE"
```

필요하면 `-p:ApplicationId=본인의.앱.ID`를 함께 전달한다. 빌드 결과 경로와 서명 오류는 dotnet 출력에서 확인한다. 장치 등록·서명을 포함한 실제 빌드는 Mac에서 완료해야 한다.

## 독립 DLL 실행을 확인할 지점

Android/Linux와 같은 net10.0 객체팩 DLL을 앱의 Content 리소스로 넣고 개인 저장소로 추출한 뒤 `PackLoader.Cook`으로 적재한다. 콘텐츠 모듈의 ProjectReference나 정적 레지스트리로 바꾸지 않았다. `UseInterpreter=true`, `TrimMode=copy`, `PublishAot=false`를 사용한다. iOS 빌드는 링커 실행이 필요하므로 Android의 `PublishTrimmed=false`를 그대로 복사하지 않는다. [공식 빌드 속성](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-properties)의 인터프리터·트리밍 의미를 따른다.

이 설정으로 번들 IL 팩이 시뮬레이터와 실기기 모두에서 동적으로 적재되고 실행되는지는 **미확인**이다. 외부에서 내려받는 팩이나 App Store 배포를 검증한 것도 아니다. 실행 계약이 통과할 때까지 iOS 지원은 실험 상태다.

## 실행 검증 절차

시뮬레이터에서 검사 모드로 시작한다. 환경변수는 simctl이 앱에 전달한다.

```sh
SIMCTL_CHILD_GOLEMANCER_SMOKE=1 xcrun simctl launch --terminate-running-process --console booted com.golemancer.factory
```

`IOS_SMOKE_PASS`와 공통 화면 검사 결과를 확인한다. 실패 시 `IOS_SMOKE_FAIL`과 예외를 출력한다. 앱 데이터의 `Library/.../Golemancer/ios-smoke.txt`에도 결과를 기록하며, 검사는 별도 SmokeSaves를 쓴다. 먼저 외부 DLL 적재·실행과 아트 디코딩을 통과한 뒤 새 게임, 저장/이어하기, 두 손가락 조작·한 손가락 취소, 홈 이동/복귀, 회전·안전 영역을 직접 확인한다. 공통 화면 스모크는 UIKit의 실제 터치를 발생시키는 검사가 아니다.

기기에서는 터치 중심으로 조작하며, 외부 키보드 및 GameController 입력 경로도 구현했다. 해당 물리 장치는 아직 검증하지 않았다. 가로 화면과 iPhone/iPad 안전 영역을 사용하고, 비활성화하면 입력을 정리하고 저장한다. 게임 화면은 Android/Linux와 같은 Presentation DLL이다.
