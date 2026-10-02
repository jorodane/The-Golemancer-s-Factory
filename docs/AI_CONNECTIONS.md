# 에디터 AI와 대화 AI

Project Studio는 게임이 없는 상태에서도 실행한다. 처음에는 AI를 준비하고, 이후 원하는 게임팩 또는 에디터팩을 선택한다. AI 없이 팩을 편집할 수도 있다. 연결 설정은 기기별 사용자 설정이며 게임팩·팩 ZIP·AI 문맥에 포함하지 않는다. 이미 설정한 연결은 재사용하며, 해제한 AI를 팩 열기나 질문 전송으로 임의 재연결하지 않는다.

## 메뉴와 역할

| 메뉴 | Windows | Android |
|---|---|---|
| 에디터 AI | Codex, Claude API, OpenAI API, 외부 `IEditorAssistant` DLL | Claude API, OpenAI API |
| 대화 AI | ChatGPT 웹, Claude 웹, 사용자가 지정한 HTTPS 웹 AI | 같은 웹 AI 선택, 내장 WebView 또는 외부 브라우저 |
| 팩 열기 | `.packproject`, 새 게임팩, 에디터팩 선택 | 설치된 에디터팩 선택, Windows에서 내보낸 팩 ZIP 가져오기 |

에디터 AI는 실제 질문과 명시적으로 지정한 문맥을 받아 공통 에디터 도구로 작업한다. 대화 AI는 해당 서비스의 웹 화면이다. 웹 로그인만으로 에디터 파일 읽기·수정 권한이나 API 사용 권한을 부여하지 않는다. 두 AI는 다른 제공자를 사용해도 된다.

위쪽 메뉴의 **연결 · 제공자 전환**으로 선택을 바꾸고 **연결 해제**로 해당 역할을 끈다. 에디터 AI를 해제하면 실행 제공자를 종료하고 그 역할의 저장된 API 키를 삭제한다. 대화 AI를 해제하면 웹 페이지와 작업 연결을 닫는다. 웹 로그인 프로필은 유지하므로 서비스 계정 로그아웃은 서비스 화면에서 수행한다. 어느 역할을 해제해도 다른 역할의 설정은 유지한다.

## Codex

Windows에서 기존 네이티브 Codex 설치와 로그인을 재사용한다. 새 설치가 필요하면 위치와 설치 동의를 확인한 뒤 공식 CLI를 준비한다. Node.js/npm이 없다면 설치 안내 후 재시도한다. 에디터 시작 프로그램이 게임팩 설정을 읽어 Codex부터 임의로 설치하지 않는다. 첫 연결에서 로그인이 필요하면 기존 Codex 로그인 화면으로 넘긴다.

연결 설정은 팩과 별개이며, 다른 팩을 열면 같은 설치·계정으로 그 팩에 바인딩한다. 팩별 대화 접근 설정과 도구 범위는 유지한다. Codex의 상세 기록·도구 프로토콜은 [RESIDENT_AGENT.md](RESIDENT_AGENT.md)을 따른다.

## Claude/OpenAI API

1. 에디터 AI 메뉴에서 제공자를 선택한다.
2. 해당 API 계정의 키를 넣는다. 같은 제공자의 저장된 키를 재사용하려면 비워 둔다.
3. API 전송과 과금을 확인한 뒤 **API 모델 목록 확인**으로 계정에서 사용할 수 있는 모델을 가져온다. 모델 ID를 직접 입력할 수도 있다.
4. 모델을 선택하고 **연결 확인 · 사용**을 누른다. 모델 목록과 인증 확인에 성공해야 연결을 적용한다. 실패하거나 취소하면 기존 연결을 보존한다.

Claude 웹/Claude Code 로그인과 Anthropic API 키, ChatGPT 구독 로그인과 OpenAI API 키는 각각 별개다. API 키는 Windows에서 현재 사용자의 DPAPI, Android에서 Android Keystore AES/GCM으로 암호화한다. API 키는 일반 연결 JSON, 요청 본문, 프로젝트 설정, 팩 내보내기, 실행 기록에 기록하지 않는다.

전송 대상은 선택한 공식 API로 고정한다. Claude는 `https://api.anthropic.com/v1/messages`, OpenAI는 `https://api.openai.com/v1/chat/completions`를 사용한다. 연결 확인은 `/v1/models`를 조회하며 모델 추론이나 팩 전송을 수행하지 않는다. 인증을 포함한 요청의 리디렉션은 따라가지 않는다. 오류는 HTTP 상태와 조치를 표시하며 서비스의 원문 오류 본문은 로그에 남기지 않는다.

Claude의 `tools`/`tool_use`/`user` 역할 `tool_result`와 OpenAI의 함수 도구/`tool_calls`/`tool` 역할 메시지는 어댑터에서 변환한다. 여러 호출을 한 응답에서 받더라도 호스트 도구는 순서대로 실행한다. 요청당 도구 호출 수·반복 수·응답 크기·대기 시간을 제한하고 취소를 전달한다. 현재 API 어댑터는 텍스트와 객체 문맥을 지원한다. API 대화는 현재 창에서만 이어가며 새 대화·팩 전환·연결 해제로 이전 문맥을 분리한다.

문서 읽기, 버전 확인, 임시 변경안, 실행 제안은 Codex와 같은 공통 도구를 사용한다. 변경은 요청의 검토용 문맥에 모이고 실제 파일은 사람이 선택해서 적용한다. 모델 응답을 받은 사실과 변경을 적용·빌드한 결과를 구별한다. Android에서는 C# 빌드 요청을 거부하고 Windows 빌드/ZIP 가져오기를 안내한다.

## 웹 AI

서비스와 웹 주소를 확인하면 내장 웹 패널을 연다. 같은 기기의 웹 로그인 프로필을 재사용한다. 서비스가 내장 브라우저를 지원하지 않으면 외부 브라우저로 이동한다. 로그인 완료 여부를 추측해서 연결 성공으로 표시하지 않는다.

ChatGPT 웹의 추가 메타데이터 등록과 작업 공유는 기존 [CHATGPT_BRIDGE.md](CHATGPT_BRIDGE.md), [SHARED_EDITOR.md](SHARED_EDITOR.md)을 따른다. 선택한 에디터 AI가 작업을 수행하지만 ChatGPT 웹 승인과 요청별 검토는 별도로 필요하다. Claude·다른 웹 AI에는 ChatGPT 전용 등록이나 입력창 첨부 코드를 적용하지 않는다. 웹 대화의 본문이나 로그인 쿠키를 가져와 API 대화에 자동으로 전달하지 않는다.

공식 API 규격: [Claude Messages](https://platform.claude.com/docs/en/api/messages/create), [Claude Models](https://platform.claude.com/docs/en/api/models/list), [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create), [OpenAI Models](https://developers.openai.com/api/reference/resources/models/methods/list).
