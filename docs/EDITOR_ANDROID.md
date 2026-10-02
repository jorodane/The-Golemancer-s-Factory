# Android 에디터팩 호스트

`Project Studio`의 Android 호스트는 PC와 같은 `editor-1` 계약, 팩 의존성·부분 상속, XML UI와 명령 DLL을 사용한다. 기본 작업 도구에 재사용 예제 `editor.lab.mobile`을 적용하여 입력 이벤트, DLL 응답, 임시 테스트 창을 바로 확인할 수 있다.

## Windows에서 APK 빌드

저장소 전체 소스를 받은 뒤 루트에서 실행한다. .NET 10 **SDK**, Android workload, JDK 21와 Android SDK가 필요하다. Windows 에디터 실행에 필요한 .NET Framework/Runtime과 빌드용 SDK는 별개다.

### Android Studio가 이미 설치되어 있다면

Android Studio의 기존 Android SDK와 내장 JDK를 우선 재사용한다. .NET 10 SDK와 `android` workload가 준비된 상태에서 `BuildEditorAndroid.bat`를 실행하면 환경 변수, 기본 SDK 경로, Android Studio 설치 위치를 검사하고 선택한 도구의 경로와 JDK 버전을 화면·로그에 표시한다. 기본 Android SDK는 `%LOCALAPPDATA%\Android\Sdk`, Android Studio의 내장 JDK는 설치 폴더의 `jbr`에서 찾는다. Windows에서는 설치 등록 정보와 PATH에 등록된 Studio 실행 파일도 확인한다.

내장 JDK는 **21 버전**이어야 한다. 이전 Android Studio의 JDK 17 등은 해당 버전을 표시한다. `JAVA_HOME`이 이전 버전을 가리키더라도 자동 탐지 모드에서는 사용 가능한 Studio의 JDK 21을 찾는다. PATH에 있는 `java`·`javac`의 실제 설치 위치와 `%ProgramFiles%`의 Microsoft·Java·Eclipse Adoptium·OpenJDK 설치 폴더도 검사하므로, JDK 21이 일반적인 위치에 설치되어 있으면 사용자가 위치를 찾아 입력할 필요가 없다. 명시적으로 준 `-AndroidSdk`·`-JavaSdk`는 우선 사용하며, 대화형 설정에서 사용자가 경로를 입력하거나 새 설치 위치에 동의한 경우에만 바꾼다.

SDK를 별도 위치로 옮겼거나 Studio를 사용자 지정 폴더에 설치했다면 **Tools → SDK Manager → Android SDK Location**에서 기존 SDK 경로를 확인하고 다음과 같이 지정한다.

```powershell
.\BuildEditorAndroid.bat -AndroidSdk "D:\Android\Sdk" -JavaSdk "D:\Apps\Android Studio\jbr"
```

예시 경로는 실제 설치 위치로 바꾼다. 보통은 경로를 직접 찾아갈 필요가 없다. 배치 파일이 기본 설치 위치를 자동으로 검사해 기존 도구를 재사용하고, 도구가 없으면 사용할 기본 SDK·JDK 위치를 먼저 보여준다. **1. 기본 위치로 준비하기**가 권장 선택이며 Enter로 선택할 수 있다. SDK는 `%LOCALAPPDATA%\Android\Sdk`, 새 빌드용 JDK 21은 `%LOCALAPPDATA%\PackEngine\BuildTools\jdk-21`을 사용한다. 이미 찾은 도구는 그 위치에서 계속 재사용한다.

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

전체 출력은 `%LOCALAPPDATA%\PackEngine\Logs\AndroidBuild\build-날짜-시각-프로세스번호.log`에 보존하며 화면 마지막에 정확한 경로를 표시한다. 프로젝트 안에 로그를 만들지 않는다. PowerShell 실행 자체가 실패하거나 매개변수를 잘못 넣은 경우에도 배치 창은 오류를 보여주고 기다린다. 자동화 실행은 `-NonInteractive` 또는 `-NoPause`로 설정 질문과 마지막 키 입력 대기를 생략한다. 표준 입력이 리디렉션된 경우에도 설정 질문을 하지 않는다. 이 모드에서는 경로가 없거나 도구가 부족하면 안내 후 실패하고, 두 설치 옵션을 명시한 경우에만 설치한다.

```powershell
.\BuildEditorAndroid.bat -NoPause -AndroidSdk C:\Android\sdk -JavaSdk C:\Android\jdk
```

생성물:

| 파일 | 용도 |
| --- | --- |
| `editor/Builds/Android/PackEngine.Editor-arm64.apk` | 기본 개발 키로 서명된 테스트용 설치 APK |
| `editor/Builds/Android/editor.lab.mobile.zip` | PC `net48`과 Android `net10.0` DLL, 같은 XML·C# 소스 |
| `editor/Builds/Android/build-info.json` | 소스 커밋·구성·APK 해시 |

APK는 직접 설치하거나 `adb install -r editor\Builds\Android\PackEngine.Editor-arm64.apk`로 설치한다. 최소 Android 8.0/API 26, arm64 또는 x64를 지원한다. 업데이트 때 동일한 개발 키를 유지해야 기존 앱에 덮어 설치할 수 있다. APK와 Android SDK/JDK는 Git에 올리지 않는다. `StartEditor.exe`만 복사한 폴더에서는 소스 빌드를 할 수 없다.

## 같은 팩으로 실험하기

1. 앱에서 입력칸에 글을 쓴다. 상태줄의 `DLL 응답 1: ...`은 외부 명령 DLL이 받은 실제 이벤트이며 숫자는 모듈의 메모리 상태다.
2. **같은 팩의 테스트 창 열기**를 누르고 입력을 바꾼 뒤 닫는다. 다시 열면 입력이 복원되고 DLL 응답 횟수도 이어진다. **창 관리**에서 임시 등록을 해제할 수 있다.
3. **XML 문서**에서 `editor.lab.mobile / ui.xml`의 버튼 문구를 수정한다. **변경 검토**에서 수정 전·후를 확인하고 **저장·적용**을 누른다. 새 XML은 먼저 검증하며 실패하면 기존 화면과 DLL을 유지한다. DLL이 같으면 재컴파일 없이 화면만 재조립한다. **되돌리기**는 이 실행에서 마지막으로 적용한 XML 변경을 복구한다.
4. **팩 ZIP 내보내기**로 수정한 XML·소스·현재 런타임 DLL을 파일 앱에 저장한다. PC용 ZIP에는 `Bin/net48` DLL도 필요하다. APK 빌드가 만든 예제 ZIP은 두 바이너리를 모두 포함한다. Android에서 내보낸 ZIP의 PC DLL은 PC에서 다시 빌드한다.
5. PC에서 `editor/examples/MobileLab` 폴더를 `%LOCALAPPDATA%/PackEngine/EditorPacks/MobileLab`에 복사하고 에디터팩을 적용한다. 기본 작업 도구를 상속한 같은 패널·명령·창을 사용할 수 있다. PC DLL은 `BuildEditor.bat`가 생성한다.
6. C#를 바꾸려면 PC에서 같은 프로젝트를 `-p:EngineTargetFramework=net48`와 `net10.0`으로 각각 빌드한다. APK를 다시 만들거나, 변경된 `Bin/net10.0` DLL을 포함한 ZIP을 **팩 ZIP 가져오기**로 설치한다. 같은 ID의 외부 팩은 교체하고, 내장 팩 ID는 가져오기로 덮어쓰지 않는다.

추가 팩 ZIP은 하나의 `pack.xml`과 선언된 XML, 해당 런타임의 DLL·관리 종속성, 필요하면 C# 소스를 포함한다. 가져오기는 먼저 파일 경로·용량·manifest를 검사하며 DLL 실행은 **설치·실행**을 누른 뒤 진행한다. XML은 OS와 무관한 `editor.stack/text/button/input` 렌더러를 사용하거나 `platform="android"`와 `platform="windows"` 변형을 선언한다. 네이티브 라이브러리는 각 OS에 맞는 구현이 필요하다.

## 구현 범위와 검증

Android UI는 터치용 네이티브 컨트롤과 대화상자이고, 공통 `EditorWindowRegistry`가 등록·열기·닫기·상태 복원을 관리한다. Android 호스트는 앱 안에서 DLL을 로드하며 `dotnet` CLI나 별도 worker 실행 파일을 요구하지 않는다. JIT 동적 로딩을 위해 trimming·AOT는 비활성화한다. XML 변경은 모듈을 유지하고, DLL 변경은 새로운 비수집 로드 문맥에서 적용한다. 교체된 이전 DLL 문맥의 메모리는 앱 프로세스 종료까지 남는다. 데스크톱 worker의 프로세스 격리·강제 중단 기능은 모바일 호스트에 없다.

이번 실험은 **공통 에디터팩 실행·편집 호스트**다. Windows의 프로젝트 트리·게임 빌드/실행·Codex/WebView·프로젝트 문서 조회 API를 모바일로 옮기지는 않았다. 프로젝트 문서 접근이 필요한 명령은 이 호스트에서 사용할 수 없다. 닫은 창과 앱 백그라운드 전환 시 입력 상태를 앱 저장소에 보존하지만, DLL 내부 상태는 앱을 완전히 재시작하면 초기화된다. 임시 창 등록도 실행 중에만 유지된다. APK를 업데이트할 때 내장 XML의 사용자 수정은 보존한다. 게임팩과 고정된 게임 SDK는 변경하지 않는다.

`python tools/verify-editor-packs.py --dotnet <dotnet 경로>`는 데스크톱 worker 회귀검사에 더해 Android 프로필의 실제 DLL 로딩·XML 재사용·동일 이름 DLL 교체·실패 복구·창 수명·구독 해제·ZIP 왕복을 검사한다. 네이티브 Android 소스는 Android 대상 빌드로 검사한다. 실제 기기의 키보드·회전·파일 선택·설치는 기기에서 확인해야 한다.
