using System.Diagnostics;
using System.Text.Json;
using Golemancer.Contracts;
using Golemancer.Engine;
using Microsoft.Extensions.FileProviders;

string root = Environment.GetEnvironmentVariable("GOLEMANCER_ROOT") ?? Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Content", "Packs"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Content/Packs not found. Run Start.bat or start.sh.");
string webRoot = Path.Combine(root, "src", "Golemancer.Host", "wwwroot");
if (!Directory.Exists(webRoot)) webRoot = Path.Combine(root, "wwwroot");
string saves = Path.Combine(root, "Saves"); Directory.CreateDirectory(saves);
var cooked = PackLoader.Cook(Path.Combine(root, "Content", "Packs"));
var game = new Simulation(cooked);
var gate = new object();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args.Where(a => a != "--open").ToArray(), ContentRootPath = root, WebRootPath = webRoot });
builder.WebHost.UseUrls("http://127.0.0.1:5187");
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; });
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Method == "POST" && context.Request.Headers.Origin is { Count: > 0 } origin && origin.ToString() != "http://127.0.0.1:5187" && origin.ToString() != "http://localhost:5187") { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
string assets = Path.Combine(root, "Assets");
if (Directory.Exists(assets)) app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(assets), RequestPath = "/assets" });
game.State.Paused = true;
app.MapGet("/api/meta", () => Results.Json(new
{
    title = "The Golemancer's Factory", version = "0.1.0", cooked.Fingerprint,
    items = cooked.Content.Items, objects = cooked.Content.Objects, recipes = cooked.Content.Recipes,
    actions = cooked.Content.Actions, quests = cooked.Content.Quests, texts = cooked.Content.Texts,
    packs = cooked.Content.Packs.Select(p => new { p.Id, p.Version, p.Dependencies, p.Assemblies }),
    saves = new[] { "manual", "autosave" }.Where(s => File.Exists(Path.Combine(saves, s + ".json")))
}));
app.MapGet("/api/state", () => { lock (gate) return Results.Text(JsonSerializer.Serialize(game.State, Simulation.Json), "application/json"); });
app.MapGet("/api/menu/{targetId}", (string targetId) =>
{
    lock (gate)
    {
        var target = game.Find(targetId); var actor = game.Find(game.State.ControlledId);
        if (target is null || actor is null) return Results.Json(Array.Empty<MenuEntry>());
        var actions = game.Definition(target)?.Actions.Select(id => game.Content.Actions.GetValueOrDefault(id)).Where(d => d is not null && (d.Condition is null || game.Evaluate(d.Condition, actor, target))).Cast<ActionDef>() ?? [];
        return Results.Json(MenuBuilder.Build(actions));
    }
});
app.MapPost("/api/command", (ActionRequest request) =>
{
    lock (gate)
    {
        if (game.State.Paused) return Results.Json(ActionResult.Fail("게임을 재개해줘.", "paused"));
        var result = game.Dispatch(request); game.State.Revision++; return Results.Json(result);
    }
});
app.MapPost("/api/dialogue", () => { lock (gate) { if (game.State.Dialogues.Count > 0) game.State.Dialogues.RemoveAt(0); return Results.Ok(); } });
app.MapPost("/api/pause/{paused:bool}", (bool paused) => { lock (gate) { game.State.Paused = paused; return Results.Ok(); } });
app.MapPost("/api/new", () => { lock (gate) { game = new Simulation(cooked); return Results.Ok(); } });
app.MapPost("/api/save", () => { lock (gate) { game.Save(Path.Combine(saves, "manual.json")); game.Notice("진행 상황을 저장했어."); return Results.Ok(); } });
app.MapPost("/api/load/{slot}", (string slot) =>
{
    if (slot is not ("manual" or "autosave")) return Results.BadRequest();
    lock (gate)
    {
        string path = Path.Combine(saves, slot + ".json");
        if (!File.Exists(path)) return Results.NotFound(new { message = "저장된 게임이 없어." });
        try { game = new Simulation(cooked, Simulation.ReadSave(path)); game.State.Paused = false; game.Notice("저장한 공방으로 돌아왔어."); return Results.Ok(); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { return Results.BadRequest(new { message = "저장 파일을 읽지 못했어. .bak 파일을 확인해줘." }); }
    }
});
using var cancellation = new CancellationTokenSource();
var loop = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); double lastSave = 0;
    try
    {
        while (await timer.WaitForNextTickAsync(cancellation.Token))
        {
            lock (gate)
            {
                if (!game.State.Paused && game.State.Dialogues.Count == 0) game.Tick(.1);
                if (game.State.Time < lastSave) lastSave = game.State.Time;
                if (game.State.Time - lastSave >= 30) { game.Save(Path.Combine(saves, "autosave.json")); lastSave = game.State.Time; }
            }
        }
    }
    catch (OperationCanceledException) { }
});
app.Lifetime.ApplicationStopping.Register(() => { cancellation.Cancel(); lock (gate) if (game.State.Time > 0) game.Save(Path.Combine(saves, "autosave.json")); });
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"Golemancer: {cooked.Content.Packs.Count} packs cooked. Open http://127.0.0.1:5187");
    if (args.Contains("--open"))
        try { Process.Start(new ProcessStartInfo("http://127.0.0.1:5187") { UseShellExecute = true }); } catch (Exception) { Console.WriteLine("Open the URL above in your browser."); }
});
await app.RunAsync();
await loop;
