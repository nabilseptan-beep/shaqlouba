using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("شقلوبة")]
[assembly: AssemblyProduct("شقلوبة")]
[assembly: AssemblyDescription("Flips text typed on the wrong keyboard layout between Arabic and English")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Shaqlouba
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool isFirstInstance;
            using (var mutex = new Mutex(true, "Shaqlouba.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show(
                        "شقلوبة شغال بالفعل. دور على أيقونته جنب الساعة.", "شقلوبة",
                        MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new TrayApp());
            }
        }
    }

    // Hidden window that receives the hotkey and owns our clipboard writes.
    internal sealed class MessageWindow : NativeWindow
    {
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public event Action HotkeyPressed = delegate { };

        public MessageWindow()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) HotkeyPressed();
            base.WndProc(ref m);
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        const int HotkeyId = 1;
        const int MenuCloseDelayMs = 150;
        const string AppName = "شقلوبة";

        // The taskbar and its flyouts take the focus when the tray icon is clicked, so they are never flip targets.
        static readonly HashSet<string> ShellWindows = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
        };

        readonly Settings settings = Settings.Load();
        readonly MessageWindow window = new MessageWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem flipItem;
        readonly Flipper flipper;

        // Kept in a field so the garbage collector doesn't free the delegate while Windows still calls it.
        readonly Native.WinEventProc foregroundChanged;
        readonly IntPtr foregroundHook;
        readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        IntPtr lastApp;

        public TrayApp()
        {
            flipper = new Flipper(window.Handle, Notify) { SwitchLayoutAfterFlip = settings.SwitchLayout };
            window.HotkeyPressed += delegate { flipper.Run(IntPtr.Zero); };

            foregroundChanged = delegate(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time) { Remember(hwnd); };
            foregroundHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                foregroundChanged, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            Remember(Native.GetForegroundWindow());

            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = new Font("Segoe UI", 10f) };

            flipItem = new ToolStripMenuItem("اقلب النص دلوقتي", null, delegate { FlipLastApp(); });
            flipItem.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(flipItem);

            menu.Items.Add("غيّر الاختصار...", null, delegate { ChangeHotkey(); });
            menu.Items.Add(new ToolStripSeparator());

            var layoutItem = new ToolStripMenuItem("بدّل لغة الكيبورد بعد القلب") { Checked = settings.SwitchLayout, CheckOnClick = true };
            layoutItem.Click += delegate
            {
                settings.SwitchLayout = layoutItem.Checked;
                flipper.SwitchLayoutAfterFlip = layoutItem.Checked;
                settings.Save();
            };
            menu.Items.Add(layoutItem);

            var startupItem = new ToolStripMenuItem("شغّله مع ويندوز") { Checked = Autostart.IsEnabled, CheckOnClick = true };
            startupItem.Click += delegate { Autostart.Set(startupItem.Checked); };
            menu.Items.Add(startupItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("عن البرنامج...", null, delegate { ShowAbout(); });
            menu.Items.Add("خروج", null, delegate { ExitThread(); });

            tray.Icon = LoadIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) FlipLastApp();
            };
            tray.Visible = true;

            // Keeps the Run entry pointing here if the exe was moved.
            if (Autostart.IsEnabled) Autostart.Set(true);

            bool registered = ApplyHotkey(settings.Hotkey);
            if (!registered)
                Notify("الاختصار " + settings.Hotkey + " مستخدم في برنامج تاني. غيّره من قايمة الأيقونة.");
            else if (!settings.Welcomed)
                Notify("شقلوبة شغال! لو كتبت باللغة الغلط دوس " + settings.Hotkey + ". أيقونته تحت السهم ^ جنب الساعة، اسحبها على الشريط عشان تفضل ظاهرة.");

            if (!settings.Welcomed)
            {
                settings.Welcomed = true;
                settings.Save();
            }
        }

        void Remember(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            uint processId;
            Native.GetWindowThreadProcessId(hwnd, out processId);
            if (processId != ownProcessId && !ShellWindows.Contains(Native.ClassOf(hwnd))) lastApp = hwnd;
        }

        void FlipLastApp()
        {
            if (lastApp == IntPtr.Zero || !Native.IsWindow(lastApp))
            {
                Notify("افتح البرنامج اللي فيه النص الأول.");
                return;
            }
            flipper.Run(lastApp);
        }

        bool ApplyHotkey(Hotkey hotkey)
        {
            Native.UnregisterHotKey(window.Handle, HotkeyId);
            bool ok = Native.RegisterHotKey(window.Handle, HotkeyId, hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key);
            if (ok)
            {
                flipper.HotkeyVk = (int)hotkey.Key;
                flipItem.ShortcutKeyDisplayString = hotkey.ToString();
                tray.Text = AppName + " (" + hotkey + ")";
            }
            return ok;
        }

        async void ChangeHotkey()
        {
            // The menu hands the focus back as it finishes closing, which would take it from the dialog.
            await Task.Delay(MenuCloseDelayMs);

            // Let go of the current hotkey, or pressing it in the dialog would trigger a flip.
            Native.UnregisterHotKey(window.Handle, HotkeyId);
            using (var form = new HotkeyForm(settings.Hotkey))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    if (ApplyHotkey(form.Result))
                    {
                        settings.Hotkey = form.Result;
                        settings.Save();
                        Notify("الاختصار الجديد: " + form.Result);
                        return;
                    }
                    Notify("الاختصار " + form.Result + " مستخدم في برنامج تاني. فضل " + settings.Hotkey + " زي ما هو.");
                }
            }
            ApplyHotkey(settings.Hotkey);
        }

        async void ShowAbout()
        {
            await Task.Delay(MenuCloseDelayMs);
            using (var form = new AboutForm()) form.ShowDialog();
        }

        void Notify(string message)
        {
            tray.ShowBalloonTip(3000, AppName, message, ToolTipIcon.Info);
        }

        static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaqlouba.icon.ico"))
                return new Icon(stream, SystemInformation.SmallIconSize);
        }

        protected override void ExitThreadCore()
        {
            Native.UnregisterHotKey(window.Handle, HotkeyId);
            Native.UnhookWinEvent(foregroundHook);
            tray.Visible = false;
            tray.Dispose();
            window.DestroyHandle();
            base.ExitThreadCore();
        }
    }
}
