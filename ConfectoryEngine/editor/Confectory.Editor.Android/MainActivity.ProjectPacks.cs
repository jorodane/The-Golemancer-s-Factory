using Android.App;
using Android.Content;
using Android.Widget;
using Confectory.EditorPacks;
using Confectory.Workspace;

namespace Confectory.Editor.Android;

public sealed partial class MainActivity
{
    private EditorEngineDistribution? installedEngine;
    private ProjectPackSession? packExecution;
    private EditorSession? projectPackExport;
    private EditorPackSource[]? projectPackExportSources;
    private readonly HashSet<string> approvedProjectPacks = new(StringComparer.Ordinal);
    private EditorEngineDistribution InstalledEngine => installedEngine ??= EditorEngineDistribution.Open(Path.Combine(root, "Engine"));
    private ProjectPackSession Execution => packExecution ??= new(InstalledEngine, host, studioSession.Project.Identity);
#pragma warning disable CA1422, CS0618
    private void ProjectPackImportPicker() => StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("*/*").AddCategory(Intent.CategoryOpenable), 6);
    private void ProjectPackExportPicker()
    {
        RequireMobileIdle();
        if (studioSession.Documents.Any(d => d.Dirty)) throw new InvalidOperationException("먼저 작업본을 확정해줘.");
        projectPackExport = studioSession; projectPackExportSources = ActiveSources().ToArray();
        StartActivityForResult(new Intent(Intent.ActionCreateDocument).SetType("application/octet-stream").AddCategory(Intent.CategoryOpenable)
            .PutExtra(Intent.ExtraTitle, studioSession.Project.Name + ".projectpack"), 7);
    }
#pragma warning restore CA1422, CS0618
    private void ExportMobileProjectPack(global::Android.Net.Uri uri)
    {
        try
        {
            if (projectPackExport is not { } project || projectPackExportSources is not { } sources) return;
            using var output = ContentResolver!.OpenOutputStream(uri) ?? throw new IOException("프로젝트팩 파일을 열지 못했어.");
            ProjectExecutionPackage.Write(InstalledEngine, project, sources, output); Report("프로젝트팩을 내보냈어.");
        }
        catch (Exception e) { Report(e.Message); }
        finally { projectPackExport = null; projectPackExportSources = null; }
    }
    private void ImportMobileProjectPack(global::Android.Net.Uri uri) => Work(async () =>
    {
        RequireMobileIdle(); string directory = Path.Combine(root, "ImportedProjects", Guid.NewGuid().ToString("N")); bool accepted = false; ProjectPackSession? candidateSession = null; EditorPackRuntime? candidate = null;
        try
        {
            ImportedProjectPack project;
            using (var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("프로젝트팩을 열지 못했어.")) project = ProjectExecutionPackage.Extract(InstalledEngine, input, directory);
            var details = new TextView(this) { Text = "이 프로젝트의 문서와 팩을 열고 실행해.\n\n" + string.Join("\n", project.Sources.Select(s => s.Id)) };
            if (!await MobileApproval("프로젝트팩 실행", details, "열기·실행")) return;
            // Prepare before leaving the current project; a failed DLL/declaration retains its session.
            candidateSession = new ProjectPackSession(InstalledEngine, host, project.Manifest);
            candidate = await candidateSession.Prepare(project.Sources, lifetime.Token);
            ReplaceMobileSession(project.Manifest, true); accepted = true; packExecution = candidateSession;
            foreach (var source in project.Sources) approvedProjectPacks.Add(source.Id);
            aiConnections.SelectedPack = "project:" + studioSession.Project.Id; aiConnections.SetupCompleted = true; SaveAiConnections();
            Publish(candidate); candidate = null; candidateSession = null; Report("프로젝트팩을 실행했어: " + studioSession.Project.Name);
        }
        finally { candidate?.Dispose(); candidateSession?.Dispose(); if (ReferenceEquals(packExecution, candidateSession)) packExecution = null; if (!accepted && Directory.Exists(directory)) Directory.Delete(directory, true); }
    });
    private void ShowEngineDocument(EditorPackSource source, string path)
    {
        var text = new TextView(this) { Text = source.Read(path) }; text.SetTextIsSelectable(true); var scroll = new ScrollView(this); scroll.AddView(text);
        new AlertDialog.Builder(this).SetTitle(source.Id + " · 엔진 원본")!.SetView(scroll)!
            .SetNegativeButton("닫기", (_, _) => { })!.SetPositiveButton("자식 프로젝트팩 만들기", (_, _) => Work(async () =>
            {
                RequireMobileIdle();
                var parent = runtime?.Snapshot.Panels.FirstOrDefault(p => p.Pack == source.Id) ?? throw new InvalidOperationException("먼저 이 팩을 실행해줘.");
                string directory = Path.Combine(root, "ImportedProjects", Guid.NewGuid().ToString("N"));
                var project = NewProject.Create(Path.Combine(directory, "CustomEditor.packproject"));
                var child = EditorPackTemplates.Create(Path.Combine(directory, "EditorPacks"), "project", "project.custom." + Guid.NewGuid().ToString("N").Substring(0, 8), parent);
                ReplaceMobileSession(project.Manifest, true); mobileEditorPackSelection = child.Id; approvedProjectPacks.Add(child.Id);
                aiConnections.SelectedPack = "project:" + studioSession.Project.Id; aiConnections.SetupCompleted = true; SaveAiConnections(); await Reload();
                Report("자식 프로젝트팩을 만들었어. XML 문서에서 필요한 부분을 바꿀 수 있어.");
            }))!.Show();
    }
}
