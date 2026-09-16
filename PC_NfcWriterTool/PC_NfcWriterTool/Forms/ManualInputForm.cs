using System;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Protocol;

namespace PC_NfcWriterTool.Forms
{
    public sealed class ManualInputForm : Form
    {
        private readonly TextBox _mn;
        private readonly TextBox _id;

        public QueueItem Item { get; private set; }
        public bool DirectWrite { get; private set; }

        public ManualInputForm()
        {
            Text = "手动输入";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 220);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(10f);

            Controls.Add(new Label { Text = "物料号(9位数字):", AutoSize = true, Location = new Point(30, 30) });
            _mn = new TextBox { Location = new Point(170, 26), Size = new Size(200, 28), MaxLength = 9 };
            Controls.Add(_mn);

            Controls.Add(new Label { Text = "ID号(4位):", AutoSize = true, Location = new Point(30, 70) });
            _id = new TextBox { Location = new Point(170, 66), Size = new Size(200, 28), MaxLength = 4 };
            Controls.Add(_id);

            var add = UiTheme.PrimaryButton("加入队列");
            add.Location = new Point(30, 130);
            add.Size = new Size(110, 36);
            add.Click += (s, e) => Finish(false);
            Controls.Add(add);

            var direct = UiTheme.SecondaryButton("直接发卡");
            direct.Location = new Point(155, 130);
            direct.Size = new Size(110, 36);
            direct.Click += (s, e) => Finish(true);
            Controls.Add(direct);

            var closeManual = UiTheme.SecondaryButton("关闭手动模式");
            closeManual.Location = new Point(280, 130);
            closeManual.Size = new Size(110, 36);
            closeManual.Click += (s, e) => { DialogResult = DialogResult.Abort; Close(); };
            Controls.Add(closeManual);
        }

        private void Finish(bool direct)
        {
            string mn = (_mn.Text ?? "").Trim();
            string id = (_id.Text ?? "").Trim().ToUpperInvariant();
            if (!NdefText.IsValidMaterialNumber(mn))
            {
                MessageBox.Show(this, "物料号必须是 9 位数字。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!NdefText.IsValidCardId(id))
            {
                MessageBox.Show(this, "ID号必须是 4 位字母或数字。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Item = new QueueItem { MaterialNumber = mn, CardId = id, Source = "手动" };
            DirectWrite = direct;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
