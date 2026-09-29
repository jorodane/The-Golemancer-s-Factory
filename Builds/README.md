# 바로 실행하는 Windows 빌드

저장소를 받은 뒤 루트의 **Start.bat**을 실행하면 돼. .NET Framework 4.8만 필요하고 SDK 설치나 빌드 ZIP 압축 해제는 필요 없어.

| 위치 | 내용 |
|---|---|
| `Builds/Windows/Golemancer.exe` | 네이티브 WPF 실행 파일 |
| `Builds/Windows/*.dll` | 엔진·공유 계약·런타임 의존 DLL |
| `Builds/Windows/Golemancer.Verification.exe` | 미리 빌드한 캠페인·회귀 검증 프로그램 |
| `Content/Packs/*/Bin/net48/*.dll` | 런타임에 로드하는 게임 모듈 |
| `Content/Packs` | 실행 파일과 소스가 함께 사용하는 XML과 로컬 이미지 |
| `Saves` | 기존 게임 저장 파일 |

`Golemancer.exe`를 직접 실행해도 상위의 저장소 `Content`를 찾아. 따라서 `Builds/Windows`만 따로 떼어내지 말고 저장소 구조 그대로 사용해.

이미지는 저장소 루트의 `Content/Packs/<팩>/Images`에 한 번 배치해두면 돼. 이후 pull이나 빌드 갱신은 그 이미지를 그대로 사용해. 새 실행 폴더에 이미지팩을 다시 풀거나 `Content`를 합칠 필요가 없어. 이미지에는 Git 제외 규칙을 적용하지 않아서 사용자 Git에서 직접 추가·푸시할 수 있어.

- **Start.bat**: 현재 빌드 바로 실행.
- **Verify.bat**: 현재 빌드로 캠페인·회귀 검사와 WPF 스모크 검사 실행. SDK 불필요.
- **Build.bat**: .NET 10 SDK로 소스를 다시 빌드하고 실행 폴더·모듈 DLL 갱신. 이미지와 세이브는 건드리지 않아.

빌드 기준 커밋은 `Windows/build-info.json`, 실행 파일과 모듈의 해시는 `Windows/SHA256SUMS`에 있어. Windows 대상 빌드와 공유 로직 검증은 완료했지만, 제작 환경에서 실제 WPF 창은 실행하지 못했어. 상세 범위는 [검증 기록](../docs/VERIFICATION.md)을 참고해.
