using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Golemancer.Contracts;
using Golemancer.Runtime;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private MemoryDraft? memoryDraft;
    private string memoryOwner = "";
    private int memoryVersion;
    private IReadOnlyList<MemoryIssue> memoryIssues = [];
    private ScrollViewer? memoryTimeline;
    private void CloseMemory()
    {
        memoryDraft?.DiscardCapture(Game); memoryDraft = null; memoryTimeline = null;
        memoryWindow.Visibility = Visibility.Collapsed; memoryKey = "";
    }
    private void OpenMemory()
    {
        if (memoryWindow.Visibility == Visibility.Visible) { CloseMemory(); return; }
        CloseBubbles(); CloseOverlay(); equipmentWindow.Visibility = Visibility.Collapsed;
        memoryWindow.Visibility = Visibility.Visible; memoryKey = ""; RefreshMemoryWindow();
    }
    private void OpenMemoryEditor(string id)
    {
        if (!Game.State.Recordings.TryGetValue(id, out var recording) || session.Actor is not { } actor) return;
        memoryDraft?.DiscardCapture(Game); memoryDraft = new(recording); memoryOwner = actor.Id;
        if (actor.Playback?.RecordingId == id && actor.Playback.Snapshot?.Revision == recording.Revision) memoryDraft.Seek(actor.Playback.Index);
        CloseBubbles(); CloseOverlay(); equipmentWindow.Visibility = Visibility.Collapsed; memoryWindow.Visibility = Visibility.Visible;
        MemoryChanged();
    }
    private void MemoryChanged()
    {
        if (memoryDraft is not null && Game.Find(memoryOwner) is { } owner) memoryIssues = memoryDraft.Validate(Game, owner);
        memoryVersion++; memoryKey = ""; RefreshMemoryWindow();
    }
    private void MemoryRecordShortcut()
    {
        if (memoryDraft?.Capturing == true)
        {
            var result = session.Command(new() { Action = "record", ActorId = memoryDraft.CaptureActor }); Notify(result.Message);
            if (memoryDraft.AcceptCapture(Game)) MemoryChanged();
        }
        else Send("record");
    }
    private bool HandleMemoryKey(KeyEventArgs e)
    {
        if (memoryWindow.Visibility != Visibility.Visible || memoryDraft is null || Game.State.Dialogues.Count > 0 || modalType.Length > 0) return false;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Z) memoryDraft.Undo();
        else if (ctrl && e.Key == Key.Y) memoryDraft.Redo();
        else if (e.Key == Key.Delete) memoryDraft.Delete();
        else return false;
        MemoryChanged(); e.Handled = true; return true;
    }
    private void RefreshMemoryWindow()
    {
        if (memoryWindow.Visibility != Visibility.Visible || session.Actor is not { } selectedActor) return;
        if (memoryDraft?.AcceptCapture(Game) == true) { memoryIssues = memoryDraft.Validate(Game, Game.Find(memoryOwner)!); memoryVersion++; }
        var actor = memoryDraft is null ? selectedActor : Game.Find(memoryOwner) ?? selectedActor;
        string key = actor.Id + ":" + actor.Recording?.Id + ":" + actor.Recording?.Steps.Count + ":" + actor.Playback?.Status + ":" + actor.Playback?.Index + ":" + actor.Playback?.Paused + ":" + actor.GetText("lastRecording") + ":" + memoryVersion + ":" + string.Join("|", Game.State.Recordings.Values.Select(r => r.Id + r.Revision)) + session.Failure;
        if (key == memoryKey) return; memoryKey = key;
        double offset = memoryTimeline?.HorizontalOffset ?? 0;
        memoryContents.Children.Clear();
        var header = new DockPanel(); memoryContents.Children.Add(header);
        var close = Button(memoryDraft is null ? "닫기 ×" : "저장 없이 닫기 ×", CloseMemory, "memory.close"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(HudLabel(actor.Name + " · 메모리", 18));
        if (memoryDraft is null) { BuildMemoryList(actor); return; }
        BuildMemoryTimeline(actor, offset);
    }
    private void BuildMemoryList(WorldObject actor)
    {
        var controls = new WrapPanel(); memoryContents.Children.Add(controls);
        controls.Children.Add(Button(actor.Recording is null ? "● 녹화 · R" : "■ 녹화 마치기 · R", MemoryRecordShortcut, "memory.record"));
        controls.Children.Add(Button(actor.Playback is null ? "▶ 장착 메모리 반복 · T" : "■ 반복 정지 · T", () => Send("play"), "memory.play"));
        if (actor.Playback is not null) controls.Children.Add(Button(actor.Playback.Paused ? "▶ 재개" : "Ⅱ 일시정지", () => Send("pause_playback"), "memory.pause"));
        memoryContents.Children.Add(HudLabel(actor.Recording is { } recording ? $"● {recording.Steps.Count}단계 · 남은 예약도 종료 시 저장해." : actor.Playback?.Status ?? "메모리를 클릭하면 타임라인을 열어.", 12));
        var list = new StackPanel(); memoryContents.Children.Add(new ScrollViewer { Content = list, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var memory in Game.State.Recordings.Values)
        {
            var row = new DockPanel(); list.Children.Add(row);
            var equip = Button(actor.GetText("lastRecording") == memory.Id ? "장착 중" : "장착", () => { actor.Data["lastRecording"] = memory.Id; Game.State.Revision++; memoryKey = ""; RefreshMemoryWindow(); }, "memory.equip." + memory.Id);
            DockPanel.SetDock(equip, Dock.Right); row.Children.Add(equip);
            row.Children.Add(Button(memory.Name + $" · {memory.Steps.Count}단계", () => OpenMemoryEditor(memory.Id), "memory.open." + memory.Id));
        }
        var options = new WrapPanel(); memoryContents.Children.Add(options);
        foreach (var entry in GameEntries("routines").Where(e => e.Id is "wait" or "failure")) options.Children.Add(HudBubble(entry, 36, "memory." + entry.Id));
    }
    private void BuildMemoryTimeline(WorldObject actor, double offset)
    {
        var draft = memoryDraft!; var memory = draft.Recording;
        var playback = actor.Playback?.RecordingId == memory.Id ? actor.Playback : null;
        bool sameVersion = playback is not null && (playback.Snapshot?.Revision ?? memory.Revision) == memory.Revision && !draft.Dirty;
        var shownPlayback = sameVersion ? playback : null;
        memoryContents.Children.Add(HudLabel(memory.Name + (draft.Dirty ? " · 수정됨" : "") + $" · {memory.Steps.Count}단계", 14));
        var actions = new WrapPanel(); memoryContents.Children.Add(actions);
        Button Control(string text, string id, Action action, bool enabled = true)
        { var button = Button(text, action, "memory." + id); button.IsEnabled = enabled; actions.Children.Add(button); return button; }
        Control("목록", "list", () => { draft.DiscardCapture(Game); memoryDraft = null; memoryTimeline = null; memoryKey = ""; RefreshMemoryWindow(); });
        Control("저장", "save", () => { var result = draft.Save(Game, actor); Notify(result.Allowed ? "메모리를 저장했어. 다음 재생부터 적용돼." : result.Message); MemoryChanged(); }, !draft.Capturing && !memoryIssues.Any(i => i.Error));
        Control("되돌리기", "undo", () => { draft.Undo(); MemoryChanged(); }, draft.CanUndo).ToolTip = "Ctrl+Z";
        Control("다시 실행", "redo", () => { draft.Redo(); MemoryChanged(); }, draft.CanRedo).ToolTip = "Ctrl+Y";
        Control("◀ 이동", "left", () => { draft.Move(-1); MemoryChanged(); }, !draft.Capturing && draft.Cursor > 0);
        Control("이동 ▶", "right", () => { draft.Move(1); MemoryChanged(); }, !draft.Capturing && draft.Cursor + 1 < memory.Steps.Count);
        Control("삭제", "delete", () => { draft.Delete(); MemoryChanged(); }, !draft.Capturing && memory.Steps.Count > 0).ToolTip = "Delete · 선택한 키프레임 제거";
        var rail = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = 88 };
        memoryTimeline = new ScrollViewer { Content = rail, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 114, Margin = new Thickness(0, 6, 0, 4) };
        memoryContents.Children.Add(memoryTimeline);
        for (int i = 0; i < memory.Steps.Count; i++)
        {
            int index = i; var step = memory.Steps[i];
            var (color, width, height) = KeyframeStyle(step.Request.Action);
            bool error = memoryIssues.Any(issue => issue.Index == index && issue.Error);
            bool live = shownPlayback is not null && shownPlayback.Index == index && !shownPlayback.Returning;
            string marker = error ? "!" : live ? "▶" : "";
            var frame = Button($"{index + 1} {marker}\n{Game.Content.Actions.GetValueOrDefault(step.Request.Action)?.Name ?? step.Request.Action}", () => { draft.Seek(index); memoryVersion++; RefreshMemoryWindow(); }, "memory.frame." + index);
            frame.Width = width; frame.Height = height; frame.Padding = new Thickness(4); frame.VerticalAlignment = VerticalAlignment.Bottom;
            frame.Background = SvgImage.Brush(error ? "#ca665c" : color); frame.BorderBrush = SvgImage.Brush(draft.Cursor == index ? "#ffffff" : live ? "#ffd56c" : "#506256"); frame.BorderThickness = new Thickness(draft.Cursor == index || live ? 3 : 1);
            frame.ToolTip = DescribeFrame(step, index, shownPlayback); frame.IsEnabled = !draft.Capturing;
            ToolTipService.SetInitialShowDelay(frame, 100); rail.Children.Add(frame);
        }
        memoryTimeline.ScrollToHorizontalOffset(offset);
        memoryContents.Children.Add(HudLabel("파랑: 이동  ·  초록: 수확  ·  보라: 운반  ·  주황: 제작  ·  회색: 대기", 11));
        var runningMemory = playback?.Snapshot ?? memory;
        string runningAction = playback is not null && playback.Index < runningMemory.Steps.Count ? Game.Content.Actions.GetValueOrDefault(runningMemory.Steps[playback.Index].Request.Action)?.Name ?? runningMemory.Steps[playback.Index].Request.Action : "원점 복귀";
        memoryContents.Children.Add(HudLabel(playback is null ? "실행 중인 메모리 없음" : $"실행 위치 {Math.Min(playback.Index + 1, runningMemory.Steps.Count)} · {runningAction} · {playback.Status}" + (!sameVersion ? " · 편집 전 저장본 실행 중" : ""), 12));
        if (memory.InitialInventory is null) memoryContents.Children.Add(HudLabel("이전 메모리: 현재 골렘의 가방을 시작 재고로 검사해.", 11));
        if (memory.Steps.Count > 0) memoryContents.Children.Add(HudLabel($"▼ 시작점 {draft.Cursor + 1} · " + DescribeFrame(memory.Steps[draft.Cursor], draft.Cursor, shownPlayback), 12));
        foreach (var issue in memoryIssues.Where(i => i.Error).Take(3)) memoryContents.Children.Add(HudLabel($"! {issue.Index + 1}번 · {issue.Message}", 12));
        if (memoryIssues.Count(i => i.Error) > 3) memoryContents.Children.Add(HudLabel("나머지 오류는 빨간 키프레임에 마우스를 올려 확인해.", 11));
        var transport = new WrapPanel(); memoryContents.Children.Add(transport);
        var play = Button("▶ 여기부터 반복", () => { var result = session.Command(new() { Action = "play", ActorId = actor.Id, Item = memory.Id, Mode = "from", X = draft.Cursor }); Notify(result.Message); memoryKey = ""; RefreshMemoryWindow(); }, "memory.from");
        play.IsEnabled = !draft.Dirty && !draft.Capturing && memory.Steps.Count > 0; play.ToolTip = draft.Dirty ? "초안을 먼저 저장해줘." : "선택한 지점부터 시작하고 이후에는 전체 메모리를 반복해."; transport.Children.Add(play);
        if (playback is not null) transport.Children.Add(Button(playback.Paused ? "▶ 현재 단계 재개" : "Ⅱ 현재 단계 정지", () => { var result = session.Command(new() { Action = "pause_playback", ActorId = actor.Id }); Notify(result.Message); memoryKey = ""; RefreshMemoryWindow(); }, "memory.pause"));
        var capture = Button(draft.Capturing ? "■ 재녹화 마치기 · R" : "● 여기부터 재녹화", () =>
        {
            if (draft.Capturing) { MemoryRecordShortcut(); return; }
            var result = draft.BeginCapture(Game, actor); Notify(result.Allowed ? "선택 지점으로 이동해. 이후 행동을 다시 녹화한 뒤 R로 마쳐줘." : result.Message); MemoryChanged();
        }, "memory.capture"); capture.IsEnabled = draft.Capturing || memory.Steps.Count > 0; capture.ToolTip = "선택 지점 이후를 새 플레이로 교체해. 원본은 저장할 때만 바뀌어."; transport.Children.Add(capture);
        memoryContents.Children.Add(HudLabel(draft.Capturing ? "● 새 행동을 녹화 중 · 종료 후 초안을 검토하고 저장해." : "클릭: 시작점 선택 · Ctrl+Z / Ctrl+Y · 닫으면 저장하지 않은 변경은 버려.", 11));
    }
    private string DescribeFrame(RecordedStep step, int index, Playback? playback)
    {
        var r = step.Request;
        string action = Game.Content.Actions.GetValueOrDefault(r.Action)?.Name ?? r.Action;
        if (r.Action == "harvest" && Game.Find(r.TargetId)?.DefinitionId == "sweetfruit_tree") action = "달달과 열매 수확 (목재는 베어넘기기)";
        string detail = action + (r.TargetId.Length > 0 ? " · " + (Game.Find(r.TargetId)?.Name ?? "없는 대상") : "") + (r.Item.Length > 0 ? " · " + Game.ItemName(r.Item) : "");
        detail += $"\n{step.Offset:0.0}초 · 시작 ({step.ActorTile.X}, {step.ActorTile.Y})";
        if (r.Action == "move") detail += $" → ({r.X}, {r.Y})";
        else if (r.Action == "transfer") detail += $" · {(r.Option == "take" ? "가져오기" : "건네기")} · {r.Mode} {r.Quantity}";
        else if (r.Action == "wait") detail += $" · {r.Quantity}초 대기";
        foreach (var issue in memoryIssues.Where(i => i.Index == index)) detail += "\n" + (issue.Error ? "! " : "참고: ") + issue.Message;
        if (playback?.Failures.TryGetValue(index, out var failure) == true) detail += "\n최근 실패: " + failure;
        return detail;
    }
    private static (string Color, int Width, int Height) KeyframeStyle(string action) => action switch
    {
        "move" or "roll" => ("#7aafcc", 70, 48), "harvest" or "fell" or "mine" => ("#89ba83", 84, 64),
        "transfer" or "pickup" or "collect_area" => ("#b3a1cc", 84, 58),
        "craft_single" or "craft_count" or "craft_until" => ("#d8ad74", 92, 72),
        "wait" => ("#a7ada5", 64, 38), _ => ("#bfbb8b", 80, 56)
    };
}
