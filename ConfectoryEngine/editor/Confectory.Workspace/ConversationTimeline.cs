namespace Confectory.Workspace;

/// <summary>A conversation exchange, shared by Workers and Helpers. Tool events stay in the archive.</summary>
public class ConversationExchange
{
    public string MessageId { get; set; } = "";
    public string ContextMessageId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string User { get; set; } = "";
    public string Answer { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> Events { get; set; } = [];
    public List<string> Resolutions { get; set; } = [];
    public YogiBox? Yogi { get; set; }
    public string Preview => ConversationTimeline.Preview(User.Length > 0 ? User : Answer);
}

public static class ConversationTimeline
{
    public static string Preview(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim() is var line && line.Length > 90 ? line.Substring(0, 90) + "…" : line;
    public static int Clamp(int current, int count) => Math.Max(0, Math.Min(current, count - 1));
    public static IEnumerable<int> Dots(int current, int count)
    {
        int length = Math.Min(11, count), start = Math.Max(0, Math.Min(Clamp(current, count) - 5, count - length));
        return Enumerable.Range(start, length);
    }
    // Append while preserving a historical selection. A live/latest selection follows the next exchange.
    public static int AfterAppend(int current, int oldCount, int newCount) => oldCount == 0 || current == oldCount - 1 ? Math.Max(0, newCount - 1) : Clamp(current, newCount);
    public static int Synchronize<T>(CollaborationWorkspace hub, string viewer, string participant, List<T> turns, int current) where T : ConversationExchange, new()
    {
        int before = turns.Count;
        foreach (var message in hub.State.Messages.Where(m => hub.CanRead(viewer, m)))
        {
            if (message.Author == viewer && message.Channel == "direct" && message.Recipient == participant && message.Yogi is not null)
            {
                if (!turns.Any(t => t.ContextMessageId == message.Id)) turns.Add(new T { ContextMessageId = message.Id, User = message.Text, Yogi = message.Yogi.Copy(), State = "delivered" });
                continue;
            }
            if (message.Author != participant) continue;
            if (turns.Any(t => t.MessageId == message.Id)) continue;
            var question = hub.State.Messages.LastOrDefault(m => m.ThreadId == message.ThreadId && m.Author != participant && hub.CanRead(viewer, m));
            var pending = question is null ? null : turns.FirstOrDefault(t => t.ContextMessageId == question.Id);
            if (pending is not null) { pending.MessageId = message.Id; pending.Answer = message.Text; pending.State = message.Importance == MessageImportance.NeedsReply ? "needs-user" : "completed"; continue; }
            // Migration of old native logs which did not store a message receipt.
            var existing = turns.LastOrDefault(t => t.MessageId.Length == 0 && t.Answer == message.Text);
            if (existing is not null) { existing.MessageId = message.Id; continue; }
            turns.Add(new T { MessageId = message.Id, User = question?.Text ?? "", Answer = message.Text, State = message.State, Yogi = message.Yogi ?? question?.Yogi });
        }
        return AfterAppend(current, before, turns.Count);
    }
    public static string Activity(string state, bool running, string activity = "") => state == "delivered" ? "전달됨 · 처리 대기" : state is "failed" or "interrupted" or "cancelled" or "suspended" ? "오류·중단" : state is "review" or "needs-user" or "handoff" ? "사용자 확인 필요" : running ? activity.Length > 0 ? activity : "작업 중" : "대기";
}
