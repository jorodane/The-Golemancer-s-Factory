# 에디터의 외부 프로세스 제공자 연결

이 문서는 기존 읽기 전용 `Command` 제공자의 규약이다. ChatGPT 구독으로 연결하는 공식 Codex app-server 제공자와 실제 편집 도구는 [RESIDENT_AGENT.md](RESIDENT_AGENT.md)를 따른다.

`IEditorAssistant`는 `PackEngine.Workspace.dll`의 공개 계약이다. 에디터/CLI가 사용자가 선택한 제공자 DLL을 로드한다. `ReplyAsync(ContextRequest, IAssistantWorkspace, CancellationToken)`가 문자열 응답을 반환한다. 모델 선택·인증·전송·비용 처리는 제공자의 책임이다. Command 제공자는 특정 모델 API를 직접 구현하지 않는다.

외부 AI 클라이언트 프로세스를 사용할 때는 다음 로컬 JSON 파일 경로를 `PACKENGINE_ASSISTANT_CONFIG` 환경변수로 설정하고 에디터를 시작한다. 계정 설정과 개인 경로는 저장소에 올리지 않는다.

```json
{
  "Executable": "C:\\Tools\\MyAiClient.exe",
  "Arguments": ["--packengine-jsonl"],
  "TimeoutSeconds": 180
}
```

에디터에서 `editor/Builds/Windows/Providers/PackEngine.Assistant.Command.dll`을 연결한다. 이 실행 파일은 아래 규약을 실제로 구현해야 한다. 기존 CLI의 일반 대화 모드에 인자만 붙여 사용할 수 있다고 가정하지 않는다. 별도 셸을 거치지 않으며 표준 출력은 한 줄당 JSON 메시지, 표준 오류는 진단 메시지다.

첫 표준 입력:

```json
{"type":"request","protocol":1,"request":{"Id":"...","Project":"golemancer","Prompt":"...","Selection":"view:golemancer.purchase","Context":[],"OpenFiles":[]}}
```

실제 Context 배열에는 포인팅한 정의·구간의 경로, 이유, Content, Partial/Draft/DiskChanged 값이 포함된다. 일반 대화에서는 비어 있다. 정의·범위의 `Hash`는 잘라내기 전 조각의 UTF-8 SHA256이고 `DocumentHash`는 전체 버퍼의 UTF-8 해시다. 기존 read 규약의 Hash는 전체 버퍼 해시다. 둘 모두 원본 파일의 바이트 해시와 구별한다. 요청의 `Input`은 전송 순간의 대상 집합, `Documents`는 내용이 포함되지 않은 열린 문서의 버전 목록이다. 이미 전달된 내용은 외부 클라이언트가 모델 입력으로 선택해 사용한다.

프로세스가 추가 읽기를 요청할 수 있다.

```json
{"type":"read","path":"Content/Packs/40.Commerce/actions.xml","maximumCharacters":16000}
```

에디터 응답은 `{"type":"read-result","item":{...ContextItem...}}`이다. 사용자가 열어 둔 버퍼가 있으면 그 내용, 아니면 선언된 디스크 문서를 읽는다. 미적용 초안과 외부 변경 여부가 포함된다. 프로젝트 외부·등록되지 않은 파일을 읽을 수 없으며 이 호출은 읽기 영수증을 만든다.

```json
{"type":"inspect","key":"view:golemancer.purchase"}
```

응답은 `{"type":"inspect-result","content":"최종 계약 및 출처 JSON 문자열"}`이다. 이 조회도 `contract:view:...` 경로의 읽기로 기록된다. 제공자가 조회한 문맥과 사용자가 연 문서를 구별할 수 있다.

마지막 출력:

```json
{"type":"reply","text":"사용자에게 보여줄 실제 모델 응답"}
```

도구 요청 실패는 `{"type":"error","message":"..."}`로 돌려준다. 제공자는 응답할 때까지 계속할 수 있지만 최대 128회 메시지, 한 메시지 2 MB, 설정된 실행 시간 제한을 따른다. 요청 취소나 종료 시 이 요청의 외부 프로세스를 끝낸다. 상태를 오래 유지하는 공유 서버를 직접 실행 파일로 사용하지 말고 요청별 클라이언트를 연결한다.

수정이나 명령 실행 메시지는 이 읽기 규약에 없다. 모델의 수정 제안은 문서 편집·미리보기·적용 흐름으로 검토한다. API 키 입력 UI, 특정 서비스의 로그인, 스트리밍 응답, 에디터 내부 자동 코드 적용은 Command 제공자에는 포함하지 않는다.

`tools/verify-editor.py`의 프로세스는 이 전송 경계를 확인하는 테스트용 클라이언트다. 실제 모델 응답을 생성하는 제품 기능으로 배포하지 않는다.
