using Golemancer.Contracts;
using Golemancer.Desktop;
using SkiaSharp;
namespace Golemancer.Presentation;

public sealed partial class GameScreen
{
    private sealed class Modal(string title, string text, bool quantity, int maximum, int value, bool paused, Action<int?> done)
    {
        public string Title = title, Text = text;
        public bool Quantity = quantity, Paused = paused, ReplaceDigits = true;
        public int Maximum = maximum, Value = value;
        public Action<int?> Done = done;
    }
    private Modal? modal;
    private SKRect modalSlider;
    private void ShowModal(string title, string text, bool quantity, int max, int value, Action<int?> done)
    {
        modal = new(title, text, quantity, max, value, session.MenuPaused, done);
        externalModal = true; session.MenuPaused = true; ClearControls();
    }
    private void CloseModal(int? result)
    {
        if (modal is not { } current) return;
        modal = null; externalModal = false; session.MenuPaused = current.Paused; ClearControls();
        current.Done(result);
    }
    private void Details(string title, string text) => ShowModal(title, text, false, 0, 0, _ => { });
    private void Confirm(string title, Action action) => ShowModal(title, "", false, 0, 0, value => { if (value.HasValue) action(); });
    private void Quantity(string title, Func<int> maximum, Func<int, ActionResult> confirm, int initial = 1)
    {
        int max = Math.Clamp(maximum(), 0, 9999);
        if (max == 0) { Notify("지금 처리할 수 있는 수량이 없어."); return; }
        int shortcut = QuantityPicker.Modifier(oneModifier || input.Held("quantity.one"), allModifier || input.Held("quantity.all"), max);
        if (shortcut > 0) { Finish(() => confirm(shortcut)); return; }
        ShowModal(title, "", true, max, QuantityPicker.Clamp(initial, max), value =>
        {
            int live = Math.Clamp(maximum(), 0, 9999);
            if (value.HasValue && live > 0) Finish(() => confirm(Math.Clamp(value.Value, 1, live)));
        });
    }
    private void SetSlider(float x)
    {
        if (modal is not { Quantity: true } m) return;
        m.Value = 1 + (int)Math.Round(Math.Clamp((x - modalSlider.Left) / Math.Max(1, modalSlider.Width), 0, 1) * (m.Maximum - 1));
        m.ReplaceDigits = true;
    }
    private bool ModalKey(string key, bool down, bool repeat)
    {
        if (modal is not { } m || !down || repeat) return true;
        if (key is "Escape" or "Back" or "ButtonB") { CloseModal(null); return true; }
        if (key is "Enter" or "ButtonA") { CloseModal(m.Value); return true; }
        if (!m.Quantity) return true;
        if (key.Length == 4 && key.StartsWith("Num", StringComparison.Ordinal)) key = key.Substring(3);
        else if (key.Length == 2 && key[0] == 'D') key = key.Substring(1);
        if (key is "Backspace" or "Delete") { m.Value /= 10; m.ReplaceDigits = false; }
        else if (key.Length == 1 && char.IsAsciiDigit(key[0]))
        { m.Value = Math.Min(m.Maximum, (m.ReplaceDigits ? 0 : m.Value) * 10 + key[0] - '0'); m.ReplaceDigits = false; }
        else if (key is "Left" or "Down") m.Value = Math.Max(1, m.Value - 1);
        else if (key is "Right" or "Up") m.Value = Math.Min(m.Maximum, m.Value + 1);
        return true;
    }
    private void RenderModal(SKCanvas c)
    {
        if (modal is not { } m) return;
        hit.Clear(); Box(c, SKRect.Create(viewWidth, viewHeight), "#C0102019", 0);
        float x = viewWidth / 2 - 300, y = viewHeight / 2 - 210;
        Box(c, SKRect.Create(x, y, 600, 420), "#FF20382B");
        Text(c, m.Title, x + 24, y + 34, 20);
        if (m.Quantity)
        {
            Text(c, $"{m.Value} / {m.Maximum}", x + 300, y + 85, 28, centered: true);
            modalSlider = SKRect.Create(x + 36, y + 101, 528, 30);
            Box(c, modalSlider, "#FF4B6550");
            float fraction = m.Maximum == 1 ? 1 : (m.Value - 1f) / (m.Maximum - 1);
            Box(c, SKRect.Create(modalSlider.Left, modalSlider.Top, Math.Max(8, modalSlider.Width * fraction), 30), "#FFE0C177");
            string[] labels = ["1개", "−절반", "중간", "+절반", "Max"];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button(c, "quantity.quick." + i, labels[i], SKRect.Create(x + 24 + i * 111, y + 145, 105, 40), () =>
                {
                    m.Value = index switch { 0 => 1, 1 => Math.Max(1, m.Value / 2), 2 => Math.Max(1, (m.Maximum + 1) / 2), 3 => (m.Value + m.Maximum + 1) / 2, _ => m.Maximum }; m.ReplaceDigits = true;
                });
            }
            for (int i = 0; i < 10; i++)
            {
                string digit = i.ToString();
                Button(c, "quantity.digit." + digit, digit, SKRect.Create(x + 24 + i % 5 * 111, y + 199 + i / 5 * 49, 105, 42), () => ModalKey(digit, true, false));
            }
            Button(c, "quantity.erase", "한 자리 지우기", SKRect.Create(x + 24, y + 302, 216, 36), () => ModalKey("Backspace", true, false));
        }
        else Wrap(c, m.Text, x + 24, y + 75, 552, 15, 12);
        Button(c, "modal.cancel", "취소 / 닫기", SKRect.Create(x + 265, y + 357, 145, 43), () => CloseModal(null));
        Button(c, "modal.ok", "확인", SKRect.Create(x + 425, y + 357, 145, 43), () => CloseModal(m.Value));
    }
}
