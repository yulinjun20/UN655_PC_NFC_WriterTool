using System;
using System.Drawing;
using System.Windows.Forms;

namespace PC_NfcWriterTool.Forms
{
    public sealed class LoginForm : Form
    {
        private readonly TextBox _employee;
        private readonly Label _hint;

        public string OperatorId { get; private set; }

        public LoginForm()
        {
            Text = "UN655 NFC 工厂发卡工具";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(480, 260);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(10f);

            var title = new Label
            {
                Text = "操作员工号",
                AutoSize = true,
                Font = UiTheme.Ui(11f),
                Location = new Point(90, 70)
            };

            _employee = new TextBox
            {
                Font = UiTheme.Ui(12f),
                Location = new Point(90, 100),
                Size = new Size(300, 32),
                MaxLength = 32
            };
            _employee.KeyDown += OnKeyDown;

            var enter = UiTheme.PrimaryButton("进入系统");
            enter.Location = new Point(160, 150);
            enter.Size = new Size(160, 40);
            enter.Click += OnEnter;

            _hint = new Label
            {
                Text = "提示：工号将写入每条发卡成功记录。",
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(90, 210)
            };

            Controls.Add(title);
            Controls.Add(_employee);
            Controls.Add(enter);
            Controls.Add(_hint);
            AcceptButton = enter;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _employee.Focus();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                TryEnter();
            }
        }

        private void OnEnter(object sender, EventArgs e)
        {
            TryEnter();
        }

        private void TryEnter()
        {
            string id = (_employee.Text ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                MessageBox.Show(this, "请输入操作员工号。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _employee.Focus();
                return;
            }

            OperatorId = id;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
