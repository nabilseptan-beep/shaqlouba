using System.Drawing;
using System.Windows.Forms;

namespace Shaqlouba
{
    // Small dialog that captures the next key combination the user presses.
    internal sealed class HotkeyForm : Form
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        readonly TextBox capture;
        readonly Label hint;
        readonly Button save;

        public Hotkey Result { get; private set; }

        public HotkeyForm(Hotkey current)
        {
            Result = current;

            Text = "تغيير الاختصار";
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(380, 170);

            var prompt = new Label
            {
                Text = "دوس الاختصار الجديد على الكيبورد:",
                Location = new Point(16, 16),
                Size = new Size(348, 24),
            };

            capture = new TextBox
            {
                Text = current.ToString(),
                Location = new Point(16, 44),
                Size = new Size(348, 28),
                ReadOnly = true,
                BackColor = SystemColors.Window,
                TextAlign = HorizontalAlignment.Center,
                RightToLeft = RightToLeft.No,
            };

            hint = new Label
            {
                Text = "لازم يكون فيه Ctrl أو Alt أو Win، أو زرار F لوحده.",
                Location = new Point(16, 78),
                Size = new Size(348, 24),
                ForeColor = SystemColors.GrayText,
            };

            save = new Button
            {
                Text = "حفظ",
                DialogResult = DialogResult.OK,
                Location = new Point(16, 118),
                Size = new Size(96, 34),
            };

            var cancel = new Button
            {
                Text = "إلغاء",
                DialogResult = DialogResult.Cancel,
                Location = new Point(120, 118),
                Size = new Size(96, 34),
            };

            Controls.AddRange(new Control[] { prompt, capture, hint, save, cancel });
            AcceptButton = save;
            CancelButton = cancel;
        }

        // Opened from the tray menu, the dialog comes up behind the shell's focus and wouldn't hear the keys.
        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            Native.SetForegroundWindow(Handle);
            Activate();
            capture.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool keyDown = msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN;
            if (keyDown && ActiveControl == capture && TryCapture(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        bool TryCapture(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);
            bool control = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0, shift = (keyData & Keys.Shift) != 0;

            bool modifierOnly = key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin;
            if (modifierOnly) return true;

            bool plainKey = !control && !alt && !shift && !win;
            if (plainKey && (key == Keys.Tab || key == Keys.Enter || key == Keys.Escape)) return false;

            // A combination without Ctrl/Alt/Win would take over a key people type with, like Shift+Q.
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            if (!control && !alt && !win && !functionKey)
            {
                hint.ForeColor = Color.Firebrick;
                return true;
            }

            uint modifiers = (control ? Native.MOD_CONTROL : 0) | (alt ? Native.MOD_ALT : 0)
                | (shift ? Native.MOD_SHIFT : 0) | (win ? Native.MOD_WIN : 0);

            Result = new Hotkey(modifiers, key);
            capture.Text = Result.ToString();
            hint.ForeColor = SystemColors.GrayText;
            return true;
        }
    }
}
