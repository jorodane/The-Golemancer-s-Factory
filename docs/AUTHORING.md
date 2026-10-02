# Confectory 에디터 제작 도구

루트 `StartEditor.exe`로 실행한다. 게임팩과 에디터팩의 내부 ID, 설정 위치, DLL 이름은 기존 값을 유지한다. 창 제목과 제품 표시는 Confectory를 사용한다.

## 팩 검색과 생성

- `packengine_find`는 게임팩과 등록된 에디터팩을 함께 검색한다. 에디터 키는 `editor:<pack>/<file>#<object>` 형태다. `packengine_inspect`는 실행 DLL이 없어도 팩 명세와 선언된 소스 목록을 반환한다.
- `packengine_read`와 `packengine_patch`는 `editor:<pack>/<file>` 경로도 받는다. 수정에는 먼저 읽은 전체 문서 해시와 한 번만 일치하는 원문이 필요하다.
- `packengine_create(domain, operation, pack, intent, files)`의 `operation`은 `create` 또는 `new_pack`이다. 에디터팩 생성 위치는 호스트가 제공한 `project`/`plugin` 범위 중에서 선택한다. 코어 설치 위치는 생성 대상으로 제공하지 않는다.
- 새 파일의 `expectedHash`는 `absent`다. 기존 XML·프로젝트 등록을 함께 고칠 때에는 먼저 읽은 해시를 사용한다. 필요한 등록과 소스 파일을 한 묶음으로 검토하며, 적용 직전에 모든 경로와 버전을 다시 확인한다. 경로 충돌이나 중간 실패가 생기면 이미 쓴 파일도 복원한다.
- 검토 전에는 디스크에 팩을 등록하지 않는다. 생성 예정 파일을 읽고 수정하는 작업은 임시 변경안에만 반영된다. 선택하지 않거나 취소한 묶음은 적용되지 않는다. 빌드·재로딩·창 열기는 별도 검토 항목이고 실제 결과를 반환한다.

## 아이콘과 슬롯 UI

`packengine_editor(operation="api")`가 현재 호스트의 계약, 구현 소스 경로와 예제를 제공한다.

| 기능 | 계약 |
|---|---|
| 객체 검색 | 선택적 `IEditorProjectCatalog.ListObjects(kind, pack, query)` |
| 이미지 목록/조회 | `ListAssets(pack)`, `ReadAsset(path)`; 팩 또는 프로젝트 스키마에 선언된 비트맵만 조회 |
| 슬롯 | Windows의 `editor.slot`: `image`, `glyph`, `count`, `value`, `tooltip`, `tint` |
| 여러 슬롯 배치 | Windows의 `editor.wrap`과 마지막 `glyph="+"` 슬롯 |
| 객체 선택 | `EditorCommandResult.PickObject`; 선택 후 같은 팩의 Text 명령에 객체 키 전달 |
| 가변 화면 | `EditorCommandResult.View`; 해당 팩 소유 창의 임시 UI만 교체 |
| 문서 변경 | 같은 명령 호출에서 읽은 문서의 해시와 함께 `DocumentChanges` 반환, 사용자 검토 후 적용 |

슬롯 UI가 게임의 데이터 계약을 바꾸지는 않는다. 현재 Golemancer `RecipeDef.Output`/`Amount`와 XML `output`/`amount`는 **출력 한 종류**다. 입력은 여러 종류를 표시할 수 있고 출력도 한 슬롯으로 표시할 수 있다. 여러 출력 추가는 게임팩의 계약·로더·제작 처리를 먼저 확장해야 하므로 엔진 UI만으로 지원됐다고 표시하면 안 된다. 이번 변경은 레시피 전용 창을 코어에 넣지 않고, 별도 에디터팩에서 창을 만들 수 있는 계약을 제공한다.

Android는 기존 stack/text/button/input 계약을 유지한다. Windows 전용 슬롯·임시 화면·선택 요청은 지원하지 않는 호스트에서 명시적으로 거절한다. SVG는 목록과 경로만 제공하고, 현재 네이티브 아이콘 미리보기에는 비트맵이 필요하다.

## 대화와 Yogi

- 현재 보이는 메시지는 드래그·Ctrl+C, 메시지별 복사, 전체 대화 복사로 가져올 수 있다. 응답 중인 내용과 보관 실패 상태에서도 화면에 있는 본문을 복사한다.
- 복구본은 짧은 파일명과 같은 폴더의 짧은 임시 파일명으로 저장한다. 기존 v1 복구본을 읽을 수 있으며 다시 저장할 때 v2로 옮긴다. 복구본 저장 실패는 경고로 남기고 정상 AI 응답이나 로컬 Codex 원본 기록을 실패 처리하지 않는다.
- 대화를 프로젝트에 넣는 동작은 계속 **‘프로젝트에 대화 저장’**을 눌렀을 때만 실행한다.
- Yogi 첨부는 대상 웹 화면, 활성 입력창 탐색, 파일 전달, 첨부 표시 확인 단계를 로그로 구분한다. 자동 탐지가 실패하면 ‘입력창 직접 지정’, 파일 복사 또는 끌어 넣기를 사용할 수 있다. 메시지 전송은 사용자가 한다.
- 공유 대화에는 사용자가 공유한 본문을 전송 시점에 고정해 넣는다. 첫 전달은 총 12,000자이며, 나머지는 `packengine_read(chat:<id>)`로 읽는다. 링크 등록만으로 웹 대화 본문을 가져오지는 않는다.

## 이미지 생성

AI 메뉴의 **이미지 생성 연결**에서 별도 OpenAI API 키와 사용 동의를 설정한다. ChatGPT 로그인이나 스킬 이름만으로 생성이 연결됐다고 표시하지 않는다. `packengine_image(status)`로 상태를 확인하고, `generate`는 실제 Images API 응답을 네이티브 창에 보여준다. 파일 저장은 미리보기 창에서 할 수 있다. `register`는 새 PNG와 `pack.xml`의 Asset 등록을 하나의 검토 항목으로 묶는다. 기존 경로는 덮어쓰지 않는다.

메인 창·팩 창·주요 대화상자의 위치, 크기, 최대화 상태는 PC별 설정에 저장한다. 모니터가 사라지면 사용 가능한 화면 안으로 위치를 보정한다.

## 검증

```sh
python3 tools/verify-authoring.py --dotnet /path/to/dotnet
python3 tools/verify-editor-packs.py --dotnet /path/to/dotnet
python3 tools/verify-conversations.py --dotnet /path/to/dotnet
python3 tools/verify-resident.py --dotnet /path/to/dotnet
bash verify.sh
```

이미지 API 응답 검사는 명시적인 HTTP fixture이며 유료 추론을 실행하지 않는다. Windows WPF 화면, 실제 ChatGPT 입력창 첨부, 실제 API 계정 응답은 별도의 기기 검증 항목이다.
