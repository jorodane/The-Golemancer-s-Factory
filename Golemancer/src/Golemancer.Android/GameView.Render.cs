using Golemancer.Contracts;
using Golemancer.Desktop;
using SkiaSharp;
namespace Golemancer.Android;

internal sealed partial class GameView
{
    private readonly SKTypeface korean = SKFontManager.Default.MatchCharacter('한') ?? SKTypeface.Default;
    private static SKColor Color(string hex) => SKColor.Parse(hex);
    private void Text(SKCanvas c, string value, float x, float y, float size = 15, string color = "#FFF0D5", bool centered = false)
    {
        using var font = new SKFont(korean, size); using var paint = new SKPaint { Color = Color(color), IsAntialias = true };
        c.DrawText(value, x, y, centered ? SKTextAlign.Center : SKTextAlign.Left, font, paint);
    }
    private static void Box(SKCanvas c, SKRect bounds, string color, float radius = 12)
    { using var paint = new SKPaint { Color = Color(color), IsAntialias = true }; c.DrawRoundRect(bounds, radius, radius, paint); }
    private void Wrap(SKCanvas c, string text, float x, float y, float width, float size = 15, int maxLines = 5)
    {
        using var font = new SKFont(korean, size); string line = ""; int count = 0;
        foreach (char ch in text)
        {
            if (ch == '\n' || font.MeasureText(line + ch) > width)
            { Text(c, line, x, y, size); line = ""; y += size * 1.45f; if (++count >= maxLines) return; }
            if (ch != '\n') line += ch;
        }
        Text(c, line, x, y, size);
    }
    private void Button(SKCanvas c, string id, string label, SKRect rect, Action run, bool active = false)
    { Box(c, rect, active ? "#DBB765" : "#DD244637"); Text(c, label, rect.MidX, rect.MidY + 5, 14, active ? "#20352C" : "#FFF0D5", true); hit.Add((rect, id, run)); }
    private void Render(SKCanvas c, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        scale = Math.Min(width / 960f, height / 540f); viewWidth = width / scale; viewHeight = height / scale;
        drawnCamera = camera.Capture(viewWidth, viewHeight);
        c.Clear(Color("#172A22")); c.Save(); c.Scale(scale); hit.Clear();
        RenderWorld(c);
        if (!session.Started)
        {
            Box(c, SKRect.Create(0, 0, viewWidth, viewHeight), "#A0152923", 0);
            Text(c, "The Golemancer’s Factory", viewWidth / 2, viewHeight / 2 - 70, 32, centered: true);
            Text(c, "엔린의 작은 골렘 공방", viewWidth / 2, viewHeight / 2 - 36, 18, centered: true);
            Button(c, "start", "새 공방", SKRect.Create(viewWidth / 2 - 160, viewHeight / 2, 150, 48), () => { session.NewGame(); Center(); ClearControls(); });
            Button(c, "continue", session.HasSave("autosave") ? "이어하기" : "저장 없음", SKRect.Create(viewWidth / 2 + 10, viewHeight / 2, 150, 48), () => { if (session.HasSave("autosave")) { session.Load("autosave"); Center(); ClearControls(); } });
        }
        else
        {
            RenderHud(c);
            if (Game.State.Dialogues.Count > 0) RenderDialogue(c);
            else if (menus.Count > 0) RenderMenu(c);
        }
        if (clock.Elapsed.TotalSeconds < toastUntil)
        { Box(c, SKRect.Create(viewWidth / 2 - 270, 74, 540, 42), "#EC203A2F"); Wrap(c, notice, viewWidth / 2 - 258, 99, 516, 14, 1); }
        c.Restore();
    }
    private void RenderWorld(SKCanvas c)
    {
        var s = Game.State;
        int left = Math.Max(0, (int)Math.Floor(drawnCamera.Left) - 1), top = Math.Max(0, (int)Math.Floor(drawnCamera.Top) - 1);
        int right = Math.Min(s.Map.Width, (int)Math.Ceiling(drawnCamera.Right) + 1), bottom = Math.Min(s.Map.Height, (int)Math.Ceiling(drawnCamera.Bottom) + 1);
        foreach (var region in terrain.Visible(s.Map, s.Seed, left, top, right, bottom))
        {
            var p = Screen(region.X - region.Gutter, region.Y - region.Gutter);
            c.DrawBitmap(region.Image, SKRect.Create(p.X, p.Y, (float)(region.Width + 2 * region.Gutter) * zoom, (float)(region.Height + 2 * region.Gutter) * zoom));
        }
        foreach (var o in s.Objects.Values.OrderBy(o => o.WorldY + (Game.Definition(o)?.Height ?? 1)))
        {
            var d = Game.Definition(o); if (d is null || o.Get("depleted") > 0) continue;
            if (!poses.TryGetValue(o.Id, out var pose)) pose = ("idle", s.Time, o.WorldX, o.WorldY, -1);
            bool moving = Math.Abs(o.WorldX - pose.X) + Math.Abs(o.WorldY - pose.Y) > .001;
            if (moving) pose.LastMove = s.Time;
            string state = !o.Alive() ? "death" : o.Get("visualUntil") > s.Time ? o.GetText("visualState", "idle") : !Game.CanOperate(o) ? "idle" : o.Work is not null || o.Production.Count > 0 || o.GetText("assembling").Length > 0 ? "work" : s.Time - pose.LastMove < .16 || o.Path.Count > 0 || o.Get("movingUntil") > s.Time ? "move" : "idle";
            if (state != pose.State) pose.Since = s.Time;
            poses[o.Id] = (state, pose.Since, o.WorldX, o.WorldY, pose.LastMove);
            var clip = art.Clip(d.Sprite, state);
            if (!o.Alive() && (clip is null || s.Time - pose.Since > clip.Frames * clip.FrameSeconds)) continue;
            if (o.X < left - 4 || o.X > right + 4 || o.Y < top - 4 || o.Y > bottom + 4) continue;
            var p = Screen(o.WorldX, o.WorldY);
            if (Game.Kind(o) == "drop")
            {
                int i = 0; foreach (var item in o.Inventory.Where(i => i.Value > 0).Take(4))
                { var rect = SKRect.Create(p.X + i % 2 * 19, p.Y + i / 2 * 19, 28, 28); art.Sprite(c, "item." + item.Key, rect); Text(c, item.Value.ToString(), rect.Right - 10, rect.Bottom, 10); i++; } continue;
            }
            var frame = clip is { FrameRects.Count: > 0 } ? clip.FrameRects[Art.Frame(clip, s.Time - pose.Since)] : null;
            float dw = (float)(clip?.DrawWidth ?? d.Width * 1.16) * zoom * (frame is null ? 1 : frame.Width / (float)clip!.FrameWidth);
            float dh = (float)(clip?.DrawHeight ?? d.Height * 1.25) * zoom * (frame is null ? 1 : frame.Height / (float)clip!.FrameHeight);
            var foot = Screen(o.WorldX + d.Width * .5 + (clip?.OffsetX ?? 0), o.WorldY + d.Height * .86 + (clip?.OffsetY ?? 0));
            var bounds = SKRect.Create(foot.X - dw * (float)(frame?.PivotX ?? clip?.PivotX ?? .5), foot.Y - dh * (float)(frame?.PivotY ?? clip?.PivotY ?? .875), dw, dh);
            if (o.Id == s.ControlledId)
            { using var outline = new SKPaint { Color = Color("#FFE6A5"), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true }; c.DrawOval(p.X + zoom / 2, p.Y + zoom * .84f, zoom * .43f, zoom * .18f, outline); }
            c.Save(); if (o.Get("rollUntil") > s.Time && clip?.State != "roll") c.RotateDegrees((float)((s.Time - o.Get("rollStarted")) / .28 * 360), foot.X, foot.Y - dh / 2);
            if (!art.Sprite(c, d.Sprite, bounds, state, s.Time - pose.Since)) Text(c, o.Name, p.X, p.Y, 12);
            c.Restore();
            if (o.Work is { } work) Bar(c, p.X, p.Y - 9, d.Width * zoom, work.Total > 0 ? work.Done / work.Total : 0, "#DBC688");
            if (o.Get("maxHealth") > 0 && o.Get("health") < o.Get("maxHealth")) Bar(c, p.X, p.Y - 4, d.Width * zoom, o.Get("health") / o.Get("maxHealth"), "#D87366");
            if (o.Recording is not null) Text(c, "●", p.X + zoom, p.Y - 6, 15, "#FF7766");
            if (o.Playback is not null) Text(c, "↻", p.X + zoom, p.Y - 6, 17);
        }
        foreach (var id in poses.Keys.Where(id => !s.Objects.ContainsKey(id)).ToArray()) poses.Remove(id);
        using var line = new SKPaint { Color = Color("#E9D898"), StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        foreach (var a in Game.OfKind("golem"))
        {
            var previous = Screen(a.WorldX + .5, a.WorldY + .5);
            foreach (var tile in a.Path) { var next = Screen(tile.X + .5, tile.Y + .5); c.DrawLine(previous, next, line); previous = next; }
            int i = 0;
            foreach (var queuedAction in a.ActionQueue)
            {
                var r = queuedAction.Request; var t = Game.Find(r.TargetId); if (t is null && r.X < 0) continue;
                var next = Screen((t?.WorldX ?? r.X) + .5, (t?.WorldY ?? r.Y) + .5); c.DrawLine(previous, next, line); Text(c, (++i).ToString(), next.X, next.Y, 13); previous = next;
            }
        }
        foreach (var effect in s.Effects)
        {
            var p = Screen(effect.X + .5, effect.Y + .5);
            if (effect.Kind.StartsWith("boss_warn", StringComparison.Ordinal) || effect.Kind is "telegraph" or "charge_warn")
            {
                using var warning = new SKPaint { Color = Color("#66DF583D"), IsAntialias = true };
                if (effect.Kind == "boss_warn_2")
                { using var path = new SKPath(); path.MoveTo(p.X, p.Y - zoom * 7); path.LineTo(p.X + zoom * 7, p.Y); path.LineTo(p.X, p.Y + zoom * 7); path.LineTo(p.X - zoom * 7, p.Y); path.Close(); c.DrawPath(path, warning); }
                else { float w = effect.Kind == "charge_warn" ? 1 : effect.Kind == "boss_warn_1" ? 11 : 3, h = effect.Kind == "charge_warn" ? 1 : 3; c.DrawRect(SKRect.Create(p.X - zoom * w / 2, p.Y - zoom * h / 2, zoom * w, zoom * h), warning); }
            }
            if (effect.Text.Length > 0) Text(c, effect.Text, p.X, p.Y - zoom * .8f, 13, centered: true);
        }
        if (Game.Night()) Box(c, SKRect.Create(viewWidth, viewHeight), "#40183C68", 0);
        if (building.Length > 0) Text(c, Game.Content.Objects[building].Name + " · 설치할 타일 터치 / 뒤로: 취소", viewWidth / 2, 137, 17, centered: true);
        if (padAim) { c.DrawCircle(aim, 8, line); c.DrawLine(aim.X - 12, aim.Y, aim.X + 12, aim.Y, line); c.DrawLine(aim.X, aim.Y - 12, aim.X, aim.Y + 12, line); }
    }
    private static void Bar(SKCanvas c, float x, float y, float width, double fraction, string color)
    { Box(c, SKRect.Create(x, y, width, 4), "#223D2B", 0); Box(c, SKRect.Create(x, y, width * (float)Math.Max(0, Math.Min(1, fraction)), 4), color, 0); }
    private void RenderHud(SKCanvas c)
    {
        var a = session.Actor; if (a is null) return;
        Box(c, SKRect.Create(12, 10, 300, 55), "#D520382D");
        Text(c, $"{a.Name}  ·  {Game.State.Get("gold"):0} G", 24, 32, 16);
        Text(c, $"내구도 {a.Get("health"):0}/{a.Get("maxHealth"):0}   마력 {a.Get("mana"):0}/{a.Get("maxMana"):0}", 24, 53, 13);
        Button(c, "inventory", "가방", SKRect.Create(viewWidth - 288, 12, 62, 40), () => Trigger("inventory"));
        Button(c, "crew", "골렘", SKRect.Create(viewWidth - 218, 12, 62, 40), () => Trigger("crew"));
        Button(c, "more", "행동", SKRect.Create(viewWidth - 148, 12, 62, 40), () => OpenRoot("행동", AllActions));
        Button(c, "menu", "메뉴", SKRect.Create(viewWidth - 78, 12, 62, 40), Back);
        var q = Game.Content.Quests.Values.FirstOrDefault(q => !Game.State.CompletedQuests.Contains(q.Id) && (q.Requires.Length == 0 || Game.State.CompletedQuests.Contains(q.Requires)));
        if (q is not null) { Box(c, SKRect.Create(12, 75, 260, 65), "#9920382D"); Text(c, q.Name, 24, 96, 14); Wrap(c, q.Description, 24, 117, 236, 12, 2); }
        if (menus.Count > 0 || Game.State.Dialogues.Count > 0) return;
        using var ring = new SKPaint { Color = Color("#80234233"), IsAntialias = true }; c.DrawCircle(StickCenter, 62, ring);
        ring.Color = Color("#AADDCE97"); var movement = input.Movement(); c.DrawCircle(StickCenter.X + (float)movement.X * 35, StickCenter.Y + (float)movement.Y * 35, 22, ring);
        Text(c, a.GetText("mode") == "combat" ? "이동" : "카메라", StickCenter.X, viewHeight - 13, 13, centered: true);
        string[] ids = ["pickup", "context", "primary", "roll", "toggle_mode"];
        for (int i = 0; i < ids.Length; i++)
        { string id = ids[i]; string name = Game.Content.InputActions[id].Name; Button(c, "input:" + id, name, SKRect.Create(viewWidth - 388 + i * 75, viewHeight - 78, 70, 58), () => { }, input.Held(id)); }
        Button(c, "input:queue", "예약", SKRect.Create(172, viewHeight - 70, 60, 46), () => { }, queued || input.Held("queue"));
        Button(c, "zoom-", "−", SKRect.Create(12, 150, 38, 38), () => camera.ZoomTo(Math.Max(24, camera.TargetZoom - 6)));
        Button(c, "zoom+", "+", SKRect.Create(56, 150, 38, 38), () => camera.ZoomTo(Math.Min(72, camera.TargetZoom + 6)));
    }
    private void RenderDialogue(SKCanvas c)
    {
        var d = Game.State.Dialogues[0]; double now = clock.Elapsed.TotalSeconds;
        if (dialogue.Id != d.Id) dialogue.Begin(d.Id, d.Text, now);
        Box(c, SKRect.Create(viewWidth, viewHeight), "#9A11281D", 0);
        var portrait = SKRect.Create(10, viewHeight - 340, 265, 320);
        string sprite = d.Portrait + "." + d.Mood;
        if (!art.Sprite(c, sprite, portrait, dialogue.MouthOpen(now) ? "talk" : "idle")) art.Sprite(c, d.Portrait + ".neutral", portrait);
        Box(c, SKRect.Create(252, viewHeight - 205, viewWidth - 272, 184), "#EF20382B");
        Text(c, d.Speaker, 274, viewHeight - 167, 21, "#EDD9A5");
        Wrap(c, dialogue.Visible(now), 274, viewHeight - 135, viewWidth - 318, 18, 4);
        Text(c, dialogue.Revealing(now) ? "터치 · 전체 보기" : "터치 · 계속 ›", viewWidth - 165, viewHeight - 40, 13);
    }
}
