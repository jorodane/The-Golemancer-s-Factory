# 골레맨서 폴더만 사용하는 확장 실험

`Golemancer` 폴더 전체가 실험 단위다. 다른 위치로 **복사**해서 사용해도 되고, 원래 저장소 안에서 작업해도 된다. `src/Golemancer.Engine`·`src/Golemancer.Contracts`의 소스와 루트 솔루션은 필요하지 않다. 게임 프로젝트는 폴더 안의 `SDK` DLL을 참조한다.

## 폴더와 편집 범위

| 경로 | 역할 |
|---|---|
| `modules/Golemancer.*` | 독립 기능 DLL의 소스 |
| `Content/Packs` | DLL·XML·번역·이미지로 구성되는 객체팩 |
| `src/Golemancer.Client` | 골레맨서 세션과 입력 상태 |
| `src/Golemancer.Host`, `src/Golemancer.Android` | 골레맨서의 Windows/Android 화면과 플랫폼 연결 |
| `examples/TeaBreak` | 새 행동·시설·입력을 추가하는 선택적 게임팩 |
| `examples/UiComposition` | 게임 규칙 없이 UI 제공·요청·공개 슬롯을 연결하는 예제 |
| `docs`, `SDK/API.txt`, `SDK/*/Golemancer.Contracts.xml` | API 공개 시그니처·XML·게임 명세 |
| `SDK/net48`, `SDK/net10.0` | 실험 중 고정하는 엔진·API DLL |
| `SDK/engine-lock.json` | 엔진 기준 커밋과 SHA256 |
| `Builds/Windows` | 바로 실행할 수 있는 Windows 호스트·의존 파일 |
| `tests/Golemancer.Verification` | 게임 캠페인과 계약·회귀 검증 |
| `Saves`, `TestResults` | 플레이 저장과 검증 결과. 각각 별도로 관리 |

일반 게임 확장은 `modules`와 `Content/Packs`에서 한다. 새 DLL은 `IGameModule.Register`로 행동·조건·시스템·월드를 등록하고 `pack.xml`의 `Assembly`로 로드한다. 대상 프레임워크별 경로는 `Bin/net48`과 `Bin/net10.0`이다. 공유 계약 DLL을 객체팩에 복제하거나 엔진 프로젝트 참조를 다시 추가하지 않는다.

공유 API가 부족해서 엔진 변경이 필요해지면 그 지점이 실험 결과다. `engine-lock.json`이나 해시 검사를 바꿔서 통과시키지 않는다. 게임 전용 세션·화면 소스도 폴더 안에 있지만, **팩만으로 확장되는지 확인하는 실험에서는 호스트 재빌드를 사용하지 않는다.**

## 실행과 빌드

Windows 실행에는 .NET Framework 4.8만 필요하다. `Start.bat`은 SDK 없이 `Builds/Windows`의 기존 실행 파일을 연다. 이미지와 팩은 이 게임 폴더의 `Content`를 읽는다.

소스 빌드에는 .NET 10 SDK가 필요하며 최초 NuGet 복원에는 인터넷 연결이 필요할 수 있다.

```bat
BuildPacks.bat
dotnet build examples\TeaBreak\TeaBreak.csproj -c Release
```

첫 명령은 기본 게임 모듈만 빌드한다. 두 번째 명령은 새 찻상 팩만 빌드한다. 생성된 `examples/TeaBreak/Pack`을 `Content/Packs/99.TeaBreak`로 복사한 뒤 게임을 다시 시작하면 찻상·휴식 행동과 입력 선언이 추가된다. 엔진과 Windows 실행 파일은 바뀌지 않는다. 제거할 때에는 게임 종료 후 추가한 `99.TeaBreak` 폴더만 제거한다. 기존 저장의 알 수 없는 팩 데이터는 보존하는 엔진 규칙을 따른다.

`Build.bat`은 게임 세션·Windows 화면·검증 프로그램까지 빌드한다. 공통 엔진/API를 빌드하거나 교체하지 않지만, 팩만 추가하는 실험과는 범위가 다르다. `BuildAndroid.bat`은 고정 net10.0 엔진을 APK에 포함한다. Android SDK 36·JDK 21·Android workload가 별도로 필요하다. APK는 별도 다운로드로 배포하며 Git에 넣지 않는다.

Linux/macOS의 공유 로직 검증은 `bash verify.sh`, 게임팩 빌드는 `bash build-packs.sh -p:GolemancerTargetFramework=net10.0`으로 실행한다. `python tools/context.py Crafting`은 제작 모듈과 XML, 읽어야 할 API 명세만 출력한다.

## 재현 가능한 독립 폴더 실험

Python 3.9 이상과 .NET 10 SDK를 준비하고, 기본 팩에 TeaBreak를 설치하기 전에 다음을 실행한다.

```bat
TestIsolation.bat
```

또는 어느 플랫폼에서든 `python tools/verify-isolation.py`를 실행한다. `--dotnet`으로 SDK 실행 파일을 지정하고, `--keep`으로 임시 사본을 남길 수 있다.

검증기는 게임 폴더만 임시 위치에 복사하고 빌드 캐시·저장·이전 테스트 결과를 제외한다. 그 사본에는 엔진/API 소스 프로젝트가 없다. 외부 프로젝트 참조를 검사한 뒤 고정 SDK로 기본 모듈과 검증기를 빌드하고 전체 캠페인을 실행한다. 새 찻상 DLL을 net10.0으로 빌드해 런타임 적재·행동 실행·메뉴·입력 연결을 검증하고, Windows용 net48 DLL도 빌드한다. 별도 레지스트리로 기본 팩을 다시 읽어 확장 제거도 확인한다.

실험 전후 SDK·엔진·Windows 호스트 파일의 SHA256이 모두 같아야 성공한다. API DLL을 임시로 변조했을 때 실제 빌드 검사가 거부하는지도 확인한 뒤 원본으로 복구한다. 결과와 명령 출력은 `TestResults/isolation/report.json` 및 같은 폴더의 로그에 남는다. 이 검증은 실제 Windows 창 조작이나 Android 기기 실행을 대신하지 않는다.

## 현재 증명할 수 있는 범위

기존 API가 제공하는 액션·조건·시스템·월드·XML·입력·UI 계약으로 새 DLL 팩을 추가할 수 있는지 확인한다. 현재 엔진과 공통 API에는 `golem`, `mana`, 녹화·재고 같은 골레맨서 전용 의미가 남아 있으므로, 모든 게임 규칙을 DLL로 완전히 분리했다고 주장하지 않는다. 새로운 데이터 모델이나 엔진 내부 생명주기가 필요한 확장은 이 실험에서 실패할 수 있다.

UI XML 카탈로그와 값·이벤트 연결은 제공하지만, `wpf.*`·`skia.*` 선언을 실제 화면으로 조립하는 범용 네이티브 어댑터는 아직 없다. 찻상 예제의 메뉴 노출은 기존 골레맨서 행동 메뉴를 사용한다. 새 XML 화면이 Windows/Android에서 바로 렌더링된다는 검증으로 해석하면 안 된다.
