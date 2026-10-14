using System;

// Compile-only API verification: never call input methods on a host runtime.
// This code is linked with the exact compatibility globals used by the kernel.
internal static class KeyboardApiSmoke
{
    public static bool IsSecureShortcut(KeyEvent key)
    {
        return key.Key == ConsoleKeyEx.Delete &&
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) != 0;
    }

    public static bool IsEditorExit(KeyEvent key) => key.Key == ConsoleKeyEx.Escape;

    public static char Character(KeyEvent key) => key.KeyChar;

    public static bool HasInput() => KeyboardManager.KeyAvailable;

    public static bool TryRead(out KeyEvent? key) => KeyboardManager.TryReadKey(out key);
}
