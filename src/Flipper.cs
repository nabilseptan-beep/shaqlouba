using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Shaqlouba
{
    // The whole flip: copy from the focused app, rewrite the text, paste it back, put the clipboard back.
    //
    // It copies with Ctrl+Insert and pastes with Shift+Insert rather than Ctrl+C/Ctrl+V, because Ctrl+C
    // with nothing selected would interrupt whatever is running in a terminal, including terminals
    // embedded in other apps, which can't be detected from outside.
    internal sealed class Flipper
    {
        const int SelectionCopyTimeoutMs = 400;
        const int LineCopyTimeoutMs = 600;
        const int ExcelCellCopyTimeoutMs = 2000;
        const int TextAppearTimeoutMs = 1500;
        const int RefocusDelayMs = 100;
        const int RefocusAttempts = 12;
        const int KeyReleaseTimeoutMs = 800;

        // Windows services read the clipboard right after it changes. An app that tries to open it in that
        // moment fails silently, so a copy or paste sent too soon just does nothing.
        const int ClipboardSettleMs = 50;

        // Apps read the clipboard after they get the paste key, so it can't be restored straight away.
        const int PasteSettleMs = 600;

        static readonly HashSet<string> Terminals = new HashSet<string>
        {
            "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "VirtualConsoleClass", "mintty", "PuTTY", "KiTTY",
        };

        // Editors that copy the whole current line when nothing is selected.
        static readonly HashSet<string> CopyLineWhenEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code", "Code - Insiders", "Cursor", "Windsurf", "VSCodium", "devenv", "sublime_text",
            "idea64", "pycharm64", "webstorm64", "rider64", "clion64", "goland64", "phpstorm64", "rubymine64", "studio64",
        };

        readonly IntPtr owner;
        readonly Action<string> notify;
        bool busy;

        // The last flip, so pressing the hotkey again restores the exact original text.
        // The layouts can't always be reversed: "لا" could have been typed as b or as g+h.
        string lastBefore, lastAfter;
        Direction lastDirection;

        public Flipper(IntPtr clipboardOwner, Action<string> notify)
        {
            owner = clipboardOwner;
            this.notify = notify;
        }

        public bool SwitchLayoutAfterFlip { get; set; }
        public int HotkeyVk { get; set; }

        // refocus: the window to go back to first, when the tray icon took the focus away from it.
        public async void Run(IntPtr refocus)
        {
            if (busy) return;
            busy = true;
            try
            {
                await FlipAsync(refocus);
            }
            catch (Exception ex)
            {
                notify("حصلت مشكلة: " + ex.Message);
            }
            finally
            {
                busy = false;
            }
        }

        async Task FlipAsync(IntPtr refocus)
        {
            await WaitForKeysReleasedAsync();

            if (refocus != IntPtr.Zero && !await RefocusAsync(refocus))
            {
                notify("مقدرتش أرجع للبرنامج اللي فيه النص. جرب الاختصار بدل الأيقونة.");
                return;
            }

            IntPtr target = Native.GetForegroundWindow();
            if (target == IntPtr.Zero) return;

            ClipboardSnapshot saved = ClipboardEx.Take(owner);
            if (saved == null)
            {
                notify("الحافظة مشغولة دلوقتي، جرب تاني.");
                return;
            }

            try
            {
                bool terminal = Terminals.Contains(Native.ClassOf(target));
                bool copiesLine = CopiesLineWhenEmpty(target);
                bool excelCell = IsExcelCell(target);

                ClipboardEx.Clear(owner);
                await Task.Delay(ClipboardSettleMs);
                string text = await CopyAsync(excelCell ? ExcelCellCopyTimeoutMs : SelectionCopyTimeoutMs);
                if (text != null && text.Length == 0)
                {
                    notify("المحدد مش نص، أو البرنامج لسه مشغول. جرب تاني.");
                    return;
                }

                bool wholeLine = text == null || (copiesLine && IsSingleLineWithBreak(text));
                if (wholeLine)
                {
                    if (terminal)
                    {
                        notify("في نافذة الأوامر لازم تحدد النص بالماوس الأول.");
                        return;
                    }

                    if (excelCell)
                    {
                        notify("الخلية ماتنسختش. جرب تاني.");
                        return;
                    }

                    ClipboardEx.Clear(owner);
                    Native.SendChord(Native.VK_SHIFT, Native.VK_HOME);
                    await Task.Delay(ClipboardSettleMs);
                    text = await CopyAsync(LineCopyTimeoutMs);

                    // Shift+Home never selects a line break, so one here means nothing got selected.
                    if (string.IsNullOrEmpty(text) || (copiesLine && IsSingleLineWithBreak(text)))
                    {
                        if (text == null) Native.SendChord(Native.VK_END);
                        notify("مفيش نص أقلبه. حدد النص أو حط المؤشر في آخر السطر.");
                        return;
                    }
                }

                Direction direction;
                string flipped;
                if (text == lastAfter)
                {
                    flipped = lastBefore;
                    direction = lastDirection == Direction.EnToAr ? Direction.ArToEn : Direction.EnToAr;
                }
                else
                {
                    flipped = wholeLine ? KeyMap.FlipLastRun(text, out direction) : KeyMap.Flip(text, out direction);
                }

                if (direction == Direction.None || flipped == text)
                {
                    if (wholeLine) Native.SendChord(Native.VK_END);
                    notify("النص ده مفيهوش حاجة تتقلب.");
                    return;
                }

                if (!ClipboardEx.WriteText(owner, flipped))
                {
                    if (wholeLine) Native.SendChord(Native.VK_END);
                    notify("الحافظة مشغولة دلوقتي، جرب تاني.");
                    return;
                }

                lastBefore = text;
                lastAfter = flipped;
                lastDirection = direction;

                uint written = Native.GetClipboardSequenceNumber();
                await Task.Delay(ClipboardSettleMs);
                Native.SendChord(Native.VK_SHIFT, Native.VK_INSERT);
                if (SwitchLayoutAfterFlip) Native.SwitchLayout(target, direction);
                await Task.Delay(PasteSettleMs);

                // If something else landed on the clipboard meanwhile, it's newer than what we saved.
                if (Native.GetClipboardSequenceNumber() != written) saved = null;
            }
            finally
            {
                if (saved != null) ClipboardEx.Restore(owner, saved);
            }
        }

        // Closing the tray flyout can hand the focus to another window just after we took it back,
        // so insist until it stays. Flipping in the wrong window would be far worse than not flipping.
        async Task<bool> RefocusAsync(IntPtr window)
        {
            int steadyChecks = 0;
            for (int i = 0; i < RefocusAttempts; i++)
            {
                if (Native.GetForegroundWindow() == window)
                {
                    if (++steadyChecks == 2) return true;
                }
                else
                {
                    steadyChecks = 0;
                    Native.SetForegroundWindow(window);
                }
                await Task.Delay(RefocusDelayMs);
            }
            return false;
        }

        // Returns the copied text, "" if something other than text was copied, or null if nothing was.
        async Task<string> CopyAsync(int timeoutMs)
        {
            uint before = Native.GetClipboardSequenceNumber();
            Native.SendChord(Native.VK_CONTROL, Native.VK_INSERT);

            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(15);
                if (Native.GetClipboardSequenceNumber() == before) continue;

                // The clipboard changes before the app has finished writing to it. Excel can take
                // several hundred milliseconds to put the text there, so don't give up too soon.
                var waiting = Stopwatch.StartNew();
                while (waiting.ElapsedMilliseconds < TextAppearTimeoutMs)
                {
                    string text = ClipboardEx.ReadText(owner);
                    if (!string.IsNullOrEmpty(text)) return text;
                    await Task.Delay(20);
                }
                return "";
            }
            return null;
        }

        // Sending Ctrl+Insert while the user still holds Alt or Shift would be a different shortcut.
        async Task WaitForKeysReleasedAsync()
        {
            var clock = Stopwatch.StartNew();
            while (AnyHotkeyKeyDown() && clock.ElapsedMilliseconds < KeyReleaseTimeoutMs) await Task.Delay(10);
            if (!AnyHotkeyKeyDown()) return;

            // Tapping an unused key first stops a lone Alt or Win release from opening a menu.
            Native.SendChord(Native.VK_NONAME);
            foreach (ushort key in new[] { Native.VK_SHIFT, Native.VK_CONTROL, Native.VK_MENU, Native.VK_LWIN, Native.VK_RWIN })
                if (Native.IsKeyDown(key)) Native.SendKeyUp(key);
        }

        bool AnyHotkeyKeyDown()
        {
            return Native.IsKeyDown(Native.VK_SHIFT) || Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MENU)
                || Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)
                || (HotkeyVk != 0 && Native.IsKeyDown(HotkeyVk));
        }

        static bool CopiesLineWhenEmpty(IntPtr window)
        {
            try
            {
                uint processId;
                Native.GetWindowThreadProcessId(window, out processId);
                using (Process process = Process.GetProcessById((int)processId))
                    return CopyLineWhenEmpty.Contains(process.ProcessName);
            }
            catch (ArgumentException)
            {
                return false;  // the window's process just exited
            }
        }

        // True when a cell is selected in Excel, as opposed to being edited. A selected cell always copies,
        // but can be slow to. Shift+Home must never be sent here: it would extend the selection over other
        // cells, and pasting on top of them would turn their formulas into plain text.
        static bool IsExcelCell(IntPtr window)
        {
            return Native.ClassOf(window) == "XLMAIN" && Native.FocusedClassIn(window) == "EXCEL7";
        }

        static bool IsSingleLineWithBreak(string text)
        {
            return text.EndsWith("\n") && text.IndexOf('\n') == text.Length - 1;
        }
    }
}
