using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PackEngine.Installation;
using PackEngine.Workspace;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly WebView2 browser = new(), connectionBrowser = new();
    private readonly TextBlock webStatus = Label("ChatGPT 웹을 준비하는 중…", 12, MutedInk);
    private readonly TextBlock webAddress = Label("chatgpt.com", 11, MutedInk);
    private readonly Grid webContent = new();
    private readonly StackPanel webFallback = new() { Margin = new Thickness(24) }, welcome = new() { Margin = new Thickness(28) };
    private readonly List<Window> webPopups = [];
    private readonly ScrollViewer welcomeView = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private Grid? webLayout, nativeBody;
    private UIElement? nativeOutput;
    private Expander? buildOptions;
    private GridLength detailedLogHeight = new(150);
    private Button? connectCurrent, returnToChat, detailsButton;
    private CoreWebView2Environment? webEnvironment;
    private WebConversationConnection? pendingWebConnection;
    private ProjectConversation? connectingProfile;
    private string connectingManifest = "", connectingTitle = "", requestedChat = "";
    private bool chatConfigured, connectionConfigured;
    private bool webInitializing, webDisposed, detailedWorkspace, preferWeb;

    private bool WebMode => preferWeb || conversation?.Mode != "local";
    private void AddBrowserWorkspace(Grid root, Grid body, UIElement output, Expander builds)
    {
        nativeBody = body; nativeOutput = output; buildOptions = builds;
        root.Children.Remove(body);
        webLayout = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        webLayout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        webLayout.ColumnDefinitions.Add(new() { Width = new GridLength(7) });
        webLayout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        Grid.SetRow(webLayout, 1); root.Children.Add(webLayout);
        var web = new DockPanel { Background = PanelInk };
        var header = new StackPanel { Margin = new Thickness(9) };
        var buttons = new WrapPanel();
        buttons.Children.Add(Action("ChatGPT", () => NavigateChat("https://chatgpt.com/")));
        buttons.Children.Add(Action("뒤로", () => Guard(() => { if (browser.CoreWebView2?.CanGoBack == true) browser.CoreWebView2.GoBack(); })));
        buttons.Children.Add(Action("새로고침", () => Guard(() => browser.CoreWebView2?.Reload())));
        connectCurrent = Action("대화 주소 저장", BeginWebConnection); connectCurrent.ToolTip = "다음에 같은 대화를 열 수 있도록 주소를 저장합니다. 에디터 작업 연결은 ‘에디터 연결’을 사용합니다."; buttons.Children.Add(connectCurrent);
        returnToChat = Action("대화로 돌아가기", CancelWebConnection); returnToChat.Visibility = Visibility.Collapsed; buttons.Children.Add(returnToChat);
        header.Children.Add(buttons); header.Children.Add(webStatus); header.Children.Add(webAddress);
        DockPanel.SetDock(header, Dock.Top); web.Children.Add(header);
        webContent.Children.Add(browser); connectionBrowser.Visibility = Visibility.Collapsed; webContent.Children.Add(connectionBrowser); sharedBrowser.Visibility = Visibility.Hidden; webContent.Children.Add(sharedBrowser);
        webContent.Children.Add(webFallback); web.Children.Add(webContent); webLayout.Children.Add(web);
        webLayout.Children.Add(Splitter(1));
        var workspace = new DockPanel(); Grid.SetColumn(workspace, 2); webLayout.Children.Add(workspace);
        var workspaceHeader = new WrapPanel { Margin = new Thickness(8) };
        detailsButton = Action("작업 도구 펼치기", () => { detailedWorkspace = !detailedWorkspace; ApplyBrowserLayout(); }); workspaceHeader.Children.Add(detailsButton);
        workspaceHeader.Children.Add(Action("대화 방식", () => { if (session is null) { SetStatus("먼저 게임팩을 만들거나 열어줘."); return; } ChooseConversationMode(); }));
        DockPanel.SetDock(workspaceHeader, Dock.Top); workspace.Children.Add(workspaceHeader);
        var main = new Grid(); main.Children.Add(body); welcomeView.Content = welcome; main.Children.Add(welcomeView); workspace.Children.Add(main); AddSharingControls(header, main);
        ShowBrowserFallback("웹 대화를 준비하고 있어.", false);
        Loaded += async (_, _) => await InitializeBrowser();
        Closed += (_, _) => { webDisposed = true; pendingWebConnection = null; webInstallCancellation?.Cancel(); foreach (var popup in webPopups.ToArray()) popup.Close(); browser.Dispose(); connectionBrowser.Dispose(); };
        RefreshWebProject();
    }
    private void ApplyBrowserLayout()
    {
        if (webLayout is null || nativeBody is null) return;
        bool web = WebMode, detailed = detailedWorkspace || !web;
        webLayout.Children[0].Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        webLayout.Children[1].Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        webLayout.ColumnDefinitions[0].MinWidth = web ? 420 : 0;
        webLayout.ColumnDefinitions[0].Width = web ? (detailed ? new GridLength(460) : new GridLength(1, GridUnitType.Star)) : new GridLength(0);
        webLayout.ColumnDefinitions[1].Width = new GridLength(web ? 7 : 0);
        nativeBody.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        welcomeView.Visibility = detailed ? Visibility.Collapsed : Visibility.Visible;
        detailsButton!.Content = detailed ? "작업 도구 접기" : "작업 도구 펼치기";
        detailsButton.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        buildOptions!.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        nativeOutput!.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        if (editorRoot is not null) editorRoot.RowDefinitions[2].Height = detailed ? detailedLogHeight : new GridLength(0);
    }
    private void RefreshWebProject()
    {
        if (webLayout is null) return;
        string currentProject = session?.Project.Manifest ?? "";
        if (attachmentProject != currentProject) { yogiAttachments.Children.Clear(); attachmentProject = currentProject; }
        pendingWebConnection = null; connectingProfile = null;
        connectionBrowser.Visibility = Visibility.Collapsed; browser.Visibility = Visibility.Visible; returnToChat!.Visibility = Visibility.Collapsed;
        welcome.Children.Clear();
        welcome.Children.Add(Label(session is null ? "대화하면서 시작해." : session.Project.Name, 26));
        welcome.Children.Add(Label(session is null ? "왼쪽에서 평소처럼 로그인하고 대화해. 작업할 게임팩은 새로 만들거나 열면 돼." : "‘에디터 연결’을 누르면 이 대화에서 객체와 문서를 공유하고 Codex에게 작업을 맡길 수 있어.", 15, MutedInk));
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 16) };
        actions.Children.Add(Action("새 게임팩", CreateGameProject)); actions.Children.Add(Action("게임팩 열기", ChooseProject)); welcome.Children.Add(actions);
        if (session is not null)
        {
            var urls = conversation is null ? (ProjectUrl: "", ChatUrl: "") : ConversationLinkMetadata.Read(conversation);
            if (urls.ProjectUrl.Length + urls.ChatUrl.Length > 0)
            {
                welcome.Children.Add(Label("저장된 연결", 14, AccentInk));
                if (urls.ChatUrl.Length > 0) welcome.Children.Add(Action("연결된 대화 열기", () => NavigateChat(urls.ChatUrl)));
                if (urls.ProjectUrl.Length > 0) welcome.Children.Add(Action("연결된 프로젝트 열기", () => NavigateChat(urls.ProjectUrl)));
            }
            welcome.Children.Add(Label("게임팩의 객체", 15));
            foreach (var pack in session.Index.Packs.Take(12))
            {
                string packId = pack.Id;
                welcome.Children.Add(Action(packId, () => { detailedWorkspace = true; ApplyBrowserLayout(); Guard(() => SelectNode("pack:" + packId)); }));
            }
        }
        webStatus.Text = session is null ? "로그인하고 대화를 시작해." : "현재 게임팩 · " + session.Project.Name;
        ApplyBrowserLayout(); RefreshBrowserAddress();
        if (WebMode && IsLoaded && !chatConfigured) _ = InitializeBrowser();
    }
    private void RefreshBrowserAddress()
    {
        if (webDisposed) return;
        var urls = WebConversationConnection.Observe(browser.Source?.AbsoluteUri);
        webAddress.Text = browser.Source?.GetLeftPart(UriPartial.Path) ?? "chatgpt.com";
        if (connectCurrent is not null) connectCurrent.IsEnabled = !busy && session is not null && (urls.ProjectUrl.Length + urls.ChatUrl.Length > 0) && pendingWebConnection is null;
    }
    private void ShowBrowserFallback(string message, bool failed)
    {
        webFallback.Children.Clear(); webFallback.Visibility = Visibility.Visible;
        webFallback.Children.Add(Label(message, 17));
        if (failed)
        {
            webFallback.Children.Add(Action("다시 열기", async () => await InitializeBrowser()));
            webFallback.Children.Add(Action("웹 실행 구성 요소 설치", InstallWebRuntime));
            webFallback.Children.Add(Action("브라우저에서 ChatGPT 열기", () => OpenUrl("https://chatgpt.com/")));
            webFallback.Children.Add(Label("웹 패널을 사용할 수 없어도 게임팩 작업 도구는 계속 사용할 수 있어.", 13, MutedInk));
        }
    }
    private async Task InitializeBrowser()
    {
        if (webInitializing || webDisposed || !WebMode) return;
        webInitializing = true;
        try
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "WebProfile");
            webEnvironment ??= await CoreWebView2Environment.CreateAsync(null, data);
            if (webDisposed) return;
            await browser.EnsureCoreWebView2Async(webEnvironment);
            if (webDisposed) return;
            if (!chatConfigured)
            {
                ConfigureBrowser(browser, false);
                browser.CoreWebView2.NavigationStarting += (_, _) => chatNavigationVersion++;
                browser.CoreWebView2.SourceChanged += (_, _) => { chatNavigationVersion++; RefreshBrowserAddress(); };
                browser.CoreWebView2.NavigationCompleted += (_, e) => { RefreshBrowserAddress(); if (!e.IsSuccess) webStatus.Text = "페이지를 열지 못했어. 새로고침하거나 다른 대화를 열어줘."; };
                chatConfigured = true;
            }
            webFallback.Visibility = Visibility.Collapsed;
            var saved = conversation is null ? (ProjectUrl: "", ChatUrl: "") : ConversationLinkMetadata.Read(conversation);
            browser.CoreWebView2.Navigate(requestedChat.Length > 0 ? requestedChat : saved.ChatUrl.Length > 0 ? saved.ChatUrl : saved.ProjectUrl.Length > 0 ? saved.ProjectUrl : "https://chatgpt.com/");
        }
        catch (Exception e) { if (!webDisposed) { ShowBrowserFallback("웹 대화를 열지 못했어. 실행 구성 요소와 네트워크를 확인해줘.", true); AppendLog("웹 패널: " + e.Message); } }
        finally { webInitializing = false; }
    }
    private void ConfigureBrowser(WebView2 view, bool messages)
    {
        view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        view.CoreWebView2.Settings.IsWebMessageEnabled = messages;
        view.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (e.Uri != "about:blank" && (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https")) e.Cancel = true;
        };
        view.CoreWebView2.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || webDisposed) return;
            var deferral = e.GetDeferral();
            var popupView = new WebView2();
            var popup = new Window { Owner = this, Title = "웹 로그인", Width = 650, Height = 780, Content = popupView, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            try
            {
                webPopups.Add(popup); popup.Closed += (_, _) => { webPopups.Remove(popup); popupView.Dispose(); }; popup.Show();
                await popupView.EnsureCoreWebView2Async(webEnvironment);
                if (webDisposed || !popup.IsVisible) return;
                ConfigureBrowser(popupView, false);
                popupView.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
                e.NewWindow = popupView.CoreWebView2;
            }
            catch (Exception error) { AppendLog("웹 로그인 창: " + error.Message); popup.Close(); }
            finally { deferral.Complete(); }
        };
    }
    private void NavigateChat(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "chatgpt.com" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return;
        CancelWebConnection();
        preferWeb = true; ApplyBrowserLayout();
        requestedChat = uri.AbsoluteUri;
        if (chatConfigured && browser.CoreWebView2 is not null) browser.CoreWebView2.Navigate(uri.AbsoluteUri);
        else if (IsLoaded) _ = InitializeBrowser();
    }
    private void UseEmbeddedChat()
    {
        preferWeb = true; ResetResidentConnection(); ApplyBrowserLayout();
        var urls = conversation is null ? (ProjectUrl: "", ChatUrl: "") : ConversationLinkMetadata.Read(conversation);
        NavigateChat(urls.ChatUrl.Length > 0 ? urls.ChatUrl : urls.ProjectUrl.Length > 0 ? urls.ProjectUrl : "https://chatgpt.com/");
    }
    private async void BeginWebConnection()
    {
        if (busy || session is null || conversation is null || webEnvironment is null || pendingWebConnection is not null) return;
        try
        {
            var urls = WebConversationConnection.Observe(browser.Source?.AbsoluteUri);
            var request = new WebConversationConnection(conversation.Id, session.Project.Name, urls.ProjectUrl, urls.ChatUrl);
            pendingWebConnection = request; connectingProfile = conversation; connectingManifest = session.Project.Manifest;
            connectingTitle = browser.CoreWebView2?.DocumentTitle ?? "";
            if (connectingTitle.Length > 160) connectingTitle = connectingTitle.Substring(0, 160);
            browser.Visibility = Visibility.Collapsed; connectionBrowser.Visibility = Visibility.Visible; returnToChat!.Visibility = Visibility.Visible; RefreshBrowserAddress();
            if (!connectionConfigured)
            {
                await connectionBrowser.EnsureCoreWebView2Async(webEnvironment);
                if (webDisposed) return;
                if (!connectionConfigured)
                {
                    ConfigureBrowser(connectionBrowser, true);
                    connectionBrowser.CoreWebView2.WebMessageReceived += ConnectionMessage;
                    connectionBrowser.CoreWebView2.NavigationCompleted += (_, e) => { if (!e.IsSuccess && pendingWebConnection is not null) webStatus.Text = "연결 화면을 열지 못했어. 대화로 돌아가 다시 연결해줘."; };
                    connectionConfigured = true;
                }
            }
            if (ReferenceEquals(pendingWebConnection, request)) connectionBrowser.CoreWebView2.Navigate(WebConversationConnection.PageUrl);
        }
        catch (Exception e) { CancelWebConnection(); webStatus.Text = e.Message; }
    }
    private void ConnectionMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (webDisposed || pendingWebConnection is not { } request || connectingProfile is null || session is null ||
            !ReferenceEquals(conversation, connectingProfile) ||
            session.Project.Manifest != connectingManifest || !WebConversationConnection.IsConnectionPage(e.Source) ||
            !WebConversationConnection.IsConnectionPage(connectionBrowser.CoreWebView2.Source)) return;
        try
        {
            string json = e.WebMessageAsJson; if (json.Length > 24000) return;
            using var doc = JsonDocument.Parse(json); var message = doc.RootElement;
            if (message.GetProperty("protocol").GetString() != WebConversationConnection.Protocol) return;
            if (message.GetProperty("type").GetString() == "ready") { connectionBrowser.CoreWebView2.PostWebMessageAsJson(request.RequestJson); return; }
            if (busy) throw new InvalidOperationException("진행 중인 작업이 끝난 뒤 현재 대화 연결을 다시 눌러줘.");
            var receipt = request.Accept(e.Source, json, conversation!.Id);
            var profile = connectingProfile;
            ConversationLinkMetadata.Set(profile, receipt.ProjectUrl, receipt.ChatUrl); profile.Title = connectingTitle; profile.Save(); conversation = profile;
            if (CurrentAccess is { } access)
            {
                access.ChatGpt.MetadataOnly = true; access.ChatGpt.Enabled = false; access.ChatGpt.AutoStartTunnel = false; access.ChatGpt.Url = profile.Url;
                StopChatGptBridge(); ResetResidentConnection(); SaveSettings();
            }
            connectionBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { protocol = WebConversationConnection.Protocol, type = "applied", requestId = request.RequestId }));
            RefreshWebProject(); ApplyConversationMode();
            webStatus.Text = "연결 저장됨 · " + receipt.AccountLabel;
            SetStatus("현재 대화를 ‘" + session.Project.Name + "’에 연결했어.");
        }
        catch (Exception error)
        {
            connectionBrowser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { protocol = WebConversationConnection.Protocol, type = "failed", requestId = request.RequestId }));
            webStatus.Text = error.Message; SetStatus("연결을 적용하지 못했어. 대화로 돌아가 다시 연결해줘.");
        }
    }
    private void CancelWebConnection()
    {
        if (sharedBrowser.Visibility == Visibility.Visible && sharingSession.Length == 0) ClearSharedEditor();
        pendingWebConnection = null; connectingProfile = null;
        browser.Visibility = Visibility.Visible; connectionBrowser.Visibility = Visibility.Collapsed;
        if (returnToChat is not null) returnToChat.Visibility = Visibility.Collapsed;
        RefreshBrowserAddress();
    }
}
