using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Shaqlouba
{
    // Small dialog with the app name, version and developer.
    internal sealed class AboutForm : Form
    {
        const string Developer = "Nabil Septan";

        public AboutForm()
        {
            Text = "عن البرنامج";
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
            ClientSize = new Size(320, 170);

            var logo = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.StretchImage,
            };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaqlouba.icon.ico"))
                logo.Image = new Icon(stream, 48, 48).ToBitmap();

            var name = new Label
            {
                Text = "شقلوبة",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Location = new Point(80, 14),
                Size = new Size(224, 30),
            };

            var version = new Label
            {
                Text = "الإصدار " + Assembly.GetExecutingAssembly().GetName().Version.ToString(2),
                Location = new Point(80, 46),
                Size = new Size(224, 24),
                ForeColor = SystemColors.GrayText,
            };

            var developer = new Label
            {
                Text = "تطوير: " + Developer,
                Location = new Point(16, 84),
                Size = new Size(288, 24),
            };

            var close = new Button
            {
                Text = "إغلاق",
                DialogResult = DialogResult.OK,
                Location = new Point(16, 120),
                Size = new Size(96, 34),
            };

            Controls.AddRange(new Control[] { logo, name, version, developer, close });
            AcceptButton = close;
            CancelButton = close;
        }

        // Opened from the tray menu, the dialog comes up behind the shell's focus.
        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            Native.SetForegroundWindow(Handle);
            Activate();
        }
    }
}
