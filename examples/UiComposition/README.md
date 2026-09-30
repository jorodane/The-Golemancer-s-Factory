# UI 계약 예제

게임 내용이 없는 UI 제공·요청·확장 예제다. [UI_CONTRACTS.md](../../docs/UI_CONTRACTS.md)에 XML과 함수 명세가 있다.

| 팩 | 역할 |
|---|---|
| `Widgets` | panel/range/text/button/toggle의 설정·이벤트·슬롯을 공개 |
| `Project` | 값 막대와 문자열, 토글, 닫기 버튼을 요청하고 `items` 슬롯을 공개 |
| `Mod` | 공개 슬롯에 추가 정보 버튼 하나를 삽입 |

`PackLoader.Cook("examples/UiComposition")`으로 이 세 팩만 쿠킹할 수 있다. 게임 객체나 게임 모듈 DLL은 필요하지 않다. 실제 화면으로 열려면 아래 context와 플랫폼 backend를 제공하고 `cooked.Registry.Ui.Mount("sample.status", context, backend)`를 호출한다.

| context 이름 | 종류/형 | 제공할 의미 |
|---|---|---|
| `status.current` | 값 / number | 현재값, 0 이상 |
| `status.maximum` | 값 / number | 최대값, 0 이상 |
| `status.caption` | 값 / text | 표시할 문자열 |
| `settings.enabled` | 값 / boolean | 확정된 설정 상태 |
| `settings.requestEnabled` | 명령 / boolean | 설정 변경 요청 처리 |
| `screen.close` | 명령 / none | 화면 종료 |
| `extension.details` | 명령 / none | 모드의 추가 정보 열기 |

예제 range 렌더러의 의미는 현재/최대 비율 표시, 최대 0이면 빈 막대, 시각적 비율은 0~1 범위다. panel은 `items`를 direction 방향으로 나열하고 gap을 적용하는 자동 배치 컨테이너다. 자동 배치된 자식의 앵커 위치는 사용하지 않으며 높이/폭 등 측정 정책은 실제 어댑터 구현 시 명세를 추가해야 한다.

`wpf.*`/`skia.*`는 구현을 요청하는 렌더러 ID다. 해당 네이티브 어댑터는 아직 제공하지 않는다. 테스트의 기록용 backend로 두 플랫폼의 선택·조립 결과를 확인한다.

```bash
dotnet build tests/Golemancer.Verification/Golemancer.Verification.csproj -c Release -p:GolemancerTargetFramework=net10.0
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll --ui
```

`--ui`는 골레맨서 게임 팩/DLL을 로드하지 않는다. XML/C# 요청의 타입 검사, 값 갱신, 이벤트 회신, 공개 슬롯 기여, 화면 해제 및 실패 시 정리까지 검증한다. 검증 구현의 `CodeRequest`에는 같은 계약을 C# 함수에서 제공하는 예제도 있다.
