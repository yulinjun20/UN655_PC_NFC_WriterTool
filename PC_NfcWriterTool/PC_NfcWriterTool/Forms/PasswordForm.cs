using System;
using System.Drawing;
using System.Windows.Forms;

namespace PC_NfcWriterTool.Forms
{
    public sealed class PasswordForm : Form
    {
        public const string DefaultPassword = "12345678";

        private readonly TextBox _password;

        public PasswordForm()
            : this("解锁手动模式")
        {
        }

        public PasswordForm(string title)
        {
            Text = string.IsNullOrEmpty(title) ? "输入密码" : title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 170);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(10f);

            Controls.Add(new Label
            {
                Text = "密码:",
                AutoSize = true,
                Location = new Point(30, 35)
            });

            _password = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new Point(90, 32),
                Size = new Size(220, 28),
                MaxLength = 32
            };
            Controls.Add(_password);

            Controls.Add(new Label
            {
                Text = "（默认 12345678）",
                AutoSize = true,
                ForeColor = Color.Gray,
                Location = new Point(90, 64)
            });

            var ok = UiTheme.PrimaryButton("确定");
            ok.Location = new Point(90, 110);
            ok.Size = new Size(90, 34);
            ok.Click += OnOk;
            Controls.Add(ok);

            var cancel = UiTheme.SecondaryButton("取消");
            cancel.Location = new Point(200, 110);
            cancel.Size = new Size(90, 34);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _password.Focus();
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_password.Text == DefaultPassword)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            MessageBox.Show(this, "密码错误。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _password.SelectAll();
            _password.Focus();
        }

        /// <summary>Show password dialog; true if user entered DefaultPassword.</summary>
        public static bool TryPrompt(IWin32Window owner, string title)
        {
            using (var dlg = new PasswordForm(title))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        /// <summary>Require DefaultPassword entered successfully twice in a row.</summary>
        public static bool TryPromptTwice(IWin32Window owner)
        {
            if (!TryPrompt(owner, "请输入密码"))
            {
                return false;
            }

            if (!TryPrompt(owner, "请再次输入密码确认"))
            {
                return false;
            }

            return true;
        }
    }
}
