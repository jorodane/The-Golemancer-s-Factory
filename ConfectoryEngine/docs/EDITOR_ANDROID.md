# Android 에디터팩 호스트

`Project Studio`의 Android 호스트는 PC와 같은 `editor-1` 계약, 팩 의존성·부분 상속, XML UI와 명령 DLL을 사용한다. 기본 작업 도구에 재사용 예제 `editor.lab.mobile`을 적용하여 입력 이벤트, DLL 응답, 임시 테스트 창을 바로 확인할 수 있다.

현재 시작 화면은 빈 배경에서 Confectory 로고·이름·`AI-Integrated Development Environment`·**AI Agent 연결**·작은 **나중에** 텍스트가 순차적으로 나타나는 인트로다. 콘솔과 에디터 도구는 숨긴다. 이후에는 기존 왼쪽 AI 관리와 팩 목록으로 이동하며, 프로젝트에서는 독립 작업자·도우미·프로젝트 채팅·신문고를 사용한다. 웹 AI WebView는 제거했다. [현재 구조와 플랫폼 범위](INTERNAL_AI_STUDIO.md)를 참고한다.

프로젝트에서는 **요소 탐색기**, **프로젝트 메뉴**, **작업자가 만든 창**을 연다. 파일 목록은 일반 동선에서 숨기고 원문은 요소 편집기의 **XML 열기**로 확인한다. 같은 코어 DLL의 그리드·세로·가로 목록, 계층·후보 편집, 생성·수정 검토와 전용 ObjectEditor를 지원한다. 프로젝트 DLL은 **프로젝트 에디터팩 적용**에서 선택하며 C# 컴파일은 Windows에서 수행한다. [요소 에디터 명세](ELEMENT_EDITOR.md)를 참고한다.

## Windows에서 APK 빌드

저장소 전체 소스를 받은 뒤 루트에서 실행한다. .NET 10 **SDK**, Android workload, JDK 21와 Android SDK가 필요하다. Windows 에디터 실행에 필요한 .NET Framework/Runtime과 빌드용 SDK는 별개다.

### Android Studio가 이미 설치되어 있다면

Android Studio의 기존 Android SDK와 내장 JDK를 우선 재사용한다. .NET 10 SDK와 `android` workload가 준비된 상태에서 `BuildEditorAndroid.bat`를 실행하면 환경 변수, 기본 SDK 경로, Android Studio 설치 위치를 검사하고 선택한 도구의 경로와 JDK 버전을 화면·로그에 표시한다. 기본 Android SDK는 `%LOCALAPPDATA%\Android\Sdk`, Android Studio의 내장 JDK는 설치 폴더의 `jbr`에서 찾는다. Windows에서는 설치 등록 정보와 PATH에 등록된 Studio 실행 파일도 확인한다.

내장 JDK는 **21 버전**이어야 한다. 이전 Android Studio의 JDK 17 등은 해당 버전을 표시한다. `JAVA_HOME`이 이전 버전을 가리키더라도 자동 탐지 모드에서는 사용 가능한 Studio의 JDK 21을 찾는다. PATH에 있는 `java`·`javac`의 실제 설치 위치와 `%ProgramFiles%`의 Microsoft·Java·Eclipse Adoptium·OpenJDK 설치 폴더도 검사하므로, JDK 21이 일반적인 위치에 설치되어 있으면 사용자가 위치를 찾아 입력할 필요가 없다. 명시적으로 준 `-AndroidSdk`·`-JavaSdk`는 우선 사용하며, 대화형 설정에서 사용자가 경로를 입력하거나 새 설치 위치에 동의한 경우에만 바꾼다.

SDK를 별도 위치로 옮겼거나 Studio를 사용자 지정 폴더에 설치했다면 **Tools → SDK Manager → Android SDK Location**에서 기존 SDK 경로를 확인하고 다음과 같이 지정한다.

```powershell
.\BuildEditorAndroid.bat -AndroidSdk "D:\Android\Sdk" -JavaSdk "D:\Apps\Android Studio\jbr"
```

예시 경로는 실제 설치 위치로 바꾼다. 보통은 경로를 직접 찾아갈 필요가 없다. 배치 파일이 기본 설치 위치를 자동으로 검사해 기존 도구를 재사용하고, 도구가 없으면 사용할 기본 SDK·JDK 위치를 먼저 보여준다. **1. 기본 위치로 준비하기**가 권장 선택이며 Enter로 선택할 수 있다. SDK는 `%LOCALAPPDATA%\Android\Sdk`, 새 빌드용 JDK 21은 `%LOCALAPPDATA%\Confectory\BuildTools\jdk-21`을 사용한다. 이미 찾은 도구는 그 위치에서 계속 재사용한다.

사용자 지정 설치를 재사용하려는 경우에만 **2. 기존 설치 폴더 직접 선택하기**를 고른다. 같은 창에서 경로를 붙여넣고 잘못된 경로나 JDK 버전은 다시 확인한다. 경로 입력을 건너뛰면 화면에 표시한 기본 준비 위치를 사용한다. **0. 취소**는 설치와 빌드를 중단한다.

프로젝트에 필요한 Android 플랫폼·Build-Tools 등이 부족하면 사용할 SDK·JDK 위치와 구성 요소를 보여주고 **설치한 뒤 빌드를 계속할지** 묻는다. 기본 위치를 고르는 것과 설치 동의는 별개다. Android SDK 라이선스 동의를 포함해 `y`를 선택한 경우에만 `InstallAndroidDependencies`를 실행하고 설치 결과를 확인한 뒤 APK 빌드를 이어간다. 이 설치 질문의 기본 선택은 거부이며 Enter 또는 `n`은 설치를 진행하지 않는다. 설치한 기본 도구는 다음 실행부터 자동 탐지한다. 기존 Studio의 `jbr`나 이전 JDK는 자동 설치로 덮어쓰지 않는다.

SDK 구성 요소는 기존 SDK의 **SDK Manager**에서 직접 추가할 수도 있다. .NET SDK/workload는 별도로 필요하며 Android Studio의 APK 빌드 버튼 대신 이 프로젝트의 빌드 스크립트를 사용한다. Studio의 SDK Manager와 에뮬레이터는 그대로 사용할 수 있다.

### 처음 개발 도구를 준비한다면

먼저 .NET 10 SDK를 설치한다. PowerShell에서 실행하거나 [Microsoft 다운로드 페이지](https://dotnet.microsoft.com/ko-kr/download/dotnet/10.0)의 **SDK / Windows** 설치 프로그램을 사용한다.

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact
```

설치 후 PowerShell을 새로 열고 저장소 폴더로 이동한다. `dotnet --list-sdks`에서 `10.0.xxx`가 보여야 한다. `global.json`은 `10.0.100`을 기준으로 호환되는 이후 .NET 10 feature band도 허용하므로 정확히 `10.0.100`만 설치할 필요는 없다. 이어서 Android 도구와 의존성을 설치한다.

```powershell
dotnet workload install android
.\BuildEditorAndroid.bat -InstallDependencies -AcceptAndroidSdkLicenses -AndroidSdk C:\Android\sdk -JavaSdk C:\Android\jdk
```

두 번째 명령은 프로젝트가 요구하는 Android SDK와 JDK를 해당 경로에 설치한 뒤 빌드한다. `-AcceptAndroidSdkLicenses`는 Android SDK 라이선스 동의다. 다음부터는 설치 옵션 없이 실행한다.

```powershell
.\BuildEditorAndroid.bat -AndroidSdk C:\Android\sdk -JavaSdk C:\Android\jdk
```

`ANDROID_HOME`과 `JAVA_HOME`이 설정되어 있으면 `.\BuildEditorAndroid.bat`만 실행해도 된다. 기본 대상은 실제 휴대폰용 `arm64`, 구성은 `Release`다. x64 에뮬레이터는 `-Architecture x64`, 디버그 빌드는 `-Configuration Debug`를 지정한다. 설치 경로에는 공백·한글을 피하는 편이 좋다. 설치 절차는 [Microsoft 공식 안내](https://learn.microsoft.com/ko-kr/dotnet/android/getting-started/installation/dependencies)를 따른다.

### 빌드 결과와 오류 확인

`BuildEditorAndroid.bat`는 성공·실패 모두 결과를 표시한 뒤 키 입력을 기다린다. .NET SDK가 없거나 `global.json`과 맞지 않으면 SDK 목록과 설치 방법을, Android workload가 없으면 해당 설치 명령을 보여준다. .NET SDK/workload 설치는 사용자가 해당 명령을 실행할 때 진행된다. 그 이후 Android SDK·JDK는 대화형 설치 제안에 동의하거나 `-InstallDependencies -AcceptAndroidSdkLicenses`를 함께 지정한 경우에 설치한다. 설치 오류나 설치 후 누락이 있으면 APK 빌드를 진행하지 않고 로그를 남긴다.

전체 출력은 `%LOCALAPPDATA%\Confectory\Logs\AndroidBuild\build-날짜-시각-프로세스번호.log`에 보존하며 화면 마지막에 정확한 경로를 표시한다. 프로젝트 안에 로그를 만들지 않는다. PowerShell 실행 자체가 실패하거나 매개변수를 잘못 넣은 경우에도 배치 창은 오류를 보여주고 기다린다. 자동화 실행은 `-NonInteractive` 또는 `-NoPause`로 설정 질문과 마지막 키 입력 대기를 생략한다. 표준 입력이 리디렉션된 경우에도 설정 질문을 하지 않는다. 이 모드에서는 경로가 없거나 도구가 부족하면 안내 후 실패하고, 두 설치 옵션을 명시한 경우에만 설치한다.

```powershell
.\BuildEditorAndroid.bat -NoPause -AndroidSdk C:\Android\sdk -JavaSdk C:\Android\jdk
```

생성물:

| 파일 | 용도 |
| --- | --- |
| `editor/Builds/Android/Confectory.Editor-arm64.apk` | 기본 개발 키로 서명된 테스트용 설치 APK |
| `editor/Builds/Android/editor.lab.mobile.zip` | PC `net48`과 Android `net10.0` DLL, 같은 XML·C# 소스 |
| `editor/Builds/Android/build-info.json` | 소스 커밋·구성·APK 해시 |

APK는 직접 설치하거나 `adb install -r editor\Builds\Android\Confectory.Editor-arm64.apk`로 설치한다. 최소 Android 8.0/API 26, arm64 또는 x64를 지원한다. 업데이트 때 동일한 개발 키를 유지해야 기존 앱에 덮어 설치할 수 있다. APK와 Android SDK/JDK는 Git에 올리지 않는다. `StartEditor.exe`만 복사한 폴더에서는 소스 빌드를 할 수 없다.

## Google Play 제출용 AAB 빌드

루트의 **`BuildEditorAndroidPlay.bat`**는 Release / arm64 AAB를 만든다. 기존 `BuildEditorAndroid.bat`는 테스트 APK를 계속 만든다. 두 빌드는 동일한 팩 기반 통합 엔진을 포함하며 프로젝트팩 실행 모델을 유지한다. Android 대상 API는 **36 (Android 16)**이고 최소 실행 버전은 API 26이다. 최신 .NET 10 Android workload와 API 36 SDK가 필요하다.

### 최초 업로드 키 준비

이미 Play 앱이 있다면 해당 앱의 업로드 키를 사용한다. 새 앱의 키가 없다면 JDK의 `keytool`로 한 번 생성한다. 키·비밀번호 파일은 저장소 밖에 보관하고 키 파일을 백업한다. 다음 명령은 비밀번호를 대화형으로 입력받는다. 실제 JDK 경로로 바꿔 실행한다.

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\ConfectorySigning"
& "C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe" -genkeypair -v -keystore "$env:USERPROFILE\ConfectorySigning\upload.jks" -alias confectory-upload -keyalg RSA -keysize 2048 -validity 10000
```

빌드 예시:

```powershell
.\BuildEditorAndroidPlay.bat -ApplicationId com.yourcompany.confectory -VersionCode 1 -VersionName 0.1.0 -KeyStore "$env:USERPROFILE\ConfectorySigning\upload.jks" -KeyAlias confectory-upload
```

앱 ID는 최초 제출 전에 결정하고 업데이트에도 유지한다. 새 AAB를 제출할 때마다 `-VersionCode`를 이전 업로드보다 높인다. `-VersionName`은 사용자에게 표시되는 버전이다. 키 경로·별칭을 생략하면 대화형으로 물어보며 비밀번호는 숨겨서 입력받는다. 입력받은 비밀번호는 현재 Windows 계정만 접근할 수 있는 임시 파일을 통해 서명 도구에 전달하고 성공·실패 후 삭제한다. 비밀번호를 명령행 인자로 전달하지 않는다.

자동 빌드는 `-NonInteractive -StorePasswordFile <외부 파일> -KeyPasswordFile <외부 파일>`을 추가한다. 각 파일에는 해당 비밀번호만 한 줄로 넣는다. 직접 준비한 파일은 자동 삭제하지 않는다. 서명 파일 경로·키 별칭·버전에는 MSBuild 구분 문자(`%`, `;`, 쉼표, 큰따옴표, 줄바꿈)를 사용할 수 없다. Debug 또는 x64 Play 빌드와 기본 `debug.keystore`는 거부한다.

결과는 **`editor/Builds/Android/PlayStore/Confectory.Editor-arm64.aab`**다. 같은 폴더의 `build-info.json`에 앱 ID·버전·커밋·패키지 SHA-256을 기록한다. 테스트 APK 결과와 분리한다. AAB는 직접 휴대폰에 설치하는 형식이 아니다.

빌드 스크립트는 생성된 AAB의 JAR 서명, 통합 엔진 아카이브 포함 여부, arm64 네이티브 ELF의 모든 LOAD 세그먼트에 대한 16KB 정렬을 검사한다. 실제 서명된 AAB 빌드·검증은 로컬 Windows SDK 환경에서 수행해야 한다. 최종 APK ZIP 정렬과 16KB 기기 실행은 Play의 App Bundle Explorer 및 실제 기기에서 추가 확인한다. ELF 검사는 기기 실행을 대체하지 않는다.

### Play Console에서 테스트

1. Play Console에 앱을 만들고 **Play App Signing**을 설정한다. 직접 준비한 키는 업로드 키로 사용한다.
2. **내부 테스트** 릴리스에 AAB를 올리고 Console의 패키지·대상 API·서명·호환성 검사 결과를 확인한다.
3. 테스트 계정을 등록하고 참여 링크를 통해 설치한다. AI 연결, 엔진 설치/복구, 프로젝트팩 실행을 확인한다.
4. 공개 배포 전에 스토어 설명·아이콘·스크린샷·개인정보처리방침·데이터 보안·콘텐츠 등급 등 Console에서 요구하는 앱 정보를 작성한다. 외부 AI에 보내는 데이터는 실제 동작에 맞게 기재한다.

AAB 형식 지원은 스토어 심사 승인을 뜻하지 않는다. 프로젝트팩의 실행 코드 처리도 실제 동작을 기준으로 검토되므로, 최종 앱의 정책 적합성과 테스트 요건은 Play Console에서 확인해야 한다.

공식 참고: [App Bundle](https://developer.android.com/guide/app-bundle), [대상 API 요구사항](https://developer.android.com/google/play/requirements/target-sdk), [16KB 페이지 지원](https://developer.android.com/guide/practices/page-sizes), [.NET Android 서명 속성](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidsigningkeypass).

## 같은 팩으로 실험하기

1. 앱에서 입력칸에 글을 쓴다. 상태줄의 `DLL 응답 1: ...`은 외부 명령 DLL이 받은 실제 이벤트이며 숫자는 모듈의 메모리 상태다.
2. **같은 팩의 테스트 창 열기**를 누르고 입력을 바꾼 뒤 닫는다. 다시 열면 입력이 복원되고 DLL 응답 횟수도 이어진다. **창 관리**에서 임시 등록을 해제할 수 있다.
3. **XML 문서**에서 `editor.lab.mobile / ui.xml`의 버튼 문구를 수정한다. **변경 검토**에서 수정 전·후를 확인하고 **저장·적용**을 누른다. 새 XML은 먼저 검증하며 실패하면 기존 화면과 DLL을 유지한다. DLL이 같으면 재컴파일 없이 화면만 재조립한다. **되돌리기**는 이 실행에서 마지막으로 적용한 XML 변경을 복구한다.
4. **팩 ZIP 내보내기**로 수정한 XML·소스·현재 런타임 DLL을 파일 앱에 저장한다. PC용 ZIP에는 `Bin/net48` DLL도 필요하다. APK 빌드가 만든 예제 ZIP은 두 바이너리를 모두 포함한다. Android에서 내보낸 ZIP의 PC DLL은 PC에서 다시 빌드한다.
5. PC에서 `editor/examples/MobileLab` 폴더를 `%LOCALAPPDATA%/Confectory/EditorPacks/MobileLab`에 복사하고 에디터팩을 적용한다. 기본 작업 도구를 상속한 같은 패널·명령·창을 사용할 수 있다. PC DLL은 `BuildEditor.bat`가 생성한다.
6. C#를 바꾸려면 PC에서 같은 프로젝트를 `-p:EngineTargetFramework=net48`와 `net10.0`으로 각각 빌드한다. APK를 다시 만들거나, 변경된 `Bin/net10.0` DLL을 포함한 ZIP을 **팩 ZIP 가져오기**로 설치한다. 같은 ID의 외부 팩은 교체하고, 내장 팩 ID는 가져오기로 덮어쓰지 않는다.

추가 팩 ZIP은 하나의 `pack.xml`과 선언된 XML, 해당 런타임의 DLL·관리 종속성, 필요하면 C# 소스를 포함한다. 가져오기는 먼저 파일 경로·용량·manifest를 검사하며 DLL 실행은 **설치·실행**을 누른 뒤 진행한다. XML은 OS와 무관한 `editor.stack/text/button/input` 렌더러를 사용하거나 `platform="android"`와 `platform="windows"` 변형을 선언한다. 네이티브 라이브러리는 각 OS에 맞는 구현이 필요하다.

## 구현 범위와 검증

Android UI는 터치용 네이티브 컨트롤과 대화상자이고, 공통 `EditorWindowRegistry`가 등록·열기·닫기·상태 복원을 관리한다. Android 호스트는 앱 안에서 DLL을 로드하며 `dotnet` CLI나 별도 worker 실행 파일을 요구하지 않는다. JIT 동적 로딩을 위해 trimming·AOT는 비활성화한다. XML 변경은 모듈을 유지하고, DLL 변경은 새로운 비수집 로드 문맥에서 적용한다. 교체된 이전 DLL 문맥의 메모리는 앱 프로세스 종료까지 남는다. 데스크톱 worker의 프로세스 격리·강제 중단 기능은 모바일 호스트에 없다.

팩 명령의 `EditorViewUpdate`는 기존 창과 같은 ID·위젯의 컨트롤을 유지하며 변경된 속성/자식만 반영한다. 동일 text echo는 `EditText.Text`를 다시 지정하지 않고, 늦은 응답은 전송 뒤 입력한 문자열을 덮어쓰지 않는다. 외부 text 변경은 IME 조합 종료까지 기다린다. 검색으로 제거한 입력의 초안은 팩이 다시 제공해야 한다. [실행 중 뷰 갱신 규칙과 수용 확인](EDITOR_PACKS.md#dll-계약과-동적-교체)을 참고한다. 프로젝트 데이터 명령/객체 선택기는 여전히 모바일 에디터팩 명령에서 지원하지 않으며 명시적인 오류로 보고한다.

에디터팩 실행·편집과 일반 프로젝트 문서 편집을 구분한다. **프로젝트 문서 ZIP**으로 연 프로젝트에서는 선언된 문서 목록·읽기·초안 편집·검토 확정·문서 채팅·원격 공동편집을 사용할 수 있다. 내부 에이전트의 Claude/OpenAI API 연결과 프로젝트 문서 도구를 지원한다. 프로젝트 빌드/실행·로컬 Codex CLI는 모바일에 포함하지 않으며, 문서 AI 도구에서도 빌드·검증·실행 명령을 차단한다. 에디터팩의 네이티브 프로젝트 데이터 명령은 기존 모바일 호스트 제한을 유지한다. 닫은 창과 앱 백그라운드 전환 시 입력 상태를 앱 저장소에 보존하지만, DLL 내부 상태는 앱을 완전히 재시작하면 초기화된다. 임시 창 등록도 실행 중에만 유지된다. APK를 업데이트할 때 내장 XML의 사용자 수정은 보존한다. 게임팩과 고정된 게임 SDK는 변경하지 않는다.

`python tools/verify-editor-packs.py --dotnet <dotnet 경로>`는 데스크톱 worker 회귀검사에 더해 Android 프로필의 실제 DLL 로딩·XML 재사용·동일 이름 DLL 교체·실패 복구·창 수명·구독 해제·ZIP 왕복을 검사한다. 네이티브 Android 소스는 Android 대상 빌드로 검사한다. 실제 기기의 키보드·회전·파일 선택·설치는 기기에서 확인해야 한다.

## 스탠드얼론 시작과 AI 연결

매번 시작할 때 에이전트 연결 또는 **나중에**를 선택하고 팩 목록으로 이동한다. 저장된 연결을 재사용할 수 있지만 이전 팩·모델 요청을 자동 실행하지 않는다. 왼쪽 AI 관리에서 연결·도우미를 추가하고 팩 안에서 독립 작업자와 프로젝트 채팅·신문고를 사용한다.

에이전트는 Claude API 또는 OpenAI API의 키·모델·전송/과금 동의를 확인한다. 키는 Android Keystore로 암호화한다. 실제 XML 변경은 사용자의 검토 또는 명시적으로 부여한 다른 AI의 승인 범위를 따른다. C# 컴파일은 Windows에서 수행하고 팩 ZIP을 가져온다. [AI 연결](AI_CONNECTIONS.md)을 따른다.

## Android 개편 사용 순서

휴대폰에서는 위쪽 **AI 목록**으로 왼쪽 관리 화면을 열고 닫는다. 넓은 화면에서는 왼쪽 목록과 작업 화면을 나란히 보여주며 회전할 때 다시 배치한다. 아래 실행 콘솔은 기본 화면에 유지한다.

### 도우미와 충돌 협의

- 도우미 프로필의 **이미지 선택·변경**에서 이미지를 고르고 미리보기 후 **이 이미지 사용**을 누른다. **이미지 제거**도 지원한다. 외부 사진 접근 권한을 따로 요구하지 않고 선택한 이미지만 내부에 복사한다.
- **성격 추론 / 캐릭터 말투 / 관계 표현**을 각각 켜고 끌 수 있다. 개인 기억과 이미지는 공동편집에 자동 전송하지 않는다.
- 독립 AI 작업자의 변경이 같은 요소에서 충돌하면 후보·공통 원본·변경 내용·라운드별 HP와 근거를 표시한다. 모든 후보가 이 기기 소유 AI이면 새 API 문맥으로 협의한다. 이때 실제 API 사용량이 발생한다.
- 관전은 사람의 참가로 기록하지 않는다. **발언** 또는 후보 직접 선택은 자동 결정을 멈춘다. 모델 오류·원격 소유 후보는 사용자 선택으로 전환한다. 결정 결과도 최종 변경 검토를 거친다. **충돌 협의 기록**에서 이전 기록을 확인한다.

### PC와 같은 프로젝트에서 함께 편집하기

1. PC에서 게임 프로젝트를 열고 **함께 편집 · 연결 → 모바일용 프로젝트 문서 ZIP 내보내기**를 누른다. 확정된 소스 문서만 내보내므로 PC에서 보낼 변경은 먼저 확정한다.
2. ZIP을 휴대폰으로 옮긴 뒤 Android 프로젝트 목록의 **프로젝트 문서 ZIP 가져오기**에서 연다. 에디터 DLL을 설치하는 **팩 ZIP 가져오기**와 별도 기능이다. 가져온 프로젝트는 목록에 남고 시작할 때 자동으로 열지 않는다.
3. 한 기기에서 **함께 편집 · 연결**을 열고 상대가 접근할 수 있는 IP 주소로 호스트를 만든다. 상대 기기는 같은 프로젝트에서 초대 코드로 참여한다. 같은 네트워크나 이미 접근 가능한 주소가 필요하며 자동 터널·포트포워딩은 하지 않는다.
4. **프로젝트 문서**에서 XML/C# 등을 열면 공동 작업본과 문서 채팅을 사용할 수 있다. 아직 완성되지 않은 구문도 작업본으로 동기화하며, 충돌은 **동시 수정 비교**에서 내 초안과 호스트 작업본을 보고 선택한다.
5. **내 초안 저장**은 파일을 확정하지 않는다. **변경 확정**은 호스트에 검토를 요청하고 호스트에서 수락해야 확정된다. Android가 호스트이면 같은 기기에서 최종 검토한다. 참여자 AI의 변경도 작업본 초안까지 전달하고 호스트 확정을 거친다.
6. 호스트가 확정하면 **호스트 확정본 검토**에서 휴대폰의 로컬 파일 반영을 별도로 검토한다. 진행 중인 초안은 유지한다. **프로젝트 문서 ZIP 내보내기**는 로컬에서 확정된 문서만 저장한다.
7. AI가 겹치는 작업을 초안으로 넘겼다면 **인계받은 문서 초안**에서 현재 작업본과 비교하고 받아온다. 이 동작도 파일 확정과 분리된다.

연결 종료·앱 종료 후 초안은 보존한다. 자동 재접속이나 저장된 AI 요청 재실행은 하지 않는다. 다른 사람 소유 AI는 참여 상태와 공개 호출로만 접근한다. 초대 코드는 접속 권한이므로 프로젝트 참여자에게만 전달한다.

프로젝트 문서 ZIP은 4,096개 항목·문서당 2 MB·총 64 MB까지다. 실행 파일·이미지·링크·상위 경로·중복 경로는 받지 않는다. 공동편집 메시지는 기존 프로토콜과 같이 2 MiB까지이며 큰 문서는 이 한도에 걸릴 수 있다.

이번 변경의 자동검증과 실제 기기 확인은 구분한다. Android/Windows 빌드, Studio 57개 검사(로컬 TLS 포함), 에디터팩 151개와 모바일 모듈 28개·문서 데이터 36개·창 모듈 32개 검사, resident 회귀검사와 전체 게임 캠페인이 통과했다. 실제 휴대폰에서의 사진 선택·회전·키보드, PC↔휴대폰 네트워크와 실제 API 모델 응답은 사용 기기에서 확인해야 한다.
