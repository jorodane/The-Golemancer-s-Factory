# 골레맨서 독립 폴더와 엔진 경계

`Golemancer` 폴더 전체를 다른 위치로 복사해 실행하고 개발할 수 있다. 공통 엔진 소스나 부모 저장소의 솔루션은 필요하지 않다.

| 경로 | 책임 |
|---|---|
| `src/Golemancer.Contracts` | 골렘·마나·인벤토리·녹화·주문·지형 등 게임 데이터와 모듈 계약 |
| `src/Golemancer.Runtime` | 게임 시뮬레이션·명령·이동·녹화·저장·메뉴·지형 연결·게임 XML 해석 |
| `modules`, `Content/Packs` | 독립 기능 DLL, XML, 번역, 이미지 |
| `src/Golemancer.Client`, `src/Golemancer.Host`, `src/Golemancer.Android` | 게임 세션과 Windows/Android 화면·입력 |
| `SDK/net48`, `SDK/net10.0` | 고정한 공통 PackEngine DLL |
| `docs/API.txt`, `docs/CONTRACTS.md` | 게임 API의 공개 시그니처와 의미 |
| `SDK/API.txt`, `SDK/RUNTIME_API.txt`, `docs/UI_CONTRACTS.md` | 공통 엔진·UI API |
| `Builds/Windows`, `tests`, `Saves` | 실행 파일, 검증, 플레이 저장 |

공통 엔진은 팩의 의존성·버전·경로를 검사하고 DLL을 로드한다. `IPackModule<TRegistry>`의 등록 대상과 `<Data>` XML 해석은 호출자가 제공한다. 골레맨서는 `IGameModule : IPackModule<IModuleRegistry>`와 게임 XML 해석기를 제공한다. 엔진에는 골레맨서 타입이나 콘텐츠 키가 없고 게임 DLL을 참조하지 않는다.

게임 규칙은 모두 게임 폴더에 있지만 모든 규칙이 개별 콘텐츠 팩으로 분해된 것은 아니다. 공통 골레맨서 규칙은 `Golemancer.Runtime`과 `Golemancer.Contracts`에 있으며, 실제로 팩만으로 기능을 추가할 수 있는지는 사용할 API와 기능에 따라 실험한다.

## 실행과 빌드

`Start.bat`은 기존 Windows 실행 파일을 연다. .NET Framework 4.8이 필요하며 빌드 도구는 필요하지 않다. 소스 빌드에는 .NET 10 SDK가 필요하고 최초 NuGet 복원에는 인터넷 연결이 필요할 수 있다.

- `BuildPacks.bat`: 게임 계약과 콘텐츠 모듈만 빌드한다. 게임 런타임·화면·공통 엔진은 빌드하지 않는다.
- `Build.bat`: 게임 계약·런타임·화면·검증기·콘텐츠 DLL을 빌드하고 Windows 실행 폴더를 갱신한다. 공통 엔진은 SDK의 DLL을 그대로 사용한다.
- `BuildAndroid.bat`: Android SDK 36·JDK 21·Android workload로 네이티브 APK를 만든다. APK는 Git에 넣지 않는다.
- `bash verify.sh`: Linux/macOS에서 기존 게임의 전체 캠페인과 회귀 검증을 실행한다.
- `python tools/context.py Crafting`: 해당 모듈·팩 XML·읽어야 할 API 문서의 위치를 출력한다.

새 게임 DLL은 `Golemancer.Contracts`와 필요한 `PackEngine.Contracts`를 참조한다. 다른 모듈의 구체 타입이나 공통 엔진 소스는 참조하지 않는다. 팩 설치 경로는 `Content/Packs/<팩 이름>`이며, DLL은 `Bin/net48` 또는 `Bin/net10.0`에 둔다.

## 저장과 DLL 호환성

이번 분리에서는 엔진·게임 실행 파일·기본 모듈을 함께 재빌드했다. 게임 DLL ABI는 v2이고 `pack.xml`은 `contracts="2"`를 사용한다. v1 DLL 팩은 새 계약으로 다시 빌드해야 하며, 기존 데이터 팩의 매니페스트도 v2로 맞춘다. XML의 게임 데이터 구조와 JSON 저장 형식(schemaVersion 1)은 유지했다. 분리 전 엔진으로 생성한 저장 파일을 읽고 실행·재저장하는 회귀 검증을 포함한다.

`SDK/engine-lock.json`은 공통 엔진의 소스 해시와 배포 DLL 해시를 기록한다. 게임 기능을 추가할 때 SDK·잠금 파일을 수정하지 않는다. 엔진 API가 부족하면 그 지점을 실험 결과로 남긴다.

## 독립 실행 검증

Python 3.9 이상과 .NET 10 SDK를 준비하고 `TestIsolation.bat` 또는 `python tools/verify-isolation.py`를 실행한다. `--dotnet`으로 SDK 경로를 지정할 수 있다.

검증기는 이 폴더만 임시 위치에 복사하고 빌드 캐시·저장·이전 결과를 제외한다. 엔진 소스 없이 게임을 빌드해 전체 캠페인·기존 DLL 로딩·저장 호환성을 실행한다. 전후 엔진·Windows 호스트 파일의 해시가 같아야 하며, 변조한 SDK는 빌드 단계에서 거부돼야 한다. 결과는 `TestResults/isolation`에 남는다.

기존 TeaBreak 예제의 확장 실험은 `--with-tea-break`를 명시할 때만 실행한다. 이번 분리 검증에는 별도 샘플 팩 실험을 포함하지 않는다.

UI XML 카탈로그와 값·이벤트 연결은 공통 엔진에 있다. `wpf.*`·`skia.*` 선언을 실제 화면으로 조립하는 범용 네이티브 어댑터는 아직 없으며 기존 골레맨서 화면은 게임 폴더의 네이티브 구현을 사용한다.
