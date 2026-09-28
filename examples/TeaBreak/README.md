# 독립 객체팩 예제

`dotnet build examples/TeaBreak/TeaBreak.csproj -m:1 --disable-build-servers`로 빌드해.

생성된 `Pack` 폴더를 `Content/Packs/99.TeaBreak`로 복사한 뒤 게임을 다시 실행하면 돼. 제작 골렘의 건설 메뉴에 작은 찻상이 나타나고, 대상의 **행동 목록 → 조용한 티타임**으로 회복해. 새 DLL의 액션은 기존 엔진 수정 없이 동작해.

이 프로젝트는 Contracts만 참조하고, 기본 솔루션에는 포함하지 않았어. 기본 캠페인과 세이브에 필수 의존성을 만들지 않아. 선택적으로 `Objects/Object`의 sprite를 `Images/tea.svg`로 지정하고 같은 팩에 이미지를 넣을 수도 있어.
