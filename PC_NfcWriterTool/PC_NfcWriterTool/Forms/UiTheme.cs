using System.Drawing;
using System.Windows.Forms;

namespace PC_NfcWriterTool.Forms
{
    public static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(245, 247, 250);
        public static readonly Color Panel = Color.White;
        public static readonly Color Accent = Color.FromArgb(0, 102, 180);
        public static readonly Color Success = Color.FromArgb(16, 128, 67);
        public static readonly Color Fail = Color.FromArgb(196, 43, 28);
        public static readonly Color Wait = Color.FromArgb(70, 80, 96);
        public static readonly Color Connected = Color.FromArgb(16, 128, 67);
        public static readonly Color Disconnected = Color.FromArgb(160, 160, 160);

        public static Font Ui(float size, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style);
            }
            catch
            {
                try
                {
                    return new Font("Microsoft YaHei", size, style);
                }
                catch
                {
                    return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style);
                }
            }
        }

        public static Font Ui(float size)
        {
            return Ui(size, FontStyle.Regular);
        }

        public static Button PrimaryButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                Font = Ui(10f, FontStyle.Bold),
                BackColor = Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Height = 36,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        public static Button SecondaryButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                Font = Ui(10f),
                BackColor = Color.FromArgb(230, 234, 240),
                ForeColor = Color.FromArgb(30, 40, 50),
                FlatStyle = FlatStyle.Flat,
                Height = 36,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
