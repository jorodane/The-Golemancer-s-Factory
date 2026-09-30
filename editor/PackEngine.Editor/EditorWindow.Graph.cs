using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PackEngine.Editor;

public sealed partial class EditorWindow
{
    private void DrawGraph(string key)
    {
        graph.Children.Clear(); trail.Children.Clear(); if (session is null) return;
        foreach (string previous in session.State.Trail.TakeLastCompat(8)) trail.Children.Add(Action(previous, () => Guard(() => SelectNode(previous))));
        var all = session.Index.Links.Where(l => l.From == key || l.To == key).ToArray();
        var neighbors = all.Select(l => l.From == key ? l.To : l.From).Distinct(StringComparer.Ordinal).Take(12).ToArray();
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal) { [key] = new(500, 375) };
        for (int i = 0; i < neighbors.Length; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / Math.Max(1, neighbors.Length);
            positions[neighbors[i]] = new(500 + 350 * Math.Cos(angle), 375 + 285 * Math.Sin(angle));
        }
        graph.Children.Add(Label("선택 주변 관계 " + neighbors.Length + "개 / 전체 연결 " + all.Length + "개 · 노드를 눌러 이동", 12, MutedInk));
        foreach (var edge in all.Where(e => positions.ContainsKey(e.From) && positions.ContainsKey(e.To)))
        {
            if (edge.From == edge.To) continue;
            Point a = positions[edge.From], b = positions[edge.To]; Vector direction = b - a; direction.Normalize();
            a += direction * 82; b -= direction * 82;
            graph.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Brush("#53677E"), StrokeThickness = 1.3 });
            var perpendicular = new Vector(-direction.Y, direction.X);
            graph.Children.Add(new Polygon { Points = new PointCollection { b, b - direction * 10 + perpendicular * 4, b - direction * 10 - perpendicular * 4 }, Fill = MutedInk });
            var label = Label(edge.Kind, 10, MutedInk); Canvas.SetLeft(label, (a.X + b.X) / 2); Canvas.SetTop(label, (a.Y + b.Y) / 2); graph.Children.Add(label);
        }
        foreach (var pair in positions)
        {
            if (!session.Index.Nodes.TryGetValue(pair.Key, out var node)) continue;
            var button = Action(node.Kind + "\n" + node.Id + (node.Status == "resolved" ? "" : "\n" + node.Status), () => Guard(() => SelectNode(node.Key)));
            button.Width = 166; button.Height = 62; button.ToolTip = node.Key + "\n" + node.File;
            button.Background = node.Key == key ? Brush("#245548") : node.Status == "resolved" ? PanelInk : Brush("#594326");
            button.Content = new TextBlock { Text = (string)button.Content, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = TextInk, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(button, pair.Value.X - 83); Canvas.SetTop(button, pair.Value.Y - 31); graph.Children.Add(button);
        }
    }
}
