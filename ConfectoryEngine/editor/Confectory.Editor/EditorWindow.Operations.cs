using System.Windows;
using System.Windows.Controls;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private readonly TextBlock operationNotice = Label("", 12, AccentInk);
    private Button? retryOperation, closeOperation, cancelOperation;
    private TaskCompletionSource<bool>? retryDecision;
    private string dismissedOperationError = "", waitingOperationError = "";

    private UIElement OperationHeader()
    {
        var header = new StackPanel(); var row = new DockPanel();
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        retryOperation = Action("다시 시도", () => { retryDecision?.TrySetResult(true); RefreshOperationControls(); });
        retryOperation.ToolTip = "SDK 설치 등 환경 문제를 해결한 뒤 실패한 작업을 다시 실행합니다. Codex 요청과 이미 적용한 변경은 반복하지 않습니다.";
        cancelOperation = Action("작업 취소", () => operation?.Cancel());
        closeOperation = Action("닫기", CloseOperationLog);
        closeOperation.ToolTip = "재시도 대기를 끝내고 남은 실행을 종료합니다. 이미 적용한 파일과 실행 기록은 유지합니다.";
        buttons.Children.Add(retryOperation); buttons.Children.Add(cancelOperation); buttons.Children.Add(closeOperation);
        DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons); row.Children.Add(Label("실행 · 빌드 기록", 12, MutedInk));
        header.Children.Add(row); operationNotice.Visibility = Visibility.Collapsed; header.Children.Add(operationNotice);
        RefreshOperationControls(); return header;
    }
    private void RefreshOperationControls()
    {
        if (retryOperation is null) return;
        bool waiting = retryDecision is not null;
        retryOperation.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        retryOperation.IsEnabled = waiting && !retryDecision!.Task.IsCompleted;
        cancelOperation!.IsEnabled = busy;
        closeOperation!.IsEnabled = !busy || waiting && !retryDecision!.Task.IsCompleted;
    }
    private void ShowOperationLog() { RefreshStudioShell(); }
    private void CloseOperationLog()
    {
        if (busy && (retryDecision is null || retryDecision.Task.IsCompleted)) return;
        if (retryDecision is not null)
        {
            dismissedOperationError = waitingOperationError;
            retryDecision.TrySetResult(false);
        }
        operationNotice.Visibility = Visibility.Collapsed;
        SetStatus("재시도 대기를 끝냈어. 실행 콘솔은 계속 표시돼."); RefreshOperationControls(); RefreshStudioShell();
    }
    private async Task<bool> AwaitBuildRetry(string title, Exception error, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.InvokeAsync(() =>
        {
            retryDecision = choice; waitingOperationError = error.Message;
            AppendLog(title + " 실패: " + error.Message);
            operationNotice.Text = title + " 실패 · 문제를 해결한 뒤 ‘다시 시도’를 눌러줘. ‘닫기’는 남은 실행을 종료해.";
            operationNotice.Visibility = Visibility.Visible;
            SetStatus("재시도 대기 · SDK 설치를 마쳤다면 다시 시도할 수 있어.");
            ShowOperationLog(); RefreshOperationControls();
        });
        try
        {
            using var cancel = token.Register(() => choice.TrySetCanceled());
            bool again = await choice.Task.ConfigureAwait(false); token.ThrowIfCancellationRequested(); return again;
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(retryDecision, choice))
                {
                    retryDecision = null; waitingOperationError = ""; operationNotice.Visibility = Visibility.Collapsed;
                    RefreshOperationControls(); RefreshStudioShell();
                }
            });
        }
    }
    private async Task RunBuildWithRetry(string title, Func<Task> action, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { SetStatus(title + " 실행 중…"); await action(); return; }
            catch (Exception e) when (e is not OperationCanceledException && !token.IsCancellationRequested)
            { if (!await AwaitBuildRetry(title, e, token)) throw; }
        }
    }
}
