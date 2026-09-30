using Golemancer.Contracts;
using Golemancer.Desktop;
using Golemancer.Runtime;
namespace Golemancer.Android;
internal sealed partial class GameView
{
    private MemoryDraft? memoryDraft;
    private string memoryOwner = "";
    private List<BubbleEntry> Memories()
    {
        var entries = Game.State.Recordings.Values.Select(r => Group("memory." + r.Id, r.Name + " · " + r.Steps.Count + "단계", () => [
            Leaf("equip", "장착", () => { session.Actor!.Data["lastRecording"] = r.Id; Notify("메모리를 장착했어."); CloseMenu(); }),
            Leaf("play", "반복 시작", () => Finish(() => Send("play", item: r.Id))),
            Leaf("edit", "타임라인 편집", () => { DiscardDraft(); memoryDraft = new(r); memoryOwner = session.Actor!.Id; ShowDraft(); })])).ToList();
        entries.Add(Leaf("record", session.Actor?.Recording is null ? "녹화 시작" : "녹화 마치기", Record));
        entries.Add(Leaf("pause", "반복 정지 / 재개", () => Finish(() => Send("pause_playback")), session.Actor?.Playback is not null));
        return entries;
    }
    private void DiscardDraft() { memoryDraft?.DiscardCapture(Game); memoryDraft = null; memoryOwner = ""; }
    private void Record()
    {
        if (memoryDraft is { Capturing: true } draft)
        {
            if (Send("record").Ok && draft.AcceptCapture(Game)) ShowDraft();
        }
        else Finish(() => Send("record"));
    }
    private void ShowDraft()
    {
        if (memoryDraft is not { } draft || Game.Find(memoryOwner) is not { } owner) return;
        OpenRoot("메모리 편집 · " + draft.Recording.Name, () =>
        {
            var issues = draft.Validate(Game, owner);
            var entries = new List<BubbleEntry> { Group("frames", "키프레임 " + draft.Recording.Steps.Count, () => draft.Recording.Steps.Select((step, index) =>
                Group("frame." + index, $"{index + 1}. {Game.Content.Actions.GetValueOrDefault(step.Request.Action)?.Name ?? step.Request.Action}" + (issues.Any(i => i.Index == index && i.Error) ? " ⚠" : ""), () =>
                {
                    draft.Seek(index);
                    return [Leaf("info", "내용 / 오류", () => Details("키프레임 " + (index + 1), step.Request.ToString() + "\n" + string.Join("\n", issues.Where(i => i.Index == index).Select(i => i.Message)))),
                        Leaf("from", "여기부터 반복", () => Finish(() => Send("play", item: draft.Recording.Id, mode: "from", x: index))),
                        Leaf("delete", "삭제", () => { draft.Delete(); ShowDraft(); }),
                        Leaf("left", "앞으로", () => { draft.Move(-1); ShowDraft(); }, index > 0),
                        Leaf("right", "뒤로", () => { draft.Move(1); ShowDraft(); }, index < draft.Recording.Steps.Count - 1),
                        Leaf("capture", "여기부터 재녹화", () => { var check = draft.BeginCapture(Game, owner); if (check.Allowed) { CloseMenu(); Notify("재녹화 중 · 녹화 버튼으로 마치고 저장해줘."); } else Notify(check.Message); })];
                })).ToList()),
                Leaf("undo", "되돌리기", () => { draft.Undo(); ShowDraft(); }, draft.CanUndo), Leaf("redo", "다시 실행", () => { draft.Redo(); ShowDraft(); }, draft.CanRedo),
                Leaf("save", "변경 저장", () => { var check = draft.Save(Game, owner); if (check.Allowed) { DiscardDraft(); CloseMenu(); Notify("메모리를 저장했어."); } else Details("저장할 수 없어", check.Message); }),
                Leaf("discard", "변경 버리기", () => { DiscardDraft(); CloseMenu(); }) };
            return entries;
        }, preserveDraft: true);
    }
}
