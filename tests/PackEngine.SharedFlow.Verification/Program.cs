using System.Text.Json;
using PackEngine.Assistant.Codex;
using PackEngine.Workspace;

// Opt-in live model probe on an isolated project copy. The relay transport is a fixture;
// the durable host runner, semantic tools and Codex model are real. Never run by default.
if (args.Length != 4 || args[0] != "--live") throw new ArgumentException("--live copied-project.packproject state-directory native-codex");
var session = new EditorSession(args[1], args[2]);
using var runner = new ProjectRunner(session, "dotnet");
using var codex = new CodexAssistant();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var account = await codex.ConnectAsync(new() { Executable = args[3], StateDirectory = Path.Combine(args[2], "web-codex"), ProjectIdentity = session.Project.Identity }, timeout.Token);
if (account.Type != "chatgpt") throw new InvalidOperationException("This explicit live probe requires an existing ChatGPT Codex login.");
session.SetPointingMode("single"); session.Point("pack:feast_trail_animations");
var snapshot = session.CaptureSharedContext("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ExactlyYogi");
string sessionId = Guid.NewGuid().ToString(), taskId = Guid.NewGuid().ToString();
int claims = 0, completions = 0, executions = 0;
JsonElement? delivered = null;
var flow = new SharedEditorTaskRunner(session, (action, payload) =>
{
    if (action == "claim") claims++;
    else { completions++; delivered = JsonSerializer.SerializeToElement(payload, SharedEditorProtocol.Json); }
    return Task.FromResult(JsonSerializer.SerializeToElement(new { }));
});
var journal = await flow.Run(taskId, sessionId, async owned =>
{
    executions++;
    var request = session.PrepareSharedTask("공유한 대상의 pack.xml을 반드시 packengine_read로 실제 조회해서 의존 팩과 데이터 파일명을 알려줘. 전달된 웹 합의의 검증 표식도 답변에 넣어줘. 답변은 세 줄이면 돼.",
        "이 웹 대화에서 합의한 검증 표식은 WEB-CONTEXT-73이다. 이번 작업은 조회만 허용한다. 파일 수정·빌드·프로젝트 실행은 하지 않는다.", snapshot);
    owned.RequestId = request.Id; session.SaveSharedTask(owned);
    using var tools = new AgentWorkspace(session, request, runner, action => action());
    return await new AssistantBridge(session, action => action()).Send(codex, request, timeout.Token, tools);
});
if (journal.State != "delivered" || delivered is null || executions != 1 || claims != 1 || completions != 1)
    throw new Exception("The full claimed host flow did not complete exactly once.");
var result = delivered.Value;
if (result.GetProperty("state").GetString() != "succeeded" || !result.GetProperty("result").GetProperty("reply").GetString()!.Contains("WEB-CONTEXT-73") ||
    !session.State.Reads.Any(r => r.Request == journal.RequestId && r.Path.EndsWith("07.FeastTrailAnimations/pack.xml")) || session.Changes().Any())
    throw new Exception("The live model did not receive the supplied context, use the real read tool, or honor the read-only scope.");
Console.WriteLine(SharedEditorProtocol.Serialize(new { passed = true, liveModel = true, relayTransport = "explicit fixture, not the user's Windows WebView", executions, claims, completions, threadId = codex.ThreadId, completion = result }));
