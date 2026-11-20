using System.Runtime.InteropServices;

namespace YTLite;

static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    static bool Down(Keys k) => (GetAsyncKeyState((int)k) & 0x8000) != 0;

    /// <summary>
    /// Current Ctrl/Shift/Alt state. Uses the asynchronous (physical) state: the keystroke was
    /// delivered to the WebView2 browser process, so this thread's own key state can be stale.
    /// </summary>
    public static Keys Modifiers =>
        (Down(Keys.ControlKey) ? Keys.Control : 0) |
        (Down(Keys.ShiftKey) ? Keys.Shift : 0) |
        (Down(Keys.Menu) ? Keys.Alt : 0);
}
