using System.Windows;
using Microsoft.Win32;
using PackEngine.EditorPacks;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private EditorEngineDistribution? installedEngine;
    private ProjectPackSession? packExecution;
    private EditorEngineDistribution InstalledEngine => installedEngine ??= EditorEngineDistribution.Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine"));
    private ProjectPackSession Execution => packExecution ??= new(InstalledEngine,
        new ProcessEditorModuleHostFactory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PackEngine.PackHost.exe"), PackDotnet), session?.Project.Identity ?? "editor");
    private void ExportProjectPack() => Guard(() =>
    {
        if (busy || session is null) return;
        if (PackDocumentDirty() || session.Documents.Any(d => d.Dirty)) throw new InvalidOperationException("먼저 작업본을 확정해줘. 프로젝트팩은 저장된 파일을 사용해.");
        var save = new SaveFileDialog { Filter = "프로젝트팩|*.projectpack", FileName = session.Project.Name + ".projectpack" };
        if (save.ShowDialog(this) != true) return;
        string temporary = save.FileName + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = File.Create(temporary)) ProjectExecutionPackage.Write(InstalledEngine, session, packSources.Where(s => s.IsReadOnly || enabledPackFolders.Contains(s.Folder)), output);
            PackEngine.Workspace.EditorSession.AtomicWrite(save.FileName, File.ReadAllBytes(temporary));
            SetStatus("프로젝트팩을 내보냈어. 같은 엔진 배포본의 실행기에서 가져올 수 있어.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });
    private void ImportProjectPack() => Guard(() =>
    {
        if (busy || WorkersRunning || PendingReviews || manualReviewActive || runner?.GameRunning == true) return;
        var open = new OpenFileDialog { Filter = "프로젝트팩|*.projectpack" };
        if (open.ShowDialog(this) != true) return;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "ImportedProjects", Guid.NewGuid().ToString("N"));
        bool accepted = false;
        try
        {
            ImportedProjectPack project;
            using (var input = File.OpenRead(open.FileName)) project = ProjectExecutionPackage.Extract(InstalledEngine, input, directory);
            if (MessageBox.Show(this, "이 프로젝트의 문서와 팩을 열고 실행해.\n\n" + string.Join("\n", project.Sources.Select(s => s.Id)),
                "프로젝트팩 실행", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var source in project.Sources) enabledPackFolders.Add(source.Folder);
            ReadyForPackSelection(); OpenProject(project.Manifest); accepted = true;
        }
        finally { if (!accepted && Directory.Exists(directory)) Directory.Delete(directory, true); }
    });
}
