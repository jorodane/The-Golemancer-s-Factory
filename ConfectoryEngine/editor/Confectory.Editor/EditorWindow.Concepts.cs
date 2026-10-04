using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Confectory.Workspace;

namespace Confectory.Editor;

public sealed partial class EditorWindow
{
    private ConceptSpace? conceptSpace;
    private readonly List<Window> conceptWindows = [];
    private readonly ContentControl conceptPageHost = new() { Visibility = Visibility.Collapsed, Background = BackgroundInk, Margin = new Thickness(0, 0, 0, 84) };
    private Action? conceptPageClosing;
    private ConceptSpace Space => conceptSpace ??= ConceptSpace.Open(session!.Project);
    private void CloseConceptPage() { conceptPageClosing?.Invoke(); conceptPageClosing = null; conceptPageHost.Content = null; conceptPageHost.Visibility = Visibility.Collapsed; }
    private FrameworkElement OpenConceptPage(UIElement content)
    {
        CloseConceptPage(); var page = new DockPanel { Background = BackgroundInk };
        var back = BareButton(Label("← 프로젝트", 11, MutedInk), CloseConceptPage); back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(18, 8, 0, 0); DockPanel.SetDock(back, Dock.Top); page.Children.Add(back); page.Children.Add(content);
        conceptPageHost.Content = page; conceptPageHost.Visibility = Visibility.Visible; return page;
    }
    private void CloseConceptWindows() { CloseConceptPage(); foreach (var window in conceptWindows.ToArray()) window.Close(); emptyProjectRunning = false; emptyProjectSurface.Visibility = Visibility.Collapsed; conceptSpace = null; }
    private Window ConceptWindow(string title, UIElement body, int width = 1040, int height = 680)
    {
        var window = new Window { Owner = this, Title = title, Width = width, Height = height, MinWidth = 600, MinHeight = 400, Background = BackgroundInk, Foreground = TextInk, Content = body };
        conceptWindows.Add(window); window.Closed += (_, _) => conceptWindows.Remove(window); window.Show(); return window;
    }
    private bool SaveSpace()
    {
        try { if (peerClient is not null) throw new InvalidOperationException("공동 프로젝트의 확정은 호스트에서 진행해줘."); Space.Save(session!); RefreshProject(); return true; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Confectory", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    }
    private bool MoveSpace(IEnumerable<string> ids, string target)
    {
        try { if (peerClient is not null) throw new InvalidOperationException("공동 프로젝트의 확정은 호스트에서 진행해줘."); Space.MoveAndSave(session!, ids, target); RefreshProject(); return true; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Confectory", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    }
    private ContextMenu ConceptElementMenu(IConceptElement element, Action changed)
    {
        var menu = new ContextMenu(); var rename = new MenuItem { Header = "이름 변경", IsEnabled = Space.Pack(element.Pack).Editable }; rename.Click += (_, _) => AskName("이름 변경", element.Name, name => { string previous = element.Name; element.Name = name; if (SaveSpace()) changed(); else element.Name = previous; }); menu.Items.Add(rename);
        var move = new MenuItem { Header = "팩으로 이동", IsEnabled = Space.Pack(element.Pack).Editable }; foreach (var pack in Space.Packs.Where(p => p.Editable)) { var target = new MenuItem { Header = pack.Name }; target.Click += (_, _) => { if (MoveSpace(new[] { element.Id }, pack.Id)) changed(); }; move.Items.Add(target); } menu.Items.Add(move); return menu;
    }
    private void OpenConceptMenu(Button anchor)
    {
        if (session is null || Standalone) return;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
        void Branch(ItemCollection items, string parent)
        {
            foreach (var category in Space.Categories.Where(c => c.Parent == parent)) { var item = new MenuItem { Header = category.Name }; Branch(item.Items, category.Id); if (item.Items.Count == 0) { var empty = new MenuItem { Header = "비어 있어", IsEnabled = false }; item.Items.Add(empty); } items.Add(item); }
            foreach (var concept in Space.Concepts.Where(c => c.Category == parent && c.Base.Length == 0)) { var item = new MenuItem { Header = ConceptMark(concept.Id) + " " + concept.Name }; item.Click += (_, _) => OpenConceptObjects(concept.Id); items.Add(item); }
        }
        Branch(menu.Items, "");
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        void Entry(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => HomeAction(click); menu.Items.Add(item); }
        Entry("개념", () => OpenConceptMap()); Entry("기능", OpenConceptFunctions); Entry("팩", OpenConceptPacks);
        menu.Items.Add(new Separator()); var tools = new MenuItem { Header = "도구" };
        foreach (var pair in new (string, Action)[] { ("참여자", OpenParticipantList), ("작업자 추가", AddWorker), ("프로젝트 채팅", () => OpenPublicChat(false)), ("이거", () => ArmYogi(false)), ("같이 보기", () => ArmYogi(true)), ("실행 설정", OpenConceptExecution), ("실행 기록", OpenExecutionLog), ("개념 다시 불러오기", CloseConceptWindows), ("기존 요소", () => OpenNativeTool(0)), ("에디터팩", () => OpenNativeTool(6)), ("신문고", OpenIncidents), ("프로젝트 목록", ShowProjectHome) })
        { var item = new MenuItem { Header = pair.Item1 }; item.Click += (_, _) => HomeAction(pair.Item2); tools.Items.Add(item); }
        var packMenu = new MenuItem { Header = "프로젝트의 추가 화면", ItemsSource = null }; foreach (var navigation in packGeneration is null ? [] : Confectory.EditorPacks.EditorNavigation.Entries(packGeneration.Snapshot, "menu")) { var item = new MenuItem { Header = navigation.Fields["title"] }; item.Click += (_, _) => ExecuteEditorCommand(packGeneration!, navigation.Fields["command"], Confectory.Contracts.UI.UiValue.Text(""), WindowCommandContext("", "")); packMenu.Items.Add(item); } if (packMenu.Items.Count > 0) tools.Items.Add(packMenu);
        menu.Items.Add(tools); menu.IsOpen = true;
    }
    private void OpenExecutionLog() => ConceptWindow("실행 기록", new ScrollViewer { Content = new TextBlock { Text = log.Text, FontFamily = new FontFamily("Consolas"), Foreground = TextInk, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap } });
    private void OpenConceptExecution()
    {
        var panel = new StackPanel { Margin = new Thickness(22) }; panel.Children.Add(Label("실행 대상", 20));
        var select = new ComboBox { ItemsSource = session!.Project.Targets.Select(t => t.Id).ToArray(), SelectedItem = Target, Margin = new Thickness(4), MinWidth = 130 }; select.SelectionChanged += (_, _) => targets.SelectedItem = select.SelectedItem; panel.Children.Add(select);
        panel.Children.Add(Label("선택한 대상의 프로젝트 빌드와 실행을 사용해.", 12, MutedInk));
        panel.Children.Add(Action("프로젝트 빌드", () => Work(() => runner!.BuildProject(Target, operation!.Token)), true));
        panel.Children.Add(Action("검증", () => Work(() => runner!.Verify(Target, cancellation: operation!.Token)), true));
        panel.Children.Add(Action("실행 기록", OpenExecutionLog)); OpenConceptPage(panel);
    }
    private string ConceptMark(string id) => Space.Concepts.Any(c => c.Base == id) ? "◎" : "○";
    private Button ConceptTypeButton(string id, Action click) => BareButton(Label(Space.TypeName(id), 12), click);
    private void TypePicker(Button anchor, string current, Action<string> apply, bool returns = false)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        foreach (string type in ConceptSpace.PrimitiveTypes.Concat(returns ? new[] { "void" } : []).Concat(Space.Concepts.Select(c => c.Id))) { var item = new MenuItem { Header = (type == current ? "✓ " : "") + Space.TypeName(type) }; item.Click += (_, _) => apply(type); menu.Items.Add(item); } menu.IsOpen = true;
    }
    private FrameworkElement FieldEditor(List<ConceptField> fields, int depth = 0, bool inherited = false)
    {
        var panel = new StackPanel { Margin = new Thickness(depth == 0 ? 0 : 14, 4, 0, 4) };
        void Render()
        {
            panel.Children.Clear();
            foreach (var field in fields.ToArray())
            {
                var row = new DockPanel(); var remove = BareButton(Label("×", 20, MainInk), () => { fields.Remove(field); Render(); }); remove.IsEnabled = !inherited; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                Button? mode = null;
                mode = BareButton(Label((field.Multiple ? "다중" : "단일") + " · " + (field.Kind == "composite" ? "복합" : field.Kind == "function" ? "기능" : "일반"), 11), () =>
                {
                    var popup = new Popup { PlacementTarget = mode, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true }; var choices = new StackPanel { Orientation = Orientation.Horizontal };
                    var left = new StackPanel(); var right = new StackPanel(); choices.Children.Add(left); choices.Children.Add(right);
                    string group = Guid.NewGuid().ToString("N");
                    foreach (bool multiple in new[] { false, true }) { var option = new RadioButton { Content = multiple ? "다중" : "단일", GroupName = group + "multiple", IsChecked = multiple == field.Multiple, Margin = new Thickness(8) }; option.Checked += (_, _) => field.Multiple = multiple; left.Children.Add(option); }
                    foreach (string kind in new[] { "normal", "composite", "function" }) { var option = new RadioButton { Content = kind == "normal" ? "일반" : kind == "composite" ? "복합" : "기능", GroupName = group + "kind", IsChecked = kind == field.Kind, Margin = new Thickness(8) }; option.Checked += (_, _) => { field.Kind = kind; if (kind != "function" && field.Type == "void") field.Type = "text"; if (kind == "normal") field.Fields.Clear(); }; right.Children.Add(option); }
                    popup.Closed += (_, _) => Render();
                    popup.Child = new Border { Child = choices, Background = PanelInk, Padding = new Thickness(8), BorderBrush = MutedInk, BorderThickness = new Thickness(1) }; popup.IsOpen = true;
                }); mode.IsEnabled = !inherited; DockPanel.SetDock(mode, Dock.Right); row.Children.Add(mode);
                Button? type = null; type = ConceptTypeButton(field.Type, () => TypePicker(type!, field.Type, selected => { field.Type = selected; Render(); }, field.Kind == "function")); type.IsEnabled = !inherited; DockPanel.SetDock(type, Dock.Right); row.Children.Add(type);
                var name = Input(); name.Text = field.Name; name.MinWidth = 100; name.IsReadOnly = inherited; name.TextChanged += (_, _) => field.Name = name.Text; row.Children.Add(name); panel.Children.Add(row);
                if (field.Kind is "composite" or "function") { if (field.Kind == "function") panel.Children.Add(Label("입력 매개변수 · 위 타입은 반환 타입", 10, MutedInk)); panel.Children.Add(FieldEditor(field.Fields, depth + 1, inherited)); }
            }
            if (!inherited) panel.Children.Add(BareButton(Label("+ 항목", 12), () => { fields.Add(new() { Id = ConceptSpace.NewId(), Name = "새 항목" }); Render(); }));
        }
        Render(); return panel;
    }
    private void OpenConceptMap(string variation = "")
    {
        var dock = new DockPanel { Margin = new Thickness(18) }; var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var canvas = new Canvas { Background = BackgroundInk }; var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dock.Children.Add(scroll);
        var window = OpenConceptPage(dock); Popup? schema = null; var references = new List<UIElement>(); string hover = "";
        var layer = variation; var path = new List<string>(); if (layer.Length > 0) path.Add(layer);
        void ClearRelations() { foreach (var line in references) canvas.Children.Remove(line); references.Clear(); }
        void Relations(bool reverse)
        {
            ClearRelations(); if (hover.Length == 0) return; var nodes = Space.Map(layer); var from = nodes.Single(n => n.Id == hover); int hidden = 0;
            foreach (string id in Space.References(hover, reverse))
            {
                var target = nodes.FirstOrDefault(n => n.Id == id);
                if (target is null) { target = new() { X = canvas.Width - 180, Y = from.Y + hidden++ * 62, Name = Space.Concept(id).Name }; var label = Label(ConceptMark(id) + " " + target.Name, 12, AccentInk); Canvas.SetLeft(label, target.X); Canvas.SetTop(label, target.Y); canvas.Children.Add(label); references.Add(label); }
                var line = new Line { X1 = (reverse ? target.X : from.X) + 70, Y1 = (reverse ? target.Y : from.Y) + 25, X2 = (reverse ? from.X : target.X) + 50, Y2 = (reverse ? from.Y : target.Y) + 25, Stroke = AccentInk, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false }; canvas.Children.Add(line); references.Add(line);
                var arrow = Label(line.X2 >= line.X1 ? "›" : "‹", 22, AccentInk); Canvas.SetLeft(arrow, line.X2); Canvas.SetTop(arrow, line.Y2 - 15); canvas.Children.Add(arrow); references.Add(arrow);
            }
        }
        void Add(string parent, bool category)
        {
            AskName(category ? "새 카테고리" : layer.Length > 0 ? "새 Variation" : "새 개념", "", name => { string id = ConceptSpace.NewId(); if (category) Space.Categories.Add(new() { Id = id, Name = name, Parent = parent, Pack = Space.MainPack }); else Space.Concepts.Add(new() { Id = id, Name = name, Category = parent, Base = layer, Pack = Space.MainPack }); if (SaveSpace()) Draw(); });
        }
        void Draw()
        {
            schema?.SetCurrentValue(Popup.IsOpenProperty, false); ClearRelations(); hover = ""; canvas.Children.Clear(); top.Children.Clear();
            top.Children.Add(Action("전체", () => { layer = ""; path.Clear(); Draw(); }));
            if (layer.Length > 0) foreach (string categoryId in Space.Breadcrumb(layer).Where(id => Space.Categories.Any(c => c.Id == id))) top.Children.Add(Label("› " + Space.Categories.Single(c => c.Id == categoryId).Name, 12, MutedInk)); foreach (string id in path.ToArray()) { top.Children.Add(Label("›", 14, MutedInk)); top.Children.Add(Action(Space.Concept(id).Name, () => { layer = id; path = path.Take(path.IndexOf(id) + 1).ToList(); Draw(); })); }
            if (layer.Length == 0) top.Children.Add(Action("+ 카테고리", () => Add("", true))); top.Children.Add(Action(layer.Length == 0 ? "+ 개념" : "+ Variation", () => Add("", false)));
            var nodes = Space.Map(layer); canvas.Width = Math.Max(940, nodes.Select(n => n.X + 380).DefaultIfEmpty(940).Max()); canvas.Height = Math.Max(540, nodes.Select(n => n.Y + 120).DefaultIfEmpty(540).Max());
            foreach (var node in nodes.Where(n => n.Parent.Length > 0)) { var parent = nodes.Single(n => n.Id == node.Parent); canvas.Children.Add(new Polyline { Points = new PointCollection { new(parent.X + 145, parent.Y + 25), new(node.X - 28, parent.Y + 25), new(node.X - 28, node.Y + 25), new(node.X - 4, node.Y + 25) }, Stroke = MutedInk, StrokeThickness = 1, IsHitTestVisible = false }); var arrow = Label("›", 18, MutedInk); Canvas.SetLeft(arrow, node.X - 13); Canvas.SetTop(arrow, node.Y + 8); canvas.Children.Add(arrow); }
            foreach (var node in nodes)
            {
                var content = new Border { Width = 145, Height = 50, Background = PanelInk, CornerRadius = new CornerRadius(node.Category ? 6 : 25), BorderBrush = node.Category ? MutedInk : AccentInk, BorderThickness = new Thickness(node.Variations ? 3 : 1), Child = Label((node.Category ? "" : ConceptMark(node.Id) + " ") + node.Name, 13) };
                var button = BareButton(content, () =>
                {
                    schema?.SetCurrentValue(Popup.IsOpenProperty, false);
                    if (node.Category) { var menu = new ContextMenu(); foreach (var action in new[] { ("+ 카테고리", true), ("+ 개념", false) }) { var item = new MenuItem { Header = action.Item1 }; item.Click += (_, _) => Add(node.Id, action.Item2); menu.Items.Add(item); } menu.IsOpen = true; return; }
                    var concept = Space.Concept(node.Id); bool editable = Space.Pack(concept.Pack).Editable; var fields = concept.Fields.Select(f => f.Copy()).ToList(); var panel = new StackPanel(); var name = Input(); name.Text = concept.Name; name.IsReadOnly = !editable; panel.Children.Add(name); var symbol = Input(); symbol.IsReadOnly = !editable; symbol.Text = concept.Symbol.Length == 0 ? concept.Id : concept.Symbol; symbol.ToolTip = "Namespace 안의 논리 이름"; panel.Children.Add(symbol);
                    if (concept.Base.Length > 0) { panel.Children.Add(Label("상속 · " + Space.Concept(concept.Base).Name, 11, MutedInk)); panel.Children.Add(FieldEditor(Space.Schema(concept.Base), inherited: true)); }
                    panel.Children.Add(FieldEditor(fields, inherited: !editable)); var actions = new WrapPanel(); actions.Children.Add(Action("스키마 저장", () => { var old = concept.Fields; string oldName = concept.Name, oldSymbol = concept.Symbol; concept.Fields = fields; concept.Name = name.Text; concept.Symbol = symbol.Text; if (SaveSpace()) { schema!.IsOpen = false; Draw(); } else { concept.Fields = old; concept.Name = oldName; concept.Symbol = oldSymbol; } })); actions.Children.Add(Action("객체 편집", () => OpenConceptObjects(concept.Id))); actions.Children.Add(PackButton(concept)); ((Button)actions.Children[0]).IsEnabled = editable; panel.Children.Add(actions);
                    schema = new Popup { PlacementTarget = buttonFor(node.Id), Placement = PlacementMode.Top, StaysOpen = true, AllowsTransparency = true, Child = new Border { Background = PanelInk, BorderBrush = AccentInk, BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = new ScrollViewer { Content = panel, Width = 500, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
                    if (schema.PlacementTarget.PointToScreen(new Point()).Y - SystemParameters.WorkArea.Top < 440) schema.Placement = PlacementMode.Bottom; schema.IsOpen = true;
                });
                button.Tag = node.Id; button.SetValue(YogiKeyProperty, (node.Category ? "concept-category:" : "concept:") + node.Id); var element = Space.Elements().Single(e => e.Id == node.Id); button.ToolTip = Space.Address(element); button.ContextMenu = ConceptElementMenu(element, Draw); button.MouseDoubleClick += (_, e) => { if (!node.Category && node.Variations) { e.Handled = true; layer = node.Id; path.Add(layer); Draw(); } };
                if (!node.Category) { button.MouseEnter += (_, _) => { hover = node.Id; Relations(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }; button.MouseLeave += (_, _) => { hover = ""; ClearRelations(); }; }
                Canvas.SetLeft(button, node.X); Canvas.SetTop(button, node.Y); canvas.Children.Add(button);
            }
        }
        Button buttonFor(string id) => canvas.Children.OfType<Button>().Single(b => (string?)b.Tag == id);
        canvas.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, canvas) && schema is not null) schema.IsOpen = false; };
        window.PreviewKeyDown += (_, e) => { if (e.Key is Key.LeftShift or Key.RightShift) Relations(true); if (e.Key == Key.Escape && schema is not null) schema.IsOpen = false; }; window.PreviewKeyUp += (_, e) => { if (e.Key is Key.LeftShift or Key.RightShift) Relations(false); };
        conceptPageClosing = () => { if (schema is not null) schema.IsOpen = false; }; Draw();
    }
    private Button PackButton(IConceptElement element, Action? changed = null)
    {
        Button? button = null; button = BareButton(Label(Space.Pack(element.Pack).Name, 11, MutedInk), () =>
        {
            var menu = new ContextMenu { PlacementTarget = button }; foreach (var pack in Space.Packs.Where(p => p.Editable)) { var item = new MenuItem { Header = (element.Pack == pack.Id ? "✓ " : "") + pack.Name }; item.Click += (_, _) => HomeAction(() => { if (MoveSpace(new[] { element.Id }, pack.Id)) { button!.Content = Label(pack.Name, 11, MutedInk); button.SetValue(YogiKeyProperty, "pack:" + element.Pack); changed?.Invoke(); } }); menu.Items.Add(item); }
            menu.Items.Add(new Separator()); var add = new MenuItem { Header = "새 팩…" }; add.Click += (_, _) => NewConceptPack(null, pack => { if (MoveSpace(new[] { element.Id }, pack.Id)) { button!.Content = Label(pack.Name, 11, MutedInk); button.SetValue(YogiKeyProperty, "pack:" + element.Pack); changed?.Invoke(); } }); menu.Items.Add(add); menu.IsOpen = true;
        }); button.SetValue(YogiKeyProperty, "pack:" + element.Pack); button.ToolTip = "Source Pack · " + Space.Address(element); button.IsEnabled = Space.Pack(element.Pack).Editable; return button;
    }
    private FrameworkElement ValueEditor(ConceptField field, ConceptValue value, bool item = false)
    {
        if (field.Multiple && !item)
        {
            var panel = new StackPanel(); void Render() { panel.Children.Clear(); foreach (var child in value.Items.ToArray()) { var row = new DockPanel(); var remove = BareButton(Label("×", 16, MainInk), () => { value.Items.Remove(child); if (SaveSpace()) Render(); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); row.Children.Add(ValueEditor(field, child, true)); panel.Children.Add(row); } panel.Children.Add(BareButton(Label("+", 20), () => { value.Items.Add(ConceptSpace.Default(field, true)); if (SaveSpace()) Render(); })); } Render(); return panel;
        }
        if (field.Kind == "composite") { var panel = new StackPanel(); foreach (var child in field.Fields) { panel.Children.Add(Label(child.Name, 10, MutedInk)); panel.Children.Add(ValueEditor(child, ConceptSpace.Value(value.Members, child))); } return panel; }
        if (field.Kind == "function" || !ConceptSpace.PrimitiveTypes.Contains(field.Type))
        {
            Button? pick = null;
            string Caption() => field.Kind == "function" ? Space.Implementations.FirstOrDefault(i => i.Id == value.Text)?.Name ?? "구현 선택" : Space.Objects.FirstOrDefault(o => o.Id == value.Text) is { } selected ? Space.DisplayName(selected) : Space.TypeName(field.Type);
            pick = BareButton(Label(Caption(), 12), () => { var menu = new ContextMenu { PlacementTarget = pick }; var choices = field.Kind == "function" ? Space.Matching(field).Select(i => (i.Id, i.Name + " · " + Space.Address(i))).ToArray() : Space.Choices(field.Type).Select(o => (o.Id, ConceptMark(o.Concept) + " " + Space.DisplayName(o) + " · " + Space.Pack(o.Pack).Name)).ToArray(); foreach (var choice in new[] { ("", "비워 두기") }.Concat(choices)) { var option = new MenuItem { Header = choice.Item2 }; option.Click += (_, _) => { value.Text = choice.Item1; if (SaveSpace()) pick!.Content = Label(Caption(), 12); }; menu.Items.Add(option); } menu.IsOpen = true; }); return pick;
        }
        if (field.Type == "boolean") { var check = new CheckBox { IsChecked = value.Text == "true", Margin = new Thickness(8) }; check.Checked += (_, _) => { value.Text = "true"; SaveSpace(); }; check.Unchecked += (_, _) => { value.Text = "false"; SaveSpace(); }; return check; }
        var input = Input(); if (field.Type == "number") input.InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }; input.Text = value.Text; input.MinWidth = 80; input.TextChanged += (_, _) => value.Text = input.Text; input.LostKeyboardFocus += (_, _) => SaveSpace(); return input;
    }
    private void OpenConceptObjects(string id, string selectedView = "")
    {
        var concept = Space.Concept(id); var dock = new DockPanel { Margin = new Thickness(20) }; var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top); dock.Children.Add(controls); var rows = new StackPanel(); dock.Children.Add(new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        string viewId = selectedView.Length > 0 ? selectedView : Space.Editors(id).FirstOrDefault()?.Id ?? "", filter = ""; bool grouped = false; OpenConceptPage(dock);
        void Render()
        {
            controls.Children.Clear(); rows.Children.Clear(); controls.Children.Add(Label(concept.Name, 22));
            var views = new ComboBox { MinWidth = 145, Margin = new Thickness(8) };
            views.Items.Add(new ComboBoxItem { Content = "기본 테이블", Tag = "" }); foreach (var v in Space.Editors(id)) views.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id, ContextMenu = ConceptElementMenu(v, Render) }); views.SelectedIndex = Math.Max(0, views.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == viewId)); views.SelectionChanged += (_, _) => { viewId = (string)((ComboBoxItem)views.SelectedItem).Tag; Render(); }; views.SetValue(YogiKeyProperty, viewId.Length > 0 ? "concept-view:" + viewId : "concept:" + id); controls.Children.Add(views);
            var packs = new ComboBox { MinWidth = 115, Margin = new Thickness(8) }; packs.Items.Add(new ComboBoxItem { Content = "모든 팩", Tag = "" }); foreach (var pack in Space.Packs) packs.Items.Add(new ComboBoxItem { Content = pack.Name, Tag = pack.Id }); packs.SelectedIndex = Math.Max(0, packs.Items.OfType<ComboBoxItem>().ToList().FindIndex(p => (string)p.Tag == filter)); packs.SelectionChanged += (_, _) => { filter = (string)((ComboBoxItem)packs.SelectedItem).Tag; Render(); }; controls.Children.Add(packs);
            var group = new CheckBox { Content = "팩별 정렬", IsChecked = grouped, Margin = new Thickness(8) }; group.Click += (_, _) => { grouped = group.IsChecked == true; Render(); }; controls.Children.Add(group); controls.Children.Add(Action("보기 추가", () => NewConceptView(id, Render))); var create = Action("+", () => { var value = Space.CreateObject(id); if (filter.Length > 0) value.Pack = filter; if (SaveSpace()) Render(); else Space.Objects.Remove(value); }); create.IsEnabled = Space.Pack(filter.Length == 0 ? Space.MainPack : filter).Editable; controls.Children.Add(create);
            var view = Space.Views.FirstOrDefault(v => v.Id == viewId); var schema = Space.Schema(id);
            if (view?.Layout == "pack") { controls.Children.Add(Action("전용 에디터 열기", () => { var value = Space.Rows(id, filter).FirstOrDefault(); if (value is not null) OpenElementEditor(new() { Key = "concept-object:" + value.Id, EditorId = view.Editor }); })); return; }
            if (view is not null && view.Fields.Any(f => Space.ResolveField(id, f.Path) is null)) { rows.Children.Add(Label("보기의 연결을 확인해줘. 기본 테이블로 편집할 수 있어.", 12, MutedInk)); view = null; }
            var columns = view is null || view.Fields.Count == 0 ? schema.Select(f => new ConceptViewField { Path = f.Id, Label = f.Name }).ToArray() : view.Fields.ToArray();
            bool table = view is null || view.Layout == "table";
            bool named = Space.NameField(id) is { } nameField && columns.Any(c => c.Path == nameField.Id), sourcePack = view is null || view.ShowSourcePack; int offset = (sourcePack ? 1 : 0) + (named ? 0 : 1);
            var widths = (sourcePack ? new[] { 110d } : Array.Empty<double>()).Concat(named ? [] : new[] { 180d }).Concat(columns.Select(_ => 190d)).ToArray();
            Grid GridRow() { var grid = new Grid(); foreach (double width in widths) grid.ColumnDefinitions.Add(new() { Width = new GridLength(width) }); return grid; }
            if (table) { var header = GridRow(); var labels = (sourcePack ? new[] { "소스 팩" } : Array.Empty<string>()).Concat(named ? [] : new[] { "이름" }).Concat(columns.Select(c => c.Label.Length == 0 ? Space.ResolveField(id, c.Path)!.Name : c.Label)).ToArray(); for (int i = 0; i < labels.Length; i++) { var label = Label(labels[i], 12, AccentInk); Grid.SetColumn(label, i); header.Children.Add(label); } rows.Children.Add(header); }
            string previous = "";
            foreach (var value in Space.Rows(id, filter, grouped))
            {
                if (grouped && previous != value.Pack) { rows.Children.Add(Label((value.Pack == Space.MainPack ? "MAIN · " : "") + Space.Pack(value.Pack).Name, 16, AccentInk)); rows.Children.Add(new Border { Height = 1, Background = MutedInk, Margin = new Thickness(3, 6, 3, 10) }); previous = value.Pack; }
                var name = Input(); name.Text = Space.DisplayName(value); name.IsReadOnly = !Space.Pack(value.Pack).Editable || Space.HasNameField(value.Concept); name.TextChanged += (_, _) => { if (!Space.HasNameField(value.Concept)) value.Name = name.Text; }; name.LostKeyboardFocus += (_, _) => { if (!name.IsReadOnly) SaveSpace(); };
                if (table)
                {
                    var row = GridRow(); row.SetValue(YogiKeyProperty, "concept-object:" + value.Id); if (sourcePack) row.Children.Add(PackButton(value, Render)); if (!named) { Grid.SetColumn(name, sourcePack ? 1 : 0); row.Children.Add(name); }
                    for (int i = 0; i < columns.Length; i++) { var cell = BoundValue(value, columns[i]); Grid.SetColumn(cell, i + offset); row.Children.Add(cell); } row.IsEnabled = Space.Pack(value.Pack).Editable; rows.Children.Add(new Border { BorderBrush = MutedInk, BorderThickness = new Thickness(0, 0, 0, 1), Child = row, Padding = new Thickness(0, 8, 0, 8) });
                }
                else
                {
                    var card = new StackPanel(); card.SetValue(YogiKeyProperty, "concept-object:" + value.Id); card.Children.Add(Space.HasNameField(id) ? Label(Space.DisplayName(value), 18) : name); if (view!.ShowSourcePack) card.Children.Add(PackButton(value, Render)); var flow = new StackPanel { Orientation = Orientation.Horizontal }; var input = new StackPanel(); var output = new StackPanel(); var extra = new StackPanel();
                    foreach (var column in columns) { var section = view.Layout == "slots" ? column.Side == "input" ? input : column.Side == "output" ? output : extra : extra; section.Children.Add(Label(column.Label.Length == 0 ? Space.ResolveField(id, column.Path)!.Name : column.Label, 11, MutedInk)); section.Children.Add(view.Layout == "slots" && column.Side is "input" or "output" ? SlotValue(value, column) : BoundValue(value, column)); }
                    if (view.Layout == "slots") { flow.Children.Add(input); flow.Children.Add(Label("⟶", 30, AccentInk)); flow.Children.Add(output); card.Children.Add(flow); } card.Children.Add(extra); card.IsEnabled = Space.Pack(value.Pack).Editable; rows.Children.Add(new Border { Background = PanelInk, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 8, 0, 8), Child = card });
                }
            }
        }
        Render();
    }
    private FrameworkElement BoundValue(ConceptObject value, ConceptViewField binding)
    {
        var panel = new StackPanel(); var resolved = Space.Bind(value, binding.Path); foreach (var v in resolved.Values) panel.Children.Add(ValueEditor(resolved.Field, v)); return panel;
    }
    private FrameworkElement SlotValue(ConceptObject value, ConceptViewField binding)
    {
        var panel = new WrapPanel();
        void Render()
        {
            panel.Children.Clear(); var resolved = Space.Bind(value, binding.Path);
            foreach (var container in resolved.Values)
            {
                var values = resolved.Field.Multiple ? container.Items : new List<ConceptValue> { container };
                void Edit(ConceptValue item)
                {
                    var body = new StackPanel(); body.Children.Add(ValueEditor(resolved.Field, item, true));
                    var popup = new Popup { Placement = PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true, Child = new Border { Background = PanelInk, Padding = new Thickness(16), Child = body } };
                    if (resolved.Field.Multiple) body.Children.Add(Action("삭제", () => { container.Items.Remove(item); if (SaveSpace()) popup.IsOpen = false; }));
                    popup.Closed += (_, _) => Render(); popup.IsOpen = true;
                }
                foreach (var item in values)
                {
                    string reference = binding.Icon.Length == 0 ? item.Text : item.Members.TryGetValue(binding.Icon, out var icon) ? icon.Text : "";
                    var target = Space.Objects.FirstOrDefault(o => o.Id == reference); string path = target?.Icon is { Length: > 0 } source ? session!.Project.Resolve(source) : "";
                    var content = new StackPanel(); content.Children.Add(ProjectIcon(path, 52)); content.Children.Add(Label(target is null ? "객체 선택" : Space.DisplayName(target), 10)); if (binding.Quantity.Length > 0 && item.Members.TryGetValue(binding.Quantity, out var quantity)) content.Children.Add(Label(quantity.Text, 12));
                    var button = BareButton(content, () => Edit(item)); button.Margin = new Thickness(6); panel.Children.Add(button);
                }
                if (resolved.Field.Multiple) panel.Children.Add(BareButton(Label("+", 26), () => { var item = ConceptSpace.Default(resolved.Field, true); container.Items.Add(item); if (SaveSpace()) { Render(); Edit(item); } }));
            }
        }
        Render(); return panel;
    }
    private void NewConceptView(string concept, Action changed)
    {
        var panel = new StackPanel { Margin = new Thickness(18) }; var name = Input(); name.Text = "새 보기"; panel.Children.Add(name); var layout = new ComboBox { ItemsSource = new[] { "table", "cards", "slots", "pack" }, SelectedIndex = 1, Margin = new Thickness(4) }; panel.Children.Add(layout); var editor = Input(); editor.ToolTip = "에디터팩의 ObjectEditor ID · pack 배치에서 사용"; panel.Children.Add(editor);
        var fields = Space.Schema(concept).Select(f => new ConceptViewField { Path = f.Id, Label = f.Name, Side = f.Multiple ? "input" : ConceptSpace.PrimitiveTypes.Contains(f.Type) ? "" : "output", Icon = f.Fields.FirstOrDefault(c => !ConceptSpace.PrimitiveTypes.Contains(c.Type))?.Id ?? "", Quantity = f.Fields.FirstOrDefault(c => c.Type == "number")?.Id ?? "" }).ToList();
        foreach (var field in fields) { var row = new DockPanel(); var side = new ComboBox { ItemsSource = new[] { "", "input", "output" }, SelectedItem = field.Side, Width = 100 }; side.SelectionChanged += (_, _) => field.Side = (string)side.SelectedItem; DockPanel.SetDock(side, Dock.Right); row.Children.Add(side); row.Children.Add(Label(field.Label, 12)); panel.Children.Add(row); }
        var source = new CheckBox { Content = "Source Pack 표시", Margin = new Thickness(8) }; panel.Children.Add(source); Window? dialog = null;
        panel.Children.Add(Action("만들기", () => { Space.Views.Add(new() { Id = ConceptSpace.NewId(), Pack = Space.MainPack, Name = name.Text, Concept = concept, Layout = (string)layout.SelectedItem, Editor = editor.Text, ShowSourcePack = source.IsChecked == true, Fields = fields }); if (SaveSpace()) { dialog!.Close(); changed(); } })); dialog = ConceptWindow("Editor View", new ScrollViewer { Content = panel }, 650, 540);
    }
    private void NewConceptPack(Action? changed = null, Action<ConceptPack>? created = null)
    {
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(Label("팩 이름", 12)); var name = Input(); panel.Children.Add(name); panel.Children.Add(Label("Namespace", 12)); var ns = Input(); panel.Children.Add(ns); Window? dialog = null; panel.Children.Add(Action("만들기", () => HomeAction(() => { var pack = Space.AddPack(name.Text, ns.Text); if (SaveSpace()) { dialog!.Close(); created?.Invoke(pack); changed?.Invoke(); } else Space.Packs.Remove(pack); }))); dialog = ConceptWindow("팩 추가", panel, 620, 410);
    }
    private void OpenConceptPacks()
    {
        var body = new StackPanel { Margin = new Thickness(24) }; OpenConceptPage(new ScrollViewer { Content = body });
        void Render()
        {
            body.Children.Clear(); foreach (var pack in Space.Packs)
            {
                var card = new StackPanel(); card.SetValue(YogiKeyProperty, "pack:" + pack.Id); card.Children.Add(Label((pack.Id == Space.MainPack ? "MAIN · " : "") + pack.Name, 20, AccentInk)); var name = Input(); name.Text = pack.Name; var ns = Input(); ns.Text = pack.Namespace; var description = Input(); description.Text = pack.Description; name.IsReadOnly = ns.IsReadOnly = description.IsReadOnly = !pack.Editable; card.Children.Add(name); card.Children.Add(Label("Namespace", 11)); card.Children.Add(ns); card.Children.Add(description); card.Children.Add(Label("의존성 · " + string.Join(", ", pack.Dependencies.Concat(Space.RequiredDependencies(pack.Id)).Distinct().Select(id => Space.Packs.FirstOrDefault(p => p.Id == id)?.Name ?? id)), 11, MutedInk));
                var actions = new WrapPanel(); var save = Action("저장", () => { pack.Name = name.Text; pack.Namespace = ns.Text; pack.Description = description.Text; if (SaveSpace()) Render(); }); save.IsEnabled = pack.Editable; actions.Children.Add(save); actions.Children.Add(Action("요소 이주", () => MigrateConceptElements(pack.Id, Render))); card.Children.Add(actions); body.Children.Add(new Border { Background = PanelInk, Padding = new Thickness(16), Margin = new Thickness(0, 8, 0, 8), Child = card });
            }
            body.Children.Add(Action("+ 팩 추가", () => NewConceptPack(Render)));
        }
        Render();
    }
    private void MigrateConceptElements(string source, Action changed)
    {
        var panel = new StackPanel { Margin = new Thickness(20) }; var packs = new ComboBox(); foreach (var pack in Space.Packs.Where(p => p.Editable && p.Id != source)) packs.Items.Add(new ComboBoxItem { Content = pack.Name, Tag = pack.Id }); packs.SelectedIndex = 0; panel.Children.Add(packs); var selections = new List<CheckBox>();
        foreach (var element in Space.Elements().Where(e => e.Pack == source)) { var check = new CheckBox { Content = (element is ConceptDefinition ? "개념" : element is ConceptImplementation ? "기능" : element is ConceptEditorView ? "View" : element is ConceptObject ? "객체" : "카테고리") + " · " + element.Name, Tag = element.Id, Margin = new Thickness(6) }; selections.Add(check); panel.Children.Add(check); }
        Window? dialog = null; panel.Children.Add(Action("이주", () => HomeAction(() => { if (packs.SelectedItem is not ComboBoxItem target) return; if (MoveSpace(selections.Where(c => c.IsChecked == true).Select(c => (string)c.Tag), (string)target.Tag)) { dialog!.Close(); changed(); } }))); dialog = ConceptWindow("요소 이주", new ScrollViewer { Content = panel }, 700, 600);
    }
    private void OpenConceptFunctions()
    {
        var dock = new DockPanel { Margin = new Thickness(20) }; var add = Action("+ 기능", () => AskName("새 기능", "", name => { Space.Implementations.Add(new() { Id = ConceptSpace.NewId(), Name = name, Symbol = "Functions." + ConceptSpace.NewId(), Pack = Space.MainPack }); if (SaveSpace()) OpenConceptFunctions(); })); DockPanel.SetDock(add, Dock.Bottom); dock.Children.Add(add); var tree = new TreeView { Background = BackgroundInk, Foreground = TextInk }; dock.Children.Add(tree); var groups = new Dictionary<string, TreeViewItem>();
        foreach (var function in Space.Implementations.OrderBy(Space.Address)) { string address = Space.Address(function), prefix = ""; TreeViewItem? parent = null; foreach (string segment in address.Split('.').Take(address.Split('.').Length - 1)) { prefix += "." + segment; if (!groups.TryGetValue(prefix, out var group)) { group = new() { Header = segment, Foreground = TextInk, IsExpanded = true }; groups.Add(prefix, group); if (parent is null) tree.Items.Add(group); else parent.Items.Add(group); } parent = group; } var leaf = new TreeViewItem { Header = function.Name, Foreground = TextInk, Tag = function.Id }; leaf.SetValue(YogiKeyProperty, "function:" + function.Id); leaf.MouseDoubleClick += (_, e) => { e.Handled = true; OpenConceptFunction(function); }; var context = new ContextMenu(); var move = new MenuItem { Header = "팩으로 이동" }; foreach (var pack in Space.Packs.Where(p => p.Editable)) { var item = new MenuItem { Header = pack.Name }; item.Click += (_, _) => HomeAction(() => { MoveSpace(new[] { function.Id }, pack.Id); }); move.Items.Add(item); } context.Items.Add(move); leaf.ContextMenu = context; if (parent is null) tree.Items.Add(leaf); else parent.Items.Add(leaf); }
        OpenConceptPage(dock);
    }
    private void OpenConceptFunction(ConceptImplementation function)
    {
        var panel = new StackPanel { Margin = new Thickness(22) }; panel.SetValue(YogiKeyProperty, "function:" + function.Id); var name = Input(); name.Text = function.Name; panel.Children.Add(name); panel.Children.Add(PackButton(function)); panel.Children.Add(Label("논리 주소", 11)); var symbol = Input(); symbol.Text = function.Symbol; panel.Children.Add(symbol); panel.Children.Add(Label("입력", 14)); var fields = function.Parameters.Select(f => f.Copy()).ToList(); panel.Children.Add(FieldEditor(fields)); panel.Children.Add(Label("출력", 14)); string result = function.Returns; Button? type = null; type = ConceptTypeButton(result, () => TypePicker(type!, result, value => { result = value; type!.Content = Label(Space.TypeName(value), 12); }, true)); panel.Children.Add(type);
        panel.Children.Add(Action("저장", () => { function.Name = name.Text; function.Symbol = symbol.Text; function.Returns = result; function.Parameters = fields; SaveSpace(); }));
        panel.Children.Add(Action("구현", () => HomeAction(() => { bool creating = function.Source.Length == 0; string code = Space.ReadImplementation(function); if (creating && !SaveSpace()) return; var editor = Input(true); editor.Text = code; editor.FontFamily = new FontFamily("Consolas"); editor.TextWrapping = TextWrapping.NoWrap; editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; var dock = new DockPanel { Margin = new Thickness(18) }; var save = Action("저장", () => HomeAction(() => { Space.WriteImplementation(function, editor.Text); SaveSpace(); })); DockPanel.SetDock(save, Dock.Bottom); dock.Children.Add(save); dock.Children.Add(editor); var build = Action("팩 빌드", () => Work(() => runner!.BuildPack(function.Pack, Target, operation!.Token)), true); DockPanel.SetDock(build, Dock.Bottom); dock.Children.Insert(0, build); OpenConceptPage(dock); })));
        OpenConceptPage(new ScrollViewer { Content = panel });
    }
}
