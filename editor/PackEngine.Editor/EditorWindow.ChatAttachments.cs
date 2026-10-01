using System.Collections.Specialized;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly StackPanel yogiAttachments = new();
    private bool attachingYogi;
    private long chatNavigationVersion;
    private string attachmentProject = "";
    private sealed class YogiDraft
    {
        public EditorSession Session = null!;
        public SharedEditorSnapshot Snapshot = null!;
        public YogiAttachment File = null!;
        public string Path = "";
        public TextBlock Status = null!;
        public Button Retry = null!;
        public StackPanel Panel = null!;
        public bool Mirrored;
    }
    private void StageYogiAttachment(SharedEditorSnapshot snapshot)
    {
        if (session is null || attachingYogi) return;
        var file = YogiAttachment.Create(snapshot);
        string directory = Path.Combine(session.StateDirectory, "chat-attachments"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, file.Name); EditorSession.AtomicWrite(path, file.Bytes);
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var name = Label(file.Name, 11, AccentInk); name.TextWrapping = TextWrapping.Wrap; name.Cursor = Cursors.Hand;
        name.ToolTip = "채팅 입력창으로 끌어 넣어도 돼."; panel.Children.Add(name);
        var note = Label("채팅 입력창에 첨부하는 중…", 12, MutedInk); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var draft = new YogiDraft { Session = session, Snapshot = snapshot, File = file, Path = path, Status = note, Panel = panel };
        var actions = new WrapPanel();
        draft.Retry = Action("첨부 다시 시도", async () => await DeliverYogiAttachment(draft)); actions.Children.Add(draft.Retry);
        actions.Children.Add(Action("파일 복사", () => Guard(() =>
        {
            Clipboard.SetFileDropList(new StringCollection { path }); note.Text = "파일을 복사했어. 채팅 입력창에 붙여넣거나 위 파일명을 끌어 넣어줘.";
        })));
        actions.Children.Add(Action("닫기", () => yogiAttachments.Children.Remove(panel))); panel.Children.Add(actions);
        Point? dragStart = null;
        name.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(name);
        name.MouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(name) - start).Length < 8) return;
            dragStart = null; Guard(() => DragDrop.DoDragDrop(name, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy));
        };
        yogiAttachments.Children.Add(panel); while (yogiAttachments.Children.Count > 3) yogiAttachments.Children.RemoveAt(0);
        if (snapshot.Kind == "ExactlyYogi") { session.SetPointingMode("none"); pointingMode.SelectedIndex = 0; editorPoints.Clear(); sharedUiTargets.Clear(); RefreshPointing(); }
        else pointedImage = null;
        _ = DeliverYogiAttachment(draft);
    }
    private async Task DeliverYogiAttachment(YogiDraft draft)
    {
        if (attachingYogi) { draft.Status.Text = "앞선 첨부가 끝나면 다시 눌러줘."; return; }
        attachingYogi = true; draft.Retry.IsEnabled = false;
        CoreWebView2? core = null; string group = "yogi-" + Guid.NewGuid().ToString("N");
        try
        {
            if (webDisposed || !WebMode || !browser.IsVisible || browser.CoreWebView2 is null)
                throw new InvalidOperationException("첨부할 ChatGPT 대화를 먼저 열어줘. 위 파일은 그대로 남아 있어.");
            core = browser.CoreWebView2;
            string url = core.Source; long navigation = chatNavigationVersion;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" || address.Host != "chatgpt.com" || !address.IsDefaultPort || address.UserInfo.Length > 0)
                throw new InvalidOperationException("ChatGPT 채팅 입력창에서 첨부를 다시 눌러줘.");
            void Current()
            {
                if (webDisposed || !ReferenceEquals(session, draft.Session) || !WebMode || !browser.IsVisible || core.Source != url || chatNavigationVersion != navigation)
                    throw new InvalidOperationException("대화나 게임팩이 바뀌었어. 현재 첨부 목록을 확인한 뒤 다시 눌러줘.");
            }
            Current(); draft.Status.Text = "채팅 입력창에 첨부하는 중…";
            using var script = new StreamReader(typeof(EditorWindow).Assembly.GetManifestResourceStream("PackEngine.Editor.ChatComposerAttachment.js")!);
            string expression = script.ReadToEnd() + "(" + SharedEditorProtocol.Serialize(new { url, name = draft.File.Name, mime = draft.File.MediaType }) + ")";
            var prepared = RemoteResult(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", SharedEditorProtocol.Serialize(new { expression, objectGroup = group, returnByValue = false })));
            string handle = prepared.GetProperty("objectId").GetString()!;
            Current();
            var status = await ComposerCall(core, handle, "status");
            if (status.GetProperty("value").GetString() != "visible")
            {
                var input = await ComposerCall(core, handle, "fileInput", false);
                Current();
                if (input.TryGetProperty("objectId", out var inputId))
                {
                    await ComposerCall(core, handle, "markDelivered"); Current();
                    await core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", SharedEditorProtocol.Serialize(new { files = new[] { draft.Path }, objectId = inputId.GetString() }));
                }
                else
                {
                    await ComposerCall(core, handle, "paste", true, Convert.ToBase64String(draft.File.Bytes));
                }
                bool visible = false;
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    Current();
                    status = await ComposerCall(core, handle, "status");
                    if (status.GetProperty("value").GetString() == "visible") { visible = true; break; }
                    await Task.Delay(250);
                }
                if (!visible) throw new InvalidOperationException("첨부 표시를 확인하지 못했어. 입력창을 확인하고, 없으면 위 파일을 끌어 넣거나 다시 눌러줘.");
            }
            Current(); await ComposerCall(core, handle, "focus"); browser.Focus();
            draft.Status.Text = "입력창에 첨부 표시됨 · 업로드가 끝나면 메시지를 보내줘.";
            sharingStatus.Text = draft.Snapshot.Kind + " · 현재 채팅 입력창에 첨부했어.";
            // The user's existing, explicit editor grant may also make the frozen snapshot available to Codex.
            // Attaching never opens a connection wizard or grants new permissions.
            if (!draft.Mirrored && SharedEditorConnected && ReferenceEquals(session, draft.Session))
            {
                try { await PublishSharedState(draft.Snapshot); draft.Mirrored = true; }
                catch (Exception e) { AppendLog("Yogi 에디터 공유: " + e.Message); draft.Status.Text += " 에디터 연결에는 전달하지 못했어."; }
            }
            if (!SharedEditorConnected || draft.Mirrored) yogiAttachments.Children.Remove(draft.Panel);
        }
        catch (Exception e) { draft.Status.Text = e.Message; AppendLog("Yogi 첨부: " + e.Message); }
        finally
        {
            if (core is not null && !webDisposed)
                try { await core.CallDevToolsProtocolMethodAsync("Runtime.releaseObjectGroup", SharedEditorProtocol.Serialize(new { objectGroup = group })); } catch (Exception) { }
            attachingYogi = false; draft.Retry.IsEnabled = true;
        }
    }
    private static async Task<JsonElement> ComposerCall(CoreWebView2 core, string handle, string method, bool byValue = true, params object[] arguments)
    {
        string function = "function() { return this[" + JsonSerializer.Serialize(method) + "].apply(this, Array.from(arguments)); }";
        return RemoteResult(await core.CallDevToolsProtocolMethodAsync("Runtime.callFunctionOn", SharedEditorProtocol.Serialize(new
        {
            objectId = handle, functionDeclaration = function, returnByValue = byValue, arguments = arguments.Select(value => new { value }).ToArray()
        })));
    }
    private static JsonElement RemoteResult(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var details))
        {
            string message = details.TryGetProperty("exception", out var exception) && exception.TryGetProperty("description", out var description)
                ? description.GetString()!.Split('\n')[0] : "채팅 입력창에 첨부하지 못했어. 위 파일을 직접 끌어 넣어줘.";
            throw new InvalidOperationException(message);
        }
        return root.GetProperty("result").Clone();
    }
}
