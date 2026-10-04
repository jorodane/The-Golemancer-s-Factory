using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
namespace Confectory.Editor.Android;
public sealed partial class MainActivity
{
    private void BuildMobileConversation(MobileWorker worker)
    {
        var character = worker.Character = new(this) { Orientation = Orientation.Vertical };
        var close = AiAction("×", () => studioSession.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Hidden)); character.AddView(close, new LinearLayout.LayoutParams(Dp(38), Dp(30)) { Gravity = GravityFlags.Right });
        worker.Question = HomeLabel("", 12); worker.Question.SetTextColor(Color.Rgb(32, 39, 51)); worker.Question.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        var question = new ScrollView(this); question.Background = BubbleShape(Color.Rgb(214, 217, 222)); question.AddView(worker.Question); character.AddView(question, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(62)) { LeftMargin = Dp(28), RightMargin = Dp(8), BottomMargin = Dp(8) });
        var answer = new LinearLayout(this) { Orientation = Orientation.Horizontal }; answer.SetGravity(GravityFlags.CenterVertical);
        worker.Previous = AiAction("◀", () => MoveMobileTurn(worker, worker.Turn - 1)); answer.AddView(worker.Previous, new LinearLayout.LayoutParams(Dp(34), Dp(50)));
        worker.Bubble = HomeLabel("대화를 시작해줘.", 13); worker.Bubble.SetTextColor(Color.Rgb(48, 41, 20)); worker.Bubble.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        var speech = new ScrollView(this); speech.Background = BubbleShape(Color.Rgb(248, 218, 121)); speech.AddView(worker.Bubble); answer.AddView(speech, new LinearLayout.LayoutParams(0, Dp(145), 1));
        worker.Next = AiAction("▶", () => MoveMobileTurn(worker, worker.Turn + 1)); answer.AddView(worker.Next, new LinearLayout.LayoutParams(Dp(34), Dp(50))); character.AddView(answer);
        worker.Bubble.Click += (_, _) => ReadMobileWorker(worker);
        worker.Dots = new(this) { Orientation = Orientation.Horizontal }; worker.Dots.SetGravity(GravityFlags.Center); character.AddView(worker.Dots, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(30)));
        worker.Package = new(this) { Orientation = Orientation.Vertical }; character.AddView(worker.Package, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(32)));
        var helper = mobileDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId); string path = helper is null ? "" : File.Exists(helper.CharacterPath) ? helper.CharacterPath : helper.AvatarPath;
        View avatar;
        if (File.Exists(path)) { var image = new ImageView(this); image.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(path))); image.SetScaleType(ImageView.ScaleType.FitCenter); avatar = image; }
        else avatar = new TextView(this) { Text = "◇", TextSize = 48, Gravity = GravityFlags.Center };
        character.AddView(avatar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(80)));
        worker.Caption = HomeLabel(worker.Participant.Name, 12); worker.Caption.Gravity = GravityFlags.Center; character.AddView(worker.Caption);
        worker.Composer = new(this) { Orientation = Orientation.Vertical }; character.AddView(worker.Composer);
        if (studioSession.Collaboration.CanControl("human", worker.Participant.Id))
        {
            var input = new EditText(this) { Hint = "대화", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine }; input.SetMaxLines(2); worker.Composer.AddView(input);
            var actions = new LinearLayout(this); actions.AddView(AiAction("보내기", async () => { string text = input.Text?.Trim() ?? ""; if (text.Length == 0 || worker.Cancellation is not null) return; input.Text = ""; await RunMobileWorker(worker, text); })); actions.AddView(AiAction("기록", () => OpenMobileWorkerLog(worker))); actions.AddView(AiAction("중단", () => worker.Cancellation?.Cancel())); worker.Composer.AddView(actions);
        }
        else worker.Composer.AddView(AiAction("프로젝트에서 대화", () => OpenMobileProjectChat("@" + worker.Participant.Id + " ")));
        float startX = 0, startY = 0; double x = 0, y = 0;
        avatar.Touch += (_, e) =>
        {
            var touch = e.Event!; var placement = studioSession.Collaboration.View("human", worker.Participant.Id);
            if (touch.ActionMasked == MotionEventActions.Down) { startX = touch.RawX; startY = touch.RawY; x = placement.X ?? worker.Participant.X; y = placement.Y ?? worker.Participant.Y; character.BringToFront(); avatar.Parent?.RequestDisallowInterceptTouchEvent(true); }
            else if (touch.ActionMasked == MotionEventActions.Move) { double scale = Resources?.DisplayMetrics?.Density ?? 1; placement.X = x + (touch.RawX - startX) / scale; placement.Y = y + (touch.RawY - startY) / scale; PlaceMobileWorker(worker); }
            else if (touch.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) { studioSession.Collaboration.Save(); ReadMobileWorker(worker); }
            e.Handled = true;
        };
        mobileWorkerLayer.AddView(character, new FrameLayout.LayoutParams(Dp(300), ViewGroup.LayoutParams.WrapContent));
        var view = studioSession.Collaboration.View("human", worker.Participant.Id); character.TranslationX = Dp((int)(view.X ?? worker.Participant.X)); character.TranslationY = Dp((int)(view.Y ?? worker.Participant.Y));
        BindMobileYogiDrop(character, box => ReceiveMobileYogi(worker, box)); RenderMobileWorker(worker);
    }
    private GradientDrawable BubbleShape(Color color) { var shape = new GradientDrawable(); shape.SetColor(color); shape.SetCornerRadius(Dp(16)); return shape; }
    private void MoveMobileTurn(MobileWorker worker, int index) { worker.Turn = ConversationTimeline.Clamp(index, worker.Exchanges.Count); RenderMobileWorker(worker); ReadMobileWorker(worker); }
    private void RenderMobileConversation(MobileWorker worker)
    {
        if (worker.Character is null) return; var hub = studioSession.Collaboration;
        if (worker.Cancellation is null) worker.Turn = ConversationTimeline.Synchronize(hub, "human", worker.Participant.Id, worker.Exchanges, worker.Turn);
        worker.Turn = ConversationTimeline.Clamp(worker.Turn, worker.Exchanges.Count); var exchange = worker.Exchanges.ElementAtOrDefault(worker.Turn);
        worker.Character.Visibility = hub.View("human", worker.Participant.Id).Display == CharacterDisplay.Full ? ViewStates.Visible : ViewStates.Gone;
        worker.Question.Text = exchange?.User ?? ""; worker.Bubble.Text = exchange is null ? "대화를 시작해줘." : exchange.Answer.Length > 0 ? exchange.Answer : ConversationTimeline.Activity(exchange.State, worker.Cancellation is not null);
        var unread = hub.Unread("human", worker.Participant.Id).Select(m => m.Id).ToHashSet(); worker.Dots.RemoveAllViews();
        foreach (int index in ConversationTimeline.Dots(worker.Turn, worker.Exchanges.Count))
        {
            var turn = worker.Exchanges[index]; var dot = HomeLabel(index == worker.Turn ? "●" : "·", index == worker.Turn ? 14 : 22); dot.Gravity = GravityFlags.Center; dot.SetTextColor(unread.Contains(turn.MessageId) ? Color.Rgb(97, 182, 255) : index == worker.Turn ? HomeAccent : HomeMuted); dot.TooltipText = turn.Preview; dot.ContentDescription = turn.Preview;
            dot.Click += (_, _) => MoveMobileTurn(worker, index); dot.LongClick += (_, _) => Toast.MakeText(this, turn.Preview, ToastLength.Short)?.Show(); worker.Dots.AddView(dot, new LinearLayout.LayoutParams(Dp(23), Dp(30)));
        }
        worker.Previous.Enabled = worker.Turn > 0; worker.Next.Enabled = worker.Turn + 1 < worker.Exchanges.Count; worker.Next.Text = worker.Exchanges.Skip(worker.Turn + 1).Any(t => unread.Contains(t.MessageId)) ? "▶●" : "▶";
        worker.Package.RemoveAllViews(); if (exchange?.Yogi is { } box) worker.Package.AddView(AiAction("📦 " + box.Caption, () => ShowMobileYogiContents(box)));
        worker.Caption.Text = worker.Participant.Name + " · " + ConversationTimeline.Activity(worker.ResultState, worker.Cancellation is not null);
    }
    private void ShowMobileWorkerProfile(View anchor, MobileWorker worker)
    {
        mobileProfile?.Dismiss(); var body = new LinearLayout(this) { Orientation = Orientation.Vertical }; body.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16)); body.SetBackgroundColor(HomePanel);
        var helper = mobileDirectory.Helpers.FirstOrDefault(h => h.Id == worker.Participant.HelperId);
        if (helper is not null && File.Exists(helper.CharacterPath)) { var image = new ImageView(this); image.SetImageURI(global::Android.Net.Uri.FromFile(new Java.IO.File(helper.CharacterPath))); image.SetScaleType(ImageView.ScaleType.FitCenter); body.AddView(image, new LinearLayout.LayoutParams(Dp(180), Dp(120))); }
        body.AddView(MobileAiCircle(worker.Participant.Name, helper?.AvatarPath ?? "", () => { })); body.AddView(HomeLabel(worker.Participant.Name, 20)); body.AddView(HomeLabel(ConversationTimeline.Activity(worker.ResultState, worker.Cancellation is not null), 12));
        if (studioSession.Collaboration.CanControl("human", worker.Participant.Id))
        {
            body.AddView(AiAction("설정", () => { mobileProfile?.Dismiss(); if (helper is not null) OpenMobileHelper(helper); else MobileName("작업자 이름", name => { worker.Participant.Name = name; studioSession.Collaboration.Save(); }); }));
            body.AddView(AiAction("연결 해제", () => MobileHomeAction(() => { if (worker.Cancellation is not null) throw new InvalidOperationException("작업을 먼저 중단해줘."); if (helper is not null) DisconnectMobileHelper(helper); else { worker.Assistant?.Dispose(); worker.Assistant = null; studioSession.Collaboration.Display("human", worker.Participant.Id, CharacterDisplay.Hidden); } mobileProfile?.Dismiss(); })));
        }
        mobileProfileBody = body; mobileProfile = new PopupWindow(body, Dp(230), ViewGroup.LayoutParams.WrapContent, false) { OutsideTouchable = true }; mobileProfile.SetBackgroundDrawable(BubbleShape(HomePanel)); mobileProfile.ShowAsDropDown(anchor, Dp(45), -anchor.Height);
    }
}
