using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace Shaqlouba
{
    // Drives the real exe against real apps: types wrong-layout text, presses the hotkey, reads the result.
    // It takes over the keyboard while it runs and refuses to send keys unless its own target window is
    // in the foreground. The user's clipboard, focused window and layout are put back at the end.
    //
    //   e2e.exe <path to Shaqlouba.exe> <scratch folder> [targets: form notepad chrome word excel console]
    internal static class E2E
    {
        const int FlipWaitMs = 1900;
        const string Sentinel = "SHAQLOUBA-CLIPBOARD-SENTINEL";
        const string AppLabel = "شقلوبة";

        static IntPtr owner;
        static IntPtr expectedForeground;
        static int passed, failed;
        static string scratch;
        static TextBoxBase formField;
        static Process app;
        static bool trayAvailable;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string exe = args[0];
            scratch = args[1];
            var targets = new List<string>();
            for (int i = 2; i < args.Length; i++) targets.Add(args[i]);
            if (targets.Count == 0) targets.AddRange(new[] { "form", "notepad", "tray", "chrome", "word", "excel", "console", "menu" });

            if (Process.GetProcessesByName("Shaqlouba").Length > 0)
            {
                Console.WriteLine("Close Shaqlouba first: the tests start their own copy.");
                return 2;
            }

            // The menu test changes the hotkey, and the tests expect the default one.
            string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shaqlouba", "settings.ini");
            byte[] savedSettings = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
            if (savedSettings != null) File.Delete(settingsFile);

            Native.SetProcessDPIAware();
            var ownerForm = new Form();
            owner = ownerForm.Handle;

            IntPtr userWindow = GetForegroundWindow();
            uint unused;
            IntPtr userLayout = GetKeyboardLayout(GetWindowThreadProcessId(userWindow, out unused));
            ClipboardSnapshot savedClipboard = ClipboardEx.Take(owner);

            app = Process.Start(exe);
            Wait(1500);
            try
            {
                foreach (string target in targets)
                {
                    Console.WriteLine("=== " + target + " ===");
                    try { RunTarget(target); }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine("ABORT " + target + ": " + ex.Message);
                    }
                    ReleaseHeldModifiers();
                }
            }
            finally
            {
                ReleaseHeldModifiers();
                if (!app.HasExited) app.Kill();
                app.WaitForExit(2000);
                if (savedSettings != null) File.WriteAllBytes(settingsFile, savedSettings);
                else if (File.Exists(settingsFile)) File.Delete(settingsFile);
                if (savedClipboard != null) ClipboardEx.Restore(owner, savedClipboard);
                expectedForeground = IntPtr.Zero;
                Focus(userWindow);
                PostMessage(userWindow, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, userLayout);
            }

            Console.WriteLine(string.Format("PASSED {0}, FAILED {1}", passed, failed));
            return failed == 0 ? 0 : 1;
        }

        // Another app's keyboard hook can swallow a key-up we injected, which leaves the key held for the
        // whole system. It happened once with a Ctrl+Alt hotkey owned by another program, so always check.
        static void ReleaseHeldModifiers()
        {
            foreach (ushort key in new[] { Native.VK_SHIFT, Native.VK_CONTROL, Native.VK_MENU, Native.VK_LWIN, Native.VK_RWIN })
            {
                if (!Native.IsKeyDown(key)) continue;
                failed++;
                Console.WriteLine("FAIL key 0x" + key.ToString("X2") + " was left held, releasing it");
                Native.SendChord(Native.VK_NONAME);
                Native.SendKeyUp(key);
            }
        }

        static void RunTarget(string name)
        {
            switch (name)
            {
                case "form": RunForm(); break;
                case "notepad": RunNotepad(); break;
                case "chrome": RunChrome(); break;
                case "word": RunWord(); break;
                case "excel": RunExcel(); break;
                case "console": RunConsole(); break;
                case "tray": trayAvailable = true; RunTray(); break;
                case "menu": RunMenu(); break;
                default: throw new ArgumentException("unknown target");
            }
        }

        // The cases every text field should pass. autoCaps: the app capitalizes the first letter as you type.
        static void StandardCases(string label, IntPtr window, bool autoCaps)
        {
            Clear();
            TypeText("hgsghl ugd;l");
            FlipAndExpect(label, "wrong layout, no selection", window, "السلام عليكم", autoCaps, 0x01);
            FlipAndExpect(label, "second press restores", window, "hgsghl ugd;l", autoCaps, 0x09);

            Clear();
            TypeText("اثممخ صخقمي");
            FlipAndExpect(label, "English typed on Arabic", window, "hello world", autoCaps, 0x09);

            Clear();
            TypeText("مرحبا hgsghl");
            FlipAndExpect(label, "keeps the correct start", window, "مرحبا السلام", autoCaps, 0x01);

            Clear();
            TypeText("abc def");
            for (int i = 0; i < 3; i++) SendArrow(VK_LEFT, true);
            FlipAndExpect(label, "selection only", window, "abc يثب", autoCaps, 0x01);

            Clear();
            TypeText("hgsghl");
            Native.SendChord(VK_RETURN);
            Wait(50);
            TypeText("ugd;l");
            SelectAll();
            FlipAndExpect(label, "multi-line selection", window, "السلام\nعليكم", autoCaps, 0x01);
        }

        static void FlipAndExpect(string label, string caseName, IntPtr window, string expected, bool autoCaps, int layoutLanguage)
        {
            EnsureFocus();
            ClipboardEx.WriteText(owner, Sentinel);
            PressHotkey();
            Wait(FlipWaitMs);

            string clipboard = ClipboardEx.ReadText(owner);
            string actual = Normalize(ReadField());
            bool textOk = autoCaps
                ? string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                : actual == expected;

            Check(label + ": " + caseName, textOk, "got [" + actual + "], expected [" + expected + "]");
            Check(label + ": " + caseName + " / clipboard restored", clipboard == Sentinel, "clipboard was [" + clipboard + "]");

            uint unused;
            long language = GetKeyboardLayout(GetWindowThreadProcessId(window, out unused)).ToInt64() & 0x3FF;
            Check(label + ": " + caseName + " / layout", language == layoutLanguage, "layout language is 0x" + language.ToString("X"));
        }

        static void Check(string name, bool ok, string detail)
        {
            if (ok) passed++;
            else failed++;
            Console.WriteLine((ok ? "pass " : "FAIL ") + name + (ok ? "" : "  -> " + detail));
        }

        static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\v", "\n").TrimEnd('\n');
        }

        // ---- targets ----

        static void RunForm()
        {
            using (var form = new Form { Text = "ShaqTest form", Width = 700, Height = 400, TopMost = true })
            {
                var plain = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Top, Height = 150 };
                var rich = new RichTextBox { Dock = DockStyle.Bottom, Height = 150 };
                form.Controls.Add(plain);
                form.Controls.Add(rich);
                form.Show();
                Wait(300);

                Focus(form.Handle);
                expectedForeground = form.Handle;

                // These two are read through their properties: an old-style TextBox ignores Ctrl+A.
                plain.Focus();
                formField = plain;
                Wait(100);
                StandardCases("TextBox", form.Handle, false);

                rich.Focus();
                formField = rich;
                Wait(100);
                StandardCases("RichTextBox", form.Handle, false);
                formField = null;
            }
        }

        static void RunNotepad()
        {
            string file = Path.Combine(scratch, "shaq_notepad_test.txt");
            File.WriteAllText(file, "");
            Process.Start("notepad.exe", "\"" + file + "\"");
            IntPtr window = WaitForWindow(w => ClassOf(w) == "Notepad" && TitleOf(w).Contains("shaq_notepad_test"), 15000);
            try
            {
                Focus(window);
                expectedForeground = window;
                StandardCases("Notepad", window, false);

                // Leave the tab clean so Notepad doesn't bring it back next time.
                Clear();
                Native.SendChord(Native.VK_CONTROL, VK_S);
                Wait(500);
                Native.SendChord(Native.VK_CONTROL, VK_W);
                Wait(500);
            }
            finally
            {
                expectedForeground = IntPtr.Zero;
                if (IsWindow(window)) PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        static void RunChrome()
        {
            string page = Path.Combine(scratch, "shaq_test.html");
            File.WriteAllText(page,
                "<!doctype html><meta charset=utf-8><title>ShaqTest</title>" +
                "<textarea id=t autofocus style='width:95%;height:40vh'></textarea>" +
                "<div id=c contenteditable style='width:95%;height:40vh;border:1px solid #888'></div>",
                new UTF8Encoding(false));

            var start = new ProcessStartInfo(@"C:\Program Files\Google\Chrome\Application\chrome.exe",
                "--user-data-dir=\"" + Path.Combine(scratch, "chrome_profile") + "\" --no-first-run --no-default-browser-check " +
                "--window-size=800,600 --app=\"file:///" + page.Replace('\\', '/') + "\"");
            Process chrome = Process.Start(start);
            IntPtr window = WaitForWindow(w => TitleOf(w).Contains("ShaqTest"), 20000);
            try
            {
                Wait(1500);
                Focus(window);
                expectedForeground = window;
                StandardCases("Chrome textarea", window, false);

                Native.SendChord(VK_TAB);
                Wait(200);
                StandardCases("Chrome contenteditable", window, false);

                if (trayAvailable) TrayClickCases("Tray click (Chrome)", window);
            }
            finally
            {
                expectedForeground = IntPtr.Zero;
                KillTree(chrome.Id);
            }
        }

        static void RunWord()
        {
            dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application"));
            word.Visible = true;
            word.DisplayAlerts = 0;
            word.Documents.Add();
            word.Activate();
            Wait(1500);
            IntPtr window = new IntPtr((int)word.ActiveWindow.Hwnd);
            try
            {
                Focus(window);
                expectedForeground = window;
                StandardCases("Word", window, true);
            }
            finally
            {
                expectedForeground = IntPtr.Zero;
                word.Quit(0);
            }
        }

        static void RunExcel()
        {
            dynamic excel = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"));
            excel.Visible = true;
            excel.DisplayAlerts = false;
            dynamic book = excel.Workbooks.Add();
            Wait(1500);
            IntPtr window = new IntPtr((int)excel.Hwnd);
            try
            {
                dynamic sheet = book.ActiveSheet;

                // A selected cell that isn't being edited.
                sheet.Range("A1").Value2 = "hgsghl";
                sheet.Range("A1").Select();
                Focus(window);
                expectedForeground = window;
                ClipboardEx.WriteText(owner, Sentinel);
                PressHotkey();
                Wait(FlipWaitMs);

                // Right after launching, Excel occasionally empties the clipboard without finishing the copy.
                // Nothing changes and the app says so, and pressing again works, which is what a user would do.
                if ((string)sheet.Range("A1").Value2 != "السلام")
                {
                    Console.WriteLine("  note: Excel missed the copy right after launching, pressing again");
                    Wait(500);
                    ClipboardEx.WriteText(owner, Sentinel);
                    PressHotkey();
                    Wait(FlipWaitMs);
                }
                Check("Excel: selected cell", (string)sheet.Range("A1").Value2 == "السلام", "got [" + sheet.Range("A1").Value2 + "]");
                Check("Excel: selected cell / clipboard restored", ClipboardEx.ReadText(owner) == Sentinel, "clipboard changed");
                Native.SendChord(VK_ESCAPE);
                Wait(200);

                // Typing inside a cell, before pressing Enter.
                sheet.Range("A2").Select();
                Wait(200);
                EnsureFocus();
                TypeText("hgsghl ugd;l");
                ClipboardEx.WriteText(owner, Sentinel);
                PressHotkey();
                Wait(FlipWaitMs);
                Native.SendChord(VK_RETURN);
                Wait(400);
                Check("Excel: while editing a cell", (string)sheet.Range("A2").Value2 == "السلام عليكم", "got [" + sheet.Range("A2").Value2 + "]");
                Check("Excel: while editing / clipboard restored", ClipboardEx.ReadText(owner) == Sentinel, "clipboard changed");

                // A selected cell again, now that Excel has settled: what a real session looks like.
                sheet.Range("A3").Value2 = "hgsghl";
                sheet.Range("A3").Select();
                Wait(300);
                EnsureFocus();
                ClipboardEx.WriteText(owner, Sentinel);
                PressHotkey();
                Wait(FlipWaitMs);
                Check("Excel: selected cell, settled", (string)sheet.Range("A3").Value2 == "السلام", "got [" + sheet.Range("A3").Value2 + "]");
                Check("Excel: selected cell, settled / clipboard restored", ClipboardEx.ReadText(owner) == Sentinel, "clipboard changed");
            }
            finally
            {
                expectedForeground = IntPtr.Zero;
                book.Close(false);
                excel.Quit();
            }
        }

        // Pressing the hotkey in a terminal running something must not interrupt it, which Ctrl+C would.
        // Windows opens the console in whichever terminal is the default, so look for any new terminal window.
        static void RunConsole()
        {
            var pingsBefore = new HashSet<int>();
            foreach (Process p in Process.GetProcessesByName("ping")) pingsBefore.Add(p.Id);

            var windowsBefore = new HashSet<IntPtr>();
            EnumWindows((w, l) =>
            {
                if (IsTerminal(w)) windowsBefore.Add(w);
                return true;
            }, IntPtr.Zero);

            IntPtr window = IntPtr.Zero;
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 40 127.0.0.1") { UseShellExecute = true });
            try
            {
                window = WaitForWindow(w => IsTerminal(w) && !windowsBefore.Contains(w), 10000);
                Wait(1000);
                Focus(window);
                expectedForeground = window;
                ClipboardEx.WriteText(owner, Sentinel);
                PressHotkey();
                Wait(FlipWaitMs);

                bool stillRunning = false;
                foreach (Process p in Process.GetProcessesByName("ping"))
                    if (!pingsBefore.Contains(p.Id)) stillRunning = true;
                Check("Terminal (" + ClassOf(window) + "): running program not interrupted", stillRunning, "ping was killed");
                Check("Terminal: clipboard restored", ClipboardEx.ReadText(owner) == Sentinel, "clipboard changed");
            }
            finally
            {
                expectedForeground = IntPtr.Zero;
                foreach (Process p in Process.GetProcessesByName("ping"))
                    if (!pingsBefore.Contains(p.Id)) p.Kill();
                Wait(500);
                if (window != IntPtr.Zero && IsWindow(window)) PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        static bool IsTerminal(IntPtr window)
        {
            string name = ClassOf(window);
            return IsWindowVisible(window) && (name == "ConsoleWindowClass" || name == "CASCADIA_HOSTING_WINDOW_CLASS");
        }

        // A real mouse click on the tray icon, which takes the focus away from the window holding the text.
        static void RunTray()
        {
            using (var form = new Form { Text = "ShaqTest tray", Width = 700, Height = 300, TopMost = true })
            {
                var box = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill };
                form.Controls.Add(box);
                form.Show();
                Wait(300);
                Focus(form.Handle);
                expectedForeground = form.Handle;
                box.Focus();
                formField = box;
                Wait(100);

                TrayClickCases("Tray click (form)", form.Handle);
                formField = null;
            }
        }

        static void TrayClickCases(string label, IntPtr window)
        {
            Clear();
            TypeText("hgsghl ugd;l");
            ClickAndExpect(label + ": no selection", window, "السلام عليكم");

            Clear();
            TypeText("abc def");
            for (int i = 0; i < 3; i++) SendArrow(VK_LEFT, true);
            ClickAndExpect(label + ": keeps the selection", window, "abc يثب");
        }

        static void ClickAndExpect(string name, IntPtr window, string expected)
        {
            ClipboardEx.WriteText(owner, Sentinel);
            ClickTrayIcon(false);
            Wait(FlipWaitMs);
            Check(name + " / focus returned", GetForegroundWindow() == window, "foreground is [" + ClassOf(GetForegroundWindow()) + "] [" + TitleOf(GetForegroundWindow()) + "]");
            string clipboard = ClipboardEx.ReadText(owner);
            string actual = Normalize(ReadField());
            Check(name, actual == expected, "got [" + actual + "], expected [" + expected + "]");
            Check(name + " / clipboard restored", clipboard == Sentinel, "clipboard was [" + clipboard + "]");
        }

        // The tray menu, the hotkey dialog and the startup toggle, driven the way a user would.
        static void RunMenu()
        {
            const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (var form = new Form { Text = "ShaqTest menu", Width = 700, Height = 300 })
            {
                var box = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill };
                form.Controls.Add(box);
                form.Show();
                Wait(300);
                Focus(form.Handle);
                expectedForeground = form.Handle;
                box.Focus();
                formField = box;
                Wait(100);

                // Change the hotkey: second item in the menu.
                ClickTrayIcon(true);
                Wait(600);
                Screenshot("tray_menu.png");
                MenuClick("الاختصار");
                Wait(700);
                EnsureAppForeground();
                Native.SendChord(Native.VK_CONTROL, Native.VK_SHIFT, VK_F9);
                Wait(300);
                Screenshot("hotkey_dialog.png");
                EnsureAppForeground();
                Native.SendChord(VK_RETURN);
                Wait(600);

                Focus(form.Handle);
                Clear();
                TypeText("hgsghl");
                Native.SendChord(Native.VK_CONTROL, Native.VK_SHIFT, VK_F9);
                Wait(FlipWaitMs);
                Check("Menu: new hotkey flips", box.Text == "السلام", "got [" + box.Text + "]");

                Clear();
                TypeText("hgsghl");
                PressHotkey();
                Wait(FlipWaitMs);
                Check("Menu: old hotkey no longer flips", box.Text == "hgsghl", "got [" + box.Text + "]");

                // Start with Windows: fourth item, separators are skipped.
                bool wasEnabled;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    wasEnabled = key.GetValue("Shaqlouba") != null;
                if (wasEnabled) throw new InvalidOperationException("autostart already on, not touching it");

                ClickTrayIcon(true);
                Wait(600);
                MenuClick("مع ويندوز");
                Wait(400);
                string value;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    value = key.GetValue("Shaqlouba") as string;
                Check("Menu: start with Windows turns on", value != null && value.Contains("Shaqlouba.exe"), "value is [" + value + "]");

                Focus(form.Handle);
                ClickTrayIcon(true);
                Wait(600);
                MenuClick("مع ويندوز");
                Wait(400);
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    value = key.GetValue("Shaqlouba") as string;
                Check("Menu: start with Windows turns off", value == null, "value is [" + value + "]");

                formField = null;
                expectedForeground = IntPtr.Zero;
            }
        }

        static void MenuClick(string text)
        {
            AutomationElement item = null;
            EnumWindows((w, l) =>
            {
                if (OwnerPid(w) != app.Id || !IsWindowVisible(w)) return true;
                foreach (AutomationElement element in AutomationElement.FromHandle(w).FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    if (element.Current.Name.Contains(text))
                    {
                        item = element;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            if (item == null) throw new InvalidOperationException("menu item [" + text + "] not found");

            POINT back;
            GetCursorPos(out back);
            System.Windows.Rect bounds = item.Current.BoundingRectangle;
            ClickAt((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2), false);
            SetCursorPos(back.X, back.Y);
        }

        static void EnsureAppForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (OwnerPid(foreground) != app.Id)
                throw new InvalidOperationException("foreground is [" + ClassOf(foreground) + "] [" + TitleOf(foreground) + "], not the app's menu or dialog; refusing to send keys");
            Console.WriteLine("  (foreground: [" + TitleOf(foreground) + "])");
        }

        // Windows reports the ^ arrow's rectangle while the icon is hidden behind it. In that case open
        // the flyout and click the icon there, the same way someone would on a first run.
        static void ClickTrayIcon(bool right)
        {
            POINT back;
            GetCursorPos(out back);

            RECT rect = FindTrayIconRect();
            int x = (rect.Left + rect.Right) / 2, y = (rect.Top + rect.Bottom) / 2;
            AutomationElement atPoint = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (!atPoint.Current.Name.Contains(AppLabel))
            {
                // A flyout left open by the previous step would close on this click, hence the second try.
                IntPtr flyout = IntPtr.Zero;
                for (int attempt = 0; attempt < 2 && flyout == IntPtr.Zero; attempt++)
                {
                    ClickAt(x, y, false);
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 1500 && flyout == IntPtr.Zero)
                    {
                        Wait(50);
                        if (ClassOf(GetForegroundWindow()) == "TopLevelWindowForOverflowXamlIsland") flyout = GetForegroundWindow();
                    }
                }
                if (flyout == IntPtr.Zero)
                    throw new InvalidOperationException("the tray flyout didn't open, foreground is [" + ClassOf(GetForegroundWindow()) + "]");
                Wait(300);

                AutomationElement icon = null;
                foreach (AutomationElement element in AutomationElement.FromHandle(flyout).FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    if (element.Current.Name.Contains(AppLabel))
                    {
                        icon = element;
                        break;
                    }
                }
                if (icon == null) throw new InvalidOperationException("the icon isn't in the tray flyout");

                System.Windows.Rect bounds = icon.Current.BoundingRectangle;
                x = (int)(bounds.Left + bounds.Width / 2);
                y = (int)(bounds.Top + bounds.Height / 2);
            }

            ClickAt(x, y, right);
            SetCursorPos(back.X, back.Y);
        }

        static void ClickAt(int x, int y, bool right)
        {
            SetCursorPos(x, y);
            Wait(150);
            mouse_event(right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Wait(40);
            mouse_event(right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            Wait(100);
        }

        static RECT FindTrayIconRect()
        {
            var found = new List<RECT>();
            EnumWindows((w, l) =>
            {
                if (OwnerPid(w) != app.Id) return true;
                var id = new NOTIFYICONIDENTIFIER { hWnd = w, uID = 1 };
                id.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                RECT rect;
                if (Shell_NotifyIconGetRect(ref id, out rect) == 0) found.Add(rect);
                return true;
            }, IntPtr.Zero);
            if (found.Count == 0) throw new InvalidOperationException("tray icon not found");
            return found[0];
        }

        static void Screenshot(string fileName)
        {
            System.Drawing.Rectangle screen = Screen.PrimaryScreen.Bounds;
            using (var bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                    g.CopyFromScreen(screen.Location, System.Drawing.Point.Empty, screen.Size);
                bitmap.Save(Path.Combine(scratch, fileName), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // ---- keyboard ----

        static void PressHotkey()
        {
            EnsureFocus();
            Native.SendChord(Native.VK_CONTROL, Native.VK_SHIFT, VK_Q);
        }

        static void SelectAll()
        {
            if (formField != null) formField.SelectAll();
            else Native.SendChord(Native.VK_CONTROL, VK_A);
            Wait(60);
        }

        static void Clear()
        {
            EnsureFocus();
            if (formField != null)
            {
                formField.Clear();
                Wait(60);
                return;
            }
            Native.SendChord(Native.VK_CONTROL, VK_A);
            Wait(60);
            Native.SendChord(VK_BACK);
            Wait(100);
        }

        static string ReadField()
        {
            EnsureFocus();
            if (formField != null) return formField.Text;

            ClipboardEx.Clear(owner);
            uint before = Native.GetClipboardSequenceNumber();
            Native.SendChord(Native.VK_CONTROL, VK_A);
            Wait(60);
            Native.SendChord(Native.VK_CONTROL, Native.VK_INSERT);

            var clock = Stopwatch.StartNew();
            while (Native.GetClipboardSequenceNumber() == before && clock.ElapsedMilliseconds < 1500) Wait(15);
            Wait(60);
            string text = ClipboardEx.ReadText(owner);
            Native.SendChord(Native.VK_CONTROL, Native.VK_END);
            Wait(80);
            return text;
        }

        static void TypeText(string text)
        {
            EnsureFocus();
            foreach (char c in text)
            {
                var inputs = new INPUT[2];
                inputs[0].type = inputs[1].type = 1;
                inputs[0].ki.wScan = inputs[1].ki.wScan = c;
                inputs[0].ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                Wait(8);
            }
            Wait(150);
        }

        static void SendArrow(ushort vk, bool shift)
        {
            var inputs = new List<INPUT>();
            if (shift) inputs.Add(Key(Native.VK_SHIFT, 0));
            inputs.Add(Key(vk, KEYEVENTF_EXTENDEDKEY));
            inputs.Add(Key(vk, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP));
            if (shift) inputs.Add(Key(Native.VK_SHIFT, KEYEVENTF_KEYUP));
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            Wait(40);
        }

        static INPUT Key(ushort vk, uint flags)
        {
            var input = new INPUT { type = 1 };
            input.ki.wVk = vk;
            input.ki.dwFlags = flags;
            return input;
        }

        // ---- windows ----

        static void EnsureFocus()
        {
            if (expectedForeground == IntPtr.Zero) throw new InvalidOperationException("no target window");
            if (GetForegroundWindow() != expectedForeground)
                throw new InvalidOperationException("target lost focus to [" + ClassOf(GetForegroundWindow()) + "], refusing to send keys");
        }

        static void Focus(IntPtr window)
        {
            uint unused;
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out unused);
            uint me = GetCurrentThreadId();
            bool attached = AttachThreadInput(me, foregroundThread, true);
            BringWindowToTop(window);
            SetForegroundWindow(window);
            if (attached) AttachThreadInput(me, foregroundThread, false);

            var clock = Stopwatch.StartNew();
            while (GetForegroundWindow() != window && clock.ElapsedMilliseconds < 2000) Wait(20);
            Wait(250);
        }

        static IntPtr WaitForWindow(Func<IntPtr, bool> match, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows((w, l) =>
                {
                    if (IsWindowVisible(w) && match(w))
                    {
                        found = w;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
                Wait(100);
            }
            throw new TimeoutException("window did not appear");
        }

        static string ClassOf(IntPtr window)
        {
            return Native.ClassOf(window);
        }

        static string TitleOf(IntPtr window)
        {
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

        static int OwnerPid(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return (int)pid;
        }

        static void KillTree(int pid)
        {
            using (var kill = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + pid) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                kill.WaitForExit(5000);
        }

        // Keeps pumping messages: our hidden clipboard window must answer when other apps empty the clipboard.
        static void Wait(int ms)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        // ---- native ----

        const int WM_CLOSE = 0x0010;
        const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_LEFT = 0x25, VK_DOWN = 0x28;
        const ushort VK_A = 0x41, VK_Q = 0x51, VK_S = 0x53, VK_W = 0x57, VK_F9 = 0x78;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
            public long padding;  // the union's size is set by its larger mouse member
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public KEYBDINPUT ki;
        }

        delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool enable);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    }
}
