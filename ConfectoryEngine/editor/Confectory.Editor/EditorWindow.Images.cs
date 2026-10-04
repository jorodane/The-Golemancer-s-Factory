using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Confectory.Assistant.Api;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private void ShowImageAiSetup()
    {
        if (busy) return;
        var dialog = new Window { Owner = this, Title = "Confectory · 이미지 생성 연결", Width = 560, Height = 460, MinWidth = 420, MinHeight = 350, Background = PanelInk, Foreground = TextInk, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        panel.Children.Add(Label("OpenAI Images API", 19)); panel.Children.Add(Label("ChatGPT 로그인과 별도로 API 키를 사용해. 생성 요청은 API 사용료가 발생해. 키는 이 PC에 암호화해서 보관해.", 12, MutedInk));
        var key = new PasswordBox { Margin = new Thickness(4) }; panel.Children.Add(Label("API 키 · 기존 키를 계속 쓰려면 비워 둬")); panel.Children.Add(key);
        var model = Input(); model.Text = aiConnections.Images.Model; panel.Children.Add(Label("이미지 모델")); panel.Children.Add(model);
        var quality = new ComboBox { ItemsSource = new[] { "low", "medium", "high" }, SelectedItem = aiConnections.Images.Quality, Margin = new Thickness(4) }; panel.Children.Add(Label("생성 품질")); panel.Children.Add(quality);
        var enabled = Setting("에디터의 이미지 생성 도구를 연결하고 API 사용료에 동의"); enabled.IsChecked = aiConnections.Images.Enabled; panel.Children.Add(enabled);
        var note = Label("API 접근과 모델 권한은 실제 생성 요청에서 확인해.", 12, MutedInk); panel.Children.Add(note);
        var buttons = new WrapPanel(); buttons.Children.Add(Action("저장", () =>
        {
            try
            {
                var settings = new ImageAiConnection { Enabled = enabled.IsChecked == true, Model = model.Text.Trim(), Quality = (string)quality.SelectedItem }; settings.Validate();
                if (settings.Enabled && key.Password.Length == 0 && aiCredentials.Read("openai-images").Length == 0) throw new InvalidDataException("이미지 생성용 API 키를 넣어줘.");
                if (key.Password.Length > 0) aiCredentials.Write("openai-images", key.Password);
                aiConnections.Images = settings; SaveAiConnections(); dialog.Close(); SetStatus(settings.Enabled ? "이미지 생성 API 설정을 저장했어. 실제 생성 결과는 요청 후 확인해." : "이미지 생성 도구 연결을 해제했어.");
            }
            catch (Exception e) { note.Text = e.Message; }
        })); buttons.Children.Add(Action("키 삭제 · 연결 해제", () => { aiCredentials.Delete("openai-images"); aiConnections.Images.Enabled = false; SaveAiConnections(); dialog.Close(); })); buttons.Children.Add(Action("취소", dialog.Close)); panel.Children.Add(buttons);
        RememberWindow(dialog, "dialog:image-setup"); dialog.ShowDialog();
    }
    private static BitmapImage LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1024; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    private void PreviewGeneratedImage(string path)
    {
        var dialog = new Window { Owner = this, Title = "생성된 이미지 · 팩에 저장하기 전 미리보기", Width = 680, Height = 660, MinWidth = 320, MinHeight = 280, Background = PanelInk, Foreground = TextInk };
        var panel = new DockPanel { Margin = new Thickness(12) }; dialog.Content = panel; var buttons = new WrapPanel();
        buttons.Children.Add(Action("파일로 저장", () => Guard(() => { var file = new SaveFileDialog { Filter = "PNG 이미지|*.png", FileName = "generated-image.png" }; if (file.ShowDialog(dialog) == true) EditorSession.AtomicWrite(file.FileName, File.ReadAllBytes(path)); })));
        buttons.Children.Add(Action("닫기", dialog.Close)); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(new Image { Source = LoadBitmap(path), Stretch = System.Windows.Media.Stretch.Uniform });
        RememberWindow(dialog, "image-preview"); dialog.Show();
    }
    private IEditorImageAccess CreateImageAccess(ChangeReviewBatch? review) => new EditorImages(this, session!, review);
    private sealed class EditorImages : IEditorImageAccess
    {
        private readonly EditorWindow owner;
        private readonly EditorSession session;
        private readonly ChangeReviewBatch? review;
        private readonly OpenAiImages api = new();
        private readonly ImageAiConnection settings;
        private readonly string key;
        private readonly Dictionary<string, string> artifacts = new(StringComparer.Ordinal);
        public EditorImages(EditorWindow owner, EditorSession session, ChangeReviewBatch? review)
        {
            this.owner = owner; this.session = session; this.review = review;
            settings = new() { Enabled = owner.aiConnections.Images.Enabled, Model = owner.aiConnections.Images.Model, Quality = owner.aiConnections.Images.Quality };
            key = settings.Enabled ? owner.aiCredentials.Read("openai-images") : "";
        }
        private static string S(JsonElement args, string key, string fallback = "") => args.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;
        public async Task<string> Call(JsonElement args, CancellationToken token)
        {
            bool available = settings.Enabled && key.Length > 0; string operation = S(args, "operation");
            if (operation == "status" || !available) return EditorSession.Serialize(new { Available = available, Configured = available, Generated = false, settings.Model,
                AccessVerified = false, Reason = available ? "Official API configured; actual model/account access is checked when generating." : "이미지 생성 API가 연결되지 않았어. AI 메뉴의 ‘이미지 생성 연결’에서 설정해줘. ChatGPT 로그인이나 스킬 이름만으로는 생성할 수 없어." });
            if (operation == "generate")
            {
                byte[] bytes = await api.Generate(settings, key, S(args, "prompt"), S(args, "size", "1024x1024"), args.TryGetProperty("transparent", out var transparent) && transparent.GetBoolean(), token).ConfigureAwait(false);
                string id = Guid.NewGuid().ToString("N"), path = Path.Combine(session.StateDirectory, "generated-images", id + ".png"); EditorSession.AtomicWrite(path, bytes); artifacts.Add(id, path);
                await owner.Dispatcher.InvokeAsync(() => owner.PreviewGeneratedImage(path));
                return EditorSession.Serialize(new { Generated = true, ArtifactId = id, Hash = WorkspaceProject.Hash(bytes), PreviewShown = true, SavedToPack = false, VisualResultVerified = false });
            }
            if (operation != "register" || review is null) throw new InvalidOperationException("Generated resource registration requires a reviewed request.");
            string artifact = S(args, "artifactId"), pack = S(args, "pack"), pathInPack = S(args, "path");
            if (!artifacts.TryGetValue(artifact, out string? file)) throw new InvalidDataException("Generate an image in this request before registering its artifact ID.");
            return await owner.Dispatcher.InvokeAsync(() =>
            {
                string kind = S(args, "domain") == "editor" ? "editor" : "game"; string manifest; Func<string, string> resolve; Func<bool> dirty;
                if (kind == "editor")
                {
                    var source = owner.packSources.SingleOrDefault(s => s.Id == pack) ?? throw new InvalidDataException("Choose an existing editor pack."); manifest = source.PathFor("pack.xml"); resolve = source.PathFor; dirty = () => owner.PackDocumentDirty(pack, "pack.xml");
                }
                else
                {
                    session.Refresh(); var source = session.Index.Packs.SingleOrDefault(p => p.Id == pack) ?? throw new InvalidDataException("Choose an existing editable game pack.");
                    if (session.Project.Sources.TryGetValue(pack, out var declared) && !declared.Editable) throw new InvalidOperationException("This game pack is read-only.");
                    manifest = session.Project.Resolve(source.Manifest); string folder = Path.GetDirectoryName(manifest)!;
                    resolve = relative => session.Project.Resolve(session.Project.Relative(Confectory.Runtime.PackCompiler.SafePath(folder, relative)));
                    dirty = () => session.Documents.Any(d => d.Path == source.Manifest && d.Dirty);
                }
                string manifestKey = kind == "editor" ? "editor:" + pack + "/pack.xml" : session.Project.Relative(manifest);
                if (review.File(kind, pack, kind == "editor" ? "pack.xml" : session.Project.Relative(manifest)) is not null || review.Items.Any(i => i.Kind == kind && i.Pack == pack && (i.PreviewImage.Length > 0 || i.Files.Any(f => f.File == manifestKey)))) throw new IOException("Image registration changes pack.xml; review other manifest/resource proposals first.");
                var change = new GeneratedAssetChange(manifest, pathInPack, File.ReadAllBytes(file), resolve);
                string id = change.Stage(review, kind, pack, S(args, "intent"), file, dirty, () => session.Refresh());
                return EditorSession.Serialize(new { ChangeId = id, ArtifactId = artifact, Pack = pack, Path = pathInPack, State = "pending-review", SavedToPack = false, Atomic = true });
            });
        }
        public void Dispose() => api.Dispose();
    }
}
