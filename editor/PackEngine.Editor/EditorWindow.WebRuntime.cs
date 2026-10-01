using System.Text;
using System.Windows;
using PackEngine.Installation;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private CancellationTokenSource? webInstallCancellation;
    private async void InstallWebRuntime()
    {
        if (webDisposed || webInstallCancellation is not null) return;
        if (MessageBox.Show(this, "ChatGPT 웹 패널에 필요한 Microsoft Edge WebView2 Runtime을 Microsoft에서 내려받아 이 PC에 설치할까?\n\nMicrosoft의 자동 업데이트가 적용되는 웹 실행 구성 요소야.",
            "웹 실행 구성 요소 준비", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        using var cancellation = new CancellationTokenSource(); webInstallCancellation = cancellation;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PackEngine", "Setup", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string installer = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");
            ShowBrowserFallback("웹 실행 구성 요소를 내려받고 있어…", false);
            webFallback.Children.Add(Action("설치 취소", () => cancellation.Cancel()));
            // Microsoft documents this Evergreen bootstrapper link in MsalError.WebView2NotInstalled.
            await ChatGptSetup.DownloadFile("https://go.microsoft.com/fwlink/p/?LinkId=2124703", installer, cancellation.Token);
            if (webDisposed) return;
            webStatus.Text = "Microsoft 서명을 확인하는 중…";
            using (var locked = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                string script = "$ErrorActionPreference = 'Stop'; $s = Get-AuthenticodeSignature -LiteralPath '" + installer.Replace("'", "''") +
                    "'; if ($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch '(?:^|,\\s*)O=Microsoft Corporation(?:,|$)') { throw 'Microsoft signature verification failed' }";
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                await SetupProcess.Run(powershell, new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }, _ => { }, cancellation.Token);
                if (webDisposed) return;
                webStatus.Text = "Microsoft 웹 실행 구성 요소를 설치하는 중…";
                await SetupProcess.Run(installer, new[] { "/silent", "/install" }, _ => { }, cancellation.Token);
            }
            if (!webDisposed) { webStatus.Text = "웹 패널을 다시 여는 중…"; await InitializeBrowser(); }
        }
        catch (OperationCanceledException) { if (!webDisposed) ShowBrowserFallback("웹 실행 구성 요소 준비를 취소했어. 다시 시도할 수 있어.", true); }
        catch (Exception error)
        {
            if (!webDisposed) { ShowBrowserFallback("웹 실행 구성 요소 준비를 마치지 못했어. 다시 시도해줘.", true); AppendLog("웹 구성 요소 설치: " + error.Message); }
        }
        finally
        {
            webInstallCancellation = null;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
