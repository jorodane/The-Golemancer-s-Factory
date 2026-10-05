using System.Runtime.InteropServices;
namespace Confectory.Platform.Sdl;

internal static class Sdl
{
    private const string Library = "libSDL2-2.0.so.0";
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal unsafe struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public int Device;
        [FieldOffset(12)] public byte Kind;
        [FieldOffset(13)] public byte Repeat;
        [FieldOffset(16)] public byte Button;
        [FieldOffset(18)] public byte Clicks;
        [FieldOffset(16)] public short AxisValue;
        [FieldOffset(20)] public int X;
        [FieldOffset(24)] public int Y;
        [FieldOffset(20)] public int Key;
        [FieldOffset(12)] public fixed byte Text[32];
        public string TextValue { get { fixed (byte* bytes = Text) return Marshal.PtrToStringUTF8((IntPtr)bytes) ?? ""; } }
    }
    [DllImport(Library)] internal static extern int SDL_Init(uint flags);
    [DllImport(Library)] internal static extern void SDL_Quit();
    [DllImport(Library)] internal static extern void SDL_StartTextInput();
    [DllImport(Library)] internal static extern void SDL_StopTextInput();
    [DllImport(Library)] internal static extern IntPtr SDL_GetClipboardText();
    [DllImport(Library)] internal static extern int SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Library)] internal static extern void SDL_free(IntPtr pointer);
    [DllImport(Library)] private static extern IntPtr SDL_GetError();
    internal static string Error => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL error";
    [DllImport(Library)] internal static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int w, int h, uint flags);
    [DllImport(Library)] internal static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport(Library)] internal static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);
    [DllImport(Library)] internal static extern void SDL_DestroyRenderer(IntPtr renderer);
    [DllImport(Library)] internal static extern int SDL_GetRendererOutputSize(IntPtr renderer, out int w, out int h);
    [DllImport(Library)] internal static extern void SDL_GetWindowSize(IntPtr window, out int w, out int h);
    [DllImport(Library)] internal static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);
    [DllImport(Library)] internal static extern void SDL_DestroyTexture(IntPtr texture);
    [DllImport(Library)] internal static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);
    [DllImport(Library)] internal static extern int SDL_RenderClear(IntPtr renderer);
    [DllImport(Library)] internal static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, IntPtr source, IntPtr destination);
    [DllImport(Library)] internal static extern void SDL_RenderPresent(IntPtr renderer);
    [DllImport(Library)] internal static extern int SDL_PollEvent(out Event e);
    [DllImport(Library)] internal static extern int SDL_PushEvent(ref Event e);
    [DllImport(Library)] internal static extern void SDL_Delay(uint ms);
    [DllImport(Library)] internal static extern IntPtr SDL_GetKeyName(int key);
    [DllImport(Library)] internal static extern int SDL_NumJoysticks();
    [DllImport(Library)] internal static extern int SDL_IsGameController(int index);
    [DllImport(Library)] internal static extern IntPtr SDL_GameControllerOpen(int index);
    [DllImport(Library)] internal static extern void SDL_GameControllerClose(IntPtr pad);
    [DllImport(Library)] internal static extern IntPtr SDL_GameControllerGetJoystick(IntPtr pad);
    [DllImport(Library)] internal static extern int SDL_JoystickInstanceID(IntPtr joystick);
    internal static string KeyName(int key)
    {
        string name = Marshal.PtrToStringUTF8(SDL_GetKeyName(key)) ?? "";
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) return "D" + name;
        return name switch { "Return" => "Enter", "Space" => "Space", "Left Shift" => "LeftShift", "Right Shift" => "RightShift", "Left Ctrl" => "LeftCtrl", "Right Ctrl" => "RightCtrl", "Left Alt" => "LeftAlt", "Right Alt" => "RightAlt", _ => name.Length == 1 ? name.ToUpperInvariant() : name };
    }
}
