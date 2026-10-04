using Confectory.Contracts.UI;
using Confectory.EditorPacks;
using Confectory.Runtime.UI;
using Confectory.Workspace;

namespace Confectory.Editor.CoreTools;

/// <summary>Explicit public worker configuration; mount and drafts do not invoke a provider.</summary>
public sealed class StudioWorkerSettings : IEditorStudioWorkerSettings
{
    private readonly CollaborationWorkspace collaboration;
    private readonly string participantId;
    private readonly Func<bool> running;
    private readonly Action changed, closed;
    private readonly UiSignal name, model, task, autoLabel = new(UiValue.Text("")), note = new(UiValue.Text(""));
    private bool auto, disposed;
    public EditorLiveView View { get; }
    public StudioWorkerSettings(EditorStudioPresentation presentation, IUiBackend backend, CollaborationWorkspace collaboration, string participantId, Func<bool> running, Action changed, Action closed)
    {
        this.collaboration = collaboration; this.participantId = participantId; this.running = running; this.changed = changed; this.closed = closed;
        var participant = Require(); name = new(UiValue.Text(participant.Name)); model = new(UiValue.Text(participant.Model)); task = new(UiValue.Text(participant.PublicTask)); auto = participant.AutoConfirm; Label();
        var context = new UiContext();
        foreach (var entry in new[] { ("name", name), ("model", model), ("task", task), ("autoLabel", autoLabel), ("note", note) }) context.AddValue("studio.worker." + entry.Item1, entry.Item2);
        foreach (var entry in new[] { ("name", name), ("model", model), ("task", task) }) context.AddCommand("studio.worker." + entry.Item1, UiValueKind.Text, value => { if (!disposed) entry.Item2.Set(value); });
        context.AddCommand("studio.worker.auto", UiValueKind.None, _ => { if (!disposed) { auto = !auto; Label(); } });
        context.AddCommand("studio.worker.save", UiValueKind.None, _ => Guard(Save));
        context.AddCommand("studio.worker.close", UiValueKind.None, _ => { if (!disposed) closed(); });
        View = new(presentation.Catalog, "editor.studio.worker-settings", context, backend);
    }
    private Participant Require()
    {
        if (disposed) throw new ObjectDisposedException(nameof(StudioWorkerSettings));
        _ = collaboration.Require("human", ParticipantPermission.Work); collaboration.RequireControl("human", participantId);
        var participant = collaboration.Require(participantId, ParticipantPermission.None);
        if (participant.Kind != ParticipantKind.AI) throw new InvalidOperationException("AI 작업자를 선택해줘.");
        return participant;
    }
    private void Label() => autoLabel.Set(UiValue.Text((auto ? "[켜짐] " : "[꺼짐] ") + "겹치지 않는 검증된 단위 자동 확정"));
    private void Guard(Action action) { if (disposed) return; try { action(); } catch (Exception failure) { note.Set(UiValue.Text(failure.Message)); } }
    public void Save()
    {
        var participant = Require();
        if (running()) throw new InvalidOperationException("이 작업자의 현재 요청이 끝난 뒤 바꿔줘.");
        string nextName = name.Read().Literal.Trim(), nextModel = model.Read().Literal.Trim(), nextTask = task.Read().Literal.Trim();
        if (nextName.Length == 0) nextName = participant.Name;
        if (nextName.Length > 80 || nextModel.Length > 200 || nextModel.Any(char.IsControl) || nextTask.Length > 4000) throw new ArgumentException("이름은 80자, 모델은 200자, 공개 작업 설명은 4,000자 이내로 입력해줘.");
        string oldName = participant.Name, oldModel = participant.Model, oldTask = participant.PublicTask; bool oldAuto = participant.AutoConfirm;
        try { participant.Name = nextName; participant.Model = nextModel; participant.PublicTask = nextTask; participant.AutoConfirm = auto; collaboration.Save(); }
        catch (Exception failure)
        {
            participant.Name = oldName; participant.Model = oldModel; participant.PublicTask = oldTask; participant.AutoConfirm = oldAuto;
            try { collaboration.Save(); } catch (Exception compensation) { throw new AggregateException("작업자 설정 저장과 복원에 실패했어. 다시 열고 확인해줘.", failure, compensation); }
            throw;
        }
        name.Set(UiValue.Text(nextName)); model.Set(UiValue.Text(nextModel)); task.Set(UiValue.Text(nextTask)); note.Set(UiValue.Text("작업자 설정을 저장했어.")); changed();
    }
    public void Dispose() { if (disposed) return; disposed = true; View.Dispose(); }
}
