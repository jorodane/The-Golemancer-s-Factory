# Confectory 멀티 작업자·충돌/격돌 시스템

## 1. 목표

Confectory의 편집 시스템을 단일 사용자 + 단일 AI 구조에서 벗어나 다음 작업자가 동시에 같은 게임팩을 편집할 수 있는 구조로 확장한다.

- 여러 Human User
- 여러 Codex/AI Worker
- Editor Pack
- 자동화 도구
- 기타 변경 생성 주체

모든 작업자는 동일한 `Participant` 계열로 취급한다.

핵심 원칙은 **작업 대상에 Lock을 걸지 않는 것**이다.

동시 작업은 자유롭게 허용하고, 완료된 변경이 다른 작업자의 현재 작업과 실제로 충돌할 때만 이를 감지하고 해결한다.

---

# 2. 기존 채팅 UI 제거

기존의 고정된 대화창은 기본 UI에서 제거한다.

각 AI는 에디터 공간 안에 존재하는 독립적인 캐릭터 객체로 표현한다.

```text
        ┌─────────────────────────┐
        │ 지금 RecipeService를   │
        │ 수정하고 있어.         │
        └─────────────────────────┘
                    ▲
                 Codex A
```

AI 캐릭터는 사용자가 마우스로 자유롭게 이동시킬 수 있다.

AI의 현재 발언은 머리 위 Chat Bubble로 표시한다.

사용자의 질문에 대한 답변인 경우 하나의 Turn 안에서 다음과 같이 표시한다.

```text
┌──────────────────────────┐
│ User                     │
│ 저장 API도 고쳐야 해?   │
├──────────────────────────┤
│ Codex A                  │
│ 응. 이번 변경에서는     │
│ 같이 수정해야 해.       │
└──────────────────────────┘
        ◀  7 / 18  ▶
```

좌우 이동을 통해 해당 AI의 이전/다음 대화 Turn을 확인할 수 있다.

---

# 3. AI별 전체 대화 로그

각 AI 캐릭터에는 별도의 `대화 로그` 버튼을 제공한다.

버튼을 누르면 독립 Window가 생성된다.

이 Window에서는 해당 AI와 주고받은 전체 대화를 기존 채팅 로그와 비슷한 형태로 볼 수 있다.

기본 작업 공간에는 긴 로그를 표시하지 않는다.

따라서 역할은 다음과 같이 분리된다.

```text
AI Character
→ 현재 상태와 최근 발화

Chat Bubble
→ 현재 Conversation Turn

Conversation Log Window
→ 전체 대화 이력
```

---

# 4. Exactly Yogi / Look At Yogi

`Exactly Yogi`와 `Look At Yogi`는 더 이상 현재 Chat에 자동으로 전달되는 전역 기능이 아니다.

먼저 대상 Participant를 선택한 뒤 사용한다.

```text
Selected:
Codex B

[Exactly Yogi]
[Look At Yogi]
```

Exactly Yogi는 선택된 객체의 구조적 정보와 메타데이터를 해당 AI에게 전달한다.

Look At Yogi는 선택된 요소 또는 사용자가 지정한 화면 영역의 시각 정보를 해당 AI에게 전달한다.

여러 AI가 존재하는 상황에서 누구에게 전달되는지 항상 명확해야 한다.

---

# 5. 작업 상태

각 AI는 독립적인 `WorkContext`를 가진다.

```text
WorkContext
├─ ParticipantId
├─ BaseRevision
├─ CurrentTask
├─ ReferenceSet
├─ WorkingChanges
├─ IncomingChanges
└─ FinalChangeSet
```

`ReferenceSet`은 단순히 열어본 파일 목록이 아니라 작업과의 관계를 기록한다.

```text
Read
Observe
Depend
ModifyIntent
```

단순 `Read`는 기본적으로 변경 전파 대상이 아니다.

`Depend`와 `ModifyIntent`가 주된 전파 대상이다.

이를 통해 공통 Core 파일을 한번 읽었다는 이유만으로 모든 AI에게 변경 알림이 폭주하는 것을 막는다.

---

# 6. 변경안 UI 개편

현재처럼 변경 전 파일 전체와 변경 후 파일 전체를 모두 보여주지 않는다.

기본적으로 **변경된 부분만 표시한다.**

```diff
Recipe/WaterJelly.xml

- <Heat>100</Heat>
+ <Heat>120</Heat>
```

필요할 경우 단계적으로 확장한다.

```text
[주변 내용 보기]
[파일 전체 보기]
[파일 열기]
```

일반 변경은 기존과 동일하게 체크박스로 선택한다.

```text
☑ Heat        100 → 120
☑ Price         8 → 10
☐ Icon          A → B
```

체크된 항목만 최종 적용한다.

---

# 7. ChangeSet

모든 변경은 `ChangeSet`으로 표현한다.

변경 주체가 User인지 Codex인지 구분하지 않는다.

```text
ChangeSet
├─ ChangeSetId
├─ Author
├─ BaseRevision
├─ Intent
├─ Operations[]
├─ ParentChanges[]
└─ ValidationResult
```

ChangeSet은 변경의 계보를 기록해야 한다.

이를 통해 다른 AI가 이미 받아들인 Resolution의 후손 ChangeSet을 다시 충돌로 오인하는 문제와 반복적인 재전파에 의한 Livelock을 막는다.

---

# 8. 변경 전파

AI A가 작업을 완료하고 ChangeSet을 생성하면 현재 관련 작업을 하고 있는 다른 Participant들에게 변경 내용을 전파한다.

```text
Codex A 완료
      │
      ├→ Codex B
      ├→ Codex C
      └→ User A
```

수신 Participant는 자신의 WorkContext와 비교하여 응답한다.

대표 응답은 다음 의미를 가진다.

```text
PASS
"상관없어."
현재 작업과 무관하며 그대로 통과 가능.

ADAPT
"내가 맞출게."
영향은 있지만 자신의 작업을 새 변경에 맞춰 수정 가능.

TAKEOVER
"내가 이어서 작업할게."
해당 Scope의 작업을 자신이 인계받을 것을 제안.

YIELD
"네 변경을 따를게."
자신의 해당 변경을 포기하고 상대 결과를 수용.

OBJECT
"이 상태로는 내 작업과 양립할 수 없어."
Resolution Session 필요.
```

PASS 또는 ADAPT만 발생한다면 사용자에게 별도의 충돌 해결을 요구하지 않는다.

---

# 9. 작업 인계

`내가 이어서 작업할게`는 전체 Task가 아니라 명확한 Scope를 대상으로 한다.

예:

```text
Codex A

RecipeEditor UI        → 계속 담당
RecipeRepository.Save  → Codex B에게 인계
Tooltip                → 계속 담당
```

Takeover는 작업 강탈이 되지 않도록 인계 요청과 수락 또는 Yield를 기반으로 처리한다.

인계 시 다음 정보도 함께 전달한다.

```text
Target
Intent
Current Changes
Remaining Work
Related ChangeSet
Required Context
```

---

# 10. 충돌과 격돌

두 개의 양립할 수 없는 결과가 동일한 변경 대상에 존재하면 UI에서는 `충돌`로 표시한다.

세 개 이상의 후보가 존재하면 `격돌`로 표시한다.

내부적으로는 동일한 Conflict 계열 데이터를 사용한다.

```text
ConflictSet
├─ Target
├─ BaseSnapshot
├─ Candidates[]
└─ Resolution
```

```text
Candidates == 2
→ 충돌

Candidates >= 3
→ 격돌
```

---

# 11. XML 충돌 단위

XML은 Text Line이 아니라 Element 단위로 비교한다.

예:

```xml
<Recipe id="WaterJelly">
    <Heat>100</Heat>
    <CraftTime>4</CraftTime>
</Recipe>
```

A가 Heat를 수정하고 B가 CraftTime을 수정했다면 충돌하지 않는다.

A와 B가 둘 다 Heat를 수정했다면 동일 Target으로 판단한다.

```text
Recipe:WaterJelly
└─ Heat
```

부모 Element 삭제와 하위 Element 수정 역시 충돌로 판단한다.

```text
A
Recipe:WaterJelly 삭제

B
Recipe:WaterJelly/Heat 변경

→ 충돌
```

Element 식별에는 가능한 한 안정적인 ID 또는 경로를 사용한다.

---

# 12. Code 충돌 단위

첫 버전에서는 지나치게 복잡한 AST 기반 충돌 분석을 요구하지 않는다.

Codex가 자신이 수정한 코드 영역을 기록하고 이를 Highlight한다.

충돌 가능성이 있는 경우 각 Participant에게 동일한 자료를 제공한다.

```text
Base Code

Candidate A
+ 변경 영역 Highlight
+ Intent

Candidate B
+ 변경 영역 Highlight
+ Intent

Validation
+ Compile
+ Test
+ Contract Check
```

기본적으로 변경된 부분과 주변 코드 일부만 제공한다.

추가 문맥이 필요한 AI는 필요한 함수, 클래스, 파일을 추가 요청할 수 있다.

따라서 Context는 필요할 때 점진적으로 확장한다.

---

# 13. 작업권 넘겨짐

충돌 가능성이 발견되었다고 해서 해당 AI의 작업을 바로 종료시키거나 사용자에게 해결을 떠넘기지 않는다.

관련 작업자들에게 `작업권 넘겨짐` 이벤트가 발생한다.

각 Participant는 먼저 PASS / ADAPT / TAKEOVER / YIELD / OBJECT 중 하나를 판단한다.

모두 PASS 또는 자율적으로 ADAPT한다면 그대로 최종 검토 단계로 넘어간다.

OBJECT가 존재할 때만 실제 Resolution Session을 생성한다.

---

# 14. 충돌 Window

충돌은 AI 캐릭터 위에 계속 표시하지 않는다.

새로운 독립 Window를 생성한다.

```text
┌────────── 충돌 #17 ──────────┐
│ RecipeService.FindRecipe     │
│                              │
│ Codex A       Codex B        │
│                              │
│ Base                         │
│ ...                          │
│                              │
│ A Change       B Change      │
│ ...            ...           │
│                              │
│ Resolution Chat              │
│ ...                          │
└──────────────────────────────┘
```

해결 후 Window는 기록 객체로 남는다.

기존 AI 로그에는 다음처럼 링크만 기록한다.

```text
[충돌 #17 발생]
[충돌 #17 해결]
```

링크를 선택하면 당시 Resolution Session을 다시 볼 수 있다.

---

# 15. 격돌 Window

세 명 이상이라면 같은 시스템을 `격돌`로 표현한다.

격돌은 참가자가 한 번씩 발언하는 Round 기반 대화를 사용한다.

```text
Round 1
A 발언
B 발언
C 발언

Round 2
A 발언
B 발언
C 발언
```

한 Participant가 연속해서 대화를 독점하지 않는다.

---

# 16. AI 전용 충돌/격돌 HP 시스템

Resolution Session에 Human Participant가 존재하지 않을 경우 AI들은 HP를 가진다.

예:

```text
Codex A  100 HP
Codex B  100 HP
Codex C  100 HP
```

HP는 말싸움의 인기 점수가 아니다.

현재 변경안을 유지할 수 있는 **논리적·기술적 근거의 잔존량**을 표현한다.

AI 자신이 Damage 값을 임의로 정하지 않는다.

충격량은 시스템 검증 결과를 기반으로 계산한다.

주요 근거는 다음과 같다.

- 사용자 명시 요구 위반
- 컴파일 실패
- 테스트 실패
- API 계약 위반
- 기존 확정 Resolution 위반
- 현재 ChangeSet과의 논리적 모순
- 상대가 제시한 대안으로 기존 변경 이유가 소멸
- 단순 스타일 또는 취향 차이는 낮거나 없는 Damage

---

# 17. Resolution Battle 종료 조건

AI 전용 Resolution은 무한 대화를 허용하지 않는다.

모든 AI는 최대 10 Turn을 사용할 수 있다.

AI의 HP가 0이 되면 해당 AI의 후보 변경안은 결정권을 잃는다.

모든 참가자가 최대 Turn을 사용했는데도 합의가 되지 않았다면 남은 HP가 가장 높은 Participant가 최종 결정권을 가진다.

```text
A 73 HP
B 41 HP
C 58 HP

→ A 결정
```

HP가 동일하다면 해당 충돌 또는 격돌을 발생시킨 **최초 Commit AI**를 우선한다.

```text
A 73 HP
C 73 HP

First Commit: C

→ C 결정
```

결정 후 나머지 Participant들은 승자의 최종 Resolution ChangeSet을 자신의 WorkContext에 반영하고 작업을 계속한다.

---

# 18. 합의는 승패보다 우선한다

HP가 존재하더라도 반드시 한 AI를 쓰러뜨릴 필요는 없다.

대화 도중 새로운 해결안에 모두 동의하면 즉시 Session을 종료한다.

예:

```text
A
FindRecipe를 IReadOnlyList로 변경

B
Preview에서 수정이 필요함

Resolution
FindRecipe → IReadOnlyList
PreviewRecipeView → 별도 Mutable 구조

A 동의
B 동의

→ Resolution 완료
```

이 경우 승자/패자를 기록하지 않는다.

---

# 19. Human Participant가 들어온 경우

사람이 실제 발언을 하여 Resolution Session에 참가하면 AI 전용 자동 승패 결정은 중지한다.

사람은 AI들과 같은 Resolution Chat에서 발언할 수 있다.

```text
User:
둘 다 별로야.
Preview 상태를 Repository에서 분리해.
```

이 발언은 단순 채팅이 아니라 Resolution Constraint로 기록할 수 있다.

```text
User Constraint
Preview state must not be persisted to Repository.
```

이 Constraint는 관련된 모든 Participant의 WorkContext에 전파한다.

관전만 하는 Human은 자동 Resolution을 방해하지 않는다.

실제 발언하거나 명시적으로 `참여`한 시점에 Resolution Participant가 된다.

---

# 20. Human Multiplayer

같은 구조를 실제 멀티플레이 에디터에도 사용한다.

```text
Human A ↔ Human B

Human A ↔ Codex A

Codex A ↔ Codex B

Human A ↔ Human B ↔ Codex A ↔ Codex B
```

모두 동일한 ResolutionSession 시스템을 사용한다.

사람과 AI를 위해 별도의 충돌 시스템을 만들지 않는다.

---

# 21. 권한 분리

멀티 환경에서는 세 가지 권한을 구분한다.

```text
Talk Permission
Resolution Session에서 발언 가능

Work Permission
작업 생성 및 인계 가능

Apply Permission
최종 ChangeSet 적용 가능
```

관전자도 경우에 따라 대화에 참가할 수 있지만 실제 게임팩 적용 권한은 없을 수 있다.

---

# 22. AI의 PASS를 맹신하지 않는다

AI가 `상관없어`라고 답해도 가능한 경우 기계적인 검증을 함께 수행한다.

```text
AI 판단
PASS

Compile
PASS

Tests
PASS

Contract Validation
PASS

→ 통과
```

반대로:

```text
AI 판단
PASS

Compile
FAIL

→ PASS 취소
→ ADAPT 또는 OBJECT 재평가
```

AI 판단과 정적/동적 검증을 함께 사용한다.

---

# 23. Livelock 방지

Lock을 제거했기 때문에 Deadlock은 구조적으로 피할 수 있지만 변경을 서로 계속 받아 수정하는 Livelock은 별도로 방지해야 한다.

모든 ChangeSet과 Resolution에는 계보를 기록한다.

```text
ChangeSet #201
Parent #187
Resolution #14
```

이미 수용한 Resolution의 후손 ChangeSet은 동일한 문제를 새로운 충돌로 다시 발생시키지 않는다.

같은 변경 계보가 순환하지 않도록 Origin과 Parent 관계를 추적한다.

---

# 24. Resolution 결과

Resolution Session 종료 시 반드시 구조화된 결과를 만든다.

```text
Resolution #17

Participants
Codex A
Codex B

Target
RecipeService.FindRecipe

Decision
FindRecipe → IReadOnlyList
PreviewRecipeView 추가

Resulting ChangeSet
#214
```

Resulting ChangeSet은 관련된 모든 Participant에게 전달된다.

각 AI는 자신의 WorkingChanges를 이 결과에 맞춰 Rebase하고 기존 작업을 계속한다.

---

# 25. 작업과 결정의 역사

Confectory에서는 파일 변경 기록뿐 아니라 **결정 기록**도 보존한다.

어떤 코드를 선택하면 다음처럼 추적할 수 있다.

```text
RecipeService.FindRecipe
↓
Revision 284
↓
Resolution #17
↓
Codex A ↔ Codex B 충돌
↓
[당시 대화 보기]
```

따라서 향후 단순한 Git Blame을 넘어:

```text
누가 바꿨는가
왜 바꿨는가
무엇과 충돌했는가
어떤 대안을 검토했는가
어떤 이유로 현재 결과가 선택됐는가
```

까지 확인할 수 있다.

---

# 26. 최종 작업 흐름

```text
Participant 작업 시작
        ↓
WorkContext 생성
        ↓
자유롭게 병렬 작업
        ↓
ChangeSet 생성
        ↓
관련 Participant에게 전파
        ↓
┌────────────────────────────┐
│ PASS     → 그대로 진행    │
│ ADAPT    → 자체 Rebase    │
│ TAKEOVER → 작업 인계      │
│ YIELD    → 상대 변경 수용 │
│ OBJECT   → Resolution     │
└────────────────────────────┘
        ↓
필요 시
충돌 / 격돌 Window
        ↓
AI 전용이면
HP + 최대 10 Turn
        ↓
합의 또는 승자 결정
        ↓
Resolution ChangeSet 생성
        ↓
모든 관련 WorkContext에 재전파
        ↓
작업 계속
        ↓
최종 변경안 검토
        ↓
체크된 변경 적용
```

---

# 27. 구현 우선순위

첫 구현에서는 지나치게 지능적인 충돌 분석보다 **프로토콜과 데이터 구조가 실제로 순환하는 것**을 우선한다.

1차 목표는 다음이다.

`Participant / AI Character UI → WorkContext → ChangeSet → 변경 부분만 표시하는 Review UI → 변경 전파 → PASS/ADAPT/OBJECT → XML Element 충돌 → Code Highlight 충돌 → Conflict Window → Resolution Log`

그 다음 단계에서 다음을 확장한다.

`TAKEOVER/YIELD → Change Lineage → HP Resolution Battle → 격돌 Round → Human Multiplayer → Semantic Code Conflict 고도화`

최초 버전부터 Lock 기반 구조는 도입하지 않는다.

전체 시스템은 **낙관적 병렬 편집 + 변경 전파 + 사후 Resolution**을 기본 원리로 한다.