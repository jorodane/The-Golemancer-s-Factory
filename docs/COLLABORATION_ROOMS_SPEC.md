# Confectory AI 멀티플레이 편집 모델 추가 설계안

이 문서는 기존의 `Participant / WorkContext / ChangeSet / 충돌·격돌 / Resolution` 설계 이후 추가로 확정된 방향을 정리한다.

핵심 변화는 다음 한 문장으로 요약할 수 있다.

> AI를 별도의 코딩 도구가 아니라, 실제 멀티플레이 에디터에 접속한 Participant로 취급한다.

따라서 Human Multiplayer와 AI Collaboration을 별개의 시스템으로 만들지 않는다.

---

# 1. AI가 존재하는 순간 이미 멀티플레이 상태다

기존 관점:

```text
Single User Editor
+ AI Assistant
+ Multiplayer
```

변경된 관점:

```text
Multiplayer Editor

Participant
├─ Human
├─ AI
└─ Tool
```

AI 역시 프로젝트의 공유 상태를 읽고, 편집하고, 다른 Participant의 변경에 영향을 받고, 대화하고, 충돌하거나 작업을 인계할 수 있다.

따라서 Human Multiplayer는 나중에 별도로 추가되는 시스템이 아니라 기존 Participant 모델에 Human Participant가 추가되는 형태로 구현한다.

---

# 2. 파일은 편집 대상이면서 동시에 Room이다

현재 열려 있는 파일은 하나의 협업 Room으로 취급한다.

예:

```text
Room: RecipeService.cs

Participants
👤 User A
👤 User B
🤖 Codex A
🤖 Claude B

Room Chat
Structured Document
```

같은 파일을 보고 있는 Human과 AI는 모두 같은 Room Participant다.

Room에서는 다음 정보를 공유한다.

- 현재 입장한 Participant
- 현재 작업 위치
- 작업 의도
- Room Chat
- 실시간 문서 상태
- Participant별 Draft 상태

---

# 3. AI 참조 상태는 Presence가 된다

기존에 제공하던:

```text
AI가 현재 참조 중인 파일
```

정보는 이제 단순 디버깅 정보가 아니다.

이를 AI의 현재 위치로 취급한다.

예:

```text
🤖 Codex A
현재 위치:
Inventory.cs

작업:
CalculateWeight()
```

AI가 다른 파일을 참조하기 시작하면 실제로 다른 Room으로 이동한 것으로 표현할 수 있다.

```text
Codex A가 Inventory.cs에서 나감
Codex A가 Character.cs에 들어옴
```

AI가 사용자가 현재 열어놓은 Room에 들어온 경우 짧은 입장 발화를 할 수도 있다.

예:

```text
🤖 Codex A

"안녕. 이 파일 좀 쓸게.
아이템이 37개이고 무게가 정확히 3.7kg이면
캐릭터가 폭발하는 기능 작업 중이야."
```

이 발화는 장식이 아니라 다음 정보를 자연어로 전달한다.

- 왜 이 Room에 왔는가
- 무엇을 수정할 예정인가
- 현재 사용자 작업과 겹칠 가능성이 있는가

---

# 4. 문서는 실시간, AI 사고는 이벤트 기반

AI를 사람처럼 Character 단위의 실시간 타이핑 Agent로 구현하지 않는다.

두 주기를 분리한다.

```text
Document Update
→ 실시간

AI Reasoning
→ 필요 시점에만
```

Shared Document는 항상 최신 상태를 유지한다.

AI의 추론은 다음과 같은 경우에만 새로 발생한다.

- 현재 작업 Scope가 변경됨
- 의존 대상이 의미 있게 변경됨
- 현재 Target이 삭제 또는 교체됨
- Semantic Unit 작업 완료
- Checkpoint 도달
- 충돌 가능성 발생
- 사용자 직접 호출

즉 문서의 모든 Key Stroke마다 AI를 호출하지 않는다.

---

# 5. Raw Edit Event를 AI에게 그대로 전달하지 않는다

사람이 다음 편집을 했다고 가정한다.

```text
10줄 작성
3줄 삭제
함수 이름 변경
다시 변경
```

AI에게 Key Stroke나 Text Operation 전체를 보내지 않는다.

에디터가 먼저 변경을 정규화한다.

```text
Raw Operations
↓
Coalescing
↓
Parsing
↓
Semantic Change
```

예:

```text
FindRecipe
→ TryFindRecipe
```

또는:

```text
RecipeService.FindRecipe implementation replaced
```

AI는 이러한 의미 단위 변경만 받는다.

---

# 6. 대규모 붙여넣기와 파괴적 편집

대규모 Paste, Block Delete, File Replace는 일반 입력과 다르게 처리한다.

예:

```text
Delete 40 lines
+ Paste 55 lines
```

가 짧은 시간 안에 발생하면:

```text
ReplaceBlock
```

또는 Parser 분석 후:

```text
Replace Method
FindRecipe
```

로 정규화한다.

붙여넣기 중간의 불완전한 코드는 AI 판단 대상으로 만들지 않는다.

AI는 편집 Transaction이 종료된 후 완성된 결과만 받는다.

---

# 7. AI 편집의 기본 단위는 완성된 Semantic Unit

AI는 한 글자씩 Shared Document를 수정하지 않는다.

자신의 Draft에서 하나의 완성된 의미 단위를 만든 뒤 적용한다.

대표 단위:

```text
Method
Constructor
Property
Field
Event
Type
Interface Member
XML Element
```

이를 `Semantic Edit Unit`으로 취급한다.

예:

```text
RecipeService.FindRecipe

Current
↓
AI Working Draft
↓
Validation
↓
Atomic Apply
```

Shared Document에는 가능한 한 항상 완성된 코드 상태만 존재하도록 한다.

---

# 8. 여러 Semantic Unit이 필요한 작업은 Transaction으로 묶는다

하나의 변경이 여러 Method에 걸치는 경우:

```text
Transaction #51

1. IRecipeService.FindRecipe
2. RecipeService.FindRecipe
3. RecipeEditor.LoadRecipe
4. RecipePreview.Refresh
5. RecipeServiceTests.FindRecipe
```

등으로 여러 Semantic Unit을 하나의 작업 Transaction에 포함할 수 있다.

중간 상태가 컴파일을 깨는 변경은:

```text
Prepare All
↓
Validate Combined Result
↓
Atomic Apply
```

를 사용할 수 있다.

서로 독립적인 변경이라면 Unit별 적용도 허용한다.

---

# 9. C# 에디터를 구조형 에디터로 확장한다

기존 XML 구조형 편집 개념을 C#에도 적용한다.

사용자는 기본적으로 `.cs` 파일 전체 Text를 직접 보는 대신 구조를 본다.

예:

```text
RecipeService

Fields
  _recipes
  _cache

Properties
  Count

Methods
  FindRecipe(...)
  SaveRecipe(...)
  RemoveRecipe(...)
```

Method를 클릭하면 해당 Method 편집 화면으로 이동한다.

예:

```text
FindRecipe

Signature
- Access
- Return Type
- Name
- Parameters

Body
- Function Body
```

Field 역시 독립 편집 대상이다.

```text
Field Editor

Name
Type
Access
Modifiers
Initializer
```

---

# 10. 구조형 편집창은 UI가 아니라 WorkScope 정의다

함수나 변수 단위 편집창을 만든 이유는 단순 가독성이 아니다.

현재 열려 있는 편집창 자체가 작업 Scope가 된다.

예:

```text
👤 User
Editing:
RecipeService.FindRecipe

🤖 Codex A
Editing:
RecipeService.SaveRecipe
```

이 경우 같은 파일 Room에 있어도 서로 작업 범위가 겹치지 않는다.

반대로:

```text
👤 User
Editing:
FindRecipe.Body

🤖 Codex A
Editing:
FindRecipe.Body
```

라면 Direct Overlap 상태다.

---

# 11. Overlap과 Dependency를 구분한다

두 작업자의 편집 범위가 직접 겹치는 것과, 서로 영향을 주는 것은 다르다.

예:

```text
User
FindRecipe.Body

Codex
FindRecipe.ReturnType
```

직접 동일 Scope는 아니지만 Return Type 변경은 Body에 영향을 줄 수 있다.

따라서 다음을 구분한다.

```text
Overlap
→ 동일 Semantic Scope를 함께 편집

Dependency Impact
→ 다른 Scope이지만 결과가 서로 영향을 줌
```

Overlap은 Draft/Handoff 결정에 사용한다.

Dependency Impact는 Change Propagation 및 ADAPT 판단에 사용한다.

---

# 12. 같은 Room의 AI 변경은 기본적으로 충돌이 아니다

AI가 같은 Shared Document를 보고 작업하고 있고 다른 Participant와 편집 Scope가 겹치지 않는다면 AI의 완료된 Semantic Unit은 일반 변경으로 적용한다.

예:

```text
👤 User
SortSlots()

🤖 Codex
CalculateWeight()
```

Codex가 CalculateWeight를 완료하면:

```text
Validate
↓
Codex 자신의 Change만 확정
↓
Room 퇴장 가능
```

사용자의 SortSlots Draft에는 영향을 주지 않는다.

---

# 13. AI의 Room 퇴장 규칙

AI가 Room을 떠날 때 `Room Exit = Confirm`으로 처리하지 않는다.

퇴장과 확정은 별개의 개념이다.

다만 AI 자신의 변경이 다른 Active Draft와 겹치지 않는 경우에는 자신의 변경만 자동 확정하고 나갈 수 있다.

```text
AI 변경 완료
↓
다른 Active Draft와 Non-overlap
↓
Validation
↓
AI Change Confirm
↓
Exit Room
```

예:

```text
"여긴 끝났어. 난 다른 데 볼게."
```

---

# 14. 사용자 작업과 겹쳤다면 Draft를 남기고 떠난다

AI의 작업이 User 또는 다른 Participant의 현재 Active Draft와 겹쳤다면 자동 확정하지 않는다.

예:

```text
FindRecipe

👤 User Draft
🤖 Codex Draft
```

AI가 떠날 경우:

```text
"여기까지 해뒀어.
네 작업이랑 겹쳐서 확정은 안 했어.
뒤는 맡길게."
```

상태:

```text
Codex Draft
→ Preserved

Commit
→ Not Published

WorkScope
→ Yield / Handoff
```

이 경우 즉시 Conflict를 만들지 않는다.

AI가 자신의 결정권을 내려놓고 Draft를 인계했으므로 `YIELD + HANDOFF`에 가깝다.

---

# 15. 저장과 확정은 계속 분리한다

기존에 결정한 두 단계는 유지한다.

```text
Working Copy
↓
저장
↓
Local / Room Draft
↓
확정
↓
Published ChangeSet
```

`저장`은 작업 상태 보존이다.

- 외부 AI에게 전파하지 않음
- Revision 생성 안 함
- 충돌 판정 안 함

`확정`은 프로젝트 세계에 변경을 Publish하는 행위다.

확정 순간 다른 WorkContext와 비교하여 필요하면 충돌/격돌이 발생한다.

---

# 16. Room 내부에서는 실시간 협업한다

Human끼리 같은 Room에 있다면 동일한 Shared Working Copy를 실시간으로 편집할 수 있다.

AI 역시 동일 Room Participant가 될 수 있다.

차이는 편집 방식뿐이다.

```text
Human
→ Text / Selection 기반 실시간 편집

AI
→ Semantic Unit Draft
→ 완성 후 Apply
```

둘 다 같은 Shared Document에 참여한다.

---

# 17. Participant List Window 추가

멀티 Human + Multi AI 환경에서는 별도의 `참여자 리스트` Window가 필요하다.

예:

```text
Participants

Humans
👤 User A
👤 User B
   └ Inventory.cs

AI
🤖 Codex A
   └ RecipeService.cs
   └ SaveRecipe() 작업 중

🤖 Claude
   └ Character.cs
   └ 읽는 중

[ + AI 추가 ]
```

이 Window는 단순 접속자 목록 이상의 역할을 가진다.

- 현재 접속자
- Human / AI 구분
- AI 개수
- 현재 위치
- 현재 작업
- 상태
- AI 추가
- AI 표시/숨김
- 읽지 않은 대화 표시
- Participant 추적

---

# 18. Project Presence와 Room Presence를 구분한다

Participant가 프로젝트에 접속해 있다는 것과 현재 Room에 있다는 것은 다르다.

예:

```text
현재 Room
👤 User
🤖 Codex A

다른 Room
👤 User B → Character.cs
🤖 Claude → RecipeService.cs
```

현재 Room Participant는 별도로 강조 표시한다.

---

# 19. AI Character 표시 상태

AI Character는 화면 점유가 크므로 Participant List에서 표시 여부를 제어한다.

최소 상태:

```text
Full
Compact
Hidden
```

### Full

- Character
- Name
- Chat Bubble
- Interaction UI

### Compact

- 작은 Character 또는 Name Tag만 표시

### Hidden

- 작업 공간에서 완전히 숨김
- Participant List에는 계속 표시

숨긴다고 AI 작업이 중단되거나 Room에서 나가는 것은 아니다.

표시 여부는 각 사용자의 로컬 UI 설정이다.

다른 사용자의 화면에는 영향을 주지 않는다.

---

# 20. 숨겨진 AI의 응답 알림

숨겨진 AI가 답변하면 Participant List에 Unread 상태를 표시한다.

예:

```text
🤖 Codex A   🔵 2
RecipeService.cs

"저장 쪽 수정은 끝났어. 그런데..."
```

또한 짧은 Toast Preview를 표시할 수 있다.

```text
Codex A
"저장 쪽 수정은 끝났어. 그런데 Revision..."
[보기]
```

사용자가 `보기` 또는 Participant 항목을 누르면:

```text
Hidden
→ Full
```

로 전환하고 Character가 마지막 응답을 Chat Bubble로 보여준다.

사용자가 내용을 확인하면 파란 점이 제거된다.

---

# 21. AI Character가 보이더라도 Read 여부는 별도로 관리

Character가 화면에 존재한다고 해서 자동으로 읽은 것으로 처리하지 않는다.

가능하면 다음 정도를 구분한다.

```text
Visible
→ 반드시 Read는 아님

Bubble Focus / Click / Explicit Open
→ Read
```

읽지 않은 중요한 메시지를 놓치지 않도록 한다.

---

# 22. AI 알림 중요도

AI 메시지는 종류에 따라 표시 강도를 다르게 할 수 있다.

```text
🔵 일반 새 답변
✓ 작업 완료
🟠 사용자 응답 필요
⚠ 충돌/격돌
```

Participant List만 봐도 어떤 AI를 확인해야 하는지 알 수 있어야 한다.

---

# 23. 전체 Project Chat 추가

프로젝트 전체 Participant가 참여하는 `Project Chat`을 제공한다.

기본 목적은 Human Participant끼리의 일반 대화다.

AI는 기본적으로 조용히 있으며 `@Mention`될 때 반응한다.

예:

```text
User:
@Codex 지금 뭐 하고 있는지 설명해줄래?
@Claude가 방금 들어와서 맥락을 아직 못 잡은 것 같아.

Codex:
지금 Recipe 저장 경로를 수정 중이야.
...
```

---

# 24. Room Chat

각 File Room은 별도의 Room Chat을 가진다.

현재 해당 파일을 보고 있는 Participant끼리 현장 대화를 할 수 있다.

예:

```text
RecipeService.cs Room Chat

User:
여기 반환형 바꾸려고.

Codex:
나도 이 함수 호출부 작업 중이야.
반환형 확정되면 맞춰갈게.
```

---

# 25. 별도 1:1 Chat Window는 만들지 않는다

AI와 개인 대화를 위한 별도 DM Window는 필요하지 않다.

AI Character에 직접 말 거는 것이 이미 1:1 대화다.

채팅 구조는 다음 정도로 유지한다.

```text
Project Chat
Room Chat
AI Character Direct Interaction
Resolution Chat
```

AI의 긴 개인 대화 이력은 기존에 결정한 `AI별 대화 로그 Window`에서 확인한다.

---

# 26. @Mention은 원거리 호출 수단이다

AI가 현재 화면에 보이면 Character를 직접 선택하여 대화한다.

AI가 다른 Room에 있거나 Hidden 상태라면 Project Chat 또는 Room Chat에서 `@AI`를 사용한다.

예:

```text
@Codex 지금 뭐 하는 중이야?

@Claude 여기 잠깐 봐줄래?

@Codex 네 작업 상황을 @Claude한테 설명해줘.
```

`@Mention`은 단순 이름 표시가 아니라 해당 Participant를 현재 Conversation Context에 호출하는 기능으로 사용한다.

---

# 27. AI끼리의 Context Handoff

Human이 다음과 같이 요청할 수 있다.

```text
@Codex @Claude한테 지금 상황 설명해줘.
```

이때 긴 대화 전체를 재전송하지 않는다.

Codex가 압축된 작업 상태를 전달한다.

예:

```text
Handoff Summary

Current Goal
Recipe 저장 시스템을 ChangeSet 기반으로 변경

Completed
- 직접 저장 제거
- BuildSaveProposal 추가

Working
- Validation 연결

Constraint
Revision은 Apply 시점에만 생성
```

이를 통해 AI 추가 시 Context 비용을 줄인다.

---

# 28. AI의 Project Chat 자발 발언 제한

AI들이 모든 Human 메시지에 반응하지 않는다.

기본 규칙:

```text
Human 메시지
→ Human에게 항상 표시

AI
→ @Mention 시 응답

또는
→ 자신의 작업과 직접 관련된 중요 Event일 때만 짧은 알림
```

AI 여러 개가 동시에 불필요한 확인 응답을 남기는 것을 방지한다.

---

# 29. AI-to-AI 무한 대화 방지

AI가 다른 AI를 계속 @Mention하여 무한 대화를 만드는 것을 막는다.

AI↔AI 대화에는 Task 또는 Conversation Thread ID를 부여한다.

일정 횟수를 넘으면 다음 중 하나로 수렴시킨다.

```text
Handoff
Resolution Session
User Request
End
```

깊은 논쟁은 일반 Project Chat에서 지속하지 않고 Conflict / Clash Resolution으로 승격한다.

---

# 30. AI Ownership

Human Multiplayer 환경에서는 AI마다 소유자가 존재할 수 있다.

예:

```text
👤 User A
└─ 🤖 Codex A

👤 User B
└─ 🤖 Claude B
```

AI Participant와 AI Ownership은 별도 개념이다.

다른 사람의 AI 역시 같은 프로젝트에 존재하는 Participant지만 내 작업자는 아니다.

---

# 31. 다른 사람 소유 AI의 기본 권한

다른 사람 소유 AI는 Presence가 보인다.

볼 수 있는 정보:

- 현재 Room
- 현재 작업 상태
- 공개 가능한 작업 목적
- Conflict / Resolution 참여 상태

허용:

```text
Presence 보기
공개 상태 보기
Project / Room Chat에서 @Mention
Conflict / Clash Resolution에서 대화
내 화면에서 숨김
```

기본적으로 금지:

```text
직접 Task 할당
작업 중단
Room 이동 명령
Exactly Yogi
Look At Yogi
개인 Conversation Log 열람
자동 확정 정책 변경
강제 Handoff
AI 삭제
```

---

# 32. 다른 사람 AI와의 @Mention

다른 사람의 AI에도 공개 Chat에서 말을 걸 수 있다.

예:

```text
User A:
@Claude-B SaveRecipe도 작업 중이야?

Claude-B:
응. User B가 요청한 저장 구조 변경 때문에
SaveRecipe와 BuildChangeSet을 보고 있어.
```

AI는 자신의 Owner가 가진 비공개 개인 대화나 비공개 Context를 노출하지 않는다.

응답 범위는 공유 프로젝트 상태와 공개 작업 정보로 제한한다.

---

# 33. 다른 사람 AI의 Character 표시

다른 사용자 소유 AI도 같은 Room에 있다면 Character Presence를 표시할 수 있다.

다만 클릭 시 가능한 Interaction이 다르다.

### 내 AI

```text
대화
Exactly Yogi
Look At Yogi
작업 보기
대화 로그
중단
표시/숨김
```

### 다른 사람 AI

```text
Owner 표시
공개 작업 상태
@Mention
내 화면에서 표시/숨김
```

소유권을 UI에서도 명확히 표시한다.

---

# 34. 다른 사람 AI를 숨기는 것은 허용

다른 사람 AI Character를 내 화면에서 숨기는 것은 AI 제어가 아니라 로컬 UI 설정이다.

따라서 허용한다.

```text
Claude-B
Full / Compact / Hidden
```

이 변경은 상대방 화면과 AI의 실제 작업에 영향을 주지 않는다.

---

# 35. Conflict / Clash에서는 Ownership보다 Resolution을 우선한다

예:

```text
내 Codex
vs
다른 User의 Claude
```

가 충돌했다면 둘 다 동일한 Resolution Session에서 필요한 범위의 대화를 할 수 있어야 한다.

다만 Resolution 참가 권한이 생긴다고 해서 상대 AI에 대한 일반 제어권이 생기지는 않는다.

```text
Resolution Conversation
→ 허용

Task Control
→ Owner 전용
```

---

# 36. 구조적 핵심 모델

이번 추가 설계까지 포함하면 다음 대응관계가 성립한다.

```text
Project
= Shared World

File
= Room

Participant
= Human / AI / Tool

AI Reference
= Presence / Location

Member / XML Element
= Semantic WorkScope

AI Draft
= 완성 전 Semantic Edit Unit

Room Save
= 작업 상태 보존

Confirm
= ChangeSet Publish

Project Chat
= 전체 Communication

Room Chat
= 현장 Communication

AI Character
= Direct Interaction

Participant List
= Presence + AI Management + Notification Hub

Conflict / Clash
= Resolution Space
```

---

# 37. 구현에서 특히 유지해야 할 원칙

### 1. AI와 Human은 같은 Participant 시스템을 사용한다

AI 전용 멀티 구조와 Human 전용 멀티 구조를 따로 만들지 않는다.

### 2. 화면 표시 상태와 실제 작업 상태를 분리한다

AI Character를 숨겨도 AI는 계속 존재하고 작업한다.

### 3. 파일 Text 전체를 기본 작업 단위로 삼지 않는다

가능한 경우 C# Member와 XML Element 등 Semantic Unit을 작업 단위로 사용한다.

### 4. Human은 실시간 Text 편집, AI는 완성된 Semantic Unit 편집을 기본으로 한다

같은 Shared Document를 사용하지만 행동 주기는 달라도 된다.

### 5. Room Exit와 Confirm을 동일시하지 않는다

AI 자신의 Non-overlap 변경만 안전하게 자동 Confirm할 수 있다.

### 6. Overlap 상태에서는 Draft를 남기고 Handoff할 수 있다

무조건 Conflict로 승격하지 않는다.

### 7. AI 메시지는 Hidden 상태에서도 절대 소실되지 않는다

Participant List의 Unread 상태를 통해 확인 가능해야 한다.

### 8. 다른 User의 AI는 보이지만 소유권 경계를 유지한다

말을 걸 수는 있어도 직접 조종할 수는 없다.

---

# 38. 이번 변경의 가장 큰 의미

기존에는:

```text
Editor
+ AI Chat
+ Multiplayer
```

를 생각했다.

현재 설계는:

```text
Shared Development World
├─ Human Participants
├─ AI Participants
├─ File Rooms
├─ Structured WorkScopes
├─ Conversations
└─ Published Changes
```

에 가까워졌다.

AI는 외부에서 소스코드를 수정해서 Patch를 보내는 존재가 아니라 프로젝트 내부에 실제로 접속하여 다른 Participant와 같은 작업 공간을 사용하는 존재다.

다만 AI 특성에 맞게:

```text
사람
→ 실시간 Text Editing

AI
→ Semantic Draft + Atomic Apply
```

라는 서로 다른 입력 방식을 사용한다.

따라서 AI 기반 에디터와 멀티플레이 에디터는 더 이상 별개의 기능이 아니다.

**AI가 들어온 순간 이미 멀티플레이가 시작된다.**