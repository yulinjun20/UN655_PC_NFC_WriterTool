using System;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;

namespace PC_NfcWriterTool.Forms
{
    public sealed class KeyArchiveForm : Form
    {
        private readonly CardStore _store;
        private readonly Label _path;
        private readonly Label _sha;
        private readonly Label _time;

        public KeyArchiveForm(CardStore store)
        {
            _store = store;
            Text = "密钥文件备案（不算密、不下发）";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 260);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(10f);

            _path = new Label { AutoSize = false, Size = new Size(470, 24), Location = new Point(24, 24) };
            _sha = new Label { AutoSize = false, Size = new Size(470, 40), Location = new Point(24, 56) };
            _time = new Label { AutoSize = false, Size = new Size(470, 24), Location = new Point(24, 100) };
            Controls.Add(_path);
            Controls.Add(_sha);
            Controls.Add(_time);

            Controls.Add(new Label
            {
                AutoSize = false,
                Size = new Size(470, 40),
                Location = new Point(24, 130),
                ForeColor = Color.FromArgb(90, 90, 90),
                Text = "说明: 正式 Kf/D 在读卡器固件内；此处仅工厂追溯用。"
            });

            var pick = UiTheme.PrimaryButton("选择文件…");
            pick.Location = new Point(24, 190);
            pick.Size = new Size(120, 36);
            pick.Click += OnPick;
            Controls.Add(pick);

            var clear = UiTheme.SecondaryButton("清除备案");
            clear.Location = new Point(160, 190);
            clear.Size = new Size(120, 36);
            clear.Click += OnClear;
            Controls.Add(clear);

            var close = UiTheme.SecondaryButton("关闭");
            close.Location = new Point(380, 190);
            close.Size = new Size(100, 36);
            close.Click += (s, e) => Close();
            Controls.Add(close);

            RefreshView();
        }

        private void RefreshView()
        {
            KeyArchiveInfo info = _store.GetKeyArchive();
            if (info == null)
            {
                _path.Text = "当前文件: （未备案）";
                _sha.Text = "文件校验: —";
                _time.Text = "加载时间: —";
                return;
            }

            _path.Text = "当前文件: " + info.FilePath;
            _sha.Text = "文件校验: SHA256= " + info.Sha256;
            _time.Text = "加载时间: " + info.LoadedAt.ToString("yyyy-MM-dd HH:mm");
        }

        private void OnPick(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dlg.Title = "选择本厂密钥说明文件（仅备案）";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    _store.SaveKeyArchive(dlg.FileName);
                    RefreshView();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnClear(object sender, EventArgs e)
        {
            _store.ClearKeyArchive();
            RefreshView();
        }
    }
}
