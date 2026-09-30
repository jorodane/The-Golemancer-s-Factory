using GameController;
using Golemancer.Presentation;
namespace Golemancer.iOS;

internal sealed class PadBridge
{
    private readonly Dictionary<nint, int> devices = new();
    private int nextDevice;
    private readonly Dictionary<(int Device, string Key), bool> buttons = new();
    public void Poll(GameScreen screen)
    {
        var connected = GCController.Controllers;
        var handles = connected.Select(p => (nint)p.Handle).ToHashSet();
        foreach (var old in devices.Where(p => !handles.Contains(p.Key)).ToArray())
        {
            screen.ReleaseGamepad(old.Value); devices.Remove(old.Key);
            foreach (var key in buttons.Keys.Where(k => k.Device == old.Value).ToArray()) buttons.Remove(key);
        }
        foreach (var controller in connected)
        {
            if (controller.ExtendedGamepad is not { } pad) continue;
            nint handle = controller.Handle;
            if (!devices.TryGetValue(handle, out int id)) devices.Add(handle, id = ++nextDevice);
            screen.GamepadAxis(id, "LeftX-", "LeftX+", pad.LeftThumbstick.XAxis.Value);
            screen.GamepadAxis(id, "LeftY-", "LeftY+", -pad.LeftThumbstick.YAxis.Value);
            screen.GamepadAxis(id, "DpadLeft", "DpadRight", pad.DPad.XAxis.Value);
            screen.GamepadAxis(id, "DpadUp", "DpadDown", -pad.DPad.YAxis.Value);
            screen.Aim(pad.RightThumbstick.XAxis.Value, -pad.RightThumbstick.YAxis.Value);
            void Button(string name, GCControllerButtonInput input)
            {
                bool down = input.Value > .5f;
                if (buttons.GetValueOrDefault((id, name)) == down) return;
                buttons[(id, name)] = down; screen.Key(name, down, gamepad: true, deviceId: id);
            }
            Button("DpadUp", pad.DPad.Up); Button("DpadDown", pad.DPad.Down); Button("DpadLeft", pad.DPad.Left); Button("DpadRight", pad.DPad.Right);
            Button("ButtonA", pad.ButtonA); Button("ButtonB", pad.ButtonB); Button("ButtonX", pad.ButtonX); Button("ButtonY", pad.ButtonY);
            Button("ButtonL1", pad.LeftShoulder); Button("ButtonR1", pad.RightShoulder);
            Button("ButtonL2", pad.LeftTrigger); Button("ButtonR2", pad.RightTrigger); Button("ButtonStart", pad.ButtonMenu);
            if (pad.ButtonOptions is { } options) Button("ButtonSelect", options);
        }
    }
    public void Reset(GameScreen? screen)
    { foreach (int id in devices.Values) screen?.ReleaseGamepad(id); devices.Clear(); buttons.Clear(); }
}
