using System.Text.Json;
namespace Confectory.Workspace;
public sealed partial class AgentWorkspace
{
    private object YogiCall(JsonElement args)
    {
        if (Review is null) throw new InvalidOperationException("YogiBox에는 프로젝트 작업 문맥이 필요해.");
        var hub = session.Collaboration; string actor = hub.Work(request.Id).ParticipantId;
        string operation = Str(args, "operation");
        if (operation == "create")
        {
            var box = new YogiBox { Author = actor, Title = Str(args, "title"), Explanation = Str(args, "explanation") };
            if (args.TryGetProperty("keys", out var keys)) foreach (var key in keys.EnumerateArray()) box.Exactly.Add(session.YogiReference(key.GetString()!));
            if (args.TryGetProperty("capture", out var capture) && capture.GetBoolean()) box.Looks.Add(new() { Label = "작업 화면", Image = CaptureYogi?.Invoke() ?? throw new InvalidOperationException("현재 호스트는 화면 캡처를 제공하지 않아. EY와 설명으로 등록할 수 있어.") });
            box.Seal(); hub.State.YogiBoxes.Add(box); hub.Save(); return new { box.Id, box.Revision, box.Caption, box.Count };
        }
        string id = Str(args, "id");
        var owned = hub.State.YogiBoxes.FirstOrDefault(b => b.Id == id && b.Author == actor);
        var shared = request.Yogi?.Id == id ? request.Yogi : hub.State.Messages.Where(m => hub.CanRead(actor, m)).Select(m => m.Yogi).FirstOrDefault(b => b?.Id == id)
            ?? hub.State.Incidents.Select(i => i.Yogi).FirstOrDefault(b => b?.Id == id);
        var value = owned ?? shared ?? throw new UnauthorizedAccessException("전달받지 않은 비공개 YogiBox야.");
        if (operation == "read") return new { value.Id, value.Title, value.Explanation, Exactly = value.Exactly.Select(r => new { r.Key, r.Label, Exists = session.YogiExists(r) }), Looks = value.Looks.Select(v => new { v.Label, v.CapturedUtc, v.Image.Sha256 }) };
        if (owned is null) throw new UnauthorizedAccessException("자신이 만든 YogiBox만 재전달할 수 있어.");
        if (operation == "deliver") { string recipient = Str(args, "recipient"); var message = hub.DeliverYogi(actor, value, recipient.Length == 0 ? "project" : "direct", recipient); return new { message.Id, message.Channel, message.Recipient, Yogi = value.ForModel() }; }
        if (operation == "report")
        {
            if (!Enum.TryParse<IncidentSeverity>(Str(args, "severity", "Notice"), out var severity)) throw new ArgumentException("긴급도를 확인해줘.");
            return hub.Report(actor, IncidentKind.Incident, severity, value.Caption, "", "", value.Explanation, blockedTask: request.Id, yogi: value).ForModel();
        }
        throw new ArgumentException("Unknown YogiBox operation.");
    }
}
