using System.Windows;
using System.Windows.Input;
namespace Golemancer.Desktop;

internal sealed partial class MainWindow
{
    private void DismissFromOpener(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || Game.State.Dialogues.Count > 0) return;
        if (bubbleLayer.IsMouseOver || overlay.Child is UIElement { IsMouseOver: true }) return;
        // Only the currently open window's own trigger may cross the modal/menu shield.
        // All other controls underneath still consume the first dismissal click.
        string id = ""; Action? close = null;
        if (modalType is "help" or "journal" or "menu")
        { id = modalType == "menu" ? "pause" : "hud." + modalType; close = CloseOverlay; }
        else if (bubbleMenuType.Length > 0)
        { id = bubbleMenuType == "orders" ? "open.orders" : "hud." + bubbleMenuType; close = CloseBubbles; }
        else if (memoryWindow.Visibility == Visibility.Visible)
        { id = "hud.routines"; close = CloseMemory; }
        else if (equipmentWindow.Visibility == Visibility.Visible && equipmentOwner == session.Actor?.Id)
        { id = "actiongrid.equipment"; close = () => equipmentWindow.Visibility = Visibility.Collapsed; }
        if (close is null || !buttons.TryGetValue(id, out var button) || !button.IsVisible || !button.IsDescendantOf(root)) return;
        var bounds = new Rect(button.TranslatePoint(new Point(), root), button.RenderSize);
        if (!bounds.Contains(e.GetPosition(root))) return;
        e.Handled = true; close();
    }
}
