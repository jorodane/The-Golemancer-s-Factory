using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using PackEngine.Installation;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private readonly TextBlock chatGptWebStatus = Label("웹 연결은 단계별 설정에서 준비해줘. Windows ChatGPT 앱은 필요 없어.", 13, MutedInk);
    private ChatGptWebTunnel? chatGptTunnel;
    private CancellationTokenSource? chatGptWebStart;
    private static string ProtectTunnelKey(string key, string identity) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(identity), DataProtectionScope.CurrentUser));
    private static string UnprotectTunnelKey(string value, string identity)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Encoding.UTF8.GetBytes(identity), DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException)
        { throw new InvalidOperationException("이 Windows 계정에서 저장된 실행 키를 열 수 없어. 연결 설정에서 다시 입력해줘."); }
    }
    private void StopChatGptWeb()
    {
        chatGptWebStart?.Cancel();
        var previous = chatGptTunnel; chatGptTunnel = null; previous?.Dispose();
        chatGptWebStatus.Text = "웹 연결 중지됨 · 연결 설정 또는 ‘웹 연결 시작’에서 준비해줘.";
        chatGptExternalConfirmed = false; chatGptConnectionChanged?.Invoke();
    }
    private void ScheduleChatGptWeb()
    {
        var opened = session;
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (ReferenceEquals(session, opened) && !busy && !chatGptSetupPending && chatGptTunnel is null && CurrentAccess?.ChatGpt is
                { Enabled: true, ConnectionKind: "web", AutoStartTunnel: true, ProtectedTunnelKey.Length: > 0 }) StartSavedChatGptWeb();
        }));
    }
    private async void StartSavedChatGptWeb()
    {
        if (busy || chatGptSetupPending || session is null || CurrentAccess is not { } access) return;
        if (conversation?.Mode != "chatgpt" || !access.ChatGpt.Enabled) { SetStatus("먼저 ChatGPT 방식과 이 프로젝트 접근을 허용해줘."); return; }
        if (access.ChatGpt.ConnectionKind != "web" || access.ChatGpt.ProtectedTunnelKey.Length == 0) { ShowChatGptSetup(); return; }
        StopChatGptWeb(); chatGptWorkspace?.Revoke();
        var cancel = chatGptWebStart = new CancellationTokenSource(); operation = cancel; SetBusy(true);
        try
        {
            string key = UnprotectTunnelKey(access.ChatGpt.ProtectedTunnelKey, session.Project.Identity);
            var setup = new ChatGptWebSetup(); string exe = await setup.Prepare(false, cancel.Token);
            await RunChatGptWeb(exe, access.ChatGpt.TunnelId, key, cancel.Token);
        }
        catch (OperationCanceledException) { StopChatGptWeb(); }
        catch (Exception e) { StopChatGptWeb(); chatGptWebStatus.Text = e.Message; SetStatus(e.Message); }
        finally
        {
            if (ReferenceEquals(chatGptWebStart, cancel)) chatGptWebStart = null;
            if (ReferenceEquals(operation, cancel)) operation = null;
            cancel.Dispose(); SetBusy(false);
        }
    }
    private async Task RunChatGptWeb(string exe, string tunnelId, string key, CancellationToken token)
    {
        if (session is null || chatGptServer is null) throw new InvalidOperationException("프로젝트 접근을 허용하고 에디터를 열어둬야 해.");
        var tunnel = new ChatGptWebTunnel(); chatGptTunnel = tunnel;
        tunnel.Exited = () => Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (!ReferenceEquals(chatGptTunnel, tunnel)) return;
            chatGptExternalConfirmed = false; chatGptWorkspace?.Revoke(); chatGptConnectionChanged?.Invoke();
            chatGptWebStatus.Text = "웹 연결 프로그램이 종료됐어. 연결 설정에서 ID·키·계정 권한을 확인하고 다시 시도해줘.";
        }));
        chatGptWebStatus.Text = "웹 연결 프로그램 시작 중…";
        try
        {
            await tunnel.Start(exe, McpExecutable, session.Project.Manifest, tunnelId, key, true, token);
            token.ThrowIfCancellationRequested();
            chatGptWebStatus.Text = "웹 연결 프로그램 실행 중 · 계정의 플러그인을 연결하고 실제 도구 호출을 확인해줘.";
        }
        catch { if (ReferenceEquals(chatGptTunnel, tunnel)) chatGptTunnel = null; tunnel.Dispose(); throw; }
    }
}
