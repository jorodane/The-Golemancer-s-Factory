using Golemancer.Contracts;
using Golemancer.Desktop;
using SkiaSharp;
namespace Golemancer.Presentation;

public sealed partial class GameScreen
{
    private sealed class MenuFrame(string title, Func<List<BubbleEntry>> build, SKPoint center)
    { public string Title = title; public Func<List<BubbleEntry>> Build = build; public SKPoint Center = center; public int Page; }
    private readonly List<MenuFrame> menus = new();
    private int focused;
    private bool oneModifier, allModifier;
    private static BubbleEntry Leaf(string id, string label, Action run, bool enabled = true, string icon = "") => new() { Id = id, Label = label, Activate = run, Enabled = enabled, IconId = icon };
    private static BubbleEntry Group(string id, string label, Func<List<BubbleEntry>> children, string icon = "") => new() { Id = id, Label = label, BuildChildren = children, IconId = icon, Keep = true };
    private void OpenRoot(string title, Func<List<BubbleEntry>> build, SKPoint? at = null, bool preserveDraft = false)
    {
        if (!preserveDraft && memoryDraft is { Capturing: false }) DiscardDraft();
        queued = input.Held("queue"); oneModifier = input.Held("quantity.one"); allModifier = input.Held("quantity.all");
        ClearControls(); menus.Clear(); menus.Add(new(title, build, at ?? new(viewWidth / 2, viewHeight / 2))); focused = 0;
    }
    private void CloseMenu() { if (memoryDraft is { Capturing: false }) DiscardDraft(); menus.Clear(); focused = 0; session.MenuPaused = false; queued = false; }
    private void Finish(Func<ActionResult> command) { if (command().Ok) CloseMenu(); }
    private void Back()
    {
        if (Game.State.Dialogues.Count > 0) { AdvanceDialogue(); return; }
        if (building.Length > 0 || targetCommand.Length > 0) { building = targetCommand = ""; return; }
        if (menus.Count > 1) { menus.RemoveAt(menus.Count - 1); focused = 0; return; }
        if (menus.Count > 0) { DiscardDraft(); CloseMenu(); return; }
        if (!session.Started) return;
        OpenRoot("공방 메뉴", () => [Leaf("resume", "계속하기", CloseMenu), Leaf("save", "저장", () => { session.Save(); Notify("저장했어."); CloseMenu(); }),
            Leaf("load", "불러오기", () => Confirm("저장된 공방을 불러올까?", () => { DiscardDraft(); session.Load("manual"); ClearControls(); Center(); CloseMenu(); }), session.HasSave("manual")),
            Leaf("new", "새 공방", () => Confirm("새 공방을 시작할까?", () => { DiscardDraft(); session.NewGame(); ClearControls(); Center(); CloseMenu(); })),
            Leaf("help", "조작 안내", () => Details("조작 안내", "일상: 스틱은 카메라, 바닥 터치는 이동\n전투: 스틱 이동 · 공격 / 구르기\n길게 터치: 주변 행동 메뉴\n예약 버튼을 누른 채 다른 손가락으로 행동 선택\n가방 / 골렘 / 행동: 우측 상단\n게임패드: 왼쪽 스틱 이동, 오른쪽 스틱 조준, A 사용, X 메뉴, Y 모드, R1 구르기\n메뉴: 방향키 선택, A 확인, B 뒤로"))]);
        session.MenuPaused = true;
    }
    private void ActivateFocused()
    {
        if (menus.Count == 0) return;
        var frame = menus[menus.Count - 1]; var entries = BubbleMenu.Visible(frame.Build()).Skip(frame.Page * 8).Take(8).ToList();
        if (entries.Count > 0) Activate(entries[((focused % entries.Count) + entries.Count) % entries.Count], frame.Center);
    }
    private void Activate(BubbleEntry entry, SKPoint position)
    {
        if (!entry.Available)
        { var p = entry.Preview?.Invoke(); Details(entry.Label, p is null ? entry.Hint : PreviewText(p)); return; }
        if ((oneModifier || allModifier || input.Held("quantity.one") || input.Held("quantity.all")) && entry.Quantity is not null) { entry.Quantity(); return; }
        if (entry.IsGroup) { menus.Add(new(entry.Label, entry.Contents, position)); focused = 0; }
        else entry.Activate!();
    }
    private static string PreviewText(BubblePreview p) => p.Description + "\n" + p.Note + "\n" + string.Join("\n", p.Materials.Select(m => $"{m.Name} {m.Available}/{m.Required}"));
    private void RenderMenu(SKCanvas c)
    {
        hit.Clear(); // The modal ring owns the entire surface, including the HUD behind it.
        Box(c, SKRect.Create(viewWidth, viewHeight), "#98102019", 0);
        var frame = menus[menus.Count - 1]; var entries = BubbleMenu.Visible(frame.Build());
        int pages = BubbleLayout.Pages(entries.Count); frame.Page = Math.Min(frame.Page, pages - 1);
        var shown = entries.Skip(frame.Page * 8).Take(8).ToList();
        frame.Center = new(Math.Max(160, Math.Min(viewWidth - 160, frame.Center.X)), Math.Max(175, Math.Min(viewHeight - 175, frame.Center.Y)));
        var center = frame.Center;
        Text(c, frame.Title + (queued ? " · 예약" : ""), center.X, center.Y - 145, 17, centered: true);
        if (shown.Count == 0) Text(c, "사용할 항목이 없어", center.X, center.Y, 15, centered: true);
        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i]; var offset = BubbleLayout.Offset(i, shown.Count); var p = new SKPoint(center.X + (float)offset.X, center.Y + (float)offset.Y);
            using var paint = new SKPaint { Color = Color(entry.Available ? "#F1E3C0" : "#AA6A7969"), IsAntialias = true };
            c.DrawCircle(p, 27, paint);
            string icon = entry.IconId.Length > 0 ? entry.IconId : entry.ItemId.Length > 0 ? "item." + entry.ItemId : "";
            if (icon.Length > 0) art.Sprite(c, icon, SKRect.Create(p.X - 22, p.Y - 22, 44, 44));
            else Text(c, entry.IsGroup ? "＋" : "•", p.X, p.Y + 6, 22, "#254634", true);
            if (((focused % Math.Max(1, shown.Count)) + shown.Count) % Math.Max(1, shown.Count) == i)
            { paint.Style = SKPaintStyle.Stroke; paint.Color = Color("#FFF3C1"); paint.StrokeWidth = 3; c.DrawCircle(p, 31, paint); }
            Box(c, SKRect.Create(p.X - 49, p.Y + 23, 98, 24), "#DC1B3227", 4);
            Text(c, entry.Label.Length > 13 ? entry.Label.Substring(0, 12) + "…" : entry.Label, p.X, p.Y + 40, 12, centered: true);
            int index = i; hit.Add((SKRect.Create(p.X - 35, p.Y - 30, 70, 76), "bubble:" + i, () => { focused = index; Activate(entry, p); }));
        }
        if (menus.Count > 1) Button(c, "back", "뒤로", SKRect.Create(center.X - 29, center.Y - 21, 58, 42), Back);
        if (pages > 1)
        {
            Button(c, "prev", "‹", SKRect.Create(center.X - 82, center.Y + 135, 42, 32), () => { frame.Page = (frame.Page + pages - 1) % pages; focused = 0; });
            Text(c, $"{frame.Page + 1}/{pages}", center.X, center.Y + 157, 13, centered: true);
            Button(c, "next", "›", SKRect.Create(center.X + 40, center.Y + 135, 42, 32), () => { frame.Page = (frame.Page + 1) % pages; focused = 0; });
        }
        if (shown.Count > 0 && shown[((focused % shown.Count) + shown.Count) % shown.Count].Preview?.Invoke() is { } preview)
        {
            float x = center.X + 172; if (x + 260 > viewWidth) x = center.X - 432;
            if (x >= 8) { Box(c, SKRect.Create(x, center.Y - 128, 255, 225), "#ED20382B"); Text(c, preview.Title, x + 12, center.Y - 102, 15); Wrap(c, PreviewText(preview), x + 12, center.Y - 77, 230, 13, 10); }
        }
    }
}
