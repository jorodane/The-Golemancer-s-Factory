using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Confectory.Workspace;
using Confectory.EditorPacks;
namespace Confectory.Editor.Android;
public sealed partial class MainActivity
{
    private FrameLayout mobileYogiSurface = null!;
    private LinearLayout mobileYogiTray = null!;
    private YogiGestureView mobileYogiOverlay = null!;
    private readonly Dictionary<View, string> mobileYogiTargets = [];
    private string mobileYogiId = "";
    private YogiBox? mobileDraggedYogi;
    private Button? mobileIncidentBubble;
    private sealed class YogiGestureView(Context context) : View(context)
    {
        public RectF? Selection;
        protected override void OnDraw(Canvas canvas) { base.OnDraw(canvas); if (Selection is not { } rect) return; using var fill = new Paint { Color = Color.Argb(55, 105, 209, 189) }; canvas.DrawRect(rect, fill); using var edge = new Paint { Color = Color.Rgb(105, 209, 189), StrokeWidth = 2 }; edge.SetStyle(Paint.Style.Stroke); canvas.DrawRect(rect, edge); }
    }
    internal View MarkMobileYogi(View view, string key) { mobileYogiTargets[view] = key; return view; }
    private void AddMobileYogi(FrameLayout surface)
    {
        mobileYogiSurface = surface; mobileYogiTray = new(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone }; mobileYogiTray.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10)); mobileYogiTray.Background = BubbleShape(HomePanel);
        surface.AddView(mobileYogiTray, new FrameLayout.LayoutParams(Dp(290), ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left) { LeftMargin = Dp(12), TopMargin = Dp(12) });
        mobileYogiOverlay = new(this) { Visibility = ViewStates.Gone }; surface.AddView(mobileYogiOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        float sx = 0, sy = 0;
        mobileYogiOverlay.Touch += (_, e) =>
        {
            var touch = e.Event!; float x = touch.GetX(), y = touch.GetY();
            try
            {
                if (touch.ActionMasked == MotionEventActions.Down) { sx = x; sy = y; }
                else if (touch.ActionMasked == MotionEventActions.Move) { mobileYogiOverlay.Selection = new RectF(Math.Min(sx, x), Math.Min(sy, y), Math.Max(sx, x), Math.Max(sy, y)); mobileYogiOverlay.Invalidate(); }
                else if (touch.ActionMasked == MotionEventActions.Up)
                {
                    var box = EditingMobileYogi();
                    if (Math.Abs(x - sx) + Math.Abs(y - sy) > Dp(6))
                    {
                        mobileYogiOverlay.Visibility = mobileYogiTray.Visibility = ViewStates.Gone;
                        var visual = new YogiVisual { Label = "프로젝트 화면", Image = CaptureMobileYogi(new Rect((int)Math.Min(sx, x), (int)Math.Min(sy, y), (int)Math.Max(sx, x), (int)Math.Max(sy, y))) };
                        box.Looks.Add(visual); try { box.Validate(); } catch { box.Looks.Remove(visual); throw; }
                    }
                    else
                    {
                        var target = MobileYogiHit(surface, x, y) ?? throw new InvalidOperationException("프로젝트 요소를 선택해줘."); string key = mobileYogiTargets.TryGetValue(target, out var stored) ? stored : "ui:" + Guid.NewGuid().ToString("N"); mobileYogiTargets[target] = key;
                        var reference = studioSession.Index.Nodes.ContainsKey(key) ? studioSession.YogiReference(key) : new YogiReference { Project = studioSession.Project.Id, Key = key, Label = target is TextView text ? (text.Text ?? "UI").Substring(0, Math.Min(200, text.Text?.Length ?? 0)) : target.GetType().Name };
                        if (box.Exactly.Count >= 32) throw new InvalidOperationException("EY는 박스마다 32개까지 담을 수 있어."); if (!box.Exactly.Any(r => r.Key == key)) box.Exactly.Add(reference);
                    }
                    studioSession.Collaboration.SaveYogi("human", box); mobileYogiOverlay.Selection = null; OpenMobileYogiBox(); mobileYogiOverlay.Visibility = ViewStates.Visible;
                }
                else if (touch.ActionMasked == MotionEventActions.Cancel) { mobileYogiOverlay.Selection = null; mobileYogiOverlay.Invalidate(); }
            }
            catch (Exception error) { mobileYogiOverlay.Visibility = ViewStates.Gone; OpenMobileYogiBox(); Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
            e.Handled = true;
        };
    }
    private View? MobileYogiHit(View rootView, float x, float y)
    {
        if (rootView.Visibility != ViewStates.Visible || rootView == mobileYogiOverlay || rootView == mobileYogiTray || rootView == mobileWorkerLayer || rootView == mobileProjectActions) return null;
        int[] origin = new int[2], at = new int[2]; mobileYogiSurface.GetLocationOnScreen(origin); rootView.GetLocationOnScreen(at);
        if (x + origin[0] < at[0] || y + origin[1] < at[1] || x + origin[0] > at[0] + rootView.Width || y + origin[1] > at[1] + rootView.Height) return null;
        if (rootView is ViewGroup group) for (int i = group.ChildCount - 1; i >= 0; i--) if (MobileYogiHit(group.GetChildAt(i)!, x, y) is { } hit) { for (View? node = hit; node is not null && node != mobileYogiSurface; node = node.Parent as View) if (mobileYogiTargets.ContainsKey(node)) return node; return hit; }
        return rootView;
    }
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode is Keycode.ShiftLeft or Keycode.ShiftRight) mobileConceptShift?.Invoke(true);
        if (keyCode == Keycode.Y && e?.IsCtrlPressed == true) { ToggleMobileYogi(); return true; }
        if (keyCode is Keycode.Escape or Keycode.Back && mobileYogiOverlay?.Visibility == ViewStates.Visible) { mobileYogiOverlay.Visibility = ViewStates.Gone; return true; }
        return base.OnKeyDown(keyCode, e);
    }
    private void ToggleMobileYogi()
    {
        mobileYogiOverlay.Visibility = mobileYogiOverlay.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible;
        if (mobileYogiOverlay.Visibility == ViewStates.Visible) { mobileYogiOverlay.BringToFront(); Toast.MakeText(this, "Yogi · 탭은 EY, 드래그는 LaY · Ctrl+Y 또는 뒤로로 마치기", ToastLength.Long)?.Show(); }
    }
    private YogiBox EditingMobileYogi()
    {
        var hub = studioSession.Collaboration; var box = hub.State.YogiBoxes.FirstOrDefault(b => b.Id == mobileYogiId && b.Author == "human");
        if (box is null || box.Sealed) { box = hub.NewYogi("human"); mobileYogiId = box.Id; } return box;
    }
    private void ApplyMobileNativeYogi(ContextRequest request, YogiBox box)
    {
        foreach (var reference in box.Exactly.Where(r => r.Project == studioSession.Project.Id && r.Key.StartsWith("ui:", StringComparison.Ordinal)))
        {
            var target = mobileYogiTargets.FirstOrDefault(p => p.Value == reference.Key && p.Key.IsAttachedToWindow).Key; if (target is null) continue;
            int[] at = new int[2]; target.GetLocationOnScreen(at); string label = target is TextView text ? text.Text ?? "" : reference.Label;
            request.UiTargets.Add(new() { Type = target.GetType().Name, Name = reference.Key, Label = label.Substring(0, Math.Min(1600, label.Length)), X = at[0], Y = at[1], Width = target.Width, Height = target.Height }); request.Omitted.RemoveAll(o => o.Contains(reference.Key));
        }
    }
    private SharedEditorImage CaptureMobileYogi(Rect region)
    {
        region.Intersect(0, 0, mobileYogiSurface.Width, mobileYogiSurface.Height); if (region.Width() < 1 || region.Height() < 1) throw new InvalidOperationException("화면 범위를 지정해줘.");
        double scale = Math.Min(1, 1600d / Math.Max(region.Width(), region.Height()));
        for (int attempt = 0; attempt < 5; attempt++, scale *= .72)
        {
            int width = Math.Max(1, (int)(region.Width() * scale)), height = Math.Max(1, (int)(region.Height() * scale)); using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!; using var canvas = new Canvas(bitmap); canvas.Scale((float)scale, (float)scale); canvas.Translate(-region.Left, -region.Top); mobileYogiSurface.Draw(canvas);
            using var stream = new MemoryStream(); bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream); byte[] bytes = stream.ToArray(); if (bytes.Length > 393216) continue;
            return new() { Data = Convert.ToBase64String(bytes), Sha256 = WorkspaceProject.Hash(bytes), Width = width, Height = height };
        }
        throw new InvalidOperationException("LaY 범위를 줄여줘.");
    }
    private SharedEditorImage CaptureMobileProjectYogi()
    {
        var views = new View[] { mobileWorkerLayer, mobileYogiTray, mobileYogiOverlay, mobileParticipantNotices };
        var states = views.Select(v => v.Visibility).ToArray();
        try { foreach (var view in views) view.Visibility = ViewStates.Invisible; return CaptureMobileYogi(new Rect(0, 0, mobileYogiSurface.Width, mobileYogiSurface.Height)); }
        finally { for (int i = 0; i < views.Length; i++) views[i].Visibility = states[i]; }
    }
    private void OpenMobileYogiBox()
    {
        var hub = studioSession.Collaboration; var box = hub.State.YogiBoxes.FirstOrDefault(b => b.Id == mobileYogiId && b.Author == "human") ?? hub.State.YogiBoxes.LastOrDefault(b => b.Author == "human") ?? hub.NewYogi("human"); mobileYogiId = box.Id;
        mobileYogiTray.RemoveAllViews(); mobileYogiTray.Visibility = ViewStates.Visible; mobileYogiTray.AddView(AiAction("📦 YogiBox " + box.Count + "   ×", () => mobileYogiTray.Visibility = ViewStates.Gone));
        if (box.Sealed)
        {
            var parcel = HomeLabel(box.Caption + "\n드래그해서 전달", 14); parcel.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8)); mobileYogiTray.AddView(parcel); long tap = 0;
            parcel.Click += (_, _) => { long now = global::Android.OS.SystemClock.UptimeMillis(); if (now - tap < 320) { box.Open(); hub.SaveYogi("human", box); OpenMobileYogiBox(); } tap = now; };
            parcel.LongClick += (_, e) => { mobileDraggedYogi = box.Copy(); parcel.StartDragAndDrop(ClipData.NewPlainText("Confectory.YogiBox", box.Id), new View.DragShadowBuilder(parcel), null, 0); e.Handled = true; };
        }
        else
        {
            var rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
            foreach (var reference in box.Exactly.ToArray()) { var row = new LinearLayout(this); row.AddView(AiAction("EY " + reference.Label, () => NavigateMobileYogi(reference)), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)); row.AddView(AiAction("×", () => { box.Exactly.Remove(reference); hub.SaveYogi("human", box); OpenMobileYogiBox(); })); rows.AddView(row); }
            foreach (var visual in box.Looks.ToArray()) { var row = new LinearLayout(this); row.AddView(AiAction("LaY " + visual.Label, () => ShowMobileYogiImage(visual)), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)); row.AddView(AiAction("×", () => { box.Looks.Remove(visual); hub.SaveYogi("human", box); OpenMobileYogiBox(); })); rows.AddView(row); }
            mobileYogiTray.AddView(ConceptScroll(rows), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(120)));
            var title = ConceptInput(box.Title); title.Hint = "박스 이름"; title.SetFilters([new global::Android.Text.InputFilterLengthFilter(200)]); title.TextChanged += (_, _) => { box.Title = title.Text ?? ""; hub.SaveYogi("human", box); }; mobileYogiTray.AddView(title);
            var explanation = ConceptInput(box.Explanation, true); explanation.Hint = "설명이나 요청"; explanation.SetFilters([new global::Android.Text.InputFilterLengthFilter(16000)]); explanation.TextChanged += (_, _) => { box.Explanation = explanation.Text ?? ""; hub.SaveYogi("human", box); }; mobileYogiTray.AddView(explanation, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(72)));
            var actions = new LinearLayout(this); actions.AddView(AiAction("수집", ToggleMobileYogi)); actions.AddView(AiAction("봉인", () => MobileHomeAction(() => { box.Seal(); hub.SaveYogi("human", box); mobileYogiOverlay.Visibility = ViewStates.Gone; OpenMobileYogiBox(); }))); mobileYogiTray.AddView(actions);
        }
        mobileYogiTray.AddView(AiAction("보관한 박스", () => { var boxes = hub.State.YogiBoxes.Where(b => b.Author == "human").Reverse().ToArray(); new AlertDialog.Builder(this).SetTitle("YogiBox")!.SetItems(boxes.Select(b => b.Caption + (b.Sealed ? " · 봉인됨" : " · 편집 중")).ToArray(), (_, e) => { mobileYogiId = boxes[e.Which].Id; OpenMobileYogiBox(); })!.Show(); }));
        mobileYogiTray.AddView(AiAction("+ 새 박스", () => { mobileYogiId = hub.NewYogi("human").Id; OpenMobileYogiBox(); }));
    }
    private void BindMobileYogiDrop(View view, Action<YogiBox> receive)
    {
        view.Drag += (_, e) =>
        {
            var drag = e.Event!; bool ours = mobileDraggedYogi is not null && drag.ClipDescription?.Label?.ToString() == "Confectory.YogiBox";
            e.Handled = ours; if (!ours) return;
            if (drag.Action == DragAction.Drop) MobileHomeAction(() => { var box = mobileDraggedYogi!.Copy(); box.Validate(true); receive(box); });
        };
    }
    private async void ReceiveMobileYogi(MobileWorker worker, YogiBox box)
    {
        try
        {
            var hub = studioSession.Collaboration;
            if (!hub.CanControl("human", worker.Participant.Id)) { var message = hub.Post("human", "@" + worker.Participant.Id + " " + box.Explanation, yogi: box); await ReplyMobileMentions(message); return; }
            hub.DeliverYogi("human", box, "direct", worker.Participant.Id);
            if (worker.Cancellation is not null) { worker.YogiQueue.Enqueue(box.Copy()); Toast.MakeText(this, "현재 작업 뒤에 YogiBox를 처리해.", ToastLength.Short)?.Show(); return; }
            worker.PendingYogi = box.Copy(); await RunMobileWorker(worker, box.Explanation.Length > 0 ? box.Explanation : box.Caption);
        }
        catch (Exception e) { Report(e.Message); }
    }
    private void ShowMobileYogiContents(YogiBox box)
    {
        var body = ConceptColumn(); body.AddView(HomeLabel(box.Caption, 20)); body.AddView(HomeLabel(box.Explanation, 14));
        foreach (var reference in box.Exactly) body.AddView(AiAction("EY · " + reference.Label + (studioSession.YogiExists(reference) || reference.Key.StartsWith("ui:", StringComparison.Ordinal) || reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal) ? "" : " · 삭제된 대상"), () => NavigateMobileYogi(reference)));
        foreach (var visual in box.Looks) body.AddView(AiAction("LaY · " + visual.Label + " · " + visual.CapturedUtc, () => ShowMobileYogiImage(visual)));
        body.AddView(AiAction("내 YogiBox에서 수정", () => { var copy = box.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Author = "human"; copy.Open(); studioSession.Collaboration.State.YogiBoxes.Add(copy); studioSession.Collaboration.Save(); mobileYogiId = copy.Id; OpenMobileYogiBox(); }));
        new AlertDialog.Builder(this).SetTitle("YogiBox")!.SetView(ConceptScroll(body))!.SetPositiveButton("닫기", (_, _) => { })!.Show();
    }
    private void ShowMobileYogiImage(YogiVisual visual)
    {
        byte[] bytes = Convert.FromBase64String(visual.Image.Data); var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length); var image = new ImageView(this); image.SetImageBitmap(bitmap); image.SetScaleType(ImageView.ScaleType.FitCenter);
        var dialog = new Dialog(this); dialog.SetTitle("LaY · " + visual.CapturedUtc); dialog.SetContentView(image); dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent); dialog.DismissEvent += (_, _) => { image.SetImageBitmap(null); bitmap?.Dispose(); };
    }
    private void NavigateMobileYogi(YogiReference reference) => MobileHomeAction(() =>
    {
        if (reference.Project != studioSession.Project.Id) throw new InvalidOperationException("이 EY의 프로젝트를 먼저 열어줘.");
        if (reference.Key.StartsWith("ui:", StringComparison.Ordinal)) { var target = mobileYogiTargets.FirstOrDefault(p => p.Value == reference.Key && p.Key.IsAttachedToWindow).Key; if (target is null) throw new InvalidOperationException("삭제되었거나 닫힌 UI 요소야."); target.RequestFocus(); return; }
        if (reference.Key.StartsWith(EditorYogiContext.Prefix, StringComparison.Ordinal)) { var parts = EditorYogiContext.Parts(reference.Key); var window = windows.Definitions.FirstOrDefault(d => d.View == parts.View) ?? throw new InvalidOperationException("삭제되었거나 연결되지 않은 Editor View야."); windows.Open(window.Id); return; }
        studioSession.Refresh(); if (!studioSession.YogiExists(reference)) throw new InvalidOperationException("삭제된 대상 · " + reference.Label); mobileConceptSpace = null;
        string key = reference.Key, id = key.Substring(key.IndexOf(':') + 1);
        if (key.StartsWith("concept-object:", StringComparison.Ordinal)) MobileConceptObjects(MobileSpace.Objects.Single(o => o.Id == id).Concept);
        else if (key.StartsWith("function:", StringComparison.Ordinal)) MobileConceptFunction(MobileSpace.Implementations.Single(f => f.Id == id));
        else if (key.StartsWith("concept-view:", StringComparison.Ordinal)) { var view = MobileSpace.Views.Single(v => v.Id == id); MobileConceptObjects(view.Concept, view.Id); }
        else if (key.StartsWith("concept:", StringComparison.Ordinal) || key.StartsWith("concept-category:", StringComparison.Ordinal)) MobileConceptMap();
        else if (key.StartsWith("pack:", StringComparison.Ordinal)) MobileConceptPacks();
        else OpenMobileElementXml(key);
        mobileConceptPageHost.Post(() => { var target = mobileYogiTargets.FirstOrDefault(p => p.Value == key && p.Key.IsAttachedToWindow).Key; target?.RequestRectangleOnScreen(new Rect(0, 0, target.Width, target.Height)); target?.RequestFocus(); });
    });
    private Button BuildMobileIncidentBubble()
    {
        mobileIncidentBubble = AiAction("신문고", MobileIncidents); BindMobileYogiDrop(mobileIncidentBubble, RegisterMobileYogiIncident); return mobileIncidentBubble;
    }
    private void RefreshMobileIncidentBubble()
    {
        if (mobileIncidentBubble is null || studioSession is null) return; var hub = studioSession.Collaboration;
        mobileIncidentBubble.Text = "신문고" + (hub.PendingIncidentCount > 0 ? "  " + hub.IncidentBadge : ""); mobileIncidentBubble.SetTextColor(hub.PendingSeverity switch { IncidentSeverity.Urgent => HomeMain, IncidentSeverity.Blocked => Color.Rgb(240, 161, 76), IncidentSeverity.Warning => Color.Rgb(248, 218, 121), _ => HomeAccent });
    }
    private void RegisterMobileYogiIncident(YogiBox box)
    {
        int severity = 0; new AlertDialog.Builder(this).SetTitle("📦 " + box.Caption)!.SetSingleChoiceItems(new[] { "일반", "중요", "차단", "긴급" }, 0, (_, e) => severity = e.Which)!.SetPositiveButton("등록", (_, _) => MobileHomeAction(() => studioSession.Collaboration.Report("human", IncidentKind.Incident, (IncidentSeverity)severity, box.Caption, "", "", box.Explanation, yogi: box)))!.SetNegativeButton("취소", (_, _) => { })!.Show();
    }
    private void RenderMobileChat(LinearLayout body, IEnumerable<CollaborationMessage> messages)
    {
        body.RemoveAllViews(); foreach (var message in messages) { body.AddView(HomeLabel(MobileParticipantName(message.Author) + "\n" + message.Text, 12)); if (message.Yogi is { } box) body.AddView(AiAction("📦 " + box.Caption, () => ShowMobileYogiContents(box))); }
    }
    private void OpenMobileInbox(string id)
    {
        var hub = studioSession.Collaboration; var body = ConceptColumn(); RenderMobileChat(body, hub.State.Messages.Where(m => m.Channel == "direct" && hub.CanRead("human", m) && (m.Author == id || m.Recipient == id)));
        BindMobileYogiDrop(body, box => { hub.DeliverYogi("human", box, "direct", id); body.AddView(AiAction("📦 " + box.Caption, () => ShowMobileYogiContents(box))); });
        var entry = ConceptInput("", true); body.AddView(entry); body.AddView(AiAction("보내기", () => { hub.Post("human", entry.Text ?? "", "direct", recipient: id); entry.Text = ""; }));
        new AlertDialog.Builder(this).SetTitle(MobileParticipantName(id))!.SetView(ConceptScroll(body))!.SetPositiveButton("닫기", (_, _) => { })!.Show();
    }
}
