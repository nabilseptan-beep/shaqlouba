using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Shaqlouba
{
    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const ushort VK_SHIFT = 0x10;
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_MENU = 0x12;
        public const ushort VK_END = 0x23;
        public const ushort VK_HOME = 0x24;
        public const ushort VK_INSERT = 0x2D;
        public const ushort VK_LWIN = 0x5B;
        public const ushort VK_RWIN = 0x5C;
        public const ushort VK_NONAME = 0xFC;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        public const uint GMEM_MOVEABLE = 0x0002;
        public const uint CF_BITMAP = 2;
        public const uint CF_METAFILEPICT = 3;
        public const uint CF_PALETTE = 9;
        public const uint CF_UNICODETEXT = 13;
        public const uint CF_ENHMETAFILE = 14;
        public const uint CF_OWNERDISPLAY = 0x0080;
        public const uint CF_DSPTEXT = 0x0081;
        public const uint CF_DSPENHMETAFILE = 0x008E;
        public const uint CF_GDIOBJFIRST = 0x0300;
        public const uint CF_GDIOBJLAST = 0x03FF;

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public uint cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int caretLeft, caretTop, caretRight, caretBottom;
        }

        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int size);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] list);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);

        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr handle);
        [DllImport("gdi32.dll")] public static extern IntPtr CopyEnhMetaFile(IntPtr source, IntPtr fileName);
        [DllImport("gdi32.dll")] public static extern bool DeleteEnhMetaFile(IntPtr handle);

        public static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        // The class of the control holding the keyboard focus inside a window of another program.
        public static string FocusedClassIn(IntPtr window)
        {
            uint processId;
            uint thread = GetWindowThreadProcessId(window, out processId);
            var info = new GUITHREADINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
            return GetGUIThreadInfo(thread, ref info) ? ClassOf(info.hwndFocus) : "";
        }

        public static bool IsKeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        // Presses the keys in order, then releases them in reverse.
        public static void SendChord(params ushort[] keys)
        {
            var inputs = new INPUT[keys.Length * 2];
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[i] = KeyInput(keys[i], false);
                inputs[inputs.Length - 1 - i] = KeyInput(keys[i], true);
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendKeyUp(ushort key)
        {
            var inputs = new[] { KeyInput(key, true) };
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static INPUT KeyInput(ushort vk, bool up)
        {
            // Without the extended flag, Insert/Home/End are read as numpad keys when Num Lock is on.
            bool extended = vk == VK_INSERT || vk == VK_HOME || vk == VK_END;

            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = vk;
            input.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            input.u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0);
            return input;
        }

        // Asks the window to switch to the first installed layout of the language the text is now in.
        public static void SwitchLayout(IntPtr window, Direction direction)
        {
            const int Arabic = 0x01, English = 0x09;
            var list = new IntPtr[32];
            int count = GetKeyboardLayoutList(list.Length, list);

            IntPtr layout = IntPtr.Zero;
            for (int i = 0; i < count && layout == IntPtr.Zero; i++)
            {
                int language = (int)(list[i].ToInt64() & 0x3FF);
                if (language == (direction == Direction.EnToAr ? Arabic : English)) layout = list[i];
            }

            if (layout != IntPtr.Zero) PostMessage(window, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout);
        }
    }
}
